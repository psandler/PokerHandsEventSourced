# PokerEventSourced

A hobby desktop app that imports Texas Hold'em hand histories, stores every hand as an **event stream**, and builds **projections** (player lists, stats like VPIP, sessions) from those events.

It's also a playground for event sourcing with [Polecat](https://polecat.jasperfx.net/), the SQL Server member of the JasperFx "Critter Stack" (Marten, Wolverine).

## Data

Hands come from the HandHQ part of the [phh-dataset](https://github.com/uoftcprg/phh-dataset): ~21.6 million anonymised no-limit hold'em cash hands from six online sites (July 2009), in the [PHH format](https://phh.readthedocs.io/). See [documents/phh-format.md](documents/phh-format.md) for how the format maps to our events and what's odd about the real data.

The data isn't in the repo. Download it into the git-ignored `_phh-dataset-local/` folder:

```powershell
./scripts/download-handhq.ps1            # sample: first 3 files of every site/stakes folder (~55 MB)
./scripts/download-handhq.ps1 -List      # folders, sizes, what's downloaded
./scripts/download-handhq.ps1 -All       # everything (~16 GB)
./scripts/download-handhq.ps1 -Site PS -Stakes 50NL -All
```

## Requirements

- .NET 10 SDK
- SQL Server 2025 (Polecat needs its native JSON type). A local default instance with Windows auth works out of the box.
- PowerShell 7 for the download script

## Getting started

```powershell
dotnet build PokerEventSourced.slnx
dotnet test --solution PokerEventSourced.slnx
dotnet run --project PokerEventSourced
```

For the app, copy `PokerEventSourced/appsettings.Local.example.json` to `appsettings.Local.json` and adjust the connection string if needed. The database is created automatically.

Database tests create a throwaway database on the local instance (or the server in `POKER_TEST_SQLSERVER`) and drop it afterwards. They're skipped if SQL Server isn't reachable.

## Layout

| Folder | What |
|---|---|
| `PokerEventSourced/` | Avalonia desktop app (MVVM) |
| `PokerEventSourced.Domain/` | Events and value types |
| `PokerEventSourced.Import/` | Hand-history parsers (files → events) |
| `PokerEventSourced.Store/` | Polecat setup, projections, queries |
| `PokerEventSourced.Tests/` | xUnit v3 tests |
| `documents/` | Plan ([TODO.md](documents/TODO.md)) and format notes |
| `samples/` | Small hand-history excerpts used by tests |
| `scripts/` | Data download script |

## License

MIT. See [LICENSE](LICENSE). Sample hands in `samples/phh/` come from the phh-dataset (MIT); its notice is in [samples/phh/README.md](samples/phh/README.md).
