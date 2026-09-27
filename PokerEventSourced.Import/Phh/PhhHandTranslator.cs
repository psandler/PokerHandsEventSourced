using System.Globalization;
using PokerEventSourced.Domain.Cards;
using PokerEventSourced.Domain.Events;

namespace PokerEventSourced.Import.Phh;

/// <summary>
/// Replays one PHH hand and turns it into domain events. PHH only records "check or call" (cc) and
/// "bet or raise to" (cbr), so this tracks the betting to work out checks vs calls, bets vs raises,
/// call amounts, all-ins and uncalled bets. Rules follow PokerKit (the PHH reference implementation);
/// see documents/phh-format.md.
/// </summary>
internal sealed class PhhHandTranslator
{
    private readonly PhhHand _hand;
    private readonly int _count;
    private readonly string[] _names;
    private readonly decimal?[] _stacks;       // remaining; null = unknown
    private readonly decimal[] _streetBets;    // put in on the current street
    private readonly decimal[] _contributed;   // put in over the whole hand
    private readonly bool[] _folded;
    private readonly bool[] _allIn;
    private readonly IReadOnlyList<Card>?[] _holeCards;
    private readonly bool[] _shown;
    private readonly bool[] _unrevealedAtShowdown;
    private readonly List<object> _events = [];
    private readonly bool _stacksKnown;
    private readonly decimal _bigBlind;

    private Street _street = Street.Preflop;
    private int _boardCount;
    private decimal _currentBet;
    private bool _roundClosed;
    private int? _nextToAct;

    private PhhHandTranslator(PhhHand hand)
    {
        _hand = hand;
        _count = hand.StartingStacks.Count;
        if (_count < 2)
        {
            throw new PhhHandException("A hand needs at least 2 players.");
        }

        RequireLength(hand.Antes.Count, "antes");
        RequireLength(hand.BlindsOrStraddles.Count, "blinds_or_straddles");
        if (hand.Players is not null) RequireLength(hand.Players.Count, "players");
        if (hand.Seats is not null) RequireLength(hand.Seats.Count, "seats");

        _names = Enumerable.Range(0, _count)
            .Select(i => hand.Players?[i] is { Length: > 0 } name ? name : $"(anonymous p{i + 1})")
            .ToArray();
        if (_names.Distinct().Count() != _count)
        {
            throw new PhhHandException("Two players have the same name.");
        }

        var raw = hand.BlindsOrStraddles;
        _bigBlind = raw.Count > 1 && raw[1] > 0 ? raw[1] : hand.MinBet ?? raw.Max();
        _stacks = hand.StartingStacks.ToArray();
        _stacksKnown = _stacks.All(s => s is not null);
        _streetBets = new decimal[_count];
        _contributed = new decimal[_count];
        _folded = new bool[_count];
        _allIn = new bool[_count];
        _holeCards = new IReadOnlyList<Card>?[_count];
        _shown = new bool[_count];
        _unrevealedAtShowdown = new bool[_count];
    }

    public static IReadOnlyList<object> Translate(PhhHand hand, HandSource source)
    {
        var translator = new PhhHandTranslator(hand);
        translator.Run(source);
        return translator._events;
    }

    private void Run(HandSource source)
    {
        StartHand(source);
        PostForcedBets();

        for (var i = 0; i < _hand.Actions.Count; i++)
        {
            var action = _hand.Actions[i];
            try
            {
                Apply(action);
            }
            catch (PhhHandException ex)
            {
                throw new PhhHandException($"Action {i + 1} '{action}': {ex.Message}");
            }
        }

        CloseBettingRound();
        for (var i = 0; i < _count; i++)
        {
            if (_unrevealedAtShowdown[i] && !_shown[i])
            {
                _events.Add(new CardsMucked(_names[i]));
            }
        }

        _events.Add(new HandCompleted(_contributed.Sum()));
    }

    private void StartHand(HandSource source)
    {
        var raw = _hand.BlindsOrStraddles;
        var smallBlind = raw[0] > 0 && raw[0] < _bigBlind ? raw[0] : (decimal?)null;

        _events.Add(new HandStarted(
            source,
            _hand.Venue,
            _hand.Hand,
            _hand.Table,
            _hand.SeatCount,
            smallBlind,
            _bigBlind,
            Currency(),
            StartedAtLocal(),
            _hand.TimeZone,
            _count));

        for (var i = 0; i < _count; i++)
        {
            _events.Add(new PlayerSeated(_names[i], i + 1, _hand.Seats?[i], _stacks[i], IsButton(i)));
        }

        if (!_stacksKnown)
        {
            _events.Add(new HandFlaggedUnusual(UnusualReason.StacksUnknown, "Starting stacks are not known, so all-ins can't be detected."));
        }

        if (_hand.Table is null)
        {
            _events.Add(new HandFlaggedUnusual(UnusualReason.MissingTableId, "The source has no table ID."));
        }
    }

    // Heads-up the order is reversed: p1 is the big blind, p2 the small blind + button.
    private bool IsButton(int player) => _count == 2 ? player == 1 : player == _count - 1;

    // Heads-up, PHH still lists blinds/antes as [SB, BB], and player i uses entry (1 - i).
    private int RawIndex(int player) => _count == 2 ? 1 - player : player;

    private void PostForcedBets()
    {
        for (var i = 0; i < _count; i++)
        {
            var ante = _hand.Antes[RawIndex(i)];
            if (ante > 0)
            {
                var amount = Pay(i, ante);
                _contributed[i] += amount; // antes are dead money: not part of the street's betting
                _events.Add(new AntePosted(_names[i], amount, _allIn[i]));
            }
        }

        var openerKey = (Amount: decimal.MinValue, Player: -1);
        for (var i = 0; i < _count; i++)
        {
            var rawIndex = RawIndex(i);
            var value = _hand.BlindsOrStraddles[rawIndex];
            if (value > 0)
            {
                // A big-blind-sized post in the small-blind slot happens when there's no small blind.
                var kind = rawIndex == 0 ? (value >= _bigBlind ? BlindKind.BigBlind : BlindKind.SmallBlind)
                    : rawIndex == 1 ? BlindKind.BigBlind
                    : BlindKind.Straddle;
                PostLive(i, kind, value);
            }
            else if (value < 0)
            {
                // Negative = a post by a new or returning player. PHH (PokerKit) treats it as a live bet,
                // and the actions that follow are written that way, so we do too. In real play a post that
                // isn't exactly one big blind may have been partly dead; PHH can't say, so flag the hand.
                var post = -value;
                PostLive(i, BlindKind.NewPlayerPost, post);
                if (post != _bigBlind)
                {
                    _events.Add(new HandFlaggedUnusual(
                        UnusualReason.OddSizedPost,
                        $"p{i + 1} posted {post} with a {_bigBlind} big blind. Counted as live; some of it may really have been dead."));
                }
            }

            // Preflop action starts after the biggest blind/straddle. New-player posts don't count
            // (PokerKit: key is bet * sign(blind), ties go to the later player).
            var key = _streetBets[i] * Math.Sign(value);
            if (key >= openerKey.Amount)
            {
                openerKey = (key, i);
            }
        }

        _currentBet = _streetBets.Max();
        _nextToAct = NextActive(openerKey.Player);
    }

    private void PostLive(int player, BlindKind kind, decimal value)
    {
        var amount = Pay(player, value);
        _streetBets[player] += amount;
        _contributed[player] += amount;
        _events.Add(new BlindPosted(_names[player], kind, amount, _allIn[player]));
    }

    private void Apply(string action)
    {
        var parts = action.Split('#', 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return; // comment only
        }

        if (parts[0] == "d")
        {
            ApplyDealer(parts);
            return;
        }

        var player = PlayerIndex(parts[0]);
        switch (parts.Length > 1 ? parts[1] : "")
        {
            case "f":
                BeginAction(player);
                _folded[player] = true;
                _events.Add(new PlayerFolded(_names[player], _street));
                EndAction(player);
                break;
            case "cc":
                BeginAction(player);
                CheckOrCall(player);
                EndAction(player);
                break;
            case "cbr" when parts.Length > 2:
                BeginAction(player);
                BetOrRaise(player, ParseAmount(parts[2]));
                EndAction(player);
                break;
            case "sm":
                ShowOrMuck(player, parts.Length > 2 ? parts[2] : "");
                break;
            default:
                throw new PhhHandException("Unsupported action for no-limit hold'em.");
        }
    }

    private void ApplyDealer(string[] parts)
    {
        switch (parts.Length > 1 ? parts[1] : "")
        {
            case "dh" when parts.Length > 3:
                var player = PlayerIndex(parts[2]);
                _holeCards[player] = ParseCards(parts[3]);
                _events.Add(new HoleCardsDealt(_names[player], _holeCards[player]));
                break;
            case "db" when parts.Length > 2:
                DealBoard(parts[2]);
                break;
            default:
                throw new PhhHandException("Unsupported dealer action.");
        }
    }

    private void DealBoard(string text)
    {
        var cards = ParseCards(text) ?? throw new PhhHandException("Board cards must be known.");
        CloseBettingRound();

        switch (_boardCount, cards.Count)
        {
            case (0, 3):
                _street = Street.Flop;
                _events.Add(new FlopDealt(cards));
                break;
            case (3, 1):
                _street = Street.Turn;
                _events.Add(new TurnDealt(cards[0]));
                break;
            case (4, 1):
                _street = Street.River;
                _events.Add(new RiverDealt(cards[0]));
                break;
            default:
                throw new PhhHandException($"Can't deal {cards.Count} board card(s) after {_boardCount}.");
        }

        _boardCount += cards.Count;
        Array.Clear(_streetBets);
        _currentBet = 0;
        _roundClosed = false;
        _nextToAct = NextActive(_count - 1); // after the flop, p1 (or the next player still in) acts first
    }

    private void CheckOrCall(int player)
    {
        var toCall = _currentBet - _streetBets[player];
        if (toCall <= 0)
        {
            _events.Add(new PlayerChecked(_names[player], _street));
            return;
        }

        var amount = Pay(player, toCall);
        _streetBets[player] += amount;
        _contributed[player] += amount;
        _events.Add(new PlayerCalled(_names[player], _street, amount, _allIn[player]));
    }

    private void BetOrRaise(int player, decimal to)
    {
        var add = to - _streetBets[player];
        if (to <= _currentBet || add <= 0)
        {
            throw new PhhHandException($"Bet/raise to {to} isn't above the current bet of {_currentBet}.");
        }

        if (_stacks[player] is { } stack && add > stack)
        {
            throw new PhhHandException($"Bet/raise needs {add} but the player has {stack} left.");
        }

        Pay(player, add);
        _streetBets[player] = to;
        _contributed[player] += add;

        if (_currentBet == 0)
        {
            _events.Add(new PlayerBet(_names[player], _street, to, _allIn[player]));
        }
        else
        {
            _events.Add(new PlayerRaised(_names[player], _street, to, to - _currentBet, _allIn[player]));
        }

        _currentBet = to;
    }

    private void ShowOrMuck(int player, string text)
    {
        CloseBettingRound();

        var cards = text == "-" ? _holeCards[player] : ParseCards(text);
        if (cards is null)
        {
            // PHH repeats "sm ????" on each street of an all-in run-out, then gives the real cards
            // at the end. Only players who never show get CardsMucked, once, at the end of the hand.
            _unrevealedAtShowdown[player] = true;
            return;
        }

        if (!_shown[player])
        {
            _shown[player] = true;
            _events.Add(new CardsShown(_names[player], cards));
        }
    }

    /// <summary>When the last bet or raise isn't fully called, the extra goes back to the bettor.</summary>
    private void CloseBettingRound()
    {
        if (_roundClosed)
        {
            return;
        }

        _roundClosed = true;
        var highest = _streetBets.Max();
        var leaders = Enumerable.Range(0, _count).Where(i => _streetBets[i] == highest).ToList();
        if (leaders.Count != 1 || highest == 0)
        {
            return;
        }

        var leader = leaders[0];
        var secondHighest = Enumerable.Range(0, _count).Where(i => i != leader).Max(i => _streetBets[i]);
        var returned = highest - secondHighest;
        _streetBets[leader] -= returned;
        _contributed[leader] -= returned;
        if (_stacks[leader] is { } stack)
        {
            _stacks[leader] = stack + returned;
            _allIn[leader] = false;
        }

        _events.Add(new UncalledBetReturned(_names[leader], returned));
    }

    private void BeginAction(int player)
    {
        if (_roundClosed)
        {
            throw new PhhHandException("Betting action after the betting round closed.");
        }

        if (_folded[player] || _allIn[player])
        {
            throw new PhhHandException("The player has already folded or is all-in.");
        }

        // With unknown stacks we can't tell who is all-in, so turn order can't be checked.
        // Out-of-turn actions are rare source quirks (e.g. heads-up with no small blind); keep the hand and flag it.
        if (_stacksKnown && _nextToAct != player)
        {
            var expected = _nextToAct is { } next ? $"p{next + 1}" : "nobody";
            _events.Add(new HandFlaggedUnusual(
                UnusualReason.ActionOutOfTurn,
                $"p{player + 1} acted on the {_street.ToString().ToLowerInvariant()} when {expected} was expected."));
        }
    }

    private void EndAction(int player) => _nextToAct = NextActive(player);

    /// <summary>The next player after <paramref name="after"/> who can still act (not folded, not all-in).</summary>
    private int? NextActive(int after)
    {
        for (var step = 1; step <= _count; step++)
        {
            var i = (after + step) % _count;
            if (!_folded[i] && !_allIn[i])
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Takes up to <paramref name="amount"/> from the player's stack and returns what was actually paid.</summary>
    private decimal Pay(int player, decimal amount)
    {
        if (_stacks[player] is not { } stack)
        {
            return amount;
        }

        var paid = Math.Min(amount, stack);
        _stacks[player] = stack - paid;
        _allIn[player] = _stacks[player] == 0;
        return paid;
    }

    private int PlayerIndex(string actor)
    {
        if (actor.Length > 1 && actor[0] == 'p'
            && int.TryParse(actor.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n >= 1 && n <= _count)
        {
            return n - 1;
        }

        throw new PhhHandException($"Unknown actor '{actor}'.");
    }

    private static decimal ParseAmount(string text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new PhhHandException($"'{text}' is not an amount.");

    private static IReadOnlyList<Card>? ParseCards(string text)
    {
        try
        {
            return Card.ParseMany(text);
        }
        catch (FormatException ex)
        {
            throw new PhhHandException(ex.Message);
        }
    }

    private void RequireLength(int length, string field)
    {
        if (length != _count)
        {
            throw new PhhHandException($"'{field}' has {length} entries but there are {_count} players.");
        }
    }

    private string? Currency() => _hand.Currency ?? _hand.CurrencySymbol switch
    {
        null => null,
        "$" => "USD",
        "€" => "EUR",
        "£" => "GBP",
        var symbol => symbol,
    };

    private DateTime? StartedAtLocal()
    {
        if (_hand is not { Year: { } y, Month: { } m, Day: { } d, Time: { } time })
        {
            return null;
        }

        try
        {
            return new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Unspecified) + time;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
