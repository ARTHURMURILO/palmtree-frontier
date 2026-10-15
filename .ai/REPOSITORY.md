# REPOSITORY — Inventory

> Source of truth is the code. Paths are relative to the repository root unless prefixed with `RobustToolbox/`.
> Facts gathered from the working tree on `master` (Frontier master `df24c19f08` + port commits),
> engine submodule `RobustToolbox` @ v267.3.0.

## What this repository is

**Palmtree Station** is a Space Station 14 game codebase — a Frontier Station fork with Palmtree
content and a ported Coyote/Floof ERP marking layer:

```
Space Station 14 (Wizden / space-wizards)
    └── Frontier Station (new-frontiers-14, prefix _NF)
            └── Palmtree Station (prefix _PS; branch master)
                    └── ported: _Floof (markings/undies), _CS (anthro, bayou, undergarments),
                                _DEN (clicker/foot protectors/ovinia), _EE (tajaran),
                                _White (eastern dragon), _Starlight (resomi)
```

It runs on the **RobustToolbox** engine, vendored as a git submodule. The engine is explicitly
**not to be modified** by content maintainers.

## Quick facts

| Item | Value |
|---|---|
| Primary language | C# 12 (nullable enabled) |
| Runtime | .NET 9 (`global.json` → SDK `9.0.100`, rollForward `latestFeature`) |
| Engine | RobustToolbox v267.3.0, submodule at `RobustToolbox/` |
| Build system | MSBuild via `dotnet`, solution `SpaceStation14.sln` |
| Configurations | `Debug`, `DebugOpt`, `Release`, `Tools` |
| Content C# files (excl. obj/bin) | ~8,276 (recount: `find Content* -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' \| wc -l`) |
| Prototype YAML files | ~3,968 under `Resources/Prototypes` |
| Tests | ~509 `[Test]` methods (222 files) |
| Fork prototype prefixes | `_AS`, `_CD`, `_CS`, `_DEN`, `_DV`, `_EE`, `_EstacaoPirata`, `_Floof`, `_Funkystation`, `_Goobstation`, `_HL`, `_Impstation`, `_Mono`, `_NF`, `_PS`, `_RMC14`, `_Starlight`, `_StarLight`, `_White` |
| Fork code prefixes | `_Corvax`, `_CS`, `_DV`, `_Emberfall`, `_EstacaoPirata`, `_Goobstation`, `_Harmony`, `_Mono`, `_NC`, `_NF`, `_RMC14`, `_PS`, `_Floof`, `_EE`, `_CD` |
| Working branch | `master` (port commits listed in `.ai/PORTING.md`; `ps-erp` merged and deleted) |

## Top-level layout

| Path | Contents |
|---|---|
| `Content.Shared/` | Components, shared systems, prototypes, network messages, data fields; referenced by both sides |
| `Content.Server/` | Authoritative game logic, managers, game ticker, DB glue, admin |
| `Content.Client/` | Client systems, UI states, UI controllers, overlays, input, prediction |
| `Content.Server.Database/` | EF Core DbContexts and Migrations (SQLite + Postgres) |
| `Content.Shared.Database/` | Shared DB enums (`LogType`, `LogImpact`, `NoteType`, `NoteSeverity`, `TypedHwid`) |
| `Content.IntegrationTests/` | NUnit integration tests with pooled server+client instances |
| `Content.Tests/` | NUnit unit tests |
| `Content.Benchmarks/` | BenchmarkDotNet benchmarks |
| `Content.MapRenderer/` | Renders maps to images / viewer JSON |
| `Content.Packaging/` | Builds distributable client/server zips |
| `Content.YAMLLinter/` | Prototype/YAML static validation (CI gate) |
| `Content.Tools/` | Map merge driver |
| `Content.PatreonParser/` | CSV → `Resources/Credits/Patrons.yml` |
| `Content.Docfx/` | DocFX config (not in solution; weekly CI) |
| `Content.Replay/` | Replay playback client executable |
| `BuildChecker/` | Git hooks / submodule bootstrap helper used by `RUN_THIS.py` |
| `Pow3r/` | Standalone power-net simulation/debug GUI (not shipped) |
| `Resources/` | Prototypes, maps, locale, textures, audio, configs, changelogs |
| `Tools/` | Build/publish/changelog scripts |
| `RobustToolbox/` | Engine submodule (never modify) |
| `.opencode/skills/palmtree-station/` | Project-local OpenCode skill: porting playbook, marking/ERP reference, species/Synth, validation & hazards, helper scripts |

## Solution projects

```
Content.Shared ──► Content.Shared.Database, Robust.Shared, Robust.Shared.Maths, Lidgren.Network
Content.Server ──► Content.Shared, Content.Server.Database, Content.Shared.Database,
                   Content.Packaging, Robust.Server, Robust.Shared, Lidgren.Network
Content.Client ──► Content.Shared, Robust.Client, Robust.Shared, ...
Content.IntegrationTests ──► Content.Client, Content.Server, Content.Shared, Robust.UnitTesting
Content.Tests ──► Content.Shared + side-specific IoC, Robust.UnitTesting
Content.Benchmarks/MapRenderer/YAMLLinter/Packaging ──► Content.Server+Client+Shared
Content.Server.Database ──► EF Core Sqlite.Core + Npgsql (library)
Pow3r, Content.PatreonParser, Content.Tools, Content.Replay — leaf tools
```

## Entry points

| Executable | Bootstrap |
|---|---|
| Server | `Content.Server/Program.cs` → `ContentStart.Start(args)` → `Entry/EntryPoint.cs : GameServer` |
| Client | `Content.Client/Program.cs` → `ContentStart.Start(args)` → `Entry/EntryPoint.cs : GameClient` |
| Shared module | `Content.Shared/Entry/EntryPoint.cs : GameShared` |
| Replay client | `Content.Replay/Program.cs` (config `replay.toml`) |
| Tools | `Content.MapRenderer/Program.cs`, `Content.Packaging/Program.cs`, `Content.YAMLLinter/Program.cs`, `Content.Tools/MappingMergeDriver.cs`, `Content.PatreonParser/Program.cs`, `Pow3r/Program.cs` |

## Languages / formats

| Format | Usage |
|---|---|
| C# | All game logic (ECS: components are data, systems are behavior) |
| YAML | Prototypes (`Resources/Prototypes`), maps (`Resources/Maps`, format 7), migrations, changelogs |
| Fluent `.ftl` | Localization under `Resources/Locale/<culture>/` |
| TOML | Runtime config + `Resources/ConfigPresets/**` |
| XAML | Client UI; loaded by engine `RobustXamlLoader`, typed name refs generated by `Robust.Client.NameGenerator` |
| JSON | `.rsi` sprite metadata, DB jsonb columns |
| Python / JS / bash / ps1 | Tooling, CI, migration scripts |

## Build & run

```
python RUN_THIS.py          # installs git hooks, updates submodules (BuildChecker/git_helper.py)
dotnet build                # debug build
dotnet run --project Content.Server      # runserver.sh
dotnet run --project Content.Client      # runclient.sh
dotnet run --project Content.Server --configuration Tools   # runserver-Tools.sh (hot reload etc.)
dotnet test Content.Tests/Content.Tests.csproj
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj
dotnet run --project Content.YAMLLinter
dotnet run --project Content.MapRenderer [ids|files]
dotnet run --project Content.Packaging server|client
```

Nix users: `flake.nix` / `shell.nix` / `.envrc` provide a dev shell with SDL2, OpenAL, GTK, .NET 9.

**Lint caveat:** `dotnet run --project Content.YAMLLinter --no-build` runs the previously built
linter output, including its copied `Content.Shared.dll`. After C# changes, rebuild
`Content.YAMLLinter` (or the solution) first, otherwise enum/prototype changes are invisible to the
linter and it may report errors for categories that actually exist (or miss new ones).

## Runtime directory

- Server user data defaults to `data/` next to the executable (or `--data-dir`), containing
  `preferences.db` (SQLite default), `replays/`, saved maps, etc.
- Server config: `server_config.toml`, CVars via CLI/env (`ROBUST_CVARS`, `ROBUST_CVAR_<NAME>`),
  `Resources/ConfigPresets/*.toml`.
- Client config: `client_config.toml` in the client user-data dir.

## Generated / vendored / non-editable

| Item | Notes |
|---|---|
| `RobustToolbox/**` | Engine submodule; do not modify |
| `RobustToolbox/NetSerializer`, `Lidgren.Network`, `XamlX`, `Avalonia.Base`, `cefglue` | Vendored engine deps |
| Source-generated code | Component network auto-states, serialization, XAML name fields, pause handling |
| `bin/`, `obj/` | Build artifacts (not source) |

## Tests / tooling quick map

| Tool | Purpose |
|---|---|
| `Content.IntegrationTests` | `PoolManager` pairs a server+client; `TestMap = "Empty"`; fork tests in `_NF/*`, `_PS/HilbertHotelTest`, `_PS/InteractionPanelTest` |
| `Content.Tests` | Unit tests (chemistry, atmos, wires, chat censor, localization, IPIntel, preferences, marking serialization) |
| `Content.YAMLLinter` | Loads all prototypes on server+client, `ValidateStaticFields`; CI `::error` annotations |
| Migration system | `Resources/migration.yml` + `nf_migration.yml` applied on map load |
| Changelog | `Resources/Changelog/Palmtree.yml` (this fork, auto-generated from `:cl:` blocks) + upstream history |
| Packaging | `Content.Packaging` zips per RID |

## Related documentation

- Architecture layers: `.ai/ARCHITECTURE.md`
- Subsystem deep dives: `.ai/systems/`
- Procedural how-to guides: `.ai/guides/`
- Hazards and unknowns: `.ai/HAZARDS.md`, `.ai/UNKNOWN.md`
- Port history and decisions: `.ai/PORTING.md`
- Maintainer-provided context: `.ai/HUMAN_CONTEXT.md`
