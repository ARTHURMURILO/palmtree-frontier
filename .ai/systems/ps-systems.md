# System: Palmtree (`_PS`) Systems

## Purpose

`_PS` is Palmtree Station's own module: lobby presentation, the concealable-clothing backpack
implant, and the server-specific weapon/grenade set ported from the old codebase.

## Locations

- Code: `Content.Server/_PS`, `Content.Shared/_PS`, `Content.Client/_PS`.
- Prototypes: `Resources/Prototypes/_PS`.
- Locale: `Resources/Locale/en-US/_PS`.
- Textures/audio: `Resources/Textures/_PS`, `Resources/Audio/_PS`.

## Concealable clothing (backpack implant)

| Piece | Path |
|---|---|
| Shared logic | `Content.Shared/_PS/Clothing/SharedConcealableClothingSystem.cs` |
| Server logic | `Content.Server/_PS/Clothing/ServerConcealableClothingSystem.cs` |
| Client visuals | `Content.Client/_PS/Clothing/ClientConcealableClothingSystem.cs` |
| Components | `Content.Shared/_PS/Clothing/Components/{ConcealableClothing,ConcealableClothingImplant,ConcealableClothingUser}Component.cs` |
| Action | `Resources/Prototypes/_PS/Actions/concealable.yml` (`ActionToggleConcealment`, `...Backpack`) |
| Implant/implanter | `Resources/Prototypes/_PS/Entities/Objects/Misc/{subdermal_implants,implanters}.yml` |
| Effects | `Resources/Prototypes/_PS/Entities/effects.yml` (`BlueFlashEffect`) |
| Loadout | `Resources/Prototypes/_PS/Loadouts/Jobs/Contractor/implanter.yml` |
| Locale | `Resources/Locale/en-US/_PS/actions/clothing.ftl` |

How it works:

1. Base `ClothingBackpack` carries `ConcealableClothing` (`toggleAction: ActionToggleConcealmentBackpack`,
   `category: backpack`). This core patch is required — without it nothing can be concealed.
2. Injecting `ClothingConcealmentImplantBackpack` adds `ConcealableClothingUserComponent` with the
   category and refreshes actions for already-equipped clothing.
3. The item action toggles `IsConcealed`; the client clears equipment visual layers for concealed
   items; the server pops up text and spawns `BlueFlashEffect`.
4. Removing the implant removes the category and action.

## Lobby presentation

- Backgrounds + music: `Resources/Prototypes/_PS/lobby.yml` (21 `lobbyBackground` prototypes plus the
  `PSLobbyMusic` sound collection), textures `Resources/Textures/_PS/Lobby/*`, audio
  `Resources/Audio/_PS/Lobby/*`.
- **Track credits**: the lobby's "Playing: X by Y" line reads the ogg's Vorbis `title`/`artist`
  comments (`LobbyState.UpdateLobbySoundtrackInfo`), not the attribution files. A track converted
  without those comments shows "Unknown title/artist" - set both when adding one (mutagen,
  ffmpeg/oggenc, vorbiscomment). Fork tracks are attributed in
  `Resources/Audio/_PS/Lobby/attributions.yml`.
- **Debug commands** (`AdminFlags.Debug`, in `Content.Server/_PS/Commands/`):
  - `setlobbybackground <lobbyBackgroundId | reset>` - forces a background and pauses the 30s
    rotation until reset (`GameTicker.LobbyBackgroundOverride`).
  - `setlobbymusic <trackPath | soundCollectionId | reset>` - forces a single track or a whole
    collection until reset; the force survives round ends, and only clients currently in the lobby
    are notified (in-game clients never start lobby music mid-round).
- Music selection: `audio.lobby_music_collection` CVar default was changed to `PSLobbyMusic` in
  `Content.Shared/CCVar/CCVars.Audio.cs`.
- Rotation: `Content.Server/GameTicking/GameTicker.LobbyBackground.cs` re-rolls the background every
  30 seconds of game time and broadcasts `TickerLobbyStatusEvent` via `SendStatusToAll()`.
- Crossfade: `Content.Client/Lobby/LobbyState.cs` keeps `_currentBg`/`_nextBg` and animates a second
  `BackgroundFade` `TextureRect` (in `LobbyGui.xaml`) over 1 second with a smoothstep curve.

## Weapons / grenades / hitscan

- `Resources/Prototypes/_PS/Entities/Objects/Weapons/Guns/Battery/battery_guns.yml` — Svalinn Caspian,
  pulse N1984, phaser, medical gun.
- `.../Guns/Launchers/launchers.yml` — China Lake flashbang variants.
- `.../Guns/Projectiles/projectiles.yml`, `.../Guns/Ammunition/explosives.yml`,
  `.../Guns/Ammunition/Projectiles/hitscan.yml` (defines `PSXrayLaserCaspian`, `PhaserLethal`,
  `HealBeam`), `.../Throwable/grenades.yml`.
- `SoundCollections/weapons.yml` + `Resources/Audio/_PS/Weapons/*`, `Resources/Audio/_PS/Effects/Grenades/*`.

Adaptations made when porting (old 2023 prototypes):

- `prefix:` on `EntityPrototype` was removed → folded into `name:`.
- `inhandVisuals` moved from `HitscanBatteryAmmoProvider` to a `- type: Item` component.
- Gun spread fields (`minAngle`, `maxAngle`, `angleIncrease`, `angleDecay`, `fireRate`) must sit on
  `- type: Gun`, not inside `soundGunshot:`.
- `proto: PulseWeak` → `NFPulse` (Frontier hitscan id).
- `BallisticAmmoProvider.count` was removed → use `capacity`.
- `SoundOnTrigger` → `EmitSoundOnTrigger`, `OnUseTimerTrigger` → `TimerTrigger`.
- `noSpawn: true` → `categories: [ HideSpawnMenu ]`.

Not ported from `_PS`: turrets, RCD, strobe lighting, shipyard cauterizer, announcement/interaction
sound collections, custom emotes. See `.ai/PORTING.md`.

Re-enabled Frontier-disabled guns: Frontier deletes whole upstream weapon directories at load
(`Resources/IgnoredPrototypes/ignoredPrototypes.yml`), so the upstream taser, elite taser, tesla
gun, energy shotgun, minigun and AKMS were copied into `_PS` with `PS` ids
(`_PS/Entities/Objects/Weapons/Guns/.../reenabled_upstream_guns.yml`, `HMGs/minigun.yml`,
`Rifles/akms.yml` plus the `_PS` projectiles/cartridge they need). Pulse/laser/x-ray/advanced/antique
energy weapons were not copied - Frontier already ships enabled NF versions of them. Practice ammo
cases/crates/table/spawner were un-commented and nested back into the dungeon ammo tables. Coyote
ports in the same batch: prototype pulse rifle, EG-4 energy revolver, Anaconda, lollypop dispensers
and the C-19r SMG (see `.ai/PORTING.md` §15). The Stechkin APS is a BYOND port from Sandstorm
(chambered in .35 auto) available from T2 dungeon loot and the emagged WeaponryWorks vendor
(`.ai/PORTING.md` §16).

Shotgun fill verbs (`Content.Server/_PS/Gambling/RandomWeaponInsertSystem.cs`): adds
"Insert randomly", "Fill sequence" and "Shoot self" to any shotgun. The two fill verbs pull from
held/nearby ammo boxes (`Radius = 2`), always load a random total between 2 and the gun's free
capacity/available shells, and the public emote lists the count per shell type
("shoves 3 .50 buckshot and 2 .50 practice into ..."). `Fill sequence` repeats a shuffled cycle in
which each shell kind appears once or twice, so patterns vary between A-B-A-B, A-B-B-A-B-B, etc.
Guns with a capacity below 2, no room for two shells, or fewer than two available shells hide the
fill verbs entirely. Shell labels strip the "shell (...)" wrapper from `Name()`; update the
`ammo-fill-*` locale keys if the naming changes.

## Akimbo hand swap (2026-10)

`Content.Shared/_PS/Weapons/GunHandSwapSystem.cs` swaps the active hand after a gun is fired when
the other hand also holds a gun (`GunComponent`), so dual pistols/SMGs can be fired alternately
without manual hand switching. It runs on client and server (predicted).

- Selecting a hand normally applies a full hand-select cooldown (`SharedGunSystem.OnGunSelected`,
  `SharedMeleeWeaponSystem.OnMeleeSelected`) - this is what blocked akimbo. Rather than keeping that
  penalty or clearing cooldowns, an akimbo swap copies the just-fired gun's `GunComponent.NextFire`
  onto the partner (never shortening its own remaining cooldown) and shortens
  `MeleeWeaponComponent.NextAttack` to the same instant, so the pair alternates at the weapon's fire
  rate while ignoring the hand-select penalty. Manual hand switches and single-gun play keep normal
  cooldowns; `GunComponent`'s `[Access]` attribute lists `GunHandSwapSystem` so it may write
  `NextFire`.
- Knives/melee (no `GunComponent`) and unwielded two-handed guns (`GunRequiresWieldComponent`
  without `WieldableComponent.Wielded`) never trigger a swap; in-progress bursts are not
  interrupted. Covered by `GunHandSwapTest`.
- `.308` and 9mm SMG magazines are per-gun: BAR takes `CSMagazine308`/`CSMagazine308Ext`, the
  M1919 and RPD share the 100-round `CSMagazine308Rpd` drum (the belt container - the separate
  `CSMagazine308Belt` item was removed), DP27 the pan, SKS its clip, and the PPSh only its 71-round
  drum. `EveryGunOnlyAcceptsItsOwnMagazines` sweeps every CS magazine against every CS gun, so a
  whitelist can never silently accept a foreign magazine. The shared `CSBaseWeaponRifleChamber308`/`CSBaseWeaponSubMachineGunChamber9mm` whitelists
  only seed the default; concrete guns override them. Covered by
  `GunMagazineWhitelistTest`/`GunAmmoTest`.
- **Mosin internal magazine**: `CSBaseWeaponRifleChamber54R` is a `BallisticAmmoProvider` (5 rounds,
  no mag slot), so the stripper clip never enters the gun. `CSMagazine54RClip` uses the new
  `BallisticAmmoProviderComponent.MayTransferAll` (annotated upstream field) to fill the whole
  magazine in one action - faster than loading loose cartridges one at a time.
- Gun magazine layers use `zeroVisible: true` with a real `mag-0` art (usually a copy of `mag-1`):
  `RoundToLevels` maps `steps: 2` to level 0 for *any* partial count, so a `zeroVisible: false` gun
  loses its magazine after the first shot. With `zeroVisible: true` the layer is only hidden when
  the magazine is actually removed (`MagLoaded = false`). The DP-27 is the exception: its source has
  no usable pan art, so the gun is magless (`dp-e` base/bolt-open/icon) and has **no** magazine
  sprite at all - do not add a composed overlay.

## Synth species (IPC replacement)

`Synth` is Palmtree's replacement for the IPC species. It is a humanoid species that uses the
marking system to look like anything and approximates IPC traits without the Einstein Engines silicon
stack.

| Piece | Path |
|---|---|
| Species + base sprites + marking points | `Resources/Prototypes/_PS/Species/synth.yml` |
| Mob + dummy | `Resources/Prototypes/_PS/Entities/Mobs/Species/synth.yml` |
| Player mob | `Resources/Prototypes/_PS/Entities/Mobs/Player/synth.yml` |
| Body parts (1.5x leg speed) | `Resources/Prototypes/_PS/Body/Parts/synth.yml` |
| Body prototype | `Resources/Prototypes/_PS/Body/Prototypes/synth.yml` |
| Damage container + modifier set | `Resources/Prototypes/_PS/Damage/{containers,modifier_sets}.yml` |
| Coolant reagent | `Resources/Prototypes/_PS/Reagents/oxidant.yml`, locale `_PS/reagents/oxidant.ftl` |
| Species locale | `Resources/Locale/en-US/_PS/species/synth.ftl` |

Properties:

- **1.5x speed**: Synth legs carry `MovementBodyPart walkSpeed: 3.75 / sprintSpeed: 6.75` (human
  2.5/4.5). The `Body` system derives base speed from leg parts, so this must be done on the parts,
  not only on the mob's `MovementSpeedModifier`. (Higher values caused client prediction instability.)
- **3x durability**: `MobThresholds` 300 Critical / 600 Dead (human 100/200); `SlowOnDamage` and gib
  thresholds scaled.
- **IPC-like**: custom `Synth` damage container (Brute/Burn groups, Radiation/Bloodloss types),
  `Synth` damage modifier set (0.8 brute, immune poison/asphyxiation/cold, weak shock, extremely
  radiation-sensitive; no heat coefficient - see below), `ZombieImmune`, `CanHostGuardian`, robot
  typing indicator, insulated temperature, `Oxidant` coolant blood, fixture density 462.5 (heavier,
  matching IPC).
- **Temperature immunity**: `Temperature heatDamageThreshold: 4000` (only a 4000K+ plasma fire
  damages them; cold threshold 0 and `Cold: 0`). The old `Heat: 1.5` coefficient was removed, so
  no direct heat or burn source is amplified either - fire is purely visual (see below).
- **No reagent effects**: `OrganSynthEyes` metabolizes Food/Drink/Medicine/Cryogenic/Narcotic/Alcohol
  with `skipEffects: true` and `OrganSynthPump` covers Poison/Gas the same way, and
  `MetabolizerSystem` additionally skips all reagent effects for any body with `SynthComponent`
  (annotated patch). So food, medicine and poisons do nothing to a synth, good or bad - reagents are
  still metabolized/removed.
- **No hunger or thirst, but can eat**: synths have no `Hunger`/`Thirst` components, so they never
  need to eat and cannot starve. They do have a `stomach: OrganSynthStomach` ("nutrient tank", human
  stomach sprite as a placeholder) in the body prototype, so eating and drinking work if the player
  wants to - the MetabolizerSystem guard means it has no effects either way.
- **Space-proof**: `PressureImmunity` on `BaseMobSynth` sets `BarotraumaComponent.HasImmunity`, so
  the sealed chassis takes no low-pressure damage in vacuum (on top of the breathing skip and the
  `Cold: 0` damage modifier).
- **Fire, lava and liquid plasma are harmless**: `BaseMobSynth` overrides `Flammable.damage` with
  `Heat: 0`, so being set on fire is purely visual. Lava and liquid plasma only apply
  `FlammableReaction`/`Ignite` tile effects (no direct damage), so a synth standing in either just
  catches fire cosmetically. The `Temperature` threshold (4000K) covers environmental heat.
- **EMPs are deadly**: `SynthComponent.EmpDamage` (100 Shock) is applied on every `EmpPulseEvent`
  by `Content.Server/_PS/Synth/SynthEmpSystem.cs`, and the 1.5x shock weakness multiplies it to 150
  per pulse. EMP grenades, ion/EMP bolts and rockets, EMP mines and implants, xenoarch EMP
  artifacts and the EMP generator all share this path, so one pulse takes half a synth's health
  and a second is fatal, while non-synths (no `SynthComponent`) are unaffected.
- **Synthetic repair topicals**: `RepairPatch`, `RepairSpray`, `RepairSprayPlus` and the
  single-use `RepairAutoinjector` (`_CS/Entities/Objects/Specific/Medical/healing.yml`, ported
  from coyote-bayou with the `Synth` damage container added) cover brute, burn, cold, shock,
  radiation and bloodloss. They print from the medical/exosuit/mercenary techfabs
  (`CSIPCTopicalsStatic` pack) and the patch can also be hand-crafted (steel + cable construction
  graph).
- **Reboot**: `DeadStartupButton` on `BaseMobSynth` adds a "Reboot" verb to a dead synth
  (`Content.{Shared,Server}/_PS/Synth/DeadStartupButton/`, ported from the EE IPC mechanic via
  coyote-bayou). It applies a small repair first (like a defib zap) and revives the synth if the
  remaining damage is below the death threshold, otherwise the chassis is "way too damaged".
- **Built-in jaws of life**: `BaseMobSynth` has a `Prying` component (`pryPowered: true`,
  `force: true`, `flatPryTime: 0.5`) so synths can force doors open bare-handed via the "Pry door"
  verb (or by clicking a door that refuses to open) in exactly 0.5s - the airlock's powered (x9) and
  bolted (x3) pry-time multipliers are ignored, while crowbars, jaws and other pryers are
  unaffected. A forced pry can also force an open bolted door shut (the door safety check still
  refuses to close on someone standing in the doorway). Welded doors and `canPry: false` doors
  (e.g. blast doors) still stop them. No item needed. Hand-prying also plays the pry sound.
- **Silicon parts**: the Synth body uses `_EE/Mobs/Species/IPC/parts.rsi` sprites (robot limbs/head),
  `Inorganic` damage container on parts, and synthetic organs (`OrganSynthEyes`, `OrganSynthPump`,
  `OrganSynthBrain`, plus unused `OrganSynthTongue`/`OrganSynthEars`). Base sprites default to the
  robot look; species-adaptor markings replace them.
- **No breathing**: `SynthComponent` marks the mob and `RespiratorSystem.Update` skips it, so synths
  do not gasp or suffocate despite having no lungs.
- **Digitigrade**: Synth base sprites carry `altSprites` to the `DigilegSynthliz*` markings; the
  Coyote leg-displacement system (`LegDisplacementPrototype`, `HumanoidAppearanceComponent.LegDisplacements`,
  `_CS/LegDisplacement.yml`) is ported and wired into `ClientClothingSystem` so jumpsuit/shoes/
  outerclothing get digileg displacement maps. Leg style is chosen in the marking picker.
- **Any look**: kinds `BasicHumanlike`/`BasicFurry`/`BasicRobot`/`VoxLike` plus all marking layers
  and unlimited points, so species-adaptor markings can replace the base robot parts. All
  species-restricted markings now carry `kindAllowance` (Coyote behavior), so any species can wear
  them.
- Sexes: Male/Female/Unsexed.
- Old exports that reference the removed `IPC` species are automatically imported as Synth
  (`SharedHumanoidAppearanceSystem.FromStream` fallback).

Dragging: `PullingSystem.OnRefreshMovespeed` was patched so a pulled entity heavier than the puller
scales the puller's speed down (`clamp(pullerMass / pulledMass, 0.35, 1)`). A Synth (~178 kg vs ~71 kg
human) slows the puller noticeably; light objects are unaffected.

Differences from old IPCs (by design): no battery/power drain, no radio/encryption holder, no
`Silicon` component (EMP/battery interactions), no silicon guidebook entry, no tongue/ear organ slots
in the body prototype.

## Dependencies

Upstream actions/implants/inventory/equipment visuals, `ContentAudioSystem`/lobby CVar, GameTicker,
`_NF` contractor loadout groups, human body parts/organs and marking system.

## Tests

`HilbertHotelTest` covers room-map loading and the hotel lifecycle; `InteractionPanelTest` covers
the interaction/organ plumbing; `SynthTest` covers synth physiology (reboot, pressure immunity,
heat/shock/radiation, EMP lethality, built-in prying) and that bodies no longer rot. Everything
else is validated by the build, the YAMLLinter and in-game checks — do not add a new test file per
feature.

## Character customization (2026-10)

- **Custom species name**: `HumanoidCharacterProfile.Customspeciesname` (DB column
  `customspeciesname`), `SpeciesPrototype.CustomName` (defaults to `true` here), editor
  `CCustomSpecieNameEdit` (`HumanoidProfileEditor`), used by `GetSpeciesRepresentation` (examine)
  and `StationRecordsSystem` (records).
- **Emote category picker**: `HumanoidCharacterProfile.HiddenEmoteCategories` (DB column
  `hiddenemotecategories`, comma-separated), `EmoteCategoryWindow` opened from the traits tab,
  wheel filtering in `EmotesUIController.IsHiddenCategory`, `EmotePrototype.ShowInWheel`.
  `EmoteCategory` is Coyote's `ushort` flag set (`Vulp`, `Felinid`, `Borg`, ...); `Sex`/`Vocal`
  are always visible.
- **Supplemental emote sounds**: `VocalComponent.SupplementalSounds` (default
  `SupplementalCoyoteEmoteStuff`) lets any vocal mob use any emote sound; `ChatSystem.TryPlayEmoteSound`
  checks the species collection first, then the supplemental one. Server-side `AllowedToUseEmote`
  returns true for everything (Coyote behavior).
- Emote content: `_Floof/Voice`, `_EE/Voice`, `_PS/Voice`, `_DEN/Voice`, `_Funkystation/Voice`,
  `_CS/Voice` + `_CS/emotes_but_cooler.yml`, `_CS/speech_emote_sounds_but_cooler.yml`.
- **Admin bypass (playtime only)**: admins don't count towards playtime requirements for jobs and
  loadout tiers. Whitelists, role bans, and species/age/traits checks still apply. Implemented via
  `JobRequirement.IsPlaytimeRequirement` + the `ignorePlaytime` flag in
  `JobRequirements.TryRequirementsMet`, the client `JobRequirementsManager.CheckRoleRequirements`,
  and `LoadoutEffect.Validate` (`RoleLoadout.IsValid/IsHidden` thread the `isAdmin` flag from
  `ServerPreferencesManager`/`ClientPreferencesManager`/`StationSpawningSystem`).
- **`exportcharacters`**: server console/admin command (`Content.Server/Administration/Commands/ExportCharactersCommand.cs`)
  dumps every saved profile to `UserData/exported_characters/<ckey>/character-<slot>.yml` using the
  editor's `HumanoidProfileExport` format. DB support: `IServerDbManager.GetAllCharacterProfiles`.

## Longer round tuning (2026-10)

- Generator fuel burn rates scale to ~2.7x the original duration (the 3 h Pacman tank now lasts
  ~8 h): Pacman/SuperPacman `optimalBurnRate` 0.0041667 (~8 h), JrPacman 0.0416667 (~6.7 h),
  DK 0.0028125 (~2.7x).
- AME jars: `AmeFuelContainerComponent` default 1333, `AmeJarBig` 4000 (~1.9 h / ~5.6 h at the
  default injection rate, ~2.7x the originals).
- Welders: `WelderComponent.FuelConsumption` 0.375 and `FuelLitCost` 0.1875 (~2.7x longer).
- Disabled station events: pest migrations (`CalmPestEventsTable`/`SpicyPestEventsTable`/
  `NFCalmPestEventsTable` are now `!type:NoneSelector`) and power shut-off (`BreakerFlip`,
  `PowerGridCheck`, `NFBreakerFlip`, `NFPowerGridCheck` removed from their tables).
- Forensic pad `ScanDelay` 0.5 s; SSD bodies with a mind skip `RespiratorSystem` updates.
- Body rotting is disabled (no `Perishable` on `BaseMobSpecies`/`SimpleSpaceMobBase`, nor on the
  mobs that redefined it: cow, xenos, hellspawn, baby dragon, argocyte), so corpses never rot and
  stay defibrillatable; food shelf life (`FoodMeatRawBase`, `FoodHivelordRemains`) is untouched.

## Currency and lobby branding (2026-10)

- Currency display renamed to "space roubles" everywhere user-facing (prototype `name:` fields,
  locale, accents, briefcases, cartridge name); prototype ids (`Speso`, `Spesso`, `SpaceCash`,
  `Credit`) are unchanged. Bank display LocIds (`bank-currency-display-spesos*`) are defined in
  `Resources/Locale/en-US/_NF/bank/currency.ftl`.
- Lobby: only `_PS` backgrounds/music remain active (`_NF/lobbyscreens.yml`,
  `SoundCollections/lobby.yml`, `_NF/SoundCollections/lobby.yml` and upstream `Blueprint` commented
  out); `splashLogo` points at `_PS/Logo/logo.png` (extracted from the old `coyote/palm3` branch).

## Slime transformation (2026-10)

Slimepeople have an innate action to turn into a slime and back, built on the stock polymorph
system (`inventory: None`, `transferDamage/Name: true`, `revertOnDeath/Crit: true`).

- Form: `Resources/Prototypes/_PS/Entities/Mobs/SlimeForm.yml` (`MobSlimeForm`, a simple mob using
  the animated `Mobs/Aliens/slimes.rsi` blob, slime speech/typing/accent, no inventory or hands).
  `EffectSlimeTransform` is the green flash + squish effect spawned on transform.
- Polymorph: `Resources/Prototypes/_PS/Polymorphs/slime.yml` (`SlimeForm`), granted through
  `PolymorphableComponent.innatePolymorphs` on `BaseMobSlimePerson`. The revert action is granted
  automatically by the polymorph system.
- **Inventory/ID/bank retention**: `inventory: None` means the original body is parked (paused map)
  with clothing, held items, ID card, bank component, organs and records intact, and restored
  untouched on revert - nothing is dropped or transferred to the inventory-less form.
- **`SlimeFormSystem`** (`Content.Server/_PS/Polymorph/SlimeFormSystem.cs`) hooks
  `PolymorphedEvent` (via `PolymorphableComponent`) and copies the `BankAccountComponent` and the
  interaction-panel preferences (consent, sounds, favorites, multipliers) onto the form. The bank
  balance lives in the player profile, so the component is a cache and the copy cannot duplicate
  money. The hook only runs for `MobSlimeForm`; other polymorphs are untouched.
- **Damage** scales both ways via `MobThresholdSystem.GetScaledDamage` (dead-threshold ratio), so
  being hurt as a slime hurts the body proportionally and transforming is not a heal.
- **No irreversible warning**: the slime actions are reversible, so `SlimeFormSystem` strips the
  `ConfirmableActionComponent` from the innate forward action (on `MapInitEvent` of
  `PolymorphableComponent`, after `PolymorphSystem` created it) and from the form's revert action
  (in the `PolymorphedEvent` handler). Only actions whose `PolymorphActionEvent.ProtoId` is
  `SlimeForm` (and the slime form's revert action) are touched; other polymorphs keep the warning.
- **Weak to water, but not instantly lethal**: `MobSlimeForm` carries the stock slime `Reactive`
  water reaction, and `BaseMobSlimePerson`'s was scaled up from `Heat: 0.1` to `Heat: 0.75` per
  unit (`scaleByQuantity`, `ignoreResistances`). Thrown/spilled water
  (`PuddleSystem.TrySplashSpillAt`), extinguisher vapor (`VaporSystem` collisions) and puddle slips
  (`PuddleSystem` slip reaction) all route through
  `ReactiveSystem.DoEntityReaction(..., ReactionMethod.Touch)`, so a splash hurts but does not
  instantly kill. The water `Gas` (inhaled) effect is still `Heat: 3` per unit.
- **Prolonged water contact is lethal**: `SlimeWaterContactComponent` marks slimes and
  `SlimeWaterContactSystem` (`Content.Server/_PS/Slime/`) applies a fixed `ContactQuantity` (10
  units) of Water as a touch reaction once per second while the slime stands in a water puddle
  (`PuddleSystem.TryGetPuddle` + any solution containing Water) or on a water tile
  (`FloorWaterEntity`). At the current 0.75/unit that is ~7.5 damage/second, so standing in water
  kills in roughly 15-25 seconds.
- **Death revert** is stock `revertOnDeath: true` (the polymorph `Update` loop reverts dead forms);
  verified with a scratch test that the parked body is restored at the form's position and the form
  is deleted.
- The SS13 transformation animation is not ported yet: the BYOND checkout was unmounted when this
  was written, so the transform uses the animated slime sprite + `slime_squish.ogg` + a green flash
  as a placeholder.

## Nutrition rate tuning (2026-10)

- `Content.Shared/_PS/Nutrition/PalmtreeNutrition.cs` holds `DecayRateMultiplier = 1f / 6f`.
- `HungerSystem.DoHungerThresholdEffects` and `ThirstSystem.UpdateEffects` multiply the computed
  `ActualDecayRate` by it (marked `// Palmtree`), so every species and NPC - including the
  per-species `baseDecayRate` overrides (Chitinid 33% faster, Avali/Resomi 2x, Diona and Sheleg,
  etc.) - now takes 6x longer to get hungry or thirsty.
- Change the one constant to retune; hunger and thirst share it.

## Weapon detector (2026-10)

SS13 scanner-gate guns-mode port (S.P.L.U.R.T-tg `code/game/machinery/scanner_gate.dm`,
`icons/obj/machines/scangate.dmi`): a walk-through arch that beeps when a gun carrier steps
onto its tile. Guns-only; no multi-mode TGUI.

- Code: `Content.Shared/_PS/WeaponDetector/WeaponDetectorComponent.cs` (scan modes + visual
  enums), `Content.Server/_PS/WeaponDetector/WeaponDetectorSystem.cs` (step-trigger handler, gun
  scan, bypass, alarm, examine hint).
- Prototypes: `Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml` -
  `PSWeaponDetector` (any gun; needs anchoring + power), `PSWeaponDetectorLethal`
  (`scanMode: LethalOnly`, skips `ToySidearm`-tagged guns), `PSWeaponDetectorSecBypass`
  (`scanMode: SecurityBypass`, silent for mindshield carriers and `Armory`-access IDs), and
  `PSWeaponDetectorDebug` (ignores power/anchoring, for testing).
- Physics: `hard: false` sensor fixture + `StepTrigger` (`stepOn: true`,
  `requiredTriggeredSpeed: 0`). The fixture is declared in full on purpose:
  `FixturesComponent.Fixtures` is `[NeverPushInheritance]`, so a partial `fix1:` override wipes
  shape/mask/layer and the gate never gets contacts with mobs (this was a real bug - the gate
  silently did nothing).
- Sprites: `Resources/Textures/_PS/Structures/Machines/weapon_detector.rsi` - `base` plus
  green/cyan/red scanline overlays (`passive`/`scanning`/`alarm`), 4 directions, switched by a
  `GenericVisualizer`; `Rotatable` with `rotateWhileAnchored` (Transform `noRot: false`).
- Feedback: SS13 `scanbuzz.ogg` (`Resources/Audio/_PS/Machines/`), large-caution popup, 2 s beep
  cooldown, scanline flash duration; `Off` hides the scanlines while unanchored/unpowered.
- Machines spawn anchored (`BaseStructure` sets `Transform anchored: true`); the usual reason a
  freshly spawned detector does nothing is missing power - `ApcPowerReceiver` examine says
  "un-powered", and the Debug variant skips both checks.
- Toy guns carry the `ToySidearm` tag; `FoamCrossbow` and the water-gun base were tagged for the
  lethal-only variant.
