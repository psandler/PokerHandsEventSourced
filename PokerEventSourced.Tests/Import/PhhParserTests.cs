using PokerEventSourced.Domain.Cards;
using PokerEventSourced.Domain.Events;
using PokerEventSourced.Import;
using PokerEventSourced.Import.Phh;
using PokerEventSourced.Tests.TestSupport;

namespace PokerEventSourced.Tests.Import;

public class PhhParserTests
{
    private const string ExcerptFile = "phh/handhq-ps-50nl-excerpt.phhs";

    private static readonly PhhParser Parser = new();

    private static ParsedHand ExcerptHand(int section)
    {
        var results = Parser.Parse(RepoPaths.ReadSample(ExcerptFile), ExcerptFile);
        return Assert.IsType<ParsedHand>(results.Single(r => r.Source.Section == section));
    }

    private static IReadOnlyList<Card> Cards(string text) => Card.ParseMany(text)!;

    [Fact]
    public void Dwan_vs_Ivey_becomes_every_table_action()
    {
        const string file = "phh/dwan-ivey-2009.phh";
        var hand = Assert.IsType<ParsedHand>(Assert.Single(Parser.Parse(RepoPaths.ReadSample(file), file)));
        const string ivey = "Phil Ivey", antonius = "Patrik Antonius", dwan = "Tom Dwan";

        Assert.StartsWith("phh:sha256:", hand.StreamKey); // famous hands have no venue/hand number
        Assert.Equal(new HandSource("PHH", file, null), hand.Source);

        var e = hand.Events;
        var started = Assert.IsType<HandStarted>(e[0]);
        Assert.Equal((1000m, 2000m, 3, "USD"), (started.SmallBlind!.Value, started.BigBlind, started.PlayerCount, started.Currency));
        Assert.Null(started.StartedAtLocal); // only the year is known

        Assert.Equal(new PlayerSeated(ivey, 1, null, 1125600m, false), e[1]);
        Assert.Equal(new PlayerSeated(antonius, 2, null, 2000000m, false), e[2]);
        Assert.Equal(new PlayerSeated(dwan, 3, null, 553500m, true), e[3]);
        Assert.Equal(new HandFlaggedUnusual(UnusualReason.MissingTableId, "The source has no table ID."), e[4]);
        Assert.Equal(new AntePosted(ivey, 500m, false), e[5]);
        Assert.Equal(new AntePosted(antonius, 500m, false), e[6]);
        Assert.Equal(new AntePosted(dwan, 500m, false), e[7]);
        Assert.Equal(new BlindPosted(ivey, BlindKind.SmallBlind, 1000m, false), e[8]);
        Assert.Equal(new BlindPosted(antonius, BlindKind.BigBlind, 2000m, false), e[9]);

        var dealt = Assert.IsType<HoleCardsDealt>(e[10]);
        Assert.Equal(Cards("Ac2d"), dealt.Cards);
        Assert.Null(Assert.IsType<HoleCardsDealt>(e[11]).Cards);
        Assert.Equal(Cards("7h6h"), Assert.IsType<HoleCardsDealt>(e[12]).Cards);

        Assert.Equal(new PlayerRaised(dwan, Street.Preflop, 7000m, 5000m, false), e[13]);
        Assert.Equal(new PlayerRaised(ivey, Street.Preflop, 23000m, 16000m, false), e[14]);
        Assert.Equal(new PlayerFolded(antonius, Street.Preflop), e[15]);
        Assert.Equal(new PlayerCalled(dwan, Street.Preflop, 16000m, false), e[16]);
        Assert.Equal(Cards("Jc3d5c"), Assert.IsType<FlopDealt>(e[17]).Cards);
        Assert.Equal(new PlayerBet(ivey, Street.Flop, 35000m, false), e[18]);
        Assert.Equal(new PlayerCalled(dwan, Street.Flop, 35000m, false), e[19]);
        Assert.Equal(new TurnDealt(Card.Parse("4h")), e[20]);
        Assert.Equal(new PlayerBet(ivey, Street.Turn, 90000m, false), e[21]);
        Assert.Equal(new PlayerRaised(dwan, Street.Turn, 232600m, 142600m, false), e[22]);
        Assert.Equal(new PlayerRaised(ivey, Street.Turn, 1067100m, 834500m, true), e[23]);
        Assert.Equal(new PlayerCalled(dwan, Street.Turn, 262400m, true), e[24]); // all-in for less
        Assert.Equal(new UncalledBetReturned(ivey, 572100m), e[25]);
        Assert.Equal(Cards("Ac2d"), Assert.IsType<CardsShown>(e[26]).Cards);
        Assert.Equal(dwan, Assert.IsType<CardsShown>(e[27]).Player);
        Assert.Equal(new RiverDealt(Card.Parse("Jh")), e[28]);
        Assert.Equal(new HandCompleted(1109500m), e[29]);
        Assert.Equal(30, e.Count);
    }

    [Fact]
    public void Hand_header_is_read_from_a_handhq_hand()
    {
        var hand = ExcerptHand(1);

        Assert.Equal("phh:PokerStars:59937793578", hand.StreamKey);
        var started = Assert.IsType<HandStarted>(hand.Events[0]);
        Assert.Equal(new HandSource("PHH", ExcerptFile, 1), started.Source);
        Assert.Equal("PokerStars", started.Venue);
        Assert.Equal("59937793578", started.SourceHandId);
        Assert.Equal("QOgaEqMcJ73pcUMMxoisUg", started.TableId);
        Assert.Equal(6, started.TableMax);
        Assert.Equal(0.25m, started.SmallBlind);
        Assert.Equal(0.5m, started.BigBlind);
        Assert.Equal("USD", started.Currency);
        Assert.Equal(new DateTime(2009, 7, 1, 0, 0, 0), started.StartedAtLocal);
        Assert.Equal("ET", started.TimeZone);

        var seats = hand.Events.OfType<PlayerSeated>().ToList();
        Assert.Equal([6, 1, 2, 3, 4, 5], seats.Select(s => s.Seat!.Value));
        Assert.Equal("HoaSQCRso+nEyO5DUhGduw", Assert.Single(seats, s => s.IsButton).Player);
    }

    [Fact]
    public void Raise_that_everyone_folds_to_gets_the_uncalled_part_back()
    {
        var hand = ExcerptHand(1);

        Assert.Contains(new PlayerRaised("HoaSQCRso+nEyO5DUhGduw", Street.Preflop, 2m, 1.5m, false), hand.Events);
        Assert.Contains(new UncalledBetReturned("HoaSQCRso+nEyO5DUhGduw", 1.5m), hand.Events);
        Assert.Equal(new HandCompleted(1.25m), hand.Events[^1]);
    }

    [Fact]
    public void Heads_up_p1_is_the_big_blind_and_p2_the_button()
    {
        var hand = ExcerptHand(11);
        var (p1, p2) = ("XFa6sBGjP2gxoIvxR3fZIg", "5IBeOgaApY1Bvzv2P8X4EA");

        var seats = hand.Events.OfType<PlayerSeated>().ToList();
        Assert.False(seats[0].IsButton);
        Assert.True(seats[1].IsButton);
        Assert.Contains(new BlindPosted(p1, BlindKind.BigBlind, 0.5m, false), hand.Events);
        Assert.Contains(new BlindPosted(p2, BlindKind.SmallBlind, 0.25m, false), hand.Events);

        var actions = hand.Events.Where(e => e is PlayerRaised or PlayerCalled or PlayerChecked or PlayerBet or PlayerFolded);
        Assert.Equal(
            [
                new PlayerRaised(p2, Street.Preflop, 1m, 0.5m, false),
                new PlayerCalled(p1, Street.Preflop, 0.5m, false),
                new PlayerChecked(p1, Street.Flop),
                new PlayerBet(p2, Street.Flop, 1m, false),
                new PlayerCalled(p1, Street.Flop, 1m, false),
                new PlayerChecked(p1, Street.Turn),
                new PlayerBet(p2, Street.Turn, 2m, false),
                new PlayerCalled(p1, Street.Turn, 2m, false),
                new PlayerChecked(p1, Street.River),
                new PlayerBet(p2, Street.River, 4.25m, false),
                new PlayerFolded(p1, Street.River),
            ],
            actions);
        Assert.Contains(new UncalledBetReturned(p2, 4.25m), hand.Events);
        Assert.Equal(new HandCompleted(8m), hand.Events[^1]);
        Assert.DoesNotContain(hand.Events, e => e is HandFlaggedUnusual);
    }

    [Fact]
    public void Unrevealed_cards_at_showdown_become_one_muck_at_the_end()
    {
        var hand = ExcerptHand(18);

        var afterRiverBetting = hand.Events
            .SkipWhile(e => e is not RiverDealt)
            .Where(e => e is CardsShown or CardsMucked or HandCompleted)
            .Select(e => e switch
            {
                CardsShown s => $"{s.Player[..6]} shows {Card.Format(s.Cards)}",
                CardsMucked m => $"{m.Player[..6]} mucks",
                _ => "completed",
            });

        Assert.Equal(["mSD1pg shows 8h7h", "ceq82/ shows 3hTc", "5JJVDl mucks", "completed"], afterRiverBetting);
    }

    [Fact]
    public void All_in_run_out_shows_cards_once_after_the_river()
    {
        var hand = ExcerptHand(42);
        var names = hand.Events.OfType<PlayerSeated>().Select(s => s.Player).ToList();
        var (p1, p2) = (names[0], names[1]);

        Assert.Contains(new PlayerRaised(p1, Street.Preflop, 3.75m, 2.75m, true), hand.Events);
        Assert.Contains(new PlayerCalled(p2, Street.Preflop, 2.75m, false), hand.Events);
        Assert.DoesNotContain(hand.Events, e => e is CardsMucked or UncalledBetReturned);

        // The file repeats "sm ????" on every street, then reveals the cards after the river.
        var events = hand.Events.ToList();
        var shown = events.OfType<CardsShown>().ToList();
        Assert.Equal([p1, p2], shown.Select(s => s.Player));
        Assert.Equal(Cards("TsAh"), shown[0].Cards);
        Assert.True(events.IndexOf(shown[0]) > events.FindIndex(e => e is RiverDealt));
        Assert.Equal(new HandCompleted(7.5m), events[^1]);
    }

    [Fact]
    public void Negative_blind_is_a_live_new_player_post()
    {
        var hand = ExcerptHand(83);
        var p8 = hand.Events.OfType<PlayerSeated>().Single(s => s.ActionOrder == 8).Player;

        Assert.Contains(new BlindPosted(p8, BlindKind.NewPlayerPost, 0.5m, false), hand.Events);
        Assert.Contains(new PlayerChecked(p8, Street.Preflop), hand.Events); // the post already matches the big blind
        Assert.Contains(new UncalledBetReturned(p8, 1m), hand.Events);
        Assert.Equal(new HandCompleted(1.25m), hand.Events[^1]);

        // Table ID is missing in this hand; a post of exactly one big blind is normal.
        var flag = Assert.Single(hand.Events.OfType<HandFlaggedUnusual>());
        Assert.Equal(UnusualReason.MissingTableId, flag.Reason);
    }

    [Fact]
    public void Odd_sized_post_is_flagged()
    {
        var hand = Assert.IsType<ParsedHand>(Assert.Single(Parser.Parse(
            MinimalHand(blinds: "[5, 10, -5]", stacks: "[1000, 1000, 1000]", actions: "'p3 cc', 'p1 f', 'p2 cc'"),
            "odd.phh")));

        Assert.Contains(hand.Events, e => e is HandFlaggedUnusual { Reason: UnusualReason.OddSizedPost });
        Assert.Contains(new PlayerCalled("c", Street.Preflop, 5m, false), hand.Events);
    }

    [Fact]
    public void Unknown_stacks_are_flagged_and_never_all_in()
    {
        var hand = Assert.IsType<ParsedHand>(Assert.Single(Parser.Parse(
            MinimalHand(blinds: "[1, 2]", stacks: "[inf, inf]", actions: "'p2 cbr 500', 'p1 cc'"),
            "inf.phh")));

        Assert.Contains(hand.Events, e => e is HandFlaggedUnusual { Reason: UnusualReason.StacksUnknown });
        Assert.All(hand.Events.OfType<PlayerSeated>(), s => Assert.Null(s.StartingStack));
        Assert.Contains(new PlayerCalled("a", Street.Preflop, 498m, false), hand.Events);
    }

    [Fact]
    public void Out_of_turn_action_is_flagged_not_rejected()
    {
        // Heads-up with no small blind: in the source the big blind acted first (seen in Ongame data).
        var hand = Assert.IsType<ParsedHand>(Assert.Single(Parser.Parse(
            MinimalHand(blinds: "[0, 10]", stacks: "[855, 1400]", actions: "'p1 cc', 'p2 cbr 30', 'p1 f'"),
            "ooo.phh")));

        Assert.Contains(hand.Events, e => e is HandFlaggedUnusual { Reason: UnusualReason.ActionOutOfTurn });
        Assert.Contains(new PlayerChecked("a", Street.Preflop), hand.Events);
    }

    [Fact]
    public void Other_variants_are_skipped()
    {
        var text = MinimalHand(blinds: "[1, 2]", stacks: "[100, 100]", actions: "'p2 f'").Replace("'NT'", "'PO'");

        var skipped = Assert.IsType<SkippedHand>(Assert.Single(Parser.Parse(text, "plo.phh")));

        Assert.Contains("PO", skipped.Reason);
    }

    [Fact]
    public void A_bad_hand_fails_alone_and_the_rest_of_the_file_still_parses()
    {
        var good = MinimalHand(blinds: "[1, 2]", stacks: "[100, 100]", actions: "'p2 f'");
        var bad = MinimalHand(blinds: "[1, 2]", stacks: "[100, 100]", actions: "'p2 cbr 500'"); // more than the stack
        var file = $"[1]\n{good}\n[2]\n{bad}\n[3]\nthis is not toml ===\n[4]\n{good}";

        var results = Parser.Parse(file, "mixed.phhs").ToList();

        Assert.IsType<ParsedHand>(results[0]);
        Assert.Contains("but the player has 99 left", Assert.IsType<FailedHand>(results[1]).Error);
        Assert.StartsWith("Unreadable", Assert.IsType<FailedHand>(results[2]).Error);
        Assert.IsType<ParsedHand>(results[3]);
        Assert.Equal([1, 2, 3, 4], results.Select(r => r.Source.Section!.Value));
    }

    [Fact]
    public void Every_sample_hand_parses()
    {
        var results = Parser.Parse(RepoPaths.ReadSample(ExcerptFile), ExcerptFile).ToList();

        Assert.Equal(22, results.Count);
        Assert.All(results, r => Assert.IsType<ParsedHand>(r));
        Assert.Equal(22, results.Cast<ParsedHand>().Select(h => h.StreamKey).Distinct().Count());
    }

    private static string MinimalHand(string blinds, string stacks, string actions)
    {
        var count = stacks.Split(',').Length;
        var zeros = "[" + string.Join(", ", Enumerable.Repeat("0", count)) + "]";
        var players = "[" + string.Join(", ", Enumerable.Range(0, count).Select(i => $"'{(char)('a' + i)}'")) + "]";
        return $"""
            variant = 'NT'
            antes = {zeros}
            blinds_or_straddles = {blinds}
            min_bet = 2
            starting_stacks = {stacks}
            actions = [{actions}]
            players = {players}
            table = 't'
            """;
    }
}
