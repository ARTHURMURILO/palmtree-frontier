# API — Key Classes, Managers and Systems

> Locations are for this checkout. Prefer symbol names when searching; paths are stable, line numbers
> are not. Add new entries when you add a major system.

## Engine foundations (RobustToolbox)

| Symbol | Location | Purpose |
|---|---|---|
| `EntitySystem` | `RobustToolbox/Robust.Shared/GameObjects/EntitySystem.cs` | Base system; lifecycle, subscriptions, queries, timers |
| `EntityManager` | `RobustToolbox/Robust.Shared/GameObjects/EntityManager*.cs` | Entity/component storage and lifecycle |
| `EntityEventBus` | `RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.*.cs` | Directed/broadcast event dispatch |
| `IPrototypeManager` | `RobustToolbox/Robust.Shared/Prototypes/IPrototypeManager.cs` | Prototype index/inheritance |
| `ISerializationManager` | `RobustToolbox/Robust.Shared/Serialization/Manager/ISerializationManager.cs` | Data (de)serialization |
| `Sandbox.yml` | `RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml` | Client IL whitelist (see HAZARDS §1) |

## Content bootstrap / managers

| Symbol | Location | Purpose |
|---|---|---|
| `GameServer`/`GameClient`/`GameShared` | `Content.{Server,Client,Shared}/Entry/EntryPoint.cs` | Content bootstrap and manager init |
| `ServerContentIoC`/`ClientContentIoC` | `Content.{Server,Client}/IoC/*` | Manager/system registrations |
| `GameTicker` | `Content.Server/GameTicking/GameTicker*.cs` | Round state machine, spawning, rules, lobby |
| `IServerDbManager` / `ServerDbManager` | `Content.Server/Database/ServerDbManager.cs` | Persistence facade |
| `MarkingManager` | `Content.Shared/Humanoid/Markings/MarkingManager.cs` | Marking prototype cache, species/sex filters, kind allowance |
| `SharedHumanoidAppearanceSystem` | `Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs` | Profile load/apply, marking add/visibility |
| `HumanoidAppearanceSystem` (client) | `Content.Client/Humanoid/HumanoidAppearanceSystem.cs` | Sprite/layer/marking rendering |
| `MarkingPicker` | `Content.Client/Humanoid/MarkingPicker.xaml.cs` | Marking editor UI (colors, scale/offset, leg style) |
| `HumanoidProfileEditor` | `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs` | Character editor; wiring for markings/leg style/loadouts |
| `StationSpawningSystem` | `Content.Server/Station/Systems/StationSpawningSystem.cs` | Spawns player mobs, applies loadouts |
| `SharedActionsSystem` | `Content.Shared/Actions/SharedActionsSystem.cs` | Action grants/removal (implant toggle action) |
| `SharedSubdermalImplantSystem` | `Content.Shared/Implants/SharedSubdermalImplantSystem.cs` | Implant insertion/removal |
| `InventorySystem` | `Content.Shared/Inventory/InventorySystem.cs` | Equipment slots and visuals |

## Frontier (`_NF`)

| Symbol | Location | Purpose |
|---|---|---|
| `BankSystem` | `Content.Server/_NF/Bank/BankSystem.cs` | Player bank accounts, ATM, transfers |
| `ShipyardSystem` | `Content.Server/_NF/Shipyard/Systems/ShipyardSystem*.cs` | Ship purchase/sale, consoles, records |
| `ShuttleRecordsSystem` | `Content.Server/_NF/ShuttleRecords/*` | Shuttle ownership/records |
| `MarketSystem` | `Content.Server/_NF/Market/*` | Market consoles/pricing |
| `CryoSleepSystem` | `Content.Server/_NF/CryoSleep/*` | Cryo storage/despawn |
| `NFCargoSystem` / cargo | `Content.Server/_NF/Cargo/Systems/*` | Frontier cargo economy |
| `BountyContractSystem` | `Content.Server/_NF/BountyContracts/*` | Bounty contracts |
| Pirate bounties | `Content.Server/_NF/Pirate/*`, `_NF/Cargo/Systems/NFCargoSystem.PirateBounty.cs` | Pirate bounty database/redemption |
| `NFGameTickerSpawning` | `Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs` | NF spawn/loadout/bank hooks (partial GameTicker) |

## Palmtree (`_PS`)

| Symbol | Location | Purpose |
|---|---|---|
| `SharedConcealableClothingSystem` | `Content.Shared/_PS/Clothing/SharedConcealableClothingSystem.cs` | Toggle action, implant checks, action refresh |
| `ServerConcealableClothingSystem` | `Content.Server/_PS/Clothing/ServerConcealableClothingSystem.cs` | Implant insert/remove category tracking |
| `ClientConcealableClothingSystem` | `Content.Client/_PS/Clothing/ClientConcealableClothingSystem.cs` | Removes equipment visual layers when concealed |
| `ConcealableClothing*Component` | `Content.Shared/_PS/Clothing/Components/*` | Data for clothing, implant category, user categories |
| `BluespaceDockSystem` | `Content.Server/_PS/BluespaceDock/` | Store/retrieve/sell player ships, registry file, admin ship tools, ship preview |
| `ShipRegistrarSystem` | `Content.Server/_PS/ShipRegistry/` | Register docked player-built ships (deed, records, IFF, marker) |
| `HilbertHotelSystem` | `Content.Server/_PS/HilbertHotel/` | Runtime hotel rooms: check-in, sweeps, locks, guest lists |
| `InteractionPanelSystem` | `Content.Server/_PS/Interactions/` (+ `Arousal`, `Ui` partials) | Data-driven social/lewd interactions, arousal, climax, ERP emotes |
| `GenitalOrganSystem` | `Content.Shared/_PS/Organs/` | Organ-based genitals: profile settings, render markings, visibility rules, drip |
| `SynthEmpSystem` / `DeadStartupButtonSystem` | `Content.Server/_PS/Synth/` | Synth EMP lethality and the reboot mechanic |
| `DeletionCensusSystem` | `Content.Server/_NF/GC/Systems/DeletionCensusSystem.cs` | Palmtree garbage collection (deep-space junk culling, touched/value protection) |
| Lobby rotation | `Content.Server/GameTicking/GameTicker.LobbyBackground.cs` | 30s background rotation + status broadcast |
| Lobby crossfade | `Content.Client/Lobby/LobbyState.cs` | `_currentBg`/`_nextBg` smoothstep fade |

## Floof/Coyote ported systems

| Symbol | Location | Purpose |
|---|---|---|
| `ModifyUndiesSystem` | `Content.Server/_Floof/ModifyUndies/ModifyUndiesSystem.cs` | Self-only show/hide verbs for undies/genitals |
| `ModifyUndiesDoAfterEvent` | `Content.Shared/_Floof/ModifyUndiesDoAfterEvent.cs` | Do-after payload (marking id, visibility) |
| `SetMarkingVisibility` | `SharedHumanoidAppearanceSystem` | Flips `Marking.Visible` and dirties the humanoid |
| `MarkingCategoriesConversion.Category2Layer` | `Content.Shared/Humanoid/Markings/MarkingCategories.cs` | Maps `Base*` categories to hidden base layers |
| `MarkingColoring.GetMarkingLayerColors` | `Content.Shared/Humanoid/Markings/MarkingColoring.cs` | Per-sprite colors, used by picker/renderer |

## CVars of note

| CVar | Default | Where |
|---|---|---|
| `game.defaultpreset` | `nfpirate` | `CCVars.Game.cs` |
| `game.map` / `game.map_pool` | `Frontier` / `NFMapPool` | `CCVars.Game.cs` |
| `audio.lobby_music_collection` | `PSLobbyMusic` | `CCVars.Audio.cs` (Palmtree change) |
| `accessibility.*censor_nudity` | false | `CCVars.Accessibility.cs` (undergarment censor path) |
