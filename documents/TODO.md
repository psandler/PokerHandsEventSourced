# TODO

## Decisions

- **Format:** start with **PHH** ([phh-format.md](phh-format.md)). Our own event model, one parser per format.
- **Data:** HandHQ cash hands from the phh-dataset (NLHE, 2009, player IDs obfuscated but stable across hands).
- **Scope:** cash games, no-limit hold'em only. Store table max (`seat_count`) as a filter/category, not a rule. A 6-max table can have 4 players.
- **Hero:** none for now. All players are equal. Might matter later for personal hands.
- **Streams:** one stream per hand. **Players don't get streams.** Player info comes from projections.
- **Session:** PHH has no session ID. `HandStarted` carries the table ID, and sessions are derived in a projection (same table, time gaps).
- **Events:** one event per table action, down to every action (see [phh-format.md](phh-format.md)).
- **Database:** local SQL Server 2025, Windows auth.
- **App:** desktop only.
- **First projection:** player list + VPIP. VPIP = hands where the player called or raised preflop ÷ hands dealt. Posting blinds doesn't count, a BB checking their option doesn't count, walks count as dealt but not VPIP.
- **Winners/pots:** not in the first cut (showdowns need a hand evaluator). When we add them, wipe and re-import.
- **Source files are kept, not copied into events.** The HandHQ files are from 2009 and never change. Each hand's first event records where it came from (dataset-relative file path + section number, e.g. `handhq/PS-.../0.5/ps NLH handhq_1-OBFUSCATED.phhs#[1]`), so any hand can be re-read from the file.
- **Data download:** `scripts/download-handhq.ps1` into the git-ignored `_phh-dataset-local/` folder in the repo root.
- **Unusual hands:** anything odd (negative blind values, missing table ID, etc.) is imported anyway and flagged with a reason. "Unusual hand" is a filter.

## Open questions (waiting on the user)

1. **How much to import at first.** One file is 1,000 hands ≈ 25,000 events. All of PS 50NL is ~1.2M hands ≈ 30M events. Proposal: start with a handful of files, measure import speed, then decide.

## Human tasks

- [ ] `git init` in `C:\projects\PokerEventSourced`, first commit
- [ ] Create the GitHub repo and push
- [x] Choose a license: MIT (`LICENSE`; dataset attribution in `samples/phh/README.md`)
- [ ] Confirm the local SQL Server login can create a database (or create an empty `PokerEventSourced` database)
- [ ] Review and commit after each chunk of work (Claude suggests commit messages)

## Setup

- [x] CLAUDE.md
- [x] TODO.md
- [x] .gitignore
- [x] PHH format research ([phh-format.md](phh-format.md)) and samples ([samples/phh/](../samples/phh/))
- [x] Download script (`scripts/download-handhq.ps1`): sample (3 files per site/stakes folder) by default, `-All` for everything, `-Site`/`-Stakes` filters, parallel, resumable
- [x] Sample downloaded (81 files + 47 more PS 50NL, ~127,700 hands) and surveyed per site ([phh-format.md](phh-format.md#quirks-by-site-sample-first-3-files-of-every-sitestakes-folder-127700-hands))
- [ ] Full download (`-All`, 16.3 GB) once the parser handles the sample cleanly
- [ ] Scaffold solution (`.slnx`, `global.json`, Avalonia app, domain/import/store/test projects)
- [ ] Polecat wired up against local SQL Server 2025. Smoke test appends and reads an event. Check string stream keys work.
- [ ] `appsettings.Local.example.json` template

## Domain / events

- [ ] Value types: `Card`, `Rank`, `Suit`, `Street`, `BlindKind`, chip amounts (`decimal`)
- [ ] Hand events:
  - `HandStarted` (source format, source file + section, venue, source hand ID, table ID, table max, stakes, currency, local start time, time zone abbreviation, player count)
  - `PlayerSeated` (player name, seat number, action order p1..pN, starting stack, is button)
  - `AntePosted`, `BlindPosted` (SB / BB / straddle)
  - `HoleCardsDealt` (cards or unknown)
  - `PlayerFolded`, `PlayerChecked`, `PlayerCalled` (amount, all-in), `PlayerBet` (amount, all-in), `PlayerRaised` (to, by, all-in)
  - `FlopDealt`, `TurnDealt`, `RiverDealt`
  - `CardsShown`, `CardsMucked`
  - `UncalledBetReturned` (derived)
  - `PotAwarded` (derived, later: needs a hand evaluator)
  - `HandFlaggedUnusual` (reason), zero or more per hand
  - `HandCompleted`
- [ ] Stream key: `phh:{venue}:{hand}`. Fallback for files without venue/hand (famous hands): hash of the hand text.

## Import (PHH)

- [ ] TOML parsing (Tomlyn), `.phh` and `.phhs`
- [ ] Table engine: check vs call, bet vs raise, call amounts, all-ins, heads-up ordering, uncalled bets
- [ ] Parser tests from `samples/phh/` (Dwan/Ivey worked example, heads-up hand, run-out hand 42, straddle hand 83)
- [ ] Add sample hands from the other sites to `samples/phh/`: ABS with antes, iPoker with `inf` stacks and known hole cards, Ongame with `finishing_stacks`
- [ ] iPoker: stacks unknown (`inf`), so no all-in detection. Import anyway and flag.
- [ ] Table max: use `seat_count` when present, otherwise infer from the table's highest seat number / player count across hands (mark as inferred)
- [ ] Work out what negative blind values mean (check PokerKit source); 5 of 6 sites use them
- [ ] Parse the whole sample (~127,700 hands) with zero unexplained errors
- [ ] Skip non-`NT` variants with a reason. Report per-hand errors without failing the whole file.
- [ ] Idempotent import (skip hands already stored)

## Projections

- [ ] Players + VPIP (first one)
- [ ] Filters: table max, stakes, site, unusual hands
- [ ] Hand summary: players, board, pot, winner
- [ ] Sessions (table + time gaps)
- [ ] Rebuild-projections command in the UI

## UI

- [ ] Main window: import button, hand count, player list with VPIP
- [ ] Hand detail / replayer
