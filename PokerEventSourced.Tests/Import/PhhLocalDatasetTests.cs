using System.Diagnostics;
using PokerEventSourced.Domain.Events;
using PokerEventSourced.Import;
using PokerEventSourced.Import.Phh;
using PokerEventSourced.Tests.TestSupport;

namespace PokerEventSourced.Tests.Import;

/// <summary>
/// Parses every downloaded HandHQ file in _phh-dataset-local. Opt-in because it's slow and needs the
/// data (scripts/download-handhq.ps1). Run with POKER_DATASET_TESTS=1.
/// </summary>
public class PhhLocalDatasetTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_downloaded_hand_parses()
    {
        if (Environment.GetEnvironmentVariable("POKER_DATASET_TESTS") != "1")
        {
            Assert.Skip("Set POKER_DATASET_TESTS=1 to parse the local dataset.");
        }

        var files = Directory.Exists(RepoPaths.LocalDataset)
            ? Directory.GetFiles(RepoPaths.LocalDataset, "*.phhs", SearchOption.AllDirectories)
            : [];
        if (files.Length == 0)
        {
            Assert.Skip("No local dataset. Run scripts/download-handhq.ps1.");
        }

        var parser = new PhhParser();
        var stopwatch = Stopwatch.StartNew();
        var parsed = 0;
        var events = 0;
        var failures = new List<FailedHand>();
        var skipped = new List<SkippedHand>();
        var flags = new Dictionary<UnusualReason, int>();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(RepoPaths.LocalDataset, file).Replace('\\', '/');
            foreach (var result in parser.Parse(File.ReadAllText(file), relative))
            {
                switch (result)
                {
                    case ParsedHand hand:
                        parsed++;
                        events += hand.Events.Count;
                        foreach (var flag in hand.Events.OfType<HandFlaggedUnusual>())
                        {
                            flags[flag.Reason] = flags.GetValueOrDefault(flag.Reason) + 1;
                        }

                        break;
                    case FailedHand failed:
                        failures.Add(failed);
                        break;
                    case SkippedHand skip:
                        skipped.Add(skip);
                        break;
                }
            }
        }

        output.WriteLine($"{files.Length} files, {parsed} hands, {events} events, {skipped.Count} skipped, {failures.Count} failed in {stopwatch.Elapsed.TotalSeconds:F1}s");
        foreach (var (reason, count) in flags)
        {
            output.WriteLine($"  flagged {reason}: {count}");
        }

        foreach (var group in failures.GroupBy(f => System.Text.RegularExpressions.Regex.Replace(f.Error, @"'[^']*'|\d+(\.\d+)?", "#")).OrderByDescending(g => g.Count()).Take(20))
        {
            var example = group.First();
            output.WriteLine($"  {group.Count()} x {group.Key}");
            output.WriteLine($"      e.g. {example.Source.File} [{example.Source.Section}]: {example.Error}");
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Ongame files record finishing stacks and winnings. For every player:
    /// starting stack - what our events say they put in + winnings = finishing stack.
    /// That checks call amounts, all-ins and uncalled bets against the source.
    /// </summary>
    [Fact]
    public void Contributions_match_ongame_finishing_stacks()
    {
        if (Environment.GetEnvironmentVariable("POKER_DATASET_TESTS") != "1")
        {
            Assert.Skip("Set POKER_DATASET_TESTS=1 to parse the local dataset.");
        }

        var files = Directory.Exists(RepoPaths.LocalDataset)
            ? Directory.GetFiles(RepoPaths.LocalDataset, "ong*.phhs", SearchOption.AllDirectories)
            : [];
        if (files.Length == 0)
        {
            Assert.Skip("No Ongame files in the local dataset. Run scripts/download-handhq.ps1.");
        }

        var parser = new PhhParser();
        var checkedHands = 0;
        var mismatches = new List<string>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var raw = Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(content)!;
            foreach (var hand in parser.Parse(content, file).OfType<ParsedHand>())
            {
                var section = (Tomlyn.Model.TomlTable)raw[hand.Source.Section!.Value.ToString()];
                if (!section.TryGetValue("finishing_stacks", out var finishing) || !section.TryGetValue("winnings", out var winnings))
                {
                    continue;
                }

                var starting = Numbers(section["starting_stacks"]);
                var finish = Numbers(finishing);
                var won = Numbers(winnings);
                var names = hand.Events.OfType<PlayerSeated>().Select(s => s.Player).ToList();
                var putIn = ContributionsFromEvents(hand.Events);
                checkedHands++;

                var wrong = Enumerable.Range(0, names.Count)
                    .Where(i => starting[i] - putIn.GetValueOrDefault(names[i]) + won[i] != finish[i])
                    .Select(i => $"p{i + 1} expected {finish[i]}, got {starting[i] - putIn.GetValueOrDefault(names[i]) + won[i]}")
                    .ToList();
                if (wrong.Count == 0)
                {
                    continue;
                }

                // Sort mismatches into explained causes, so a real engine bug can't hide among them.
                // Our pot is checked against the source's winnings (the difference must be a plausible rake:
                // Ongame took up to 5%). If that holds but the source's own stacks don't balance
                // (start - finish should equal that rake), the source's finishing stacks are wrong.
                var pot = hand.Events.OfType<HandCompleted>().Single().TotalPot;
                var rake = pot - won.Sum();
                var plausibleRake = rake >= 0 && rake <= pot * 0.05m + 0.01m;
                var sourceStacksBalance = starting.Sum() - finish.Sum() == rake;
                var category =
                    hand.Events.OfType<HandFlaggedUnusual>().Any(f => f.Reason == UnusualReason.OddSizedPost) ? "odd-sized post (PHH can't say what was dead)"
                    : won.Sum() == 0 ? "source winnings all zero"
                    : plausibleRake && !sourceStacksBalance ? "pot matches winnings, but the source's stacks don't balance"
                    : "unexplained";
                mismatches.Add($"{category}|{Path.GetFileName(hand.Source.File)} [{hand.Source.Section}] pot {pot}, won {won.Sum()}: {string.Join("; ", wrong)}");
            }
        }

        output.WriteLine($"{checkedHands} Ongame hands checked, {mismatches.Count} mismatches");
        foreach (var group in mismatches.GroupBy(m => m.Split('|')[0]))
        {
            output.WriteLine($"  {group.Count()} x {group.Key}");
            foreach (var example in group.Take(group.Key == "unexplained" ? 15 : 2))
            {
                output.WriteLine("      " + example.Split('|')[1]);
            }
        }

        Assert.True(checkedHands > 0);
        Assert.DoesNotContain(mismatches, m => m.StartsWith("unexplained", StringComparison.Ordinal));
    }

    private static List<decimal> Numbers(object array) =>
        ((Tomlyn.Model.TomlArray)array).Select(v => v is long l ? (decimal)l : (decimal)(double)v!).ToList();

    /// <summary>What each player put into the pot, rebuilt from the events alone.</summary>
    private static Dictionary<string, decimal> ContributionsFromEvents(IEnumerable<object> events)
    {
        var total = new Dictionary<string, decimal>();
        var street = new Dictionary<string, decimal>(); // player's total on the current street

        void Add(string player, decimal amount) => total[player] = total.GetValueOrDefault(player) + amount;

        void SetStreetTotal(string player, decimal to)
        {
            Add(player, to - street.GetValueOrDefault(player));
            street[player] = to;
        }

        foreach (var e in events)
        {
            switch (e)
            {
                case AntePosted a: Add(a.Player, a.Amount); break;
                case BlindPosted b: SetStreetTotal(b.Player, street.GetValueOrDefault(b.Player) + b.Amount); break;
                case PlayerCalled c: SetStreetTotal(c.Player, street.GetValueOrDefault(c.Player) + c.Amount); break;
                case PlayerBet b: SetStreetTotal(b.Player, b.Amount); break;
                case PlayerRaised r: SetStreetTotal(r.Player, r.To); break;
                case UncalledBetReturned u: SetStreetTotal(u.Player, street.GetValueOrDefault(u.Player) - u.Amount); break;
                case FlopDealt or TurnDealt or RiverDealt: street.Clear(); break;
            }
        }

        return total;
    }
}
