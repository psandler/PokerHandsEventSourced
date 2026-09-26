# PHH format: how to read it, action by action

Reference for the PHH parser. Sources: the [PHH spec](https://phh.readthedocs.io/en/stable/) and a real file from the [phh-dataset](https://github.com/uoftcprg/phh-dataset) (`data/handhq/PS-2009-07-01_2009-07-23_50NLH_OBFU/0.5/ps NLH handhq_1-OBFUSCATED.phhs`: 1,000 PokerStars $0.25/$0.50 hands). The findings under "Real-data quirks" come from analysing that file.

Samples in this repo (MIT, from phh-dataset): [samples/phh/](../samples/phh/).

## File shape

- **TOML.** `.phh` = one hand per file. `.phhs` = many hands, each under a numbered table header (`[1]`, `[2]`, ...).
- Numbers can be integers or floats (`0.50`, `2`, `1125600`). Parse them all to `decimal`. `inf` means an unknown stack.
- `#` comments can appear anywhere, including after array items. A TOML library handles this (candidate: **Tomlyn** on NuGet).

## Players and positions

- Players are **p1..pN** (1-indexed). Every per-player array (`starting_stacks`, `antes`, `blinds_or_straddles`, `players`, `seats`, `winnings`) is in the same p1..pN order.
- **3+ players:** p1 = small blind, p2 = big blind, p3 = first to act preflop (UTG), ..., **pN = button**.
- **Heads-up (2 players):** the order is reversed. **p1 = big blind, p2 = small blind + button.** The arrays still read `[SB, BB]` (e.g. `[0.25, 0.50]`), and the engine applies them reversed. Checked against real hands: p2 acts first preflop, p1 acts first after the flop.
- `seats` = the real seat numbers (e.g. `[4, 5, 6, 8, 9, 1, 3]` → SB in seat 4, button in seat 3). `seat_count` = table max (2 / 6 / 9 in the sample). This is our "table max" filter.
- `players` = names. In HandHQ they're obfuscated IDs (`'s83dnhZ6VC53vY0/f6OV/g'`), but **the same player keeps the same ID across hands**, so player projections work.

## Header fields → what we keep

| PHH field | Meaning | Use |
|---|---|---|
| `variant` | `'NT'` = no-limit Texas hold'em | Import only `NT` for now; skip other variants with a reason |
| `antes` | ante per player | `AntePosted` for each non-zero |
| `blinds_or_straddles` | index 0 = SB, 1 = BB, 2+ = straddles | `BlindPosted` (kind SmallBlind/BigBlind/Straddle) |
| `min_bet` | minimum bet (= BB) | stakes |
| `starting_stacks` | stack per player at hand start | `PlayerSeated` |
| `actions` | the hand, in order (see below) | one event per action |
| `venue` | site (`'PokerStars'`) | stream key, filter |
| `hand` | site hand number | stream key |
| `table` | table ID | session projection |
| `seat_count` | table max | filter |
| `seats` | seat numbers | `PlayerSeated` |
| `players` | names | `PlayerSeated` |
| `year`/`month`/`day`/`time`/`time_zone_abbreviation` | local start time (`'ET'` in HandHQ) | `HandStarted.StartedAt` |
| `currency` / `currency_symbol` | `USD` / `$` | stakes |
| `winnings` | pot collected per player, after rake | **Unreliable, see quirks.** Keep as info only |
| `finishing_stacks` | stack at hand end | not present in HandHQ |
| `event`, `author`, `url` | used by the famous-hand files | info |

## Action codes (every one)

An action string is `<actor> <code> [argument] [# comment]`. The actor is `d` (dealer) or `pN`.

| Code | Example | Meaning | Event(s) |
|---|---|---|---|
| `d dh pN <cards>` | `d dh p3 7h6h` / `d dh p1 ????` | deal hole cards to pN. `????` = unknown | `HoleCardsDealt` (cards may be unknown) |
| `d db <cards>` | `d db Jc3d5c`, `d db 4h` | deal board cards. 3 cards = flop, then 1 = turn, 1 = river | `FlopDealt` / `TurnDealt` / `RiverDealt` |
| `pN f` | `p2 f` | fold | `PlayerFolded` |
| `pN cc` | `p3 cc` | **check or call.** Check if pN already matches the current bet, otherwise call. A call for less than the full amount = all-in call | `PlayerChecked` / `PlayerCalled(amount, allIn)` |
| `pN cbr <to>` | `p1 cbr 23000` | **bet or raise, TO a total for the street** (not "by"). Bet if nobody has bet on this street, otherwise raise. Preflop the big blind counts as a bet, so every preflop `cbr` is a raise | `PlayerBet(to, allIn)` / `PlayerRaised(to, by, allIn)` |
| `pN sm <cards>` | `p1 sm Ac2d` | show cards at showdown | `CardsShown` |
| `pN sm ????` | `p3 sm ????` | at showdown, cards not revealed (mucked / unknown) | `CardsMucked` |
| `pN sm -` | | (spec) show previously dealt cards | `CardsShown` using the dealt cards |
| `pN sd ...` | | stand pat / discard: **draw games only** | reject for `NT` |
| `pN pb` | | bring-in: **stud only** | reject for `NT` |
| `# text` | | comment / no-op | ignore |

Cards: rank `23456789TJQKA`, suit `cdhs`, two characters each, concatenated (`Jc3d5c`). `?` = unknown rank or suit.

### The parser has to run a small table engine

PHH records *what* the player did (`cc`, `cbr`) but not check-vs-call, bet-vs-raise, amounts called, all-ins, or who won. To turn that into proper events, the parser tracks per street: the current bet level, what each player has put in this street, stacks, and who has folded or is all-in. Blinds count toward the preflop amount put in. Antes are dead money and don't.

Things PHH **doesn't record** that we derive at the end of the hand:

- **Uncalled bet returned.** When the last bet or raise isn't fully called, the extra goes back (Dwan/Ivey example below).
- **Pots, side pots and winners.** If only one player is left, they win without a showdown. At a showdown the winner needs a **hand evaluator** (best 5 of 7), because `winnings` is usually missing there (see quirks).
- **Rake** is not given. `winnings`, when present, is after rake.

## Worked example: Dwan vs Ivey, 2009 ([samples/phh/dwan-ivey-2009.phh](../samples/phh/dwan-ivey-2009.phh))

`antes [500, 500, 500]`, `blinds [1000, 2000, 0]`, stacks `[1,125,600, 2,000,000, 553,500]`. p1 = Ivey (SB), p2 = Antonius (BB), p3 = Dwan (button).

| # | PHH | What happened | Event |
|---|---|---|---|
| | header | 3 players seated | `HandStarted`, 3 × `PlayerSeated` |
| | `antes` | each posts a 500 ante | 3 × `AntePosted(500)` |
| | `blinds` | Ivey posts SB 1,000, Antonius posts BB 2,000 | `BlindPosted(SB)`, `BlindPosted(BB)` |
| 1 | `d dh p1 Ac2d` | Ivey dealt A♣2♦ | `HoleCardsDealt(Ivey, Ac2d)` |
| 2 | `d dh p2 ????` | Antonius dealt unknown | `HoleCardsDealt(Antonius, unknown)` |
| 3 | `d dh p3 7h6h` | Dwan dealt 7♥6♥ | `HoleCardsDealt(Dwan, 7h6h)` |
| 4 | `p3 cbr 7000` | Dwan raises to 7,000 | `PlayerRaised(Dwan, to 7000)` |
| 5 | `p1 cbr 23000` | Ivey re-raises to 23,000 | `PlayerRaised(Ivey, to 23000)` |
| 6 | `p2 f` | Antonius folds | `PlayerFolded(Antonius)` |
| 7 | `p3 cc` | Dwan calls 16,000 | `PlayerCalled(Dwan, 16000)` |
| 8 | `d db Jc3d5c` | flop J♣3♦5♣ | `FlopDealt` |
| 9 | `p1 cbr 35000` | Ivey bets 35,000 (first bet on the street) | `PlayerBet(Ivey, 35000)` |
| 10 | `p3 cc` | Dwan calls 35,000 | `PlayerCalled(Dwan, 35000)` |
| 11 | `d db 4h` | turn 4♥ | `TurnDealt` |
| 12 | `p1 cbr 90000` | Ivey bets 90,000 | `PlayerBet` |
| 13 | `p3 cbr 232600` | Dwan raises to 232,600 | `PlayerRaised` |
| 14 | `p1 cbr 1067100` | Ivey raises to 1,067,100: **all-in** (his whole remaining stack) | `PlayerRaised(allIn)` |
| 15 | `p3 cc` | Dwan calls, but has only 495,000 left: **all-in for less** | `PlayerCalled(Dwan, 262400, allIn)` |
| | (derived) | Ivey's uncalled 572,100 returned | `UncalledBetReturned(Ivey, 572100)` |
| 16–17 | `p1 sm Ac2d`, `p3 sm 7h6h` | both show (all-in, so before the river) | 2 × `CardsShown` |
| 18 | `d db Jh` | river J♥ | `RiverDealt` |
| | (derived) | Dwan's 7-high straight beats Ivey's 5-high straight. Dwan wins the 1,109,500 pot | `PotAwarded(Dwan, 1109500)` |
| | (derived) | | `HandCompleted` |

VPIP from this hand: Ivey yes (raised), Dwan yes (raised), Antonius no (only posted the BB, then folded).

## Real-data quirks (HandHQ PokerStars 50NL, 1,000 hands)

- **Hole cards are always `????` when dealt.** Cards only appear through `sm` at showdown.
- **`sm` can repeat.** In all-in run-outs the file shows `pN sm ????` for each player on each street, then the real cards at the end. Use the last known value.
- **Board cards can come after `sm`** when players are all-in before the river.
- **`winnings` is unreliable.** It's missing in 178 of the 183 showdown hands, and sometimes all zeros when someone clearly won (hand 1: button raises, blinds fold). We compute results ourselves and treat `winnings` as info only.
- **`table` and `seat_count` are missing** in 15 of 1,000 hands.
- **Negative value in `blinds_or_straddles`** (hand 83: `[0.25, 0.5, 0, 0, 0, 0, 0, -0.5, 0]`). The spec says non-negative. Probably a dead/late post by a new player. Needs checking against PokerKit (the reference implementation) before we decide the meaning. Until then, import it as a flagged post.
- 13 hands have more than two forced posts (straddles or posts).
- No antes in this file.
- **Lots of heads-up:** 536 of 1,000 hands are heads-up (`seat_count` 2). The rest is 6-max and 9-max with 4–9 players.
- **No session ID.** Sessions can be derived from `table` plus players and time gaps (one HU pair played 86 hands in 18 minutes on the same table).
- `time` is local time without a date. Combine it with `year`/`month`/`day`. The time zone is only an abbreviation (`'ET'`).
- Player IDs: 211 distinct players in 1,000 hands.

## Quirks by site (sample: first 3 files of every site/stakes folder, ~127,700 hands)

All files parsed as TOML, all hands are `NT`, and only the action codes `d dh`, `d db`, `f`, `cc`, `cbr`, `sm` appear. The sites differ in which fields they fill:

| Site | Hands | `seat_count` (table max) | `winnings` at showdown | Negative blinds | Other |
|---|---|---|---|---|---|
| PS (PokerStars) | 64,982 | present (2/6/9), missing in 881 | rarely (333 of 11,453) | 863 | `table` missing in 881 |
| FTP (Full Tilt) | 8,980 | only 2,467 hands (6) | 440 of 1,131 | 0 | |
| ABS (Absolute) | 17,967 | **never** | 139 of 3,376 | 1 | **antes in 3,883 hands** |
| IPN (iPoker) | 14,797 | **never** | always | 292 | **every starting stack is `inf` (unknown)**. 3,165 hole-card deals are known (not `????`). Has `currency` |
| ONG (Ongame) | 8,992 | **never** | always | 247 | has `finishing_stacks` |
| PTY (Party) | 11,968 | **never** | always | 227 | has `currency` |

What this means for us:

- **Table max can't come from `seat_count` for most sites.** Fallback: infer it from the highest seat number or largest player count seen at that `table` across hands, and mark it as inferred.
- **iPoker has no stack sizes**, so we can't tell when a player is all-in or cap a call at the stack. Import with stacks unknown and all-in flags left empty, and flag the hand.
- **Antes appear in cash games** (Absolute). `AntePosted` is needed from day one.
- Negative blinds show up on 5 of 6 sites, so it's a converter convention, not a one-off. Worth understanding properly (check PokerKit's source).
- Where `winnings` is present at showdown (iPoker, Ongame, Party), it can be used to check our own pot calculation later.

## What else is in the dataset

- **HandHQ:** ~21.6M anonymised NLHE **cash** hands, July 2009, from 6 sites (PokerStars, Full Tilt, Party, Absolute, iPoker, Ongame), 25NL to 1000NL. This is the one we want. On GitHub it's split into 27 site/stakes folders, 1,000 hands per `.phhs` file (~700 KB each), 21,782 files, 16.3 GB. `scripts/download-handhq.ps1` downloads them (sample by default, `-All` for everything).
- **Pluribus:** 10,000 6-max hands (AI vs pros, fixed player names).
- **WSOP 2023 event #43:** tournament. Skip.
- **ACPC:** hundreds of millions of bot-vs-bot heads-up hands. Probably skip.
- Famous hands (single `.phh` files).
- The full dataset is also on [Zenodo](https://zenodo.org/records/17136841).
