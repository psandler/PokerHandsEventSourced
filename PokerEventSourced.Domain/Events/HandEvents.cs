using PokerEventSourced.Domain.Cards;

namespace PokerEventSourced.Domain.Events;

// Every hand is one stream. Events are appended in table order:
// HandStarted, PlayerSeated (one per player, in action order), HandFlaggedUnusual (0+),
// AntePosted / BlindPosted, then the hand's actions, then HandCompleted.
// A HandFlaggedUnusual can also appear among the actions, where the oddity happened (e.g. out of turn).
// Players are identified by name (HandHQ names are obfuscated but stable across hands).

public enum Street
{
    Preflop,
    Flop,
    Turn,
    River,
}

public enum BlindKind
{
    SmallBlind,
    BigBlind,
    Straddle,

    /// <summary>A newly seated or returning player posting to play straight away. Counted as a live bet (the PHH rule).</summary>
    NewPlayerPost,
}

public enum UnusualReason
{
    /// <summary>Starting stacks aren't known (iPoker), so all-ins can't be detected.</summary>
    StacksUnknown,

    /// <summary>The source has no table ID, so the hand can't be placed in a session.</summary>
    MissingTableId,

    /// <summary>A player acted when the rules say someone else should have. Amounts are still tracked.</summary>
    ActionOutOfTurn,

    /// <summary>
    /// A new/returning player's post wasn't exactly one big blind. It's counted as live (the PHH rule),
    /// but in real play part of it was probably dead, so that player's amounts may be slightly off.
    /// </summary>
    OddSizedPost,
}

/// <summary>Where a hand came from, so it can always be re-read from the original file.</summary>
/// <param name="Format">Source format, e.g. "PHH".</param>
/// <param name="File">Dataset-relative path of the file.</param>
/// <param name="Section">Section number within a multi-hand file (the "[n]" header), or null for a single-hand file.</param>
public sealed record HandSource(string Format, string File, int? Section);

/// <param name="SmallBlind">The small blind posted this hand, or null when nobody posted one (e.g. the small-blind player left).</param>
/// <param name="BigBlind">The big blind (the stakes).</param>
/// <param name="TableMax">Seats at the table (6-max, 9-max...) when the source says. Null when unknown.</param>
/// <param name="StartedAtLocal">Local start time at the venue, when the source has a full date and time.</param>
/// <param name="TimeZone">IANA zone or an abbreviation such as "ET", as given by the source.</param>
public sealed record HandStarted(
    HandSource Source,
    string? Venue,
    string? SourceHandId,
    string? TableId,
    int? TableMax,
    decimal? SmallBlind,
    decimal BigBlind,
    string? Currency,
    DateTime? StartedAtLocal,
    string? TimeZone,
    int PlayerCount);

/// <param name="ActionOrder">1-based position in the deal order (p1..pN). With 3+ players p1 is the small blind and pN the button; heads-up p1 is the big blind and p2 the button.</param>
/// <param name="Seat">Seat number at the table, when known.</param>
/// <param name="StartingStack">Stack at the start of the hand, or null when unknown.</param>
public sealed record PlayerSeated(string Player, int ActionOrder, int? Seat, decimal? StartingStack, bool IsButton);

public sealed record HandFlaggedUnusual(UnusualReason Reason, string Detail);

public sealed record AntePosted(string Player, decimal Amount, bool IsAllIn);

public sealed record BlindPosted(string Player, BlindKind Kind, decimal Amount, bool IsAllIn);

/// <param name="Cards">The hole cards, or null when not known.</param>
public sealed record HoleCardsDealt(string Player, IReadOnlyList<Card>? Cards);

public sealed record PlayerFolded(string Player, Street Street);

public sealed record PlayerChecked(string Player, Street Street);

/// <param name="Amount">Chips added by this call. Less than the full amount when all-in.</param>
public sealed record PlayerCalled(string Player, Street Street, decimal Amount, bool IsAllIn);

/// <param name="Amount">The bet (first bet on a street after the flop).</param>
public sealed record PlayerBet(string Player, Street Street, decimal Amount, bool IsAllIn);

/// <param name="To">The player's total for the street after raising.</param>
/// <param name="By">How much the raise added on top of the previous highest bet.</param>
public sealed record PlayerRaised(string Player, Street Street, decimal To, decimal By, bool IsAllIn);

public sealed record FlopDealt(IReadOnlyList<Card> Cards);

public sealed record TurnDealt(Card Card);

public sealed record RiverDealt(Card Card);

public sealed record CardsShown(string Player, IReadOnlyList<Card> Cards);

/// <summary>At showdown the player's cards weren't revealed.</summary>
public sealed record CardsMucked(string Player);

/// <summary>The part of a bet nobody called goes back to the bettor.</summary>
public sealed record UncalledBetReturned(string Player, decimal Amount);

/// <param name="TotalPot">Everything put in (antes, blinds, bets) minus uncalled bets returned. Before rake.</param>
public sealed record HandCompleted(decimal TotalPot);
