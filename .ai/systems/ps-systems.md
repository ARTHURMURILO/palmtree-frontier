# System: Palmtree (`_PS`) Systems

## Purpose

`_PS` is Palmtree Station's own module: lobby presentation, the concealable-clothing backpack
implant, the server-specific weapon/grenade set ported from the old codebase, and the ship
persistence consoles (Bluespace Dock, Ship Registrar) with their ship preview.

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

- `Resources/Prototypes/_PS/Entities/Objects/Weapons/Guns/Battery/battery_guns.yml` — Svalinn 770
  (prototype id stays `NFWeaponEnergyPistolLaserSvalinnCaspian`), pulse N1984, phaser, medical gun.
  All four are admin-spawn only and are declared with `StaticPrice: 0` so spawning them cannot
  inject money into the economy.
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

Not ported from `_PS`: turrets, strobe lighting, shipyard cauterizer, announcement/interaction
sound collections, custom emotes. (The `_PS` **RCD/RPD** were ported in 2026-10 — see
`.ai/PORTING.md` §24.)

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
- **Silicon parts**: the Synth body uses `Resources/Textures/_EE/Mobs/Species/IPC/parts.rsi` sprites
  (robot limbs/head), `Inorganic` damage container on parts, and synthetic organs (`OrganSynthEyes`,
  `OrganSynthPump`, `OrganSynthBrain`, plus unused `OrganSynthTongue`/`OrganSynthEars`). Base
  sprites default to the robot look; species-adaptor markings replace them.
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
heat/shock/radiation, EMP lethality, built-in prying) and that bodies no longer rot.
`BluespaceDockTest` covers the dock registry/ship-file round trip and its self-heal sweep,
`BluespaceDockSaveTest` covers the save-side hazards (a used plant analyzer / an in-progress do-after
must not fail a save, migrated files must not keep references to dropped contents, and radar targets
must not outlive the ship they point at), `BluespaceDockPowerTest` covers APC power after a
store/retrieve round trip and the load-time re-link, `BluespaceDockExpeditionTest` covers a retrieved
expedition ship keeping its station's expedition data, `BluespaceDockShipSystemsTest` and
`BluespaceDockDeviceLinkTest` cover the map-init registration (guns, emitters, device links,
container contents), and `ShipRegistrarTest` covers registration turning a docked grid into a
civilian vessel. Everything
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
  `EmoteCategory` is Coyote's flag set (`Vulp`, `Felinid`, `Borg`, ...) widened to `int` for
  Palmtree's `Funny` bit; `Sex`/`Vocal` are always visible.
- **Funny emotes (SPLURT)**: `EmoteCategory.Funny = Vocal | (1 << 16)`, 116 SPLURT emotes
  (`Laugh2`-`Laugh6`, `Cackle`, `Bruh`, the gachi/Ace Attorney sets, ...) in
  `_PS/Voice/speech_emotes_funny.yml`. They are `available: true` with no species restriction, so
  `*laugh2`/`*laugh3` work for every mob; sounds come from
  `_PS/Voice/speech_emote_sounds_funny.yml` via `SupplementalFunnyEmotes` (parent of
  `SupplementalCoyoteEmoteStuff`). Only the 33 headline emotes set `showInWheel: true` - the ring
  cannot fit all 116 - but every trigger works. The category can be hidden in the picker.
  Chat triggers send the prototype's localized `ChatMessages` text instead of the raw trigger
  (`SendEntityEmote`), and `Objection`/`HoldIt`/`TakeThat` use
  `VocalComponent.SexSupplementalSounds` (`SupplementalFunnyMale`/`Female`) for male/female voices.
  `emotelist` dumps all triggerable emotes as `[*emote = "text"]`, three per line.
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

- `Content.Shared/_PS/Nutrition/PalmtreeNutrition.cs` holds `DecayRateMultiplier = 1f / 2f`
  (was `1f / 6f`, retuned to 2x slower).
- `HungerSystem.DoHungerThresholdEffects` and `ThirstSystem.UpdateEffects` multiply the computed
  `ActualDecayRate` by it (marked `// Palmtree`), so every species and NPC - including the
  per-species `baseDecayRate` overrides (Chitinid 33% faster, Avali/Resomi 2x, Diona and Sheleg,
  etc.) - takes 2x longer to get hungry or thirsty.
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

## Shuttle console: Dock All (2026-10)

A "Dock All" button on the shuttle console dock screen, under Frontier's "Undock All". It sends
every free dock of the current shuttle and the server tries to dock each one; ports that cannot
dock are silently skipped (same as clicking "Dock" on them), and a failure popup only appears if
nothing docked at all.

- Message: `Content.Shared/_PS/Shuttles/Events/DockAllRequestMessage.cs` (list of the shuttle's
  free dock entities).
- Server: `OnRequestDockAll` in `Content.Server/Shuttles/Systems/DockingSystem.cs` - validates the
  console's shuttle (`CanShuttleDock`), collects every anchored dock on other grids, and for each
  listed port docks it to the first candidate passing `CanDock` (range + alignment + dock-type
  mask). Receive-only and already-docked ports are skipped, as are candidates on the shuttle's own
  grid.
- Client: `DockAllButton` in `Content.Client/Shuttles/UI/DockingScreen.xaml`, event plumbing in
  `DockingScreen.xaml.cs`, `ShuttleConsoleWindow.xaml.cs` and `ShuttleConsoleBoundUserInterface.cs`
  (all marked `// Palmtree`). Enabled whenever the shuttle has at least one undocked port.
- Locale: `Resources/Locale/en-US/_PS/shuttles.ftl` (`shuttle-console-dock-all`).
- Docking geometry gotcha: `CanDock` uses the world-angle convention (0 = south) and
  `Vector2.ToWorldAngle()` adds pi/2 - two docks at the exact same position are *not* aligned
  (zero vector -> pi/2), so scratch tests must offset the ports by a fraction of a tile.

## Expedition rattle return (2026-10)

When a body is recovered from an expedition it is marked with `ExpedRecoveredBodyComponent`
(`Content.Server/_PS/Salvage/Components`); when its shuttle lands back on the main map,
`SalvageSystem.RetriggerRecoveredRattles` removes the marker and raises `RetriggerRattleEvent`
(`Content.Shared/_PS/Implants`), which the implant relay forwards to any rattle implants so medics
get a "STILL dead/critical" message with real coordinates. While on the expedition map the rattle
says "on an expedition" instead of useless coordinates. Locale:
`Resources/Locale/en-US/_PS/salvage.ftl`.

## Jukebox skip (2026-10)

The jukebox menu gained a **Skip** button next to Play/Stop; it advances to the next queued track
(or stops when the queue is empty) instead of only being able to queue something new.

- Message: `Content.Shared/_PS/Audio/JukeboxSkipMessage.cs`; the handler lives in the core
  `Content.Server/Audio/Jukebox/JukeboxSystem.cs` (`// Palmtree`) because `JukeboxComponent` is
  `[Access(typeof(SharedJukeboxSystem))]` and a `_PS` system cannot write `SelectedSongId`.
- Behaviour: stops the current audio stream (`Audio.Stop` queues the stream entity for deletion) -
  the same path a track takes when it finishes on its own, so the `JukeboxMusicComponent` shutdown
  handler advances the queue. With an empty queue the selection is cleared first, so the jukebox
  goes idle instead of holding the song (or repeat-requeueing it). Do **not** clear
  `JukeboxComponent.AudioStream` before the deletion runs, or the shutdown handler will not
  recognise the stream and the next song never starts.
- Client: `SkipButton` in `Content.Client/Audio/Jukebox/JukeboxMenu.xaml` + event in
  `JukeboxMenu.xaml.cs`, wired to the message in `JukeboxBoundUserInterface.cs` (all `// Palmtree`).
- Locale: `jukebox-menu-buttonskip` in `Resources/Locale/en-US/_PS/jukebox.ftl`.

## Theobromine intolerance trait (2026-10)

Theobromine (chocolate/coffee/tea) used to poison any species whose organs carry the `Animal`
metabolizer type. It is now an opt-in **trait**:

- Trait: `Resources/Prototypes/_PS/Traits/traits.yml` (`TheobromineIntolerance`, Disabilities
  category) adds `TheobromineIntoleranceComponent` (`Content.Shared/_PS/Traits`), which is also in
  the clone whitelist (`Resources/Prototypes/Entities/Mobs/Player/clone.yml`).
- Condition: `Content.Shared/_PS/EntityEffects/TheobromineIntolerance.cs` - a plain
  `EntityEffectCondition` that checks for the marker component on the target; the reagent
  (`Resources/Prototypes/Reagents/toxins.yml`, both the `HealthChange` and the `ChemVomit` effects)
  uses `!type:TheobromineIntolerance` instead of `!type:OrganType type: Animal`.
- Locale: `Resources/Locale/en-US/_PS/theobromine.ftl` (trait name/desc + guidebook line).

## Character database export fix (2026-10)

`exportcharacters` threw `ArgumentException: Path must be rooted` for every profile because
`ExportCharactersCommand` built a *relative* `ResPath` (`new ResPath("exported_characters")`) and
`IWritableDirProvider.CreateDir`/`OpenWriteText` require rooted paths
(`WritableDirProvider.GetFullPath` asserts `path.IsRooted`). The command now roots the directory
with `ResPath.FromRelativeSystemPath(directory).ToRootedPath()` and prints the physical path via
`root.ToRelativePath()`.

## Admin meme items (2026-10)

The maintainer's roundly admin upload (kept in the local, untracked `.ai/workspace/Upload.yml`) is now a proper prototype set.
Everything is admin-only (no lathe, loot table, uplink or vending machine references them) and
carries `suffix: Admeme` so it is easy to find in the spawn menu:

- `PortableGeneratorBlyatman` (the purple cone light only lights while the generator runs, through
  the `_PS` `GeneratorRunningLight` marker + `GeneratorRunningLightSystem`, which replaced the
  original upload's `SlimPoweredLight`) -
  `Resources/Prototypes/_PS/Entities/Structures/Power/Generation/portable_generator_blyatman.yml`.
  JrPacman parent, 190 kW target power, burns Vodka through `ChemicalFuelGeneratorAdapter`, purple
  rotating cone light and `BlyatmanGeneratorHum` ambience. The original upload's `clockstepConstant`
  no longer exists on `FuelGeneratorComponent` and was dropped. Frontier's generator radiation code
  (`GeneratorSystem.TryUpdateGeneratorRadiation`) only runs for generators that carry a
  `RadiationSourceComponent`; portable generators (JrPacman and this one) do not, so the Blyatman
  does **not** radiate (only the RTGs and the Bananium DK do). If it ever gets a `RadiationSource`,
  `radiationIntensity * targetPower` would be 19 rad/s at its 190 kW target.
- `MobDimitry` - `Resources/Prototypes/_PS/Entities/Mobs/NPCs/dimitry.yml`: vulpkanin body with a
  Russian accent, the generator hum as ambience and pressure/step-trigger/flash immunity plus
  insulation.
- `DrinkBottleVodkaInfinite` - `.../Objects/Consumable/Drinks/vodka_infinite.yml`: bottomless vodka
  through `SolutionRegeneration`.
- `FoodKebabSkewerDeath` and `FoodPotatoInfinite` - `.../Objects/Consumable/Food/meme_food.yml`:
  150 piercing kebab skewer ("pocket immovable rod") and a self-refilling radioactive potato.
- `ClothingHandsGlovesRedStar` - `Resources/Prototypes/_PS/Clothing/Hands/gloves.yml`: north star
  gloves with a Russian accent and boxing-red sprites.
- Audio: `Resources/Audio/_PS/Machines/blyatman/blyatman{1,2,3}.ogg` with an `attributions.yml`
  (custom, provided by the maintainer) and the `BlyatmanGeneratorHum` collection in
  `Resources/Prototypes/_PS/SoundCollections/blyatman.yml`.
- Test: `Content.IntegrationTests/Tests/_PS/AdmemePrototypesTest.cs` spawns every item and checks
  that the collection's sound files exist.

## Bluespace Dock + Ship Registrar (2026-10)

Two admin/SR consoles that give player-built ships a persistence path, plus a live ship preview.

### Bluespace Dock (`ComputerBluespaceDock`)

| Piece | Path |
|---|---|
| Shared (component, UI key/state/messages, ID slot + deed helper) | `Content.Shared/_PS/BluespaceDock/` |
| Server (registry, save/load, store/retrieve/sell flows) | `Content.Server/_PS/BluespaceDock/` |
| Client (window + rows + BUI) | `Content.Client/_PS/BluespaceDock/` |
| Prototype | `Resources/Prototypes/_PS/Entities/Structures/Machines/Computers/bluespace_dock.yml` |
| Sprite (copy of the shipyard console states) | `Resources/Textures/_PS/Structures/Machines/bluespace_dock.rsi` |
| Locale | `Resources/Locale/en-US/_PS/bluespace-dock.ftl` |

- **Persistence**: `UserData/bluespace_dock/registry.yml` is the authoritative list of stored ships
  (`BluespaceDockEntry`: ship id, file, owner ckey + character name, vessel prototype id, model,
  full name, suffix, recorded value, timestamp); `UserData/bluespace_dock/<shipId>.yml` is the ship
  payload written by `MapLoaderSystem.TrySaveGrid`. Ownership is per **character** (ckey *and* ID
  card name must match), so another character of the same player does not see the ship. Both writes
  happen synchronously at store time (`TrySaveGrid` and `SaveRegistry` write straight to `UserData`)
  and the original grid is deleted only afterwards, so a crash cannot lose a stored ship and nothing
  waits for a round end. The registry is cached per round, re-read on round restart, and
  `PurgeMissingEntries()` drops entries whose
  ship file has disappeared (self-heal, also runs on first load). Retrieval also drops an entry
  whose file no longer parses as YAML (corrupt); a file that parses but fails to load is kept, since
  that can be a transient content error.
- **Store**: ID with a deed + live ship, docked to the console's station, no organics aboard
  (`ShipyardSystem.FoundOrganics`), vessel group in `{Shipyard, Scrap, Expedition}` (NFSD/black
  market/SR/unknown are refused — hand-built ships must be registered first), bank covers the
  `nf14.bluespace_dock.store_tax` (10%) tax. Then: **evacuate** (see below), undock,
  `TrySaveShipFile` (IncludeNullspace, Ignore fallback, then re-read + YAML-parse the file), charge,
  write the registry entry, strip the deed from the ID and delete the station + grid. If the save
  fails, the stripped components are put back, the deed is reassigned, the ship is re-docked and
  nothing is charged.
- **Evacuation (`EvacuateGrid`, 2026-10-15)**: before a ship is stored, admin-deleted or sold, every
  ghost and mob on it (pets, corpses, even a live player the checks missed) is moved to the grid the
  ship is docked to (at the partner airlock) or the default map when it is not docked; mobs inside
  containers are taken out first. Nothing alive is saved into the ship file or deleted with the grid.
  The shipyard's own sale path only preserved ghosts (`preserveOnSale`), so pets used to be destroyed
  by a sale.
- **In-flight projectiles are never saved (`DeleteProjectiles`, 2026-10-16)**: `TrySaveShipFile`
  (and the admin-delete/sale paths) delete a ship's shots - anything flying over the grid and
  anything whose weapon/shooter is on it - before saving/deleting. A stored bolt would come back
  frozen at its position, and a shot left in the world would keep pointing at a weapon that leaves
  with the ship (`ProjectileComponent.Weapon`/`Shooter` are networked `EntityUid`s; referencing a
  deleted one errors during game state serialization until the projectile despawns). Files written
  before this rule can still contain projectiles; `TryLoadShipFile` drops them on load.
- **Names**: a deed is stored split into a base name and a registration suffix (`ShipNameHelper` in
  `Content.Shared/_PS/ShipRegistry`). `BluespaceDockEntry.Name` holds the base name and
  `DisplayName` is rebuilt from the normalized parts on every store and retrieve, so the suffix
  cannot accumulate ("Dove NX-674 NX-647") - the first version stored the full name as the base
  name. The registrar shows a live full-name preview and never appends a suffix the name already
  ends with.
- **Docking is runtime state**: `DockingComponent.DockedWith`/`DockJointId` are `[DataField]`s, so
  they *are* serialized. The dock clears them (and any orphan joint whose partner is gone) before
  saving and again when loading, because a file that says the ship is docked makes the next dock
  attempt reuse a joint id that is already taken ("the joint already existed for the connected
  entity").
- **Migration on load** (`CollectMissingPrototypes` + `TryWriteSanitizedShipFile`): an update can remove
  an entity prototype a stored ship uses, and the engine's loader **refuses the whole file** over a
  single missing prototype (`EntityDeserializer.ValidatePrototypes`), which would make the ship
  undockable. Before loading, the dock scans the file, and when something it uses is gone it writes a
  sanitized copy with those entities **and everything parented to them** (their contents) removed, loads
  the copy and deletes it; the original is left untouched for recovery and a warning lists what was
  dropped. Removed *components* and removed *fields* need no help (the deserializer logs and skips
  them), and *renamed* or migration-listed prototypes are handled by the engine/content
  (`MapMigrationSystem` fills `BeforeEntityReadEvent` globally, so it applies to ship files too). The
  engine's own `DeletedPrototypes` hook is deliberately **not** used: it spawns those entities without
  their prototype, which throws on components with required fields (`MeleeWeaponComponent.damage`, for
  instance) and takes the whole load down.
- **Captain on retrieve**: retrieval writes the owner a `GeneralStationRecord` with the `Captain` job
  on the ship's station (`StationRecordsSystem.CreateGeneralRecord`, mirroring a shipyard purchase)
  and adds `BluespaceDockConsoleComponent.NewAccessLevels` (Captain by default) to their ID. Without
  the record the ship came back with no crew at all - no captain, nothing on the manifest. The Ship
  Registrar does the same at registration.
- **Action cooldowns are cleared on save (`ResetRuntimeCooldowns`, 2026-10-19)**: use delays, gun
  `NextFire` and melee `NextAttack` are absolute round times, and the round clock restarts every
  round. Without the reset a ship stored late in a round came back with its buttons, switches and
  toolbelts still "on cooldown" for as long as the store happened into the round: left click and E
  silently do nothing while right-click verbs (which bypass `UseDelay`) still work. The save clears
  them; see `.ai/HAZARDS.md` section 27 for the full trap and the regression test
  (`BluespaceDockSaveTest.SavedShipForgetsActionCooldowns`).
- **One unserializable entity no longer fails a store (2026-10-22)**: the save first tries strictly
  (`IncludeNullspace` then `Ignore`). When something on the grid cannot be serialized at all (a
  component field the serializer has no data definition for), it retries with
  `EntityExceptionBehaviour.IgnoreEntityAndChildren`, writes the file without that entity (and its
  transform children - container contents are transform children of their container, so a dropped
  machine loses its contents), and records the skipped entities under `droppedEntities` in the file
  (prototype, local position, rotation, anchored). Entities that lived **inside a container** (fridge,
  toolbox, machine) are recorded with their container (`containerOwner` prototype + grid-local
  position, `containerId`) and are re-inserted into it on load; contents of *nested* containers (a
  toolbox inside a backpack) are not covered. `TryLoadShipFile` then spawns a **fresh copy** of each
  (`TrySpawnDroppedEntities`) - at its saved position for top-level entities, at its container's tile
  and inside the container for contents; mobs and ghosts are never recorded. The rewritten file also
  prunes container/storage references to uids it no longer declares. Diagnostics:
  `grep "could not be serialized and were skipped"` in the server log.
- **Store feedback (2026-10-22)**: every store denial from the console is a popup *and* a red server
  message in that player's chat (`Deny` -> `WarnChat`, private `DispatchServerMessage`), and a store
  that had to skip items also warns in chat with `bluespace-dock-warning-skipped` (and in the server
  log). `forcedock` prints the same reason via `shell.WriteError` and the skip count as an error line
  in the admin console.
- **Failed stores restore the ship (2026-10-22)**: the store strips station references, the deed and
  the docking *before* saving, so any failure after that point must undo it. The `TryStoreShip`
  exception path now calls `RestoreStoreFailure` too (it used to just log), and the restore re-docks
  to the grid the ship was docked to at store time - it used to pass the ship's *own* station, whose
  largest grid is the ship itself. A ship left stripped is station-less, which is what made its
  machinery stop working after a failed store.
- **Smart fridges are saveable (2026-10-22)**: `SmartFridgeComponent.ContainedEntries` (the UI entry
  cache: `Dictionary<SmartFridgeEntry, HashSet<NetEntity>>`) is a mapping-keyed dictionary with
  session-scoped refs and cannot be serialized at all - a live save of a used fridge threw
  "Caught exception while serializing component SmartFridge" and failed the whole store. It is no
  longer a `[DataField]`; `SmartFridgeSystem` rebuilds it from the container on `ComponentStartup`
  and `MapInit` (startup runs for loaded ships, which skip MapInit), so the fridge and its contents
  survive. This is persistence-only: insertion/ejection behave exactly as before.
- **Retrieved ships get the shipyard's station (`GetShipStationConfig`, 2026-10-18)**: the station a
  ship lives on carries its capabilities, not the grid. Retrieval used to rebuild *every* ship with a
  plain `StandardFrontierVessel` station, which silently cost expedition ships their
  `BaseStationExpeditions` (`SalvageExpeditionDataComponent`): their salvage expedition consoles came
  back with nothing to read (`SalvageSystem.UpdateConsole` finds no data on the station) and every
  button disabled - the ship was no longer expedition capable. Retail: mirror the shipyard's purchase
  path (`ShipyardSystem.Consoles.TryPurchaseShuttle`), which takes the station config from the
  vessel's **game map prototype** (`GameMapPrototype.Stations[vesselId]`, keyed by the vessel id, e.g.
  `Aegis` -> `StandardFrontierExpeditionVessel`). Ships with no vessel prototype (hand-built ones)
  still fall back to `StandardFrontierVessel`. Entries stored before the fix carry the vessel id, so
  an existing stored ship is repaired by its next retrieval.
- **No pre-save power-down, no MapInit re-raise** (both inherited from Coyote's drydock, both removed):
  powering down fuel/charge systems wrote `on: false` / `switchedOn: false` into the file, so a
  retrieved ship came back unpowered (its APC off, the bridge door refusing to open). Re-raising
  `MapInitEvent` on top of the engine's own initialization is also wrong: a live save carries
  `mapInit: true` on every entity, and the loader deliberately skips MapInit for those - forcing it
  re-runs every spawn-time fill (`StorageFill`, `ContainerFill`, ...) and duplicates or spills the
  contents a player already has in their bags/crates.
- **`mapInit: true` skips MapInit on load (2026-10-16 fix)**: because the loader only bumps the life
  stage for post-init entities, registration work that normally happens in MapInit never runs for a
  retrieved ship. That silently broke **buttons and logic** (devices were never re-joined to the
  device network), shuttle cannons (their sink ports were missing), and **guns/emitters**
  (`GunComponent`'s modified values - fire rate, projectile speed, angles - are computed in
  `SharedGunSystem.OnMapInit` via `RefreshModifiers`; without it a saved gun refuses to fire and
  emitter/A.P.E. bolts spawn with zero velocity and "stay in place"). `TryLoadShipFile` now calls
  `RestoreMapInitRegistration` after loading, which does *only* the registration: `ConnectDevice`
  for every auto-connecting `DeviceNetworkComponent`, `EnsureSinkPorts` for
  `GunSignalControlComponent` (cannons), and `RefreshModifiers` for every `GunComponent`. Do **not**
  replace this with a blanket MapInit re-raise - inventories depend on it. Also note device network
  packets are processed **one tick later** (`DeviceNetworkSystem.SwapQueues`), so a button's effect
  is never visible in the same tick.
  **Fault tolerance (2026-10-18)**: the sweep is per entity in a `try`/`catch` (a single entity that
  throws used to skip the registration of every entity after it), and it also **re-links power**:
  every `ExtensionCableReceiverComponent` without a provider is connected to the nearest APC again
  (`ExtensionCableSystem.Connect`). A machine whose APC link is missing is otherwise "un-powered"
  until a player deconstructs and rebuilds it.
- **One unserializable field fails the whole save (2026-10-18)**: a live save writes every component
  on the grid, and a `[DataField]` whose type has no `[DataDefinition]` throws
  ("No data definition found for type X when writing"), so the file is never written and the dock
  reports `bluespace-dock-error-save-failed` (nothing is charged or deleted - the store is undone).
  Two offenders were fixed: the plant analyzer's `PlantInfo` scan memory is runtime UI state and is
  no longer saved, and `DoAfterId` is a data definition now (**in-place core patch** in
  `Content.Shared/DoAfter/DoAfter.cs`, marked Palmtree) because nine components store one as a
  `[DataField]` (climbing, digging, scanning, escaping inventory, ...). Details and diagnosis:
  `.ai/HAZARDS.md` section 25.
- **Dead radar targets (2026-10-18)**: `RadarConsoleComponent.TargetEntity` is a saved `EntityUid`,
  so a console can end up pointing at a ship the dock stored, sold or deleted. Every console state
  refresh (all of them refresh whenever any ship docks) then logs
  `Can't resolve MetaDataComponent` for the dead target. The dock clears targets that point at a
  ship before deleting it (`SharedRadarConsoleSystem.ClearTargetsPointingAt`) and both console
  systems drop dead targets before using them (`ClearTargetIfDead`).
- **Migration drops container references (2026-10-18)**: `TryWriteSanitizedShipFile` removes entities
  whose prototype disappeared, and now also removes `ContainerContainer` (`ents` and `ent`) and
  `Storage.storedItems` references to them - a dangling reference deserializes to
  `EntityUid.Invalid` and logs "unknown entity yaml uid" per reference. Idea (and the test shape)
  from HardLight's `ShipSaveYamlSanitizer`; their save system is reviewed in the local, untracked
  `.ai/workspace/hardlight-ship-save-review-2026-10-18.md` - they never load ships back, but their
  `FilteredTypes`/`FilteredPrototypes` lists are a useful checklist when an
  "unresolved MetaDataComponent" or "Encountered deleted entity 0" report comes in.
- **What a ship file may not contain**: `TrySaveShipFile` strips the deed and everything referencing
  the ship's station (`StationMemberComponent`, `StationBoundObjectComponent`) *before* serializing —
  the station is a mapped entity the file cannot carry, and stale references make the deserializer
  log invalid-uid errors and make `MapInit` handlers throw (the holopad station-name rename calls
  `Name()` on an invalid station). The deed is rebuilt from the registry on retrieval.
  `TryLoadShipFile` strips the same components again for files written before this cleanup and
  deletes the half-loaded grid and its map if the load throws.
- **Retrieve**: `nf14.bluespace_dock.retrieve_tax` (5%) of the *recorded* value, no other *live*
  ship (one with a valid station) flying under the same name, file present; loads onto a new map
  (the engine initializes it), recreates the `StandardFrontierVessel` station + name, restores
  `ExtraShuttleInformationComponent.Vessel` when known, `TryFTLDock` to the console's station, adds
  `DeletionCensusExemptComponent` + `LinkedLifecycleGridParentComponent`, writes the deed on the
  ship and the ID, then removes the registry entry (the file is kept).
  **Hardening (2026-10-15):** the tax is withdrawn *before* the ship is handed over (a failed
  withdrawal deletes the freshly loaded grid again), the loaded grid joins `_busyShips` for the
  duration, and any exception after the load consumes the entry - so a retry can never materialize
  a second copy of the same ship.
- **Sell**: the per-row `[SELL]` (`ConfirmButton`) pays `nf14.bluespace_dock.stored_sell_rate` (0.95)
  of the recorded value and deletes file + entry; the bottom `[SELL]` sells the docked ship through
  `ShipyardSystem.TrySellShuttle` exactly like a shipyard console (with the shipyard sell blockers
  applied for the civilian key, and `SetupShipyardIfNeeded` first because `TrySellShuttle` refuses
  every sale while the shipyard parking map does not exist).
- **Announcements**: stores, retrievals and sales are announced on the console's radio channel
  (`BluespaceDockConsoleComponent.AnnounceChannel`, default `Traffic` - the channel displayed as
  Shortband, the same one the shipyard console uses) and spoken by the console locally, exactly like
  the shipyard console's purchase/sale messages (`RadioSystem.SendRadioMessage` +
  `ChatSystem.TrySendInGameICMessage`). Strings: `bluespace-dock-announce-{store,retrieve,sell}`.
- **Console**: admin spawn only (`suffix: Palmtree`, indestructible, no board, `ActivatableUIRequiresPower`),
  sprite is a copy of `_NF`'s shipyard states — do not point it at `_NF/Structures/Machines/computers.rsi`.
- CVars (in `Content.Shared/_NF/CCVar/NFCCVars.cs`, marked Palmtree): `nf14.bluespace_dock.enabled`,
  `.store_tax`, `.retrieve_tax`, `.stored_sell_rate`, `.max_ships` (0 = unlimited).

### Admin tool: orphaned ships (`restoreorphanship`)

`Content.Server/_PS/BluespaceDock/RestoreOrphanShipEui.cs` + `Content.Client/_PS/BluespaceDock/` (window
`RestoreOrphanShipWindow`), opened by the `restoreorphanship` admin command (`AdminFlags.Admin`,
`Content.Server/_PS/Commands/RestoreOrphanShipCommand.cs`). "Orphaned" means a ship file in
`UserData/bluespace_dock/` that no registry entry points at (retrieval keeps the file on purpose, and
a crash can leave files behind). The tool:

- lists orphans with their save time (`BluespaceDockSystem.GetOrphanedFiles`, `TryDeleteOrphanedFile`,
  `TryMoveFile`),
- previews one with `TryLoadOrphanedFile`, which loads it onto a **paused** map and opens the normal
  `ShipPreviewEui`; the grid and map are deleted when the preview window closes, when another file is
  previewed, when the tool closes, or after one minute as a safety net (`ShipPreviewEui` takes an
  optional `onClosed` callback for this),
- can delete every orphan at once (`DeleteAllOrphanedFiles` behind the window's red-confirm DELETE ALL
  button) - the registry and its entries are untouched,
- restores one into a character's dock with `TryRestoreOrphanedFile`: it loads the file once for the
  appraisal, model and vessel identity, moves the file back into the ship directory under a fresh id
  and writes a registry entry (refusing when that character already has a ship with that name). The
  owner is the player picked in the dropdown - the server resolves their character from the ID card
  they carry (falling back to their mob's name), so there is no name to type - and the tool refuses
  without a player. No automatic sweeping, moving or expiry exists any more - the tool is the only
  thing that touches orphans.
- **Identity without an ID** (`BluespaceDockSystem.GetCharacterName` / `PickCharacterName`): the
  character name comes from the player's **profile** (`IServerPreferencesManager` ->
  `SelectedCharacter`, the same source the bank keys per-character money on), then the inserted ID
  card, then the mob's name. A lost, replaced or renamed ID therefore does not lock a character out
  of their ships, and the admin restore needs no ID on the player's side at all. An ID card is still
  required in the console slot, because the **deed** (Frontier's ship ownership document) is written
  onto an ID and read from there by the shipyard and the dock - that is a Frontier mechanic, not the
  dock's storage.
- **Ownership matching** (`BluespaceDockSystem.EntryBelongsToCharacter`): a normal entry needs the
  ckey *and* the character name to match, so another character of the same player does not see it. An
  entry with an **empty ckey** (written before a player could be picked, or by hand) matches by
  character name alone - that is the only way a name-keyed entry can ever be used, since an ID card
  does not carry a ckey.

### Admin tools: player ships (`manageships`, `forcedock`)

- `manageships` (`Content.Server/_PS/Commands/ManageShipsCommand.cs`, EUI `ManageShipsEui` + client
  `ManageShipsWindow`) lists **player ships by deed** - not by connected player, so ships whose owner
  is offline are included - in a dropdown (`BluespaceDockSystem.GetDeedShips`, with owner and docked
  state), previews one with the live ship camera and deletes one (`TryDeleteShip`, which clears every
  deed pointing at the grid).
- `forcedock <ship name | grid uid> [owner]` (`ForceDockCommand`) stores a ship in the dock without
  requiring it to be docked. Every other store check still runs (`FindStoreBlocker`, so "living things
  aboard" and the civilian-vessel rule still apply) and failures are printed to the console. The owner
  defaults to the deed owner; when the given name matches a player in the round their ckey is recorded
  too, otherwise the entry is keyed by character name alone. No tax is charged. The store mechanics
  live in `BluespaceDockSystem.TryStoreShip`, shared with the dock console's own store button, and
  both go through `EvacuateGrid` first (ghosts, pets and corpses are moved off, never deleted with
  the grid).

### Ship Registrar (`ComputerShipRegistrar`)

- `Content.Shared/_PS/ShipRegistry/`, `Content.Server/_PS/ShipRegistry/`,
  `Content.Client/_PS/ShipRegistry/`, prototype
  `Resources/Prototypes/_PS/Entities/Structures/Machines/Computers/ship_registrar.yml` (sprite copy
  `Resources/Textures/_PS/Structures/Machines/ship_registrar.rsi`, the SR shipyard states).
- Privileged slot = Station Representative's ID (the console's `AccessReader` requires
  `HeadOfPersonnel`, which the SR has through `AllAccess`; admins bypass). The **owner is picked from
  a dropdown of players** (`GetOwnerOptions`, keyed on their profile name like the dock) - there is
  no second ID slot, and the owner has to be in the round holding their ID card, which takes the
  deed. Registration also writes them a `GeneralStationRecord` with the `Captain` job on the ship's
  station and adds `NewAccessLevels` (Captain) to their ID, exactly like a shipyard purchase.
- Lists every grid docked to the console's station that has no deed and no
  `RegisteredVesselComponent` (`GetDockableGrids`), with name/size/value and a live `[PREVIEW]`.
- Registration (`TryRegisterVessel`, all checks re-run server side) creates the
  `StandardFrontierVessel` station, renames the grid (IFF/radar name), stamps
  `RegisteredVesselComponent { Group = Shipyard, Model = "Custom" }`, writes the deed on the ship and
  the owner's ID, adds a `ShuttleRecord` and the life-cycle components, and charges
  `nf14.ship_registrar.fee` (default 0).
- The dock's civilian check accepts registered ships through the marker (`TryGetVesselInfo`).
- **Warp points**: registration (and retrieval) makes sure the ship has a named `WarpPointComponent`
  (`BluespaceDockSystem.EnsureShipWarpPoint` / `FindShipWarpPoint`), the way shipyard ship files carry
  one - a hand-built ship never had one (it did not come from a ship file), and a file's warp keeps
  the name it was saved with. Hand-built ships get theirs at the grid's centre; `UseStationName` is
  turned on so Frontier's station rename sync (`StationRenameWarpsSystems`) keeps the name in step
  with the ship. That makes the ship reachable with the admin `warp` command and in the ghost warp
  menu (which any player sees), exactly like shipyard ships.
- **Radar / IFF**: registration (and retrieval, for files written before this) adds the player-shuttle
  `IFFComponent` a shipyard vessel gets from `BaseVessel` (`SharedShuttleSystem.AddIFFFlag(grid,
  IFFFlags.IsPlayerShuttle)` + `SetIFFColor(grid, Color.White)`). `ShuttleNavControl` only draws an
  IFF label when the grid *has* an `IFFComponent`, so registered ships used to be anonymous contacts
  on the mass scanner and radar even though their grid carries the deed name.
- **Currency**: every amount the consoles print is formatted with
  `BankSystemExtensions.ToIndependentString` (same number formatting as the bank) and the currency
  name comes from the `bank-currency-display-spesos` locale message (Palmtree renamed the display to
  "space roubles") - never a hardcoded currency word in `_PS` locale strings.

### Ship preview

`Content.Server/_PS/ShipRegistry/ShipPreviewEui.cs` + `Content.Client/_PS/ShipRegistry/ShipPreviewEui.cs`
(+ `ShipPreviewWindow`/`ShipPreviewControl`) are the admin camera's mechanism without the admin
powers: a hidden `ShipPreviewCamera` entity (visibility layer 8, `Eye`) is spawned at the ship's AABB
centre, `SharedViewSubscriberSystem.AddViewSubscriber` force-sends it to the viewer, the client
attaches the eye through `EyeLerpingSystem` and zooms with the mouse wheel (initial zoom fits the
ship). EUIs resolve the camera entity on a later frame because EUI state arrives before the gamestate.
**Hardening (2026-10-15):** the registrar's preview message only accepts grids that
`GetDockableGrids` would offer (docked to the console's station, unregistered, a `MapGridComponent`),
opening a preview replaces any existing one for that session (no camera accumulation), and both
`ShipPreviewEui` and `GetNewState` handle a deleted target or camera without throwing.

### Notes / deviations from Coyote's drydock

- Modelled on coyote-frontier's unfinished `_NF/Shipyard/Systems/BluespaceDrydockSystem.cs`, but
  storage is a persistent file registry instead of a YAML string on an ID card/record (which was
  round-scoped and one ship per ID), and there are no 3-second timer chains or `.Result` async
  hacks: store/retrieve run synchronously in the message handler, like the shipyard's own purchase
  path.
- `Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs` carries a Palmtree patch: its
  `[Access]` list now also names `SharedBluespaceDockSystem`, which owns the `AssignDeed` helper both
  consoles use to create deeds (the shared assembly cannot name the server systems directly).
- Known follow-up (not in this batch): unnamed single-tile grids flood the object scanner/IFF with
  "grid" entries.
- Drive-by fix: `Content.Client/Shuttles/FtlArrivalOverlay.cs` clamps its tile enumeration to the
  grid's `LocalAABB` (`// Palmtree`). `SharedMapSystem.TilesEnumerator.MoveNext` *recurses* once per
  skipped empty tile (`ignoreEmpty` defaults to `true`), so a zoomed-out ghost looking at an FTL
  arrival visualizer over a mostly empty grid used to stack-overflow the client.

## Dungeon lighting variants (flickering / rave)

- Prototypes: `Resources/Prototypes/_PS/Entities/Structures/Lighting/lighting_effects.yml` and
  `.../Objects/Power/lighting_effects.yml`. Every standard wall/small/post light and bulb has
  `*Flickering` and `*Rave` variants. `AlwaysPowered...` variants set
  `ApcPowerReceiver.needsPower: false`, so they light without an APC but still accept bulbs.
- Flicker: `FlickeringLightComponent` marks the bulb. `Content.Server/_PS/Light/FlickeringLightSystem`
  polls `PoweredLightComponent`s (the event bus only allows one subscription per component/event,
  and the base system owns the container events) and drives the existing `IsBlinking` appearance
  animation. It only turns blinking back off for lights it enabled itself, so the variation pass's
  aged-tube flicker is preserved. Rave takes priority over flicker.
- Rave: `RaveLightComponent` carries `Period` (0.5/1/2 s), optional `MaxEnergy` (peak point-light
  energy) and `Phase`. The pulse is client-only (`Content.Client/_PS/Light/RaveLightSystem`) so the
  server does not network `PointLight.Energy` every tick; it animates the point light and glow sprite
  layer on a triangle wave (100% -> 0% -> 100%). Screwdriver cycles the period through
  `SharedRaveLightSystem`; rave fixtures carry the component directly and their spawned rave bulbs
  carry their own copy.
- Tests: `Content.IntegrationTests/Tests/_PS/LightEffectsTest.cs` (flicker sync, always-power,
  screwdriver cycle, client pulse).

## 2026-10 misc additions (walkman, UI sounds, editor QoL, RTG radiation)

Small features that did not get their own section yet:

- **Walkman** (`Resources/Prototypes/_PS/Entities/Objects/Fun/walkman.yml`, commit `a3f80ab4f4`): a
  pocketable MIDI synthesiser instrument; also spawns rarely in the random instrument spawner.
- **Super walkman** (same file, 2026-10): admin-only variant carrying the super synthesizer's
  instrument flags (`allowPercussion`, `allowProgramChange`, `respectMidiLimits: false`) plus the
  walkman's full General MIDI program list and pocketable UI; violet tape layer, no loot/vendor
  wiring (mirrors `SuperSynthesizerInstrument`). Test: `Tests/_PS/WalkmanTest.cs`.
- **Admin placement offset** (2026-10): content-side `SnapgridCenterOffset` placement mode
  (`Content.Client/_PS/Placement/Modes`, subclass of the engine's `SnapgridCenter`) adds the
  `ps.placement.offset_x`/`_y` client CVars (in tiles, archived) on top of the tile-center snap, so
  admins and mappers can place entities slightly off-center (like hand-placed shuttle buttons).
  The adminbus tab has X/Y fields plus a Reset; the mode appears in every placement mode dropdown
  (spawn menu Override and the map editor's entity selector). A zero offset behaves exactly like
  `SnapgridCenter`. Test: `Tests/_PS/PlacementOffsetTest.cs`.
- **Vote sounds** (commit `241ea1bc6c`): custom votes play `vote_started`/`vote_yes`/`vote_no`/
  success/failure cues from `Resources/Audio/_PS/Voting/` (client `VoteManager`).
- **UI open/close sounds** (commit `2f307becc5`): panel open/close clicks from
  `Resources/Audio/_PS/UserInterface/` are wired to the interaction/hotel/BUI windows.
- **Exact-value inputs** (commit `8563de9647`): the interaction panel's multiplier sliders have
  numeric input boxes next to them (`InteractionWindow.xaml`).
- **Copy/Paste colors** (commit `b18d315d5e`): every character-editor and marking color picker uses
  `ColorSelectorWithClipboard` (copy/paste buttons under the swatch).
- **Lobby in debug builds** (commit `4070dd3b69`): `Resources/ConfigPresets/Build/debug.toml` enables
  the lobby so it can be exercised locally.
- **RTG radiation** (commit `4d990c2942`): RTGs carry a small `RadiationSourceComponent` and emit
  radiation while running (portable fuel generators still do not).
- **RPD + RCD improvements** (2026-10, ported from the old Coyote `palm3` code): the RPD tool
  (atmos build modes, 60 charges, radial categories), shuttle-wall RCD mode + cheap/fast shuttle
  wall deconstruct, instant-construction flag for the admeme `RCDExperimental`, and mirror
  prototypes (gas mixer/filter flip with the placement flip key). RPD modes
  `Resources/Prototypes/_PS/RCD/rpd.yml`, RCD modes `.../_PS/RCD/rcd.yml`, textures under
  `Resources/Textures/_PS/...`, locale `Resources/Locale/en-US/_PS/rcd.ftl`. Details:
  `.ai/PORTING.md` §24; traps: `.ai/HAZARDS.md` §26. The RPD deconstruct whitelist (`rpd: true`)
  is wired on `GasPipeBase`, so the RPD removes atmos piping while the RCD stays off it.
- **Salto expedition ship** (2026-10, ported from the old Coyote `_CS` code): `LL0 Salto`, a Small
  Expedition/AME ship, map `Resources/Maps/_CS/Shuttles/Expedition/salto.yml`, vessel prototype
  `Resources/Prototypes/_CS/Shipyard/Expedition/salto.yml`. Details and adaptations (dropped
  `CompanionInterview`, `[ 0, 0 ]` job slots): `.ai/PORTING.md` §25.
