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
- **Unusual hands:** anything odd is imported anyway and flagged with a `HandFlaggedUnusual` reason: `StacksUnknown`, `MissingTableId`, `ActionOutOfTurn`, `OddSizedPost`. "Unusual hand" is a filter.

## Open questions (waiting on the user)

1. **How much to import at first.** One file is 1,000 hands ≈ 25,000 events. All of PS 50NL is ~1.2M hands ≈ 30M events. Proposal: start with a handful of files, measure import speed, then decide.

## Human tasks

- [x] `git init` in `C:\projects\PokerEventSourced`, first commit
- [x] Create the GitHub repo and push
- [x] Choose a license: MIT (`LICENSE`; dataset attribution in `samples/phh/README.md`)
- [x] Confirm the local SQL Server login can create a database (the smoke test creates and drops one)
- [ ] Copy `PokerEventSourced/appsettings.Local.example.json` to `appsettings.Local.json` (only needed once the app talks to the database)
- [ ] Review and commit after each chunk of work (Claude suggests commit messages)

## Setup

- [x] CLAUDE.md
- [x] TODO.md
- [x] .gitignore
- [x] PHH format research ([phh-format.md](phh-format.md)) and samples ([samples/phh/](../samples/phh/))
- [x] Download script (`scripts/download-handhq.ps1`): sample (3 files per site/stakes folder) by default, `-All` for everything, `-Site`/`-Stakes` filters, parallel, resumable
- [x] Sample downloaded (81 files + 47 more PS 50NL, ~127,700 hands) and surveyed per site ([phh-format.md](phh-format.md#quirks-by-site-sample-first-3-files-of-every-sitestakes-folder-127700-hands))
- [ ] Full download (`-All`, 16.3 GB): the parser handles the sample cleanly now; wait until import speed is measured
- [x] Scaffold solution (`.slnx` with a `docs` folder for the .md files, `global.json`, `Directory.Build.props`, Avalonia app shell, Domain/Import/Store/Tests projects)
- [x] Polecat wired up against local SQL Server 2025. Smoke test appends and reads a string-keyed stream in a throwaway database.
- [x] `appsettings.Local.example.json` template
- [ ] App reads the connection string from `appsettings.Local.json` and creates the database on startup

## Domain / events

- [x] Value types: `Card` (JSON as "Ah"), `Rank`, `Suit`, `Street`, `BlindKind`, `UnusualReason`; amounts are `decimal`
- [x] Hand events ([HandEvents.cs](../PokerEventSourced.Domain/Events/HandEvents.cs)): `HandStarted`, `PlayerSeated`, `HandFlaggedUnusual`, `AntePosted`, `BlindPosted`, `HoleCardsDealt`, `PlayerFolded`/`Checked`/`Called`/`Bet`/`Raised`, `FlopDealt`/`TurnDealt`/`RiverDealt`, `CardsShown`, `CardsMucked`, `UncalledBetReturned`, `HandCompleted` (total pot)
- [ ] `PotAwarded` (later: needs a hand evaluator; then wipe and re-import)
- [x] Stream key: `phh:{venue}:{hand}`; famous hands without venue/hand get `phh:sha256:<hash of the hand text>`
- [x] Events round-trip through Polecat (test)

## Import (PHH)

- [x] TOML parsing (Tomlyn), `.phh` and `.phhs`; each `[n]` section parsed on its own
- [x] Table engine: check vs call, bet vs raise, call amounts, all-ins, heads-up ordering, uncalled bets, turn-order check
- [x] Parser tests from `samples/phh/` (Dwan/Ivey event by event, heads-up, run-out, new-player post, muck) plus small inline hands for edge cases
- [x] Negative blinds = PokerKit "post bet", counted live. Posts that aren't exactly 1 BB are flagged `OddSizedPost` (see [phh-format.md](phh-format.md#negative-posts))
- [x] iPoker unknown stacks: imported, flagged `StacksUnknown`, no all-in detection or turn-order check
- [x] Missing small blind / heads-up out-of-turn quirks handled and flagged
- [x] Whole sample parses (127,686 hands, 0 failures) and amounts match Ongame finishing stacks, all mismatches explained (`POKER_DATASET_TESTS=1`)
- [x] Skip non-`NT` variants with a reason; per-hand errors don't fail the file
- [ ] Add sample hands from the other sites to `samples/phh/` so the default test run covers them: ABS with antes, iPoker with `inf` stacks, Ongame no-SB hand
- [ ] Table max: use `seat_count` when present, otherwise infer from the table's highest seat number / player count across hands (mark as inferred). Probably a projection.
- [ ] Importer: read files from `_phh-dataset-local`, append each `ParsedHand` as a new stream, skip hands already stored (idempotent), report failures
- [ ] Measure import speed on a few files (open question 1)

## Projections

- [ ] Players + VPIP (first one)
- [ ] Filters: table max, stakes, site, unusual hands
- [ ] Hand summary: players, board, pot, winner
- [ ] Sessions (table + time gaps)
- [ ] Rebuild-projections command in the UI

## UI

- [ ] Main window: import button, hand count, player list with VPIP
- [ ] Hand detail / replayer
