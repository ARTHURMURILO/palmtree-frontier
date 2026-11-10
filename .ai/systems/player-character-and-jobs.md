# System: Player, Character, Jobs & Species

## Purpose

How a connection becomes a character with a profile, species, markings, loadouts and job; plus the
species roster in this repo.

## Locations

| Piece | Path |
|---|---|
| Sessions/players | `Content.Server/Players/*`, engine `ICommonSession` |
| Preferences | `Content.Server/Preferences/*`, `Content.Shared/Preferences/*` |
| Profile data | `Content.Shared/Preferences/HumanoidCharacterProfile.cs`, `HumanoidCharacterAppearance.cs` |
| Species prototypes | `Resources/Prototypes/Species/*`, `_DV/Species`, `_NF/Species`, `Nyanotrasen/Species`, `_CS/Species`, `_EE/Species` |
| Species mobs | `Resources/Prototypes/Entities/Mobs/Species/*` and prefix equivalents |
| Jobs/loadouts | `Resources/Prototypes/Roles/Jobs/*`, `Resources/Prototypes/_NF/Loadouts/*` |
| Job systems | `Content.Server/Station/Systems/StationJobsSystem.cs`, `StationSpawningSystem.cs` |
| Mind | `Content.Server/Mind/*`, `Content.Shared/Mind/*` |
| Markings | see `.ai/systems/marking-and-appearance.md` |

## Flow

1. Client connects; `PlayerSystem`/`PlayerManager` track sessions.
2. Character editor edits `HumanoidCharacterProfile` (name, species, appearance, markings, loadouts,
   traits, job priorities, bank balance). Profile saved to DB on slot save.
3. Round spawn: `GameTicker` → `RulePlayerSpawningEvent` → `StationSpawningSystem.SpawnPlayerMob`
   (job + profile + station) → NF bank/loadout hooks → `HumanoidAppearanceSystem.LoadProfile`.
4. Mind attaches to the mob (`MindSystem`); job roles/objectives attach to the mind.
5. Disconnect/cryo persists the profile; mob is deleted or stored.

## Prone / crawling rotation

`Content.Server/_PS/Standing/ProneRotationSystem.cs` flips downed/crawling humanoids so their head
points toward horizontal movement. It updates `RotationVisualsComponent.HorizontalRotation`
(270° = head right/east, 90° = head left/west; north/south keeps the last side) on `DownedEvent`,
`MoveEvent` and `UnbuckledEvent`. Buckled mobs are skipped so bed strap rotation still owns the
angle, and the client `RotationVisualizerSystem` now also reacts to `AfterAutoHandleStateEvent`
(`RotationVisualsComponent` is generated with `raiseAfterAutoHandleState`) so networked horizontal
angle changes animate immediately.

## Species roster (this repo)

| Species | Source | Notes |
|---|---|---|
| Human, Dwarf, Moth, Reptilian, SlimePerson, Vox, Diona, Arachnid, Skeleton, Gingerbread | core | all have `kind` + Floof layers |
| SlimePerson | core | Palmtree: breathes and drinks plasma harmlessly (`Plasma` reagent got Slime `Oxygenate`/`Drink` effects, poison effects excluded for Slime); innate **SlimeForm** polymorph action (see `.ai/systems/ps-systems.md`) |
| Harpy, Vulpkanin | `_DV` | have digitigrade `altSprites` |
| Felinid, Oni | `Nyanotrasen` | |
| Goblin, Sheleg | `_NF` | have digitigrade `altSprites` |
| Rodentia | `_DV` (ported) | 999-point tail/ears defaults, digileg support, rat speech |
| Anthromorph | `_CS` (ported) | BasicHumanlike + BasicFurry, custom body sprites |
| Tajaran | `_EE` (ported) | body/markings/sprites/damage/names, Felinid component |
| Synth | `_PS` (new) | IPC replacement: 1.5× speed, 3× durability, marking-based appearance, IPC-like resistances; immune to cold and to heat below 4000K, and reagent effects are skipped (`skipEffects`) |
| IPC | — | **not present**; replaced by Synth (see `.ai/systems/ps-systems.md`) |

All playable species carry `kind` lists so Floof/Coyote markings apply via `kindAllowance`.

## Jobs and loadouts

- Jobs are prototypes under `Resources/Prototypes/Roles/Jobs` + `_NF/Roles`; loadout groups are
  `loadoutGroup` prototypes; jobs reference groups/subgroups.
- The contractor chain (`_NF/Loadouts/contractor_loadout_groups.yml`) is the common case; Palmtree
  added `CoyoteJumpsuit` as a subgroup in 19 job group files, plus entries for the concealment
  implanter, clicker, leather satchel and foot protectors.
- Palmtree also added the `ContractorMechSuit` group (`minLimit: 0`, "mech suit" category) to
  `JobContractor`: the three SS13 mech suit uniforms (`ClothingUniformMechSuitRed/White/Blue`,
  sprites in `_PS/Clothing/Uniforms/MechSuit/*.rsi`).
- `RoleLoadout` stores selected loadouts per group; `StationSpawningSystem` applies them.
- **Job whitelists are disabled** in the config presets (`Resources/ConfigPresets/_NF/frontier.toml`,
  `hypatia.toml`, `maunder.toml`, `Build/development.toml`): `game.role_whitelist = false`. Playtime
  requirements (`game.role_timers`) are untouched, and admins bypass playtime only.

## Markings in the profile

- Markings live in `HumanoidCharacterAppearance.Markings` and are persisted in the profile JSON
  (DB string format, see `.ai/PERSISTENCE.md`).
- `LegStyle` is part of the appearance and is exported/imported.
- Traits exist upstream but the 46 Coyote trait ids are not ported; unknown traits are filtered on
  import.

## Voice barks

- `HumanoidCharacterProfile.VoiceBark` (`ProtoId<SpeechSoundsPrototype>?`) is a Palmtree addition.
  `null` ("Default (species)") keeps the speech sounds baked into the mob prototype.
- `SharedHumanoidAppearanceSystem.LoadProfile` applies the choice as a
  `Content.Shared/_PS/Speech/VoiceBarkOverrideComponent` (server side) and removes it when the
  profile has no voice, so reloading a default profile (DNA scramble, admin spawn) restores the
  species voice. `SpeechComponent.SpeechSounds` itself is never modified.
- `SpeechSoundSystem.GetSpeechSound` (server) checks the override before the component's speech
  sounds; surveillance camera speakers go through the same path.
- The character editor lists every `speechSounds` prototype (Default first, then alphabetically by
  localized name). Names come from `voice-bark-<lowercase-id>` keys in
  `Resources/Locale/en-US/_PS/preferences/voice-barks.ftl`; missing keys fall back to the id, and
  new `speechSounds` prototypes appear automatically.
- The `▶` preview button plays the selected bark (or the species default, resolved from the species
  mob prototype's `SpeechComponent`) locally with `Filter.Local()`; the previous preview stream is
  stopped before a new one starts.
- Persisted as the nullable `Profile.VoiceBark` (`voice_bark`) column; see `.ai/PERSISTENCE.md`.
- Export/import carries `VoiceBark` through `HumanoidCharacterProfile` serialization; `EnsureValid`
  drops ids that no longer exist.

## Ghosts / antags

- Ghost/observer systems are upstream; upstream antag presets are inert (`rules: []`).
- NF pirate/adventure presets provide the live round flavor; `_NF/GameRule` and `Events` add
  station events.

## Unknowns

- Which jobs use which loadout groups on the live server.
- Whether the Synth ever gets the full IPC silicon stack (battery/radio/EMP); see `.ai/PORTING.md`.
