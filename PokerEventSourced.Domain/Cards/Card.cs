using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokerEventSourced.Domain.Cards;

public enum Rank
{
    Two = 2,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace,
}

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

/// <summary>A playing card. Written and stored as two characters: rank then suit, e.g. "Ah", "Tc", "2d".</summary>
[JsonConverter(typeof(CardJsonConverter))]
public readonly record struct Card(Rank Rank, Suit Suit)
{
    private const string Ranks = "23456789TJQKA";
    private const string Suits = "cdhs";

    public override string ToString() => $"{Ranks[(int)Rank - 2]}{Suits[(int)Suit]}";

    public static Card Parse(string text) =>
        TryParse(text, out var card) ? card : throw new FormatException($"'{text}' is not a card.");

    public static bool TryParse(ReadOnlySpan<char> text, out Card card)
    {
        card = default;
        if (text.Length != 2)
        {
            return false;
        }

        var rank = Ranks.IndexOf(char.ToUpperInvariant(text[0]));
        var suit = Suits.IndexOf(char.ToLowerInvariant(text[1]));
        if (rank < 0 || suit < 0)
        {
            return false;
        }

        card = new Card((Rank)(rank + 2), (Suit)suit);
        return true;
    }

    /// <summary>
    /// Parses concatenated cards such as "Jc3d5c". Returns null when any card is unknown
    /// ("??", or a "?" in the rank or suit). Throws on anything else that isn't a card.
    /// </summary>
    public static IReadOnlyList<Card>? ParseMany(string text)
    {
        if (text.Length == 0 || text.Length % 2 != 0)
        {
            throw new FormatException($"'{text}' is not a list of cards.");
        }

        if (text.Contains('?'))
        {
            return null;
        }

        var cards = new Card[text.Length / 2];
        for (var i = 0; i < cards.Length; i++)
        {
            if (!TryParse(text.AsSpan(i * 2, 2), out cards[i]))
            {
                throw new FormatException($"'{text}' is not a list of cards.");
            }
        }

        return cards;
    }

    public static string Format(IEnumerable<Card> cards) => string.Concat(cards);
}

internal sealed class CardJsonConverter : JsonConverter<Card>
{
    public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Card.Parse(reader.GetString() ?? throw new JsonException("A card can't be null."));

    public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
