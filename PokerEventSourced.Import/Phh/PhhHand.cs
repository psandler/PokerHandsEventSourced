namespace PokerEventSourced.Import.Phh;

/// <summary>
/// The fields of one PHH hand that we use, as written in the file (before any interpretation).
/// Per-player arrays are in p1..pN order. See documents/phh-format.md.
/// </summary>
internal sealed record PhhHand(
    string Variant,
    IReadOnlyList<decimal> Antes,
    IReadOnlyList<decimal> BlindsOrStraddles,
    decimal? MinBet,
    IReadOnlyList<decimal?> StartingStacks, // null = "inf" (unknown)
    IReadOnlyList<string> Actions,
    IReadOnlyList<string>? Players,
    IReadOnlyList<int>? Seats,
    int? SeatCount,
    string? Venue,
    string? Hand,
    string? Table,
    int? Year,
    int? Month,
    int? Day,
    TimeSpan? Time,
    string? TimeZone,
    string? Currency,
    string? CurrencySymbol);
