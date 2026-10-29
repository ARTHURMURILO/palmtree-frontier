# System: Core Gameplay (Summary)

> Upstream SS14 systems that the fork builds on. Only the parts most relevant to this fork are called
> out; verify behavior in the engine/upstream source.

## Interactions and verbs

- `InteractionSystem` + `GetVerbsEvent<T>` power context menus and interaction verbs.
- `ModifyUndies` uses `GetVerbsEvent<Verb>` (per-marking toggle opt-in) — see `systems/marking-and-appearance.md`.
- `SharedHandsSystem` handles pickup/drop; `ItemToggle`, `UseDelay`, `DoAfter` gate actions.

## Body, damage and health

- `BodySystem`/`BodyPartComponent` model limbs/organs; `DamageableSystem` applies damage via
  `damageModifierSet` prototypes.
- `MobStateSystem` (Alive/Critical/Dead), `StaminaSystem`, `BloodstreamSystem`, `RespiratorSystem`.
- Ported species supply body prototypes and damage sets (Rodentia/Tajaran/Anthromorph).

## Atmos, power, chemistry, construction

- Atmos: pipe networks, vents/scrubbers, gas mixtures; `_NF/Atmos` adds Frontier variants.
- Power: `PowerNet` + `Pow3r` debug tool; solars, generators, batteries, `_NF/Power`.
- Chemistry: reagent prototypes + reactions; metabolism in `MetabolismSystem`.
- Construction: construction graphs + `ConstructionSystem`; `_NF/Construction` adds Frontier
  recipes. Lathe recipes under `Resources/Prototypes/Recipes`.

## Shuttles

- `ShuttleSystem` (+ partials FTL/Impact/Docking), `ShuttleConsoleSystem`, `_NF/Shuttles`,
  `_NF/Shipyard` for purchase/drydock.
- Map format is 7; grids are separate entities; FTL uses `FTLDestinationComponent` prototypes.

## NPCs

- `NPCSystem` + HTN tasks/operators; `_NF/NPC` adds Frontier NPCs for expeditions/pirates.
- Palmtree **roomba** (`MobRoomba`, `_PS`): a cleanbot variant that behaves like a real robot
  vacuum instead of planning routes. `RoombaSystem` (`Content.Server/_PS/Roomba`) drives it through
  `NPCSteeringSystem` direct moves: straight legs with a persistent heading, a bump-and-turn on
  stalled progress (including the engine's own anti-stuck `NoPath`), and it never targets a tile
  without solid floor — path samples plus a momentum margin keep it from rolling off the grid (its
  `TransformComponent.GridTraversal` is disabled so the engine does not detach it, and a watchdog
  rubber-bands it back if it still overshoots). Legs are picked in two passes: tiles first, then a
  broadphase pass for hard-collidable **obstacles** (walls, machines) and **living mobs**, so it
  steers around them; if everything ahead is blocked it takes the least bad option and bumps, which
  is the roomba charm. Its fixtures use `SmallMobMask`/`SmallMobLayer` (like mice) and its sprite
  `drawdepth: SmallMobs`, so it rolls **under tables and chairs** and renders beneath them too. Cleaning happens underfoot: every 0.45 tiles it scrubs `Cleanable`
  decals around itself (cum drips/ejaculate, grime, crayon — the space cleaner set), mops puddles
  on its tile using the cleanbot's own water tank (sound rate-limited), and vacuums loose
  `Trash`/`Cartridge`/`GoblinMade` items into a replaceable internal `TrashBag` (the bag itself
  supplies the whitelist). Items overlapping a `PlaceableSurface` (tables, beds, crates) are
  skipped: SS14 has no z-levels, so "on the table" is the only way to tell them apart from the
  floor. **Roombas spawn switched off**; left-clicking (or activating) toggles power (`Appearance`
  + `GenericVisualizer` dim the sprite, and the NPC is slept while off). Right-click verbs toggle
  **trash collection** (decals/puddles keep being cleaned when off) and **mute** the voice module,
  and take a full bag. `RoombaComponent` is
  **shared** and `SharedRoombaSystem` cancels `AttemptMobTargetCollideEvent` for it (the prone-mob
  trick), so it never shoves mobs on either side — clients predict their own pushes. It also talks:
  rare bump lines (25%, 6 s cooldown), very rare idle lines (90-240 s) and less rare comments when it
  sees a character without a jumpsuit or outer clothing (`Resources/Locale/en-US/_PS/roomba.ftl`).
  Speech is sent with `hideChat`/`hideLog`, so it appears as an overhead speech bubble but never in
  chat history (the vending-machine pattern). The entity's HTN is only a wait loop that keeps the
  NPC awake. Crafted in the utilities
  construction menu with the cleanbot parts plus a mop.
- `RandomHumanoidSpawner` markers self-delete on MapInit; for preset looks use static mobs with
  `HumanoidAppearance.initial`.

## Radio/speech/chat

- Chat/radio/speech systems are upstream; no consent/censor contributors are registered here.
- Palmtree/Floof subtle chat (`/subtle`, `/subtlelooc`, ported from Coyote): 1.5-tile range, blocked
  by walls, never sent to ghosts — including admin ghosts and ghosts with ghost hearing toggled off.
  `SendEntitySubtle`/`SendSubtleLOOC` filter listeners with `ChatSystem.IsGhostListener` on top of
  `ChatTransmitRange.NoGhosts`; subtle emotes render smaller and uncapitalized. Implemented in
  `ChatSystem.SendEntitySubtle` / `SendSubtleLOOC` with commands in
  `Content.Server/Chat/Commands/Subtle*Command.cs`.
- Subtle chat is also selectable in the chat box (`ChatSelectChannel.Subtle`/`SubtleLOOC`, prefixes
  `-` and `=`, focus keybinds `FocusSubtle`/`FocusSubtleLOOCWindow`) and can play a notification
  sound (`CCVars.SubtleSoundEnabled`, Audio options tab) driven by `ChatMessage.IsSubtle`.
- Ported species add speech sounds/verbs/typing indicators (Rodentia, Tajaran).

## Lighting/storage/misc

- Standard upstream systems; `_PS` strobe lighting was not ported.
- Storage/containers are upstream; concealable clothing manipulates equipment visuals, not storage.

## Hazards

- Engine/API drift is the main risk (see `.ai/HAZARDS.md`); verify against the pinned engine.
