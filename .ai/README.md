# .ai — AI Navigation Guide (Palmtree Station)

This directory is the entry point for AI agents working on the **Palmtree Station** repository:
a Space Station 14 fork of **Frontier Station** running on the **RobustToolbox** engine, with
Palmtree-specific (`_PS`) content and a ported ERP/marking layer from Coyote/Floof.

Read this file first, then the document that matches your task.

> **Changelog rule for agents:** document every player-visible change with a `:cl:` block in the
> commit message (`:cl:` on its own line, then `- add|remove|tweak|fix: message` lines). The commit
> workflow appends it to `Resources/Changelog/Palmtree.yml`; details and fallbacks in
> `.ai/guides/changelogs.md`. Never write entries into the upstream changelog files.

## 1. What this project is

- Round-based multiplayer space game: C# ECS gameplay on RobustToolbox (git submodule, v267.3.0)
  plus thousands of YAML prototype files.
- Fork chain:

  ```
  Space Station 14 (Wizden)
      └── Frontier Station (new-frontiers-14, prefix _NF)
              └── Palmtree Station (this repo, prefix _PS)
                      └── ported content: _Floof, _CS, _DEN, _EE, _White, _Starlight
  ```

- Live gameplay is Frontier-style: a hub station, ships, economy, sectors, salvage, no station
  antagonists (`rules: []` upstream presets). ERP is explicitly allowed by the maintainer.
- Maintainer constraints (see `.ai/HUMAN_CONTEXT.md`): engine must not be modified; new code goes
  under `_PS` folders; content ported from other forks keeps its original prefix for merge sanity.
- Current branch: **`master`** (the `ps-erp` port branch was merged and deleted; Frontier base
  `df24c19f08`, kept current with upstream merges). The port history and decisions are in
  `.ai/PORTING.md`.

## 2. Major architectural layers

```
RobustToolbox engine (submodule, frozen)
   └── Content.Shared  (components, prototypes, net messages)
           ├── Content.Server  (authoritative gameplay, DB, admin)
           └── Content.Client  (UI, prediction, input, audio)
                   └── Resources/ (prototypes, maps, locale, textures, configs)
```

Fork modules are folder/namespace prefixes:

| Prefix | Origin | Used here for |
|---|---|---|
| `_NF` | Frontier Station (parent) | Economy/bank, shipyard, sectors, salvage, cryo, pirates |
| `_PS` | Palmtree (this repo) | Lobby screens/music, concealable clothing backpack implant, weapons/grenades, Synth species, Hilbert's Hotel, Bluespace Dock/Ship Registrar, interaction panel, genital organs |
| `_Floof` | Floofstation | Marking system extensions, `ModifyUndies` (ERP toggle verbs) |
| `_CS` | Coyote Sector | Anthromorph species, undergarments, Coyote Bayou clothes (incl. latex), undies locale |
| `_HL` | HardLight (via Coyote) | Ported shower (prototype, construction, sprite, audio) |
| `_DEN` | TheDen | Clicker, foot protectors, Ovinia markings/sprites |
| `_EE` | Einstein Engines | Tajaran species (ported), carrying |
| `_DV` | Delta-V | Vulpkanin/Harpy/Rodentia species, abilities, markings |
| `_White` | White Dream | Eastern Dragon reptilian markings |
| `_Starlight` / `_StarLight` | Starlight | Resomi markings (note both case spellings exist) |
| others (`_CD`, `_Corvax`, `_EstacaoPirata`, `_Goobstation`, `_Harmony`, `_Impstation`, `_NC`, `_RMC14`) | various | inherited Frontier imports |

## 3. Where systems live

| Area | Root paths |
|---|---|
| Engine | `RobustToolbox/` |
| Shared gameplay | `Content.Shared/<Feature>` |
| Server gameplay | `Content.Server/<Feature>` |
| Client gameplay/UI | `Content.Client/<Feature>` |
| Round/game rules | `Content.Server/GameTicking`, `Content.Server/GameTicking/Rules` |
| Database | `Content.Server.Database`, `Content.Server/Database` |
| Frontier gameplay | `Content.Server/_NF`, `Content.Shared/_NF`, `Content.Client/_NF` |
| Palmtree additions | `Content.Server/_PS`, `Content.Shared/_PS`, `Content.Client/_PS` |
| Marking/ERP layer | `Content.Shared/Humanoid/Markings`, `Content.Shared/Humanoid`, `Content.Client/Humanoid`, `Content.Server/_Floof` |
| Content/data | `Resources/Prototypes`, `Resources/Maps`, `Resources/Locale`, `Resources/Textures`, `Resources/Audio` |
| Tests | `Content.Tests`, `Content.IntegrationTests` |
| Tools | `Content.YAMLLinter`, `Content.MapRenderer`, `Content.Packaging`, `Content.Tools`, `Content.PatreonParser`, `Content.Replay`, `Pow3r` |

## 4. Documentation map

| Document | Contents |
|---|---|
| `.ai/REPOSITORY.md` | Inventory: languages, projects, tooling, build, resources |
| `.ai/ARCHITECTURE.md` | Layer model, bootstraps, ECS, round architecture, fork modules |
| `.ai/API.md` | Key classes/managers/systems with locations and purposes |
| `.ai/DEPENDENCIES.md` | Project graph, subsystem chains, coupling, external deps |
| `.ai/DATA_FLOW.md` | Round, player, chat, damage, UI, economy, marking flows |
| `.ai/EVENTS.md` | Event dispatch model, lifecycle, event catalog, gotchas |
| `.ai/PERSISTENCE.md` | DB schema areas, migrations, files, save/load paths, marking DB format |
| `.ai/CONVENTIONS.md` | Observed patterns + explicit inconsistencies |
| `.ai/HAZARDS.md` | Things likely to be misunderstood or broken by blind edits |
| `.ai/UNKNOWN.md` | Open questions and unverifiable deployment facts |
| `.ai/PORTING.md` | What was ported from Coyote/Floof, decisions, what was left out |
| `.ai/systems/marking-and-appearance.md` | Marking system, genital markings, digitigrade, base layers, kindAllowance, ModifyUndies |
| `.ai/systems/ps-systems.md` | Lobby screens/music/crossfade, concealable clothing implant, weapons |
| `.ai/systems/frontier-nf-systems.md` | Bank/market/shipyard/cargo/sectors/cryo/pirates |
| `.ai/systems/dungeons.md` | Procedural dungeons/salvage: `DungeonSystem`, dungeon configs/room packs/themes, expeditions and bluespace events |
| `.ai/systems/hilbert-hotel.md` | Hilbert's Hotel: runtime room maps, room codes, locking, guest lists |
| `.ai/systems/player-character-and-jobs.md` | Sessions, minds, profiles, species, jobs/loadouts, ghosts, antags |
| `.ai/systems/consent-and-erp.md` | NSFW age gate (account flag, lobby prompt, `nsfwtoggle`, what it gates), ERP feature set, how to add full consent later |
| `.ai/systems/interaction-panel.md` | Sandstorm interaction panel port: prototypes, organ-based genitals, session consent, lust/moans, UI, keybind |
| `.ai/systems/genital-organs.md` | Organ-based genitals replacing genital markings: catalog, profile/DB, editor tab, cum overlay and semen drip |
| `.ai/systems/game-ticker.md` | Round state machine, presets, rules, maps, spawn |
| `.ai/systems/core-gameplay.md` | Interactions, body/damage, atmos, power, chemistry, construction, NPC, shuttles |
| `.ai/systems/content-pipeline-and-tooling.md` | Prototypes, localization, maps, tests, CI, packaging |
| `.ai/guides/porting-from-coyote.md` | Repeatable method for porting Coyote/Floof content into this repo |
| `.ai/guides/porting-from-ss13.md` | Porting BYOND SS13 features, `.dmm` maps and sprites into this repo |
| `.ai/guides/adding-markings.md` | Adding markings (normal, genital, kind-shared, layered, digitigrade) |
| `.ai/guides/adding-species.md` | Adding/porting a species with body, layers, markings, speech |
| `.ai/guides/adding-loadouts.md` | Adding loadouts, their items, and group wiring |
| `.ai/guides/adding-hotel-rooms.md` | Adding Hilbert's Hotel room maps + archetype prototypes |
| `.ai/guides/adding-dungeons.md` | Adding dungeon rooms, themes/room packs, `dungeonConfig` wiring and rare boss arenas |
| `.ai/guides/changelogs.md` | Changelog YAML authoring + `:cl:` automation for this fork |
| `.ai/ideas/README.md` | Feature designs: statuses range from unimplemented (`highroller-table.md`) to partially implemented (Fallout weapon backlog) and implemented (`bluespace-dock.md`) |

### Procedural guides

| Guide | Answers |
|---|---|
| `.ai/guides/porting-from-coyote.md` | How to port content from `coyote-frontier` and other forks, field adaptations, asset copying, lint gates |
| `.ai/guides/porting-from-ss13.md` | BYOND SS13 sources, TGM `.dmm` parsing, format-7 map generation, sprite limits, SS13→SS14 concept mapping, hazards |
| `.ai/guides/adding-markings.md` | `MarkingPrototype` fields, categories, `kindAllowance`, `layering`/`colorLinks`, `altSprites`, sprites/locale, genital rules |
| `.ai/guides/adding-species.md` | Species + mob + body + layers + `altSprites` + damage + speech + names recipe |
| `.ai/guides/adding-loadouts.md` | Loadout + item + group wiring + locale + verification |

## 5. Where the main APIs are

- Engine foundation: `RobustToolbox/Robust.Shared/GameObjects/EntitySystem.cs`, `EntityManager*.cs`,
  `EntityEventBus.*.cs`, `Prototypes/IPrototypeManager.cs`, `Serialization/Manager/ISerializationManager.cs`.
- Server managers/IoC: `Content.Server/IoC/ServerContentIoC.cs`.
- Round: `Content.Server/GameTicking/GameTicker.cs`.
- Persistence: `Content.Server/Database/ServerDbManager.cs` (`IServerDbManager`).
- Client: `Content.Client/IoC/ClientContentIoC.cs`, `Content.Client/Entry/EntryPoint.cs`.
- Shared: `Content.Shared/**/Shared*System.cs` bases.
- Frontier: `_NF/Bank/BankSystem.cs`, `_NF/Shipyard/Systems/ShipyardSystem.cs`.
- Markings: `Content.Shared/Humanoid/Markings/MarkingManager.cs`, `MarkingPrototype.cs`,
  `Content.Client/Humanoid/HumanoidAppearanceSystem.cs`, `Content.Client/Humanoid/MarkingPicker.xaml.cs`.
- Palmtree: `_PS/Clothing/SharedConcealableClothingSystem.cs`, `Content.Server/GameTicking/GameTicker.LobbyBackground.cs`,
  `Content.Client/Lobby/LobbyState.cs`.

## 6. Dependency boundaries to respect

1. `Content.Shared` must not reference server/client code; put new cross-side types there.
2. All network messages live in `Content.Shared`; only register handlers per side.
3. The client only sees replicated state; anything private requires filters/`SessionSpecific`/`SendOnlyToOwner`.
4. DB access is server-only through `IServerDbManager`; never share DB models with the client.
5. Engine code is out of bounds; build features on engine APIs, do not patch `RobustToolbox/`.
6. Upstream files are patched in-place by forks (`// Frontier`, `// Coyote`, `// Palmtree` markers).
   Prefer `_PS`/`_Floof` modules and partial classes; when touching a core file, preserve marker
   comments so the patch remains identifiable and minimal.
7. **Asset placement:** new fork assets (audio, textures, UI art) live under the matching `_PS`
   resource folders (`Resources/Audio/_PS`, `Resources/Textures/_PS`, ...). Only content ported from
   another source keeps that source's prefix (`_CS`, `_NF`, `_DV`, ...). Repoint references instead
   of adding new files to upstream folders.

## 7. Most important hazards (details in `.ai/HAZARDS.md`)

- Client assemblies run the engine **sandbox type checker**: not all BCL APIs are allowed in
  `Content.Shared`/`Content.Client` (e.g. `string.Create(IFormatProvider, ...)` is blocked).
- `MarkingsSet.EnsureSpecies` and `MarkingManager` must use the kind-aware
  `IsAllowedBySpeciesOrKindAllowance` check; direct `SpeciesRestrictions.Contains` strips
  kind-shared markings.
- `MarkingsSet.EnsureDefault` loop must use `&&` (it once overran `DefaultMarkings`).
- The concealable backpack implant requires the `ConcealableClothing` component on base
  `ClothingBackpack`; the lobby rotation requires the timer patch in `GameTicker.LobbyBackground.cs`.
- `MarkingPrototype` filters: all species-restricted markings carry `kindAllowance`
  (`[BasicHumanlike, BasicFurry, BasicRobot, VoxLike]`, Coyote behavior). Markings without
  `kindAllowance` will be stripped from species that only match through kinds — always add it to new
  species-restricted markings (`.ai/guides/adding-markings.md`).
- Digitigrade legs: species base sprites use `altSprites`; Synth uses the `DigilegSynthliz*` set.
  Clothing accommodation goes through `LegDisplacementPrototype` /
  `HumanoidAppearanceComponent.LegDisplacements` and `ClientClothingSystem`.
- Synths skip `RespiratorSystem` via `SynthComponent`; they have no lungs and do not gasp.
- `dotnet run --project Content.YAMLLinter --no-build` uses stale assemblies; rebuild the project
  after C# changes before trusting lint results.
- Fork-patched core files are common; never assume a file is untouched upstream.
- Directed vs broadcast events: `RaiseLocalEvent<T>(msg)` does not reach component handlers.
- Subscribe only in `Initialize()`; subscriptions lock after startup.
- DB migrations are load-bearing; both SQLite and Postgres must be updated.
- No per-toggle consent system exists in this repo (only the account-level NSFW age gate);
  `ModifyUndies` is gated per marking by
  `canToggleVisible`/`otherCanToggleVisible` (see `systems/consent-and-erp.md`).

## 8. Before modifying common systems, search these

| You want to change | Search first |
|---|---|
| GameTicker behavior | `GameTicker` partials incl. `_NF/GameTicking/GameTicker.NFSpawning.cs` (namespace collision is intentional) |
| Spawning/jobs | `StationSpawningSystem`, `StationJobsSystem`, `_NF` loadout/bank hooks in `SpawnPlayerMob` |
| Any component state | `[AutoGenerateComponentState]` / `[AutoNetworkedField]` / manual `GetState` |
| Markings/appearance | `Content.Shared/Humanoid/Markings/*`, `Content.Client/Humanoid/*`, species YAML |
| Genital/ERP toggles | `_Floof/ModifyUndies`, `SharedHumanoidAppearanceSystem.SetMarkingVisibility`, consent doc |
| UI | the BUI + `UserInterfaceComponent` YAML + `ActivatableUISystem`, then client BUI class by `ClientType` |
| Prototypes | `Resources/Prototypes/<prefix>` + `ignoredPrototypes.yml` + `migration.yml`/`nf_migration.yml` |
| Persistence | `Model.cs`, both migration folders, `ServerDb*.cs`, and the drift test |
| NF economy/ships | `_NF/Bank`, `_NF/Shipyard`, `_NF/ShuttleRecords`, `_NF/Cargo`, `_NF/Market` |
| Palmtree additions | `Content.Server/_PS`, `Content.Shared/_PS`, `Content.Client/_PS`, `Resources/Prototypes/_PS` |
| Input keys/contexts | `ContentKeyFunctions.cs`, `ContentContexts.cs`, `Resources/keybinds.yml` |
| Config | `Content.Shared/CCVar/CCVars.*`, `_NF/CCVar/NFCCVars.cs`, `Resources/ConfigPresets` |

## 9. Poorly understood areas (verify before relying)

- Production config/topology (presets, auth mode, DB, multi-server list).
- IPC silicon stack (IPC is replaced by the Synth species; battery and radio/encryption behavior is
  not implemented, and EMP/ion pulses are deliberately lethal — see `.ai/PORTING.md`).
- Un-ported Coyote systems (consent, traits, scent, size, vore, RPI) — see `.ai/PORTING.md`.
- Deep engine internals (PVS budgets, physics solver, handshake, sandbox whitelist coverage).
- See `.ai/UNKNOWN.md` for the full list.

## 10. External dependencies that must be consulted

| Dependency | When to consult |
|---|---|
| `RobustToolbox/` source (pinned commit) | Any engine API behavior, serialization, networking, UI, map format |
| Frontier Station upstream (`new-frontiers-14/frontier-station-14`) | `_NF` merge semantics/upstream fixes |
| Wizden SS14 (`space-wizards/space-station-14`) | Upstream content origins and benchmarks |
| Coyote checkout (`COYOTE`, see `.ai/file_paths.md`) | Source of ported `_Floof`/`_CS`/ERP content |
| SS13/BYOND checkouts (see `.ai/file_paths.md`) | Source of SS13 features, `.dmm` room maps and art (see `guides/porting-from-ss13.md`) |
| EF Core docs | Migrations/model changes (two providers) |
| Fluent docs | `.ftl` syntax and functions |
| NetCord docs | Discord integration changes |

Machine-specific checkout paths are **not** stored in this directory. They live in
`.ai/file_paths.md` (gitignored); the names above (`COYOTE`, `BYOND`, ...) resolve there. On a new
machine, create that file using its header template.

## "How to Investigate This Repository"

```
1. Read .ai/README.md (this file).
2. Read the relevant system doc (see §4) or procedural guide and the matching sections of
   ARCHITECTURE / DATA_FLOW / EVENTS / PERSISTENCE.
3. Search the source for the relevant symbols:
     grep -rn "SymbolName" Content.Shared Content.Server Content.Client
     grep -rn "protoId" Resources/Prototypes
   Remember fork folders (_NF/_PS/_Floof/_CS/...) and in-place fork patches.
4. Trace callers and dependencies:
     - component → subscribing systems (grep SubscribeLocalEvent<TComp)
     - event → producers and consumers (grep RaiseLocalEvent/RaiseNetworkEvent)
     - prototype → C# class ([Prototype("kind")])
     - BUI → ClientType class
     - partial class → search all files declaring it (namespace collisions are real)
5. Verify framework behavior against the pinned engine source (RobustToolbox), not public docs.
6. Check hazards: events delivery mode, subscription lock, paused entities, prediction,
   config defaults, both DB migrations, prototype migration entries, sandbox API whitelist.
7. Only then propose a change. For content changes prefer new prototypes + `_PS` code;
   for core changes preserve upstream patch markers and update `.ai/` docs if architecture shifts.
   When committing a player-visible change, include a `:cl:` block (see `guides/changelogs.md`).
```

## Maintenance of this documentation

When making architectural changes, update the affected `.ai/` document in the same change. Prefer
symbol names over line numbers as anchors; line numbers drift. Facts in these docs were gathered
from the working tree on `master` (Frontier `df24c19f08` + the port commits listed in
`.ai/PORTING.md`, plus later upstream merges).

A project-local OpenCode skill with the same knowledge as an operational playbook lives at
`.opencode/skills/palmtree-station/SKILL.md` (porting, markings/ERP, species/Synth, validation,
hazards, helper scripts). Keep it in sync when workflows change.
