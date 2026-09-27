using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PokerEventSourced.Domain.Events;
using Tomlyn;
using Tomlyn.Model;

namespace PokerEventSourced.Import.Phh;

/// <summary>
/// Reads PHH files: ".phh" (one hand) and ".phhs" (many hands, each under a "[n]" header).
/// Each section is parsed on its own so one bad hand can't take the rest of the file down.
/// </summary>
public sealed partial class PhhParser : IHandHistoryParser
{
    public const string Format = "PHH";

    public IReadOnlyCollection<string> FileExtensions { get; } = [".phh", ".phhs"];

    public IEnumerable<HandParseResult> Parse(string content, string sourceFile)
    {
        foreach (var (section, text) in SplitSections(content))
        {
            var source = new HandSource(Format, sourceFile, section);
            yield return ParseSection(source, text);
        }
    }

    private static HandParseResult ParseSection(HandSource source, string text)
    {
        PhhHand hand;
        try
        {
            hand = Read(TomlSerializer.Deserialize<TomlTable>(text) ?? throw new FormatException("The hand is empty."));
        }
        catch (Exception ex) when (ex is TomlException or FormatException or InvalidCastException or KeyNotFoundException)
        {
            return new FailedHand(source, $"Unreadable: {ex.Message}");
        }

        if (hand.Variant != "NT")
        {
            return new SkippedHand(source, $"Variant '{hand.Variant}' is not no-limit Texas hold'em (NT).");
        }

        try
        {
            var events = PhhHandTranslator.Translate(hand, source);
            return new ParsedHand(source, StreamKey(hand, text), events);
        }
        catch (PhhHandException ex)
        {
            return new FailedHand(source, ex.Message);
        }
    }

    /// <summary>"phh:{venue}:{hand}", or a hash of the hand's text when the source has no venue/hand number.</summary>
    internal static string StreamKey(PhhHand hand, string text)
    {
        if (!string.IsNullOrWhiteSpace(hand.Venue) && !string.IsNullOrWhiteSpace(hand.Hand))
        {
            return $"phh:{hand.Venue}:{hand.Hand}";
        }

        var normalized = text.Replace("\r\n", "\n").Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"phh:sha256:{Convert.ToHexStringLower(hash)[..32]}";
    }

    internal static IEnumerable<(int? Section, string Text)> SplitSections(string content)
    {
        var headers = SectionHeader().Matches(content);
        if (headers.Count == 0)
        {
            yield return (null, content);
            yield break;
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var start = headers[i].Index + headers[i].Length;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : content.Length;
            var number = int.Parse(headers[i].Groups[1].Value, CultureInfo.InvariantCulture);
            yield return (number, content[start..end]);
        }
    }

    [GeneratedRegex(@"^\[(\d+)\][ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex SectionHeader();

    private static PhhHand Read(TomlTable t) => new(
        Variant: (string)t["variant"],
        Antes: DecimalArray(t, "antes"),
        BlindsOrStraddles: DecimalArray(t, "blinds_or_straddles"),
        MinBet: t.TryGetValue("min_bet", out var minBet) ? ToDecimal(minBet) : null,
        StartingStacks: ((TomlArray)t["starting_stacks"]).Select(ToDecimal).ToList(),
        Actions: ((TomlArray)t["actions"]).Select(a => (string)a!).ToList(),
        Players: t.TryGetValue("players", out var players) ? ((TomlArray)players).Select(p => (string)p!).ToList() : null,
        Seats: t.TryGetValue("seats", out var seats) ? ((TomlArray)seats).Select(s => Convert.ToInt32(s, CultureInfo.InvariantCulture)).ToList() : null,
        SeatCount: Int(t, "seat_count"),
        Venue: Text(t, "venue"),
        Hand: t.TryGetValue("hand", out var h) ? Convert.ToString(h, CultureInfo.InvariantCulture) : null,
        Table: t.TryGetValue("table", out var table) ? Convert.ToString(table, CultureInfo.InvariantCulture) : null,
        Year: Int(t, "year"),
        Month: Int(t, "month"),
        Day: Int(t, "day"),
        Time: t.TryGetValue("time", out var time) ? ((TomlDateTime)time).DateTime.TimeOfDay : null,
        TimeZone: Text(t, "time_zone") ?? Text(t, "time_zone_abbreviation"),
        Currency: Text(t, "currency"),
        CurrencySymbol: Text(t, "currency_symbol"));

    private static List<decimal> DecimalArray(TomlTable t, string key) =>
        ((TomlArray)t[key]).Select(v => ToDecimal(v) ?? throw new FormatException($"'{key}' can't be infinite.")).ToList();

    private static decimal? ToDecimal(object? value) => value switch
    {
        long l => l,
        double d when double.IsInfinity(d) => null,
        double d => (decimal)d,
        _ => throw new FormatException($"'{value}' is not a number."),
    };

    private static int? Int(TomlTable t, string key) =>
        t.TryGetValue(key, out var value) ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;

    private static string? Text(TomlTable t, string key) => t.TryGetValue(key, out var value) ? (string)value : null;
}

/// <summary>A hand whose actions don't make sense under the rules we understand.</summary>
internal sealed class PhhHandException(string message) : Exception(message);
