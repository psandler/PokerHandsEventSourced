using PokerEventSourced.Domain.Events;

namespace PokerEventSourced.Import;

/// <summary>
/// Turns one hand-history file into hands of domain events. One implementation per source format.
/// Parsers never touch the store: the importer appends what they return.
/// </summary>
public interface IHandHistoryParser
{
    /// <summary>File extensions this parser reads, e.g. ".phh".</summary>
    IReadOnlyCollection<string> FileExtensions { get; }

    /// <summary>
    /// Parses every hand in <paramref name="content"/>. A bad hand is returned as a
    /// <see cref="FailedHand"/>; it never stops the rest of the file from parsing.
    /// </summary>
    /// <param name="sourceFile">Dataset-relative path, recorded in each hand's <see cref="HandSource"/>.</param>
    IEnumerable<HandParseResult> Parse(string content, string sourceFile);
}

public abstract record HandParseResult(HandSource Source);

/// <param name="StreamKey">The hand's stream key, unique per hand (e.g. "phh:PokerStars:59937793578").</param>
public sealed record ParsedHand(HandSource Source, string StreamKey, IReadOnlyList<object> Events)
    : HandParseResult(Source);

/// <summary>A valid hand we deliberately don't import (e.g. not no-limit hold'em).</summary>
public sealed record SkippedHand(HandSource Source, string Reason) : HandParseResult(Source);

/// <summary>A hand that couldn't be read or didn't make sense.</summary>
public sealed record FailedHand(HandSource Source, string Error) : HandParseResult(Source);
