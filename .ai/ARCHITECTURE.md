# ARCHITECTURE

> Derived from source inspection. Where behavior comes from the engine (RobustToolbox), it is marked
> as engine. Prefer symbol names over line numbers; lines drift.

## Layer model

```
┌────────────────────────────────────────────────────────────────────────────┐
│ RobustToolbox engine (submodule; frozen)                                   │
│  ContentStart → ModLoader → GameServer / GameClient entry points           │
│  IoC/DI · CVars · VFS/ResourceManager · GameLoop/GameTiming                │
│  EntityManager + EntitySystemManager + EventBus + ComponentFactory         │
│  PrototypeManager · Serialization (YAML/binary) · NetManager · PVS         │
│  Map/Grid/Transform · Physics · Input · Audio · UserInterface (Clyde/XAML) │
└───────────────────────────────▲────────────────────────────────────────────┘
                                │ engine APIs
┌───────────────────────────────┴────────────────────────────────────────────┐
│ Content.Shared (GameShared)                                                │
│  Components + SharedXSystem bases · prototypes · net messages              │
│  shared events · preferences/mind/humanoid data models                     │
└───────────────▲──────────────────────────────────▲─────────────────────────┘
                │ referenced by both               │
┌───────────────┴──────────────┐   ┌───────────────┴──────────────────────────┐
│ Content.Server (GameServer)  │   │ Content.Client (GameClient)              │
│  authoritative systems       │   │  states · UI controllers · overlays      │
│  managers · GameTicker       │   │  prediction · input · audio · BUI clients│
│  DB (EF Core) · admin · EUIs │   │  lobby/character editor · map editor     │
└───────────────┬──────────────┘   └───────────────┬──────────────────────────┘
                │                                  │
┌───────────────┴──────────────────────────────────┴─────────────────────────┐
│ Resources/ (data-driven content)                                           │
│  Prototypes (~3,777 YAML) · Maps · Locale (.ftl) · Textures/Audio          │
│  ConfigPresets (.toml) · Changelog · migration.yml/nf_migration.yml        │
└────────────────────────────────────────────────────────────────────────────┘
```

Boundary rules observed in code:

- `Content.Shared` never references `Content.Server` or `Content.Client`.
- All network message classes live in `Content.Shared`; sides only register handlers.
- PVS/session visibility is server-authoritative; the client sees replicated state only.
- Map/prototype data flows one way: YAML → `PrototypeManager` → entity/component instances.

## Server bootstrap (summary)

1. Engine `BaseServer.Start` → loads engine CVars/config, starts network, mounts VFS,
   `ModLoader.TryLoadModulesFrom("/Assemblies","Content.")`.
2. Content `EntryPoint` (`Content.Server/Entry/EntryPoint.cs`): config presets, ACZ provider,
   component auto-registration, prototype ignore lists, `ServerContentIoC.Register()`,
   `IoCManager.BuildGraph()`, net IDs.
3. `Init`: entity manager/prototypes init; managers resolved and initialized in fixed order
   (`IServerDbManager.Init()`, preferences, connection manager, etc.).
4. `PostInit`: sanitization/chat, recipe manager, admin, rules, Discord, EUIs, map manager,
   `GameTicker.PostInitialize()`.
5. Main loop: engine ticks → `ModUpdateLevel` passes → `EntityManager.TickUpdate` (systems in
   topological order, queued events/deletions) → send game state.
6. Shutdown: content `Dispose` (playtime, DB, API, Discord), engine cleanup.

## Client bootstrap (summary)

1. `Content.Client/Program.cs` → `GameController` → content `EntryPoint.Init`: IoC registration,
   localization, component auto-registration, prototype ignore list, net IDs, manager init.
2. `PostInit`: stylesheets, input contexts, parallax, overlays, preferences/EUI/vote, theme,
   run-level → state switching.
3. State selection: replay → launcher → `MainScreen`; in-game states come from `ClientGameTicker`
   network events (`LobbyState` → `GameplayState`).
4. **The client runs the engine IL sandbox type checker at startup.** See `.ai/HAZARDS.md` §1.

## What the engine owns vs content

| Concern | Engine | Content |
|---|---|---|
| Process/assembly bootstrap, sandbox, mod loader | ✔ | EntryPoint classes, IoC registrations |
| ECS storage, lifecycle, queries, event bus | ✔ | Components/systems/events |
| Prototype parsing/inheritance/validation | ✔ | All gameplay prototypes |
| YAML/binary serialization, component net state | ✔ | `[DataField]`, `[AutoGenerateComponentState]` usage |
| Network transport, PVS, prediction, replay framework | ✔ | NetMessages, filters, replay triggers |
| Maps/grids/transforms/physics | ✔ | Map prototypes, fixtures, shuttle logic |
| UI framework (Clyde/XAML/controls) | ✔ | Screens, controllers, XAML, stylesheets |
| CVar registry/replication | ✔ | `CCVars` definitions |
| Console command discovery/toolshed | ✔ | Commands implementation |
| Game round, roles, DB, gameplay | ✘ | ✔ |

## ECS model (summary)

- Entities are `EntityUid` ints; network identity is `NetEntity`. `EntityManager` owns lifecycle.
- Components are data-only classes (`[RegisterComponent]`, `[DataField]`); behavior lives in
  `EntitySystem` subclasses. `ComponentFactory` auto-registers by reflection.
- Systems subscribe explicitly in `Initialize()` (`SubscribeLocalEvent`, `SubscribeNetworkEvent`,
  `SubscribeAllEvent`); subscriptions lock after startup.
- Shared systems are abstract bases (`SharedXSystem`) subclassed per side; some run on both sides for
  prediction.
- Event dispatch distinguishes **directed** (component handler) vs **broadcast** (system) events;
  `RaiseLocalEvent<T>(msg)` does NOT reach `SubscribeLocalEvent<TComp,TEvent>` handlers.

## Content loading

- Prototypes load from engine `/EnginePrototypes/` then content `/Prototypes/`.
- `Content.Shared/Entry/EntryPoint.cs` reads `Resources/IgnoredPrototypes/ignoredPrototypes.yml` and
  marks entries abstract so upstream parents can be overridden/deleted.
- Prototype IDs are referenced in C# via `ProtoId<T>` / `EntProtoId` (validated at load; no compile-time
  constants).
- Runtime prototype errors are logged and skipped per file; `Content.YAMLLinter` is the CI gate.

## Round architecture

- `GameTicker` (server) is a partial class split across files: `GameTicker.cs`, `.CVars`,
  `.GamePreset`, `.GameRule`, `.Lobby`, `.LobbyBackground`, `.Player`, `.Replays`, `.RoundFlow`,
  `.Spawning`, `.StatusShell`, plus `_NF/GameTicking/GameTicker.NFSpawning.cs` (intentional namespace
  collision).
- Run levels: `PreRoundLobby → InRound → PostRound` (no `RoundIdle`).
- Game presets are prototypes (`gamePresets`); live presets are `NFAdventure`, `NFPirate`, `NFTest`
  (`Resources/Prototypes/_NF/game_presets.yml`); upstream presets have `rules: []`.
- Defaults: `game.defaultpreset = "nfpirate"`, `game.map = "Frontier"`, `game.map_pool = "NFMapPool"`.
- Round flow: `StartRound` → map load → `RoundStartingEvent` → `RoundStartAttemptEvent` →
  `RulePlayerSpawningEvent` → job assignment → per-player `SpawnPlayer` → `RoundStartedEvent` →
  `EndRound` → `RoundEndTextAppendEvent` → `RoundRestartCleanupEvent`.

## Fork module architecture

Modular forks are merged by folder/namespace prefix in all projects and `Resources`:

| Prefix | Origin | Significance here |
|---|---|---|
| `_NF` | New Frontier (parent) | Economy/bank, shipyard, sectors/POIs, salvage/expeditions, cryo, pirates, missions |
| `_PS` | Palmtree | Lobby, concealable clothing, weapons, Synth, Hilbert's Hotel, Bluespace Dock/Ship Registrar, interaction panel, genital organs |
| `_Mono` | Monolith (via Frontier imports) | Armor plates, carried content |
| `_Funkystation` | Funky Station (via Frontier imports) | Inherited content |
| `_Floof` | Floofstation | Marking extensions, ModifyUndies |
| `_CS` | Coyote | Anthromorph, undergarments, Bayou clothes, undergarments/undies locale |
| `_DEN` | TheDen | Clicker, foot protectors, Ovinia |
| `_EE` | Einstein Engines | Tajaran (ported), carrying |
| `_DV` | Delta-V | Vulpkanin/Harpy/Rodentia species, abilities, markings |
| `_White` | White Dream | Eastern dragon markings |
| `_Starlight`/`_StarLight` | Starlight | Resomi markings; both case spellings exist |
| others | various | Inherited Frontier imports |

Upstream files are patched in place and annotated with `// Frontier`, `// Coyote`, `// Palmtree`,
`// Floof`, etc. Some partial classes deliberately use the upstream namespace to extend classes
across folders (e.g. `Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs`).

## Data movement (one paragraph)

Player input → client `InputSystem` command → server re-execution → gameplay systems mutate
components (authoritative) → dirty component fields → `PvsSystem` game-state messages → client
`ClientGameStateManager` applies state/interpolates; UI state flows via BUIs (`SetUiState` →
client BUI `UpdateState`); echoed chat/announcements flow through `MsgChatMessage`; persistence flows
through `IServerDbManager` on connect/disconnect and periodic ticks. Marking changes ride on
`HumanoidAppearanceComponent`'s auto-networked `MarkingSet`. Full detail: `.ai/DATA_FLOW.md`.

## Important architectural boundaries

1. **Client/server**: enforced by assemblies, `NetMessage` registration and engine serialization.
2. **Engine/content**: content must not assume engine internals; client code must stay within the
   sandbox whitelist.
3. **DB**: only `Content.Server` touches `IServerDbManager`; clients never see DB models.
4. **Fork code vs upstream**: merges are the highest-risk area (see `.ai/HAZARDS.md`).
