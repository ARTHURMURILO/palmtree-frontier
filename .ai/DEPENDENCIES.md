# DEPENDENCIES — Project Graph and Coupling

## Project graph

```
Content.Shared ──► Content.Shared.Database, Robust.Shared, Robust.Shared.Maths, Lidgren.Network
Content.Server ──► Content.Shared, Content.Server.Database, Content.Shared.Database,
                   Content.Packaging, Robust.Server, Robust.Shared, Lidgren.Network
Content.Client ──► Content.Shared, Robust.Client, Robust.Shared, ...
Content.IntegrationTests ──► Content.Client, Content.Server, Content.Shared, Robust.UnitTesting
Content.Tests ──► Content.Shared + side IoC, Robust.UnitTesting
Content.YAMLLinter ──► Content.Server + Content.Client + Content.Shared
Content.MapRenderer/Packaging/Benchmarks ──► all content projects
Content.Server.Database ──► EF Core Sqlite.Core + Npgsql (library)
```

Rules:

- `Content.Shared` must never reference side assemblies.
- `Content.Client` may reference `Content.Shared` only (no server).
- DB models (`Content.Server.Database`) are server-only; never reference from shared/client.
- Tests may reference both sides.

## Subsystem chains worth knowing

### Character → appearance → marking render

```
preferences DB/JSON ──► HumanoidCharacterProfile ──► SharedHumanoidAppearanceSystem.LoadProfile
    ──► MarkingSet (component, auto-networked) ──► Client HumanoidAppearanceSystem.UpdateSprite
    ──► UpdateLayers (speciesBaseSprites + altSprites) ──► ApplyMarkingSet
    ──► ApplyMarking (layering/colorLinks/scale/offset) ──► UpdateLayersAgain (HiddenBaseLayers)
```

### Marking verbs

```
GetVerbsEvent ──► ModifyUndiesSystem (per-marking opt-in) ──► ModifyUndiesDoAfterEvent
    ──► SharedHumanoidAppearanceSystem.SetMarkingVisibility ──► Marking.Visible ──► Dirty
    ──► client re-renders on state update
```

### Concealable clothing

```
BaseSubdermalImplant + ConcealableClothingImplant ──► ServerConcealableClothingSystem
    ──► ConcealableClothingUserComponent categories ──► RefreshConcealmentActions
    ──► SharedActionsSystem grants ActionToggleConcealment ──► ToggleClothingConcealmentEvent
    ──► ConcealableClothingComponent.IsConcealed ──► client removes equipment visuals
```

### Lobby presentation

```
GameTicker.LobbyBackground (30s timer) ──► TickerLobbyStatusEvent ──► ClientGameTicker
    ──► LobbyState.UpdateLobbyBackground ──► _nextBg + crossfade in FrameUpdate
```

### Round/spawn

```
GameTicker.StartRound ──► GamePreset prototype ──► GameRules ──► RulePlayerSpawningEvent
    ──► StationSpawningSystem.SpawnPlayerMob ──► NF loadout/bank hooks ──► HumanoidAppearance
```

## Coupling hotspots

| Area | Coupling | Note |
|---|---|---|
| `GameTicker` partials | NF/`_PS` patches in `_NF` folders | Intentional namespace collision; grep all partials |
| Marking filters | `MarkingManager` + `MarkingsSet` | Both must use the kind-aware check (HAZARDS §2) |
| Species YAML | Base sprite sets + mob sprite layers | Missing layer in either place silently disables markings |
| Lobby | Server timer + client state + XAML | Three files must stay in sync |
| Loadouts | `_NF` job groups + `_PS`/`_CS`/`_DEN` loadouts | A loadout is unreachable until added to a group |
| ERP toggles | `_Floof` server + shared humanoid | `ModifyUndies` has no consent dependency; the NSFW age gate reads `IServerPreferencesManager` |
| DB | `Model.cs` + two migration sets | Any model change requires both providers |

## External dependencies

| Dependency | Why |
|---|---|
| RobustToolbox (submodule, v267.3.0) | Engine APIs, serialization, sandbox whitelist |
| .NET 9 SDK | Build/runtime |
| EF Core (Sqlite + Npgsql) | Persistence |
| Lidgren.Network | Engine networking (vendored) |
| NetCord | Discord integration |
| Fluent | Locale files |
| NUnit | Tests |
| Nix (optional) | Dev shell |
| Coyote checkout (`COYOTE`, see `.ai/file_paths.md`) | Source of ported content (outside this repo) |
