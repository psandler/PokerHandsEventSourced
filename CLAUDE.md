# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Hard rules

- **No git write operations of any kind. Only humans do git writes.** That covers `init`, `add`, `commit`, `branch`, `checkout`/`switch`, `merge`, `rebase`, `cherry-pick`, `stash`, `tag`, `reset`, `restore`, `rm`, `mv`, `push`, `pull`, `fetch`, `config`, and anything else that changes the repo, index, refs, or remotes. Read-only commands (`status`, `diff`, `log`, `show`, `blame`) are fine. When work is ready to commit, say so and suggest a commit message. Don't run the commit.
- **Never commit or print secrets.** Connection strings with passwords go in git-ignored local config, never in source.
- **Never drop or wipe a database you didn't create.** Test databases created by the test suite are fine to tear down; the user's dev database is not.

## Project

PokerEventSourced is a hobby desktop app that takes Texas Hold'em hand histories, stores them as **event streams** (event sourcing), and builds **projections** (read models) from them.

- Scope: **no-limit hold'em cash games** only. Table max (`seat_count`) is stored as a filter, not a rule.
- First format: **PHH** (Poker Hand History, TOML), data from the HandHQ part of the [phh-dataset](https://github.com/uoftcprg/phh-dataset). Read [documents/phh-format.md](documents/phh-format.md) before touching the PHH parser: it covers player ordering (heads-up is reversed), every action code, and quirks in the real data. One parser per format, behind an interface, so other formats can be added without touching the event model.
- **One stream per hand.** Players don't get streams; player info comes from projections. There's no session ID in PHH, so sessions are a projection over table ID + time.
- One event per table action (every deal, post, fold, check, call, bet, raise, show, muck).
- **Parsers produce domain events; they don't write to the store.** Import = parse → events → append. That keeps parsing testable without a database.
- Events are the source of truth. Projections must be rebuildable from events at any time. Never "fix" data by editing a projection. Fix the parser/projection and rebuild.
- Events are immutable once stored. Changing an event's shape needs an upcaster or a new event type, not an edit to the old class.
- Re-importing the same hand must not duplicate it (idempotent import keyed on the source's hand ID).

The work plan, decisions, and open questions are in [documents/TODO.md](documents/TODO.md). Keep it current: check off finished items and add new ones as they come up.

## Tech stack

- .NET 10 (`net10.0`), C# with nullable enabled
- Avalonia (Fluent theme), MVVM via CommunityToolkit.Mvvm
- **Polecat** (JasperFx "Critter Stack") for the event store and projections. It's a port of Marten to SQL Server, so Marten docs and patterns mostly apply, but check the Polecat docs (https://polecat.jasperfx.net/) before assuming an API exists. NuGet package: `Polecat`. Requires .NET 10 and **SQL Server 2025** (native JSON type).
- Database: the local SQL Server 2025 instance (`localhost`, default instance, Windows auth). Docker (`mcr.microsoft.com/mssql/server:2025-...`) is available as a fallback.
- xUnit v3 for tests.

## Commands

```bash
dotnet build PokerEventSourced.slnx
dotnet test --solution PokerEventSourced.slnx
dotnet run --project PokerEventSourced
```

- `global.json` opts `dotnet test` into Microsoft.Testing.Platform (required for xUnit v3 on the .NET 10 SDK), so use `--solution`/`--project`, not a positional path.
- Dataset checks (`PhhLocalDatasetTests`) are opt-in: `$env:POKER_DATASET_TESTS = "1"; dotnet test --solution PokerEventSourced.slnx`. They parse everything in `_phh-dataset-local` and cross-check amounts against Ongame finishing stacks. Run them after any change to the PHH parser; they must stay at 0 failures and 0 unexplained mismatches.
- Database tests use `TestSupport/TestDatabase`: a uniquely named throwaway database (`PokerEventSourced_Test_<guid>`) on the local instance (or `POKER_TEST_SQLSERVER`), dropped afterwards. They skip if SQL Server isn't reachable.
- If the app is running, builds fail because the exe is locked. Don't kill the user's app. Ask them to close it, or build to a scratch folder with `--artifacts-path`.
- Don't launch the GUI app without asking the user first. Verify with `dotnet build` and `dotnet test`.

## Layout and conventions

```
PokerEventSourced/          Avalonia app
  Views/                    .axaml windows/controls (+ minimal code-behind)
  ViewModels/               view models, derive from ViewModelBase
PokerEventSourced.Domain/   events, value types (Card, Seat, Chips), aggregates. No Polecat/Avalonia refs.
PokerEventSourced.Import/   hand-history parsers → domain events
PokerEventSourced.Store/    Polecat configuration, projections, queries
PokerEventSourced.Tests/    xUnit v3 tests
documents/                  planning docs (TODO.md)
samples/                    sample hand-history files for tests (no real personal data unless the user says it's fine)
scripts/                    helper scripts (download-handhq.ps1)
```

- Shared build settings (`net10.0`, nullable, implicit usings) are in `Directory.Build.props`.
- The project docs (`README.md`, `CLAUDE.md`, `documents/*.md`) are listed in the `docs` solution folder in `PokerEventSourced.slnx`. When you add or rename one, update the `.slnx` too. (`samples/phh/README.md` is only the dataset's license notice and stays out of it.)
- `PokerStore.Create` (Store project) builds the Polecat store with string stream keys. `PokerStore.EnsureDatabaseExistsAsync` creates the database; Polecat creates its own tables on first use.

- MVVM: views hold no logic, and view models don't reference Avalonia controls.
- Observable properties use the partial-property form: `[ObservableProperty] public partial string Name { get; set; }`. Commands use `[RelayCommand]`.
- Use compiled bindings: set `x:DataType` on every view.
- Events are `record` types, named in past tense (`HandStarted`, `PlayerFolded`), and carry only data (no behavior, no service references).
- Money: use `decimal` for chip/cash amounts, never `double`.
- Async all the way. No `.Result`/`.Wait()`. Pass `CancellationToken` through long operations.
- Tests: add or update tests with every behavior change, and run `dotnet test` before saying work is done. Parser and projection-logic tests should run without a database. Integration tests against Polecat use a throwaway database (unique name per run) and clean it up.

## Secrets and local data

- **HandHQ data files** live in `_phh-dataset-local/` in the repo root. It's git-ignored (~16 GB if you download everything). `scripts/download-handhq.ps1` fills it: no arguments = a sample (first 3 files of every site/stakes folder), `-All` = everything, `-Site PS -Stakes 50NL` filters, `-List` shows folders and what's downloaded. Re-runs skip what's already there. The layout matches the dataset: `handhq/<site-stakes>/<bb>/<file>.phhs`. They are read-only source data: never edit, move, or delete them. Each hand's first event records its dataset-relative file path + section number so the hand can always be re-read. Only the small excerpts in `samples/` are committed.

- Connection string: `appsettings.Local.json` (git-ignored, copied to build output). Commit an `appsettings.Local.example.json` template. Windows auth is preferred so there's no password at all.
