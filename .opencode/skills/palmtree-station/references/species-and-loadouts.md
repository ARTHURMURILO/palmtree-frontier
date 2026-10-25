# Species, Synth & loadouts reference

Deep dives: `.ai/systems/ps-systems.md`, `.ai/systems/player-character-and-jobs.md`,
`.ai/guides/adding-species.md`, `.ai/guides/adding-loadouts.md`.

## Species roster

| Species | Source | Notes |
|---|---|---|
| Human, Dwarf, Moth, Reptilian, SlimePerson, Vox, Diona, Arachnid, Skeleton, Gingerbread | core | kinds + Floof layers added |
| Harpy, Vulpkanin | `_DV` | digitigrade `altSprites` |
| Felinid, Oni | Nyanotrasen | |
| Goblin, Sheleg | `_NF` | digitigrade `altSprites` |
| Rodentia | `_DV` (ported) | 999-point tail/ear defaults, rat speech |
| Anthromorph | `_CS` (ported) | BasicHumanlike + BasicFurry, custom body sprites |
| Tajaran | `_EE` (ported) | body/markings/sprites/damage/names |
| **Synth** | `_PS` | IPC replacement (below) |
| IPC | — | not present; old saves fall back to Synth |

## Synth (IPC replacement)

Files:

- `Resources/Prototypes/_PS/Species/synth.yml` — species, marking points, IPC-sprite base sprites
  with `altSprites` to `DigilegSynthliz*`.
- `_PS/Entities/Mobs/Species/synth.yml` — mob + dummy.
- `_PS/Entities/Mobs/Player/synth.yml` — `MobSynth`.
- `_PS/Body/Parts/synth.yml` — silicon parts (`Inorganic` container, IPC sprites, legs
  `MovementBodyPart 3.75/6.75` = 1.5× standard).
- `_PS/Body/Organs/synth.yml` — eyes/pump/brain/tongue/ears.
- `_PS/Body/Prototypes/synth.yml` — body with `OrganSynthEyes`, `OrganSynthBrain`,
  `OrganSynthPump`.
- `_PS/Damage/{containers,modifier_sets}.yml` — `Synth` container (Brute/Burn + Radiation/
  Bloodloss) and modifier set (0.8 brute, immune poison/asphyxiation/cold, weak heat/shock).
- `_PS/Reagents/oxidant.yml` — coolant blood; locale in `_PS/reagents/oxidant.ftl`.
- `Content.Shared/_PS/Synth/SynthComponent.cs` — marker; `RespiratorSystem.Update` skips synths
  (no lungs, no gasping).

Properties: 1.5× speed (body parts drive it), 3× durability (`MobThresholds` 300/600), IPC-like
damage/resistances, `ZombieImmune`, `CanHostGuardian`, robot typing, insulated temperature, heavy
fixture density 462.5 (mass-based pull slowdown), sexes Male/Female/Unsexed, kinds
`BasicHumanlike`/`BasicFurry`/`BasicRobot`/`VoxLike` so any marking/adaptor applies.

Speed boosts are ignored: `MovementSpeedModifierSystem` flags the refresh event for
`SynthComponent` entities, so every positive modifier (speed boots, stimulants, held/wieldable speed)
is clamped out while slowdowns still stack. Keep `SynthComponent` networked - client movement
prediction needs the same rule.

Differences from old IPCs: no battery/power drain, no radio/encryption, no EMP/Silicon component,
human-sized organ set (synthetic), no guidebook entry.

**Import fallback**: `SharedHumanoidAppearanceSystem.FromStream` maps exports whose species no
longer exists to `Synth` (if present) else the default species. This is how old IPC saves import.

## Adding/porting a species

1. Species prototype (`species`), base sprites (`speciesBaseSprites`), marking points
   (`markingPoints`).
2. Mob entity (`BaseMobSpeciesOrganic`) + dummy (`MobHumanDummy` + species override).
3. Body parts + body prototype; damage set; speech/names/typing indicator/locale.
4. `kind` + all marking layers + sex-morph base sprites (`MobXHeadMale/Female`, `MobXTorsoMale/Female`)
   or they render wrong/invisible.
5. Optional `altSprites` on `humanoidBaseSprite` for digitigrade.
6. Remove unsupported fields (`minHeight`, `defaultHeight`, `maxHeight`, `minWidth`, `defaultWidth`,
   `maxWidth`, `averageHeight`, `averageWidth`, `guideBookIcon`, `customName`) and `Needs`.
7. Validate with `EntityTest` (spawns every entity) and lint.

## Loadouts

- Item entity first, then `- type: loadout` (equipment/storage/implants/hideEffects), then add the
  loadout id to a `loadoutGroup` `loadouts:` list. Large themed sets get their own group referenced
  from job groups via `subgroups:` (see `CoyoteJumpsuit`).
- Ported additions: concealment backpack implanter (`_PS`), `ContractorClicker` (`_NF` + `_DEN`
  item), `ContractorClothingBackpackSatchelLeather` (`_CS`), `LoadoutFootProtectors` (`_DEN`),
  Coyote Bayou clothing incl. latex (`_CS/CoyoteBayouClothes`).
- A loadout that is not in any group is unreachable.
- Locale: `loadout-name-<id>`, `loadout-group-*`; item names are usually inline.
- Spawn test template: any `Content.IntegrationTests/Tests/_PS` test
  (`StationSpawningSystem.SpawnPlayerMob` + `RoleLoadout`).
