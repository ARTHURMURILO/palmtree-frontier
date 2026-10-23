# PORTING — Coyote/Floof → Palmtree (Frontier)

> This document records what was ported from the old `coyote-frontier` checkout into this repository,
> what was deliberately left out, why, and how to port more. The earlier full-merge attempt and its
> audit folder were deleted by the maintainer; this document is the surviving decision history.

## 1. Background

- Old base: the Coyote checkout (`COYOTE`, see `.ai/file_paths.md`; branch `palm3`), fork chain Wizden → Frontier → Coyote (`_CS`) →
  Palmtree (`_PS`). Frozen since Coyote was abandoned in 2026.
- New base: this repo, Frontier Station @ 2026-10-03 (`df24c19f08`) + Palmtree branch `ps-erp`
  (merged into `master`, branch deleted).
- An earlier attempt merged the **entire** Coyote tree into Frontier (11,722 changed files, 1,347
  conflicts). It was discarded; the current `master` history is the only maintained line.
- The current approach is **targeted**: only the requested features are ported, using additive
  changes to Frontier code where possible, and adapting old content to 2026 APIs.

## 2. Port commits (originally on `ps-erp`)

| Commit | Contents |
|---|---|
| `805dfc6b5f` | ERP marking system core (Genital category/layer, kindAllowance, layering/colorLinks, scale/offset, ModifyUndies, genital content, `_PS` lobby/backpack/weapons, Rodentia + Anthromorph) |
| `8eacfa17e4` | Fix client sandbox violation (`string.Create` in `Marking.ToString`); strongly type `Layering` |
| `0acc512d11` | Fix concealable backpack implant (component on base `ClothingBackpack`, loadout group entry, tests) |
| `d4e63c28e7` | Rotate lobby background every 30s (Coyote timer patch) |
| `4f8dd74e02` | Crossfade lobby backgrounds (second `TextureRect` + smoothstep fade) |
| `76e0e55d1c` | Digitigrade/base-marking infrastructure, missing markings, Tajaran, Bayou clothing, missing loadouts |
| `afdd481c16` | Fix kind-allowed markings being stripped by `EnsureSpecies` |

## 3. What was ported

### 3.1 Marking infrastructure (`Content.Shared/Humanoid`, `Content.Client/Humanoid`)

- `Genital` + `TailBehind` + all Floof layers (`TailOversuit`, `NeckFluff`, behind-leg layers,
  `UndershirtUnderclothes`/`UndershirtOverclothes`, `TailExtras`, `Disregard`).
- `MarkingPrototype`: `kindAllowance`, `layering`, `colorLinks`, `altSprites`, `hidden`, `baseLayerSprite`.
- `Marking`: `scale`, `offsetX/Y` with backward-compatible DB string format.
- `MarkingCategories`: `Genital`, `NeckFluff`, `TailExtras`, and the Floof `Base*` categories plus
  `Category2Layer`.
- `SpeciesPrototype`: `kind`, `DefaultLegStyle`, `AllowDigilegDisplacement`.
- `HumanoidAppearanceComponent`: `HiddenBaseLayers`, `LegStyle`.
- `HumanoidCharacterAppearance`: `LegStyle` field and `WithLegs`.
- `MarkingPicker`: advanced size/offset editor and Leg Style selector.
- Client renderer: layering routing, color links, scale/offset, genital-under-clothing ordering,
  `altSprites` swapping for base layers and markings, `HiddenBaseLayers` hiding.
- `ModifyUndies` (`Content.Server/_Floof`, `Content.Shared/_Floof`): per-marking show/hide verbs
  (`canToggleVisible` default off except undergarments, `otherCanToggleVisible` default off).

### 3.2 Content

- **Genital marking sets**: core Floof library (379), butts/bellies (19), Palmtree breasts (18) with
  sprites (`Resources/Textures/Mobs/Customization/Markings/Genital/*`, `_PS/.../Genital/breasts.rsi`)
  and locale (`_CS/genitals.ftl`, `_PS/genitals.ftl`).
- **Floof anthro markings**: ears, tails, snouts, wings, digilegs/chests, species adaptors
  (`_Floof/Entities/Mobs/Customization/markings/*` + `Floof/Mobs/Customization/*` + `_CS`/`_DV` parts).
- **Core undergarments** (218 markings, source `undergarments.yml` is a superset of Frontier's 17) and
  **underovergarments** (67).
- **Feroxi, Ovinia, Resomi, Eastern Dragon** markings with sprites and locale.
- **Tajaran** species (species/entity/player/body/markings/damage/names/sprites/locale) from `_EE`.
- **Rodentia** (`_DV`) and **Anthromorph** (`_CS`) species, adapted to Frontier 2026.
- **Coyote Bayou clothes** (`_CS/CoyoteBayouClothes/*`), including latex items
  (`CoolCoyoteClothesDomina`, `CoolCoyoteClothesLatexHalf`), wired into job loadout groups via a
  `CoyoteJumpsuit` subgroup.
- **Missing contractor loadouts/items**: `ContractorClicker` (+ `Clicker` item/audio),
  `ContractorClothingBackpackSatchelLeather`, `LoadoutFootProtectors` (+ `ClothingShoesFootProtectors`).
- **Palmtree `_PS`**: lobby backgrounds + `PSLobbyMusic` (CVar default), concealable backpack implant
  (systems/components/action/implanter/loadout), battery guns/launchers/grenades/hitscan, audio.
- **Digitigrade leg displacement** (`_CS/LegDisplacement.yml` + `LegDisplacements`), wired into
  `ClientClothingSystem` for jumpsuit/shoes/outerclothing (partial coverage, same maps as Coyote).
- **Subtle chat** (`/subtle`, `/subtlelooc`): range- and wall-limited emotes/LOOC that ghosts cannot
  see (Coyote port; `ChatTransmitRange.NoGhosts`). Also ported: `-`/`=` chat prefixes, selectable
  Subtle/Subtle LOOC channels, `FocusSubtle`/`FocusSubtleLOOCWindow` keybinds and the subtle
  notification sound option.
- **Shower** (`_HL` prefix, from Coyote's HardLight content): `Shower`/`ShowerUnfinished`, the
  `Shower` construction graph/recipe, sprite (`_HL/Structures/Furniture/shower.rsi`) and toggle
  audio (`_HL/Ambience/Shower/*`). Adaptations: the unfinished prototype inlines the `_HL` sink
  base (no sink port needed) and the plumbing construction node was dropped (this fork has no
  plumbing assembly). `Tools/convert_dmm_room.py` maps `/obj/machinery/shower` to `Shower`, so the
  converted hotel rooms include working showers.

### 3.3 Sandstorm interaction panel (2026-10, `_PS`)

- Data-driven `interaction` prototypes, organ-based genital capability detection, session consent,
  lust/moans/climax, a 3-tab XAML window (Interactions / Genital Options / Preferences),
  search/favorites/auto-repeat, Ctrl+Shift-click + verb opening, purple ERP emotes and 47 Sandstorm
  ogg sounds.
- **Genital organs**: player-configured penis/vagina/balls/breasts/butt/belly with type + size and a
  semen-per-climax amount, persisted in `Profile.Genitals` (new `genitals` column + migrations).
  Replaces genital markings; the marking library is reused as the render backend.
- **Cum**: SPLURT cum overlay (converted `cumoverlay.dmi` RSI, washable with water/space cleaner),
  `Semen` reagent, Sandstorm-style drip puddles from internal climaxes, and an `AutoCumExterior`
  preference. No pregnancy, womb, other fluids or refractory period.
- Ported from `Sandstorm-Station-13` and `S.P.L.U.R.T-Station-13` (BYOND), not Coyote; see
  `.ai/systems/interaction-panel.md` and `.ai/systems/genital-organs.md`. Tests:
  `Content.IntegrationTests/Tests/_PS/InteractionPanelTest.cs` and the `ServerDbSqliteTests`
  round-trip.

## 4. What was deliberately NOT ported

| Feature | Reason / status |
|---|---|
| Consent system (Floof UI/DB/toggles) | The Floof system is not ported; instead the account-level **NSFW age gate** (one-time lobby prompt, `nsfwtoggle`, `NsfwConsent` DB column + migrations) was added in 2026-10, and `ModifyUndies` uses the per-marking `otherCanToggleVisible` opt-in (default off). See `systems/consent-and-erp.md`. |
| Traits (`BodyType*`, `Horny*`, `Scent*`, 46 ids) | Tied to un-ported HornyQuirks/BodyType/Scent systems. Imports filter unknown traits out safely (`HumanoidCharacterProfile.EnsureValid`). |
| Scent / aphrodisiac visibility | Explicitly excluded. `ScentSystem`, `AphroLacedVisibility`, smell traits absent. |
| Size manipulation / height-width system | The Coyote size manipulator is excluded, but character height/width **are** first-class now (profile → DB → networked → client scale; editor sliders, commit `ed5b2fc511`). |
| Vore | Not ported. |
| RPI economy, Needs, Healing bank, Space janitor, etc. (`_CS`) | Not ported; not requested. |
| IPC species | Replaced by the Palmtree **Synth** species (`_PS`) — see `systems/ps-systems.md`. Battery and radio/encryption remain unported; **EMP and the reboot mechanic are implemented** (deadly EMP pulses, `DeadStartupButton`). |
| Kitsune species | **Does not exist in the old codebase.** It only appears inside marking allowlists. Nothing to port. |
| Turrets, RCD, strobe lighting, shipyard cauterizer | Not requested / possibly stale systems. (The **RCD/RPD** were ported in 2026-10 — see §24; turrets, strobe lighting and the shipyard cauterizer remain unported. Sandstorm interaction sounds/panel were ported — see `systems/interaction-panel.md`.) |

## 5. Bugs found and fixed during the port

These are important because they are easy to reintroduce:

1. **Sandbox violation** — `Marking.ToString()` used
   `string.Create(IFormatProvider, ref DefaultInterpolatedStringHandler)`, which is not on the
   engine's client sandbox whitelist. Replaced with `float.ToString(CultureInfo.InvariantCulture)`
   concatenation. `Layering` was also changed from `Dictionary<string,string>` to
   `Dictionary<string,HumanoidVisualLayers>` to avoid runtime `Enum.Parse`.
2. **Concealable backpack did nothing** — the old repo patched base `ClothingBackpack` with a
   `ConcealableClothing` component (`toggleAction: ActionToggleConcealmentBackpack`,
   `category: backpack`). Without it no clothing could be concealed. Also added the implanter to the
   `ContractorImplanter` group.
3. **Lobby never rotated** — Coyote patched `GameTicker.LobbyBackground.cs` with a 30-second
   `Timer.SpawnRepeating(30000, CycleLobbyBackground, ...)` + `SendStatusToAll()`. Frontier only
   randomized once at startup.
4. **`MarkingsSet.EnsureDefault` overrun** — the loop condition used `||`, so a category with more
   points than `DefaultMarkings` (e.g. Rodentia's 999-point tail) indexed past the list and crashed
   entity spawning. Fixed to `&&`.
5. **Kind-shared markings stripped** — `MarkingsSet.EnsureSpecies` filtered with
   `SpeciesRestrictions.Contains(species)` only, removing markings allowed through `kindAllowance`
   (e.g. `FeroxiTorsoCountershadingF` on a Vulpkanin). Fixed to use
   `MarkingManager.IsAllowedBySpeciesOrKindAllowance`.
6. **Stale YAMLLinter assemblies** — running `Content.YAMLLinter --no-build` after C# changes used
   old `Content.Shared.dll` copies, producing bogus "value not found" enum errors. Always rebuild
   before linting.

## 6. IPC replacement: Synth (implemented)

IPC was held back because the old implementation (`_EinsteinEngines`) needs the EE silicon stack:
`Silicon`, `BatterySlotRequiresLock`, `EncryptionHolderRequiresLock`, `SiliconEmitSoundOnDrained`,
`EmitBuzzWhileDamaged`, `DeadStartupButton`, `EncryptionKeyHolder`, `BatteryDrinker`, the `ipc`
inventory template, and silicon body parts/organs.

Instead, Palmtree implemented a replacement species, **Synth** (`_PS`), which reuses the marking
system so players can look like any species and approximates IPC behavior: 1.5× speed, 3× durability,
IPC-like damage container/modifier set, silicon body parts/organs (IPC sprites, `Inorganic` parts),
coolant blood, robot typing indicator, insulated temperature, heavy fixture density, no
hunger/thirst, and mass-based pull slowdown. See `.ai/systems/ps-systems.md` for details and known
differences (no battery, no radio/encryption; EMP/ion pulses are deadly to synths and they are
extremely radiation-sensitive).

If a true silicon implementation is ever wanted, build it on the upstream/engine silicon & borg
systems already in this repo (`Content.Server/Silicons`, `Content.Shared/Silicons`) instead of
importing the EE stack.

## 7. Character saves compatibility

The maintainer's exported characters live outside the repo
(`~/Documents/SS14/Characters`). Facts established while porting:

- Import path: client `HumanoidAppearanceSystem.FromStream` → `HumanoidProfileExport` →
  `HumanoidCharacterProfile.EnsureValid`.
- Unknown fields in the YAML (e.g. `height`, `width`, old Floof marking fields like `glowLevels`,
  `takeOffVerb2p`, `customName`) are ignored by the serializer.
- Unknown prototypes (traits, markings, loadouts) are filtered by `EnsureValid`/`EnsureSpecies`
  rather than failing the import.
- After the port, all 114 markings, 166 loadouts/items and all species referenced by those saves
  resolve except IPC.
- `legStyle: Digitigrade` is now imported and honored.
- **Unknown species fallback**: `FromStream` maps exports whose species no longer exists to `Synth`
  (if present) or the default species, so old IPC saves import cleanly and can be switched to Synth
  in the editor.

## 8. How to port more (short version)

See `.ai/guides/porting-from-coyote.md` for the full method. In short:

1. Find the source files in the Coyote checkout (`COYOTE`, see `.ai/file_paths.md`) by prototype ID or feature name.
2. Classify each field/component against this repo (`grep` the C# type / prototype type).
3. Copy content files and assets; adapt fields that do not exist here (remove or rewrite).
4. Wire loadouts/groups/locale; keep prefixes for provenance.
5. Rebuild the solution, rebuild `Content.YAMLLinter`, run it; then run relevant tests.
6. Commit with a note about adaptations; update `.ai/PORTING.md` and `.ai/HAZARDS.md` if you learn
   something new.

For **SS13 (BYOND) content** — features from the `BYOND` checkouts (see `.ai/file_paths.md`),
`.dmm` room maps and `.dmi` art — use `.ai/guides/porting-from-ss13.md` instead. It covers the TGM
`.dmm` format, the `Tools/convert_dmm_room.py` generator, format-7 map requirements (air, gravity,
power, anchoring), SS13→SS14 concept mapping and the related hazards.

## 9. 2026-10 feature batch (Coyote ports + Palmtree systems)

Ports from the `COYOTE` checkout (see `.ai/file_paths.md`):

- **Feroxi species** (`_DV/Species`, `_DV/Body/{Parts,Organs,Prototypes}`, `_DV/Entities/Mobs`,
  `_DV/Damage/modifier_sets.yml`, `_DV/Chemistry/metabolizer_types.yml`, `_DV/SoundCollections/feroxi.yml`,
  `_DV/Voice/*`, `_DV/typing_indicator.yml`, guidebook XML). Coyote's unused `FeroxiDehydrate`
  component and the RPI `Needs` system were dropped (not ported here); Hunger/Thirst are used instead.
  The body prototype uses `OrganFeroxiLungs` (Coyote's used human lungs and only had the metabolizer
  type on the unused organ) so Feroxi can breathe **water vapor and oxygen**:
  `Water` reagent `Gas` metabolism got the Feroxi `Oxygenate`/`SatiateThirst`/`ModifyLungGas` effects
  and `Oxygen` got a Feroxi `Oxygenate`.
- **Water vapor tanks** (`_DV/Entities/Objects/Tools/gas_tank.yml`,
  `_DV/Catalog/Fills/Items/gas_tanks.yml`, `_DV/Recipes/Lathes/misc.yml` + `DeltaV/Objects/Tanks/*.rsi`,
  tank dispenser inventory entries). Coyote's water-vapor survival boxes were skipped (they reference
  unported food/medipens).
- **Emotes + emote picker**: see `.ai/systems/ps-systems.md` for the system map. Content files were
  copied from `Floof/`, `_EinsteinEngines/`, `_PS/`, `_DEN/`, `_Funkystation/` and `_CS/` into the
  matching `_Floof`/`_EE`/`_PS`/`_DEN`/`_Funkystation`/`_CS` folders. `Whine` was kept and Coyote's
  separate `Whimper` emote was added next to it. The deathgasp was fixed: `DefaultDeathgasp` has its
  `deathgasp` chat triggers back (organic message + species sound), `SiliconDeathgasp` is named
  "Silicon Deathgasp", categorized `Borg` and whitelisted to `BorgChassis`/`Synth`, and both borgs
  and the Synth species use it as their on-death gasp.
- **Glass gas mask** (SS13 Skyrat/SPLURT `/obj/item/clothing/mask/gas/glass`): prototype
  `_PS/Clothing/Mask/glass_gas_mask.yml` + RSI extracted from the SS13 `.dmi` (`gas_clear`). It has no
  `IdentityBlocker`/`HideLayerClothing`, so the face/snout stays visible, and is a contractor face
  loadout. Species worn variants were extracted from Skyrat's `mask_muzzled.dmi` and `species/vox/mask.dmi`
  (`equipped-MASK-vulpkanin`/`felinid`/`rodentia`/`feroxi`/`harpy`/`reptilian` use the muzzled sprite,
  `equipped-MASK-vox` the vox one); the client picks these up from `InventoryComponent.SpeciesId`, no C#.
- **Custom species name** (profile field, DB column, editor field, examine/records use).
  Coyote's `SpeciesPrototype.CustomName` gated the feature but no species set it, so it never saved;
  here the default is `true` (all round-start species allow it).
- **Emote category picker** (profile field + DB column, `EmoteCategoryWindow`, wheel filtering,
  `EmoteCategory` enum widened to Coyote's `ushort` set, `ShowInWheel`, supplemental emote sounds).
  Server-side emote restrictions were disabled (Coyote behavior); the picker controls visibility.

Palmtree-original changes in the same batch: currency renamed to "space roubles" (ids unchanged),
fuel scaling for longer rounds (~2.7x the original durations), pest/power station events disabled, forensic swab 0.5 s,
SSD bodies don't suffocate, admin playtime-only bypass, `exportcharacters` console command,
Palmtree splash logo, non-`_PS` lobby backgrounds/music disabled.

## 10. 2026-10 marking port batch (Coyote)

- **Cybernetic markings** (`_EE/Entities/Mobs/Customization/`): all seven shell sets (bishop,
  hephiastos, morpheus, shellguard, wardtakahashi, xion, zenghu) + `Resources/Textures/_EE/Mobs/Species/Cybernetics/*`.
  Coyote lists them as `speciesRestriction: [IPC]` (no IPC here), but every marking carries
  `kindAllowance: [BasicHumanlike, BasicFurry]`, and `IsAllowedBySpeciesOrKindAllowance` allows a
  kind match even when the species restriction does not, so the **Synth** species (kind includes
  `BasicRobot`/`BasicHumanlike`/`BasicFurry`) can use all heads/chests/limbs. Organics with matching
  kinds can use the limb sets (arms/legs/hands/feet).
- **Robot antennae + screen faces** (`_EE/.../antenna.yml`, `screens.yml` + `_EE/Mobs/Customization/ipc_antenna.rsi`,
  `ipc_screens.rsi`): same kind-allowance sharing, usable by Synth.
- **Tail/wing/hair marking sets**: `_CS/aggie_tails.yml` (Kemono tails), `_CS/.../Markings/tail_adapters.yml`
  (hidden digitigrade vulp tails, used through `altSprites`), `_CS/.../Markings/wings.yml` (draconic/fairy
  wings), `_CS/.../Markings/anthro_tails.yml` (Aussie Shepherd tail + wag), `_DV/.../Markings/hairextensions.yml`
  (91 hairstyles, `_CS/Hairmarkings/*`), `_Starlight` Avali/Resomi sets (parts, crests, eyes, jewelry,
  hair, gauze, tails) and Avali/Resomi undergarments, `_DV`/`_NF` chitinid sets. All sprites copied.
- **Adaptations**: Coyote's `waggingId`/`staticId` marking fields were stripped (its modified
  wag-toggle `WaggingSystem` is not ported; this fork keeps the older "Animated"-suffix system and the
  separate `*Wag` markings are selected manually). Vulp tail markings got `altSprites → DigiVulpTail*`
  so digitigrade swaps tails too.
- **Digitigrade fixes**: Anthromorph and Reptilian base sprites got `altSprites`; the shared organic
  base layer list got the behind-leg layers; Reptilian got the behind-layer `speciesBaseSprites`
  entries. See `.ai/systems/marking-and-appearance.md`.
- **Slime batch**: water weakness + confirm suppression + death revert; see
  `.ai/systems/ps-systems.md` ("Slime transformation").

## 11. 2026-10 species + marking locale batch

- **Marking locale**: the 13 Coyote FTL files for the previously ported marking sets
  (`_EE/silicons/cyberlimbs.ftl`, `_EE/.../ipcAntenna.ftl`/`ipcScreens.ftl`,
  `_Starlight/markings/{avali,avali_tattoos,gauze_resomi,tattoos_resomi,undergarments}.ftl`,
  `_Starlight/accessories/{avali-crest,resomi-hair}.ftl`, `_DV/markings/chitinid.ftl`,
  `_NF/markings/chitinid.ftl`, `_CS/markings.ftl`). Without them every marking name rendered as
  its raw id.
- **Species ports** (from Coyote, same file layout as `.ai/guides/adding-species.md`):
  - **Chitinid** (`_DV`): species/body/parts/organs/player/names, `Chitinid` radiation-absorbing
    C# (`Content.Server/_DV/Abilities/Chitinid`), the `Chitzite` item + `ActionChitzite` (uses the
    already-ported `ItemCougherSystem`). Adaptations: `Needs` → `Hunger`/`Thirst` (33% faster),
    `AphrodisiacDrinks` metabolizer group dropped (not ported), custom Sprite layer block dropped,
    `chitinid0/1` typing states merged into `_DV/Effects/speech.rsi`.
  - **Ovinia** (`_DEN`): species/body/parts/mob/player (Coyote keeps the player mob under `_DV`),
    sound collections + baa/moo audio, `OviniaEmotes` tag + emote whitelists, typing indicator,
    damage set. `minHeight`/`maxHeight` removed, `Needs` dropped, custom Sprite dropped.
  - **Avali + Resomi** (`_Starlight`): species/bodies/organs/mobs/players/dummies, names, voice,
    sound collections, tags, inventories, metabolizer types (`Avali`/`Resomi`), Avali blood reagents
    (`AvaliBlood`/`ResomiBlood`) and `Amoxla`. Dropped: the Coyote `Stasis`/Nanite Shell actions and
    the Mono `FoodMeatResomi` (uses `FoodMeatHuman`). `Needs` → `Hunger`/`Thirst` (Avali 2x).
  - **Avali core reagent conditions** (ported into the shared reagent files): Ammonia heals Avali
    asphyxiation and is otherwise harmless to them, Iron poisons them, Dexalin/Dexalin+ are lethal
    to them, Saline burns them, and the DeltaV Feroxi saline nerf was ported in the same pass.
- **Every species now has a guidebook entry**: added index entries and XMLs for Feroxi, Rodentia,
  Tajaran and Anthromorph (from Coyote), new pages for Synth/Gingerbread/Skeleton, and the four new
  species' pages; the Species overview embeds all of them.
- **Survival boxes**: all ported species were added to the `OxygenBreather` loadout effect group -
  previously Vulpkanin/Felinid/etc. failed the species check and spawned without a survival box.
- **Shared organic layer list**: `UndershirtUnderclothes` and `clownedon` were added to the
  `BaseMobSpeciesOrganic` sprite layer list (the first block and custom species already had them),
  so base-list species render those layers in the right order.
- **Slime water rebalance** and the `SlimeWaterContactSystem`: see `.ai/systems/ps-systems.md`.

## 12. 2026-10 feature batch (PDA IFF, Shortband, crit speech)

- **PDA IFF blips (Coyote)**: `_CS/BlipCartridge` (`BlipCartridgeComponent`,
  `BlipColorSetPrototype`, `BlipShapeSetPrototype`, `RadarBlipPresetPrototype`, server
  `BlipCartridgeSystem`) plus the `_CS/Blipz` prototypes. Every PDA inherits `BlipCartridge` from
  `BasePDA` with per-department presets; admin PDAs are disabled. It reuses the existing `_NF`
  radar-blip pipeline: `RadarBlipShape` gained `Heart`, `X`, `CircleWithLine` and the client
  `ShuttleNavControl` draws the X and geometric heart shapes. Pocket PDAs appear as customizable
  blips on mass scanners / shuttle consoles (verbs: preset, colour, shape, size, toggle).
- **Shortband radio (Coyote "Traffic" replacement)**: `RadioChannelPrototype` gained the range
  degradation fields; `RadioSystem.MangleRadioMessage` / `MangleMessage` / `GenerifyName` rebuild a
  degraded message, with hooks in `RadioSystem.OnIntrinsicReceive`, `HeadsetSystem`,
  `RadioDeviceSystem` and `_NF/HandheldRadioSystem`. `Traffic` keeps its id/frequency but displays
  as "Shortband" and degrades with distance (650 m optimal, static-glyph text beyond, 50% drop past
  the heavy range). The `_CS/RadioNoises` static-sound system (component, prototype, system and
  `Resources/Audio/_CS/RadioStatic`) is attached to headsets, handheld radios and intercoms, with
  squelch/volume verbs.
- **Speech in crit (Coyote)**: `MobStateSystem` only cancels `SpeakAttemptEvent` and
  `EmoteAttemptEvent` while dead; critical mobs can talk and emote. The one-shot
  `AllowNextCritSpeechComponent` is still consumed by `SleepingSystem` but no longer by
  `MobStateSystem`.
- **Housekeeping**: solar flare and bluespace cargo events are disabled in the NF and upstream
  calm-event tables; slime base sprites gained the missing marking layers (`Tail`, `HeadTop`,
  `HeadSide`, `Snout`, `Special`, `NeckFluff`, `RArmExtension`); the synthesizer now carries
  Coyote's full 128-program General MIDI list.

## 13. 2026-10 Sandstorm clothing batch (Soviet set, RD beret, admiral jumpsuit)

- **DMI conversion verified**: extraction follows the BYOND cell layout (row-major grid,
  `dimX = width / 32`; each state consumes `dirs * frames` cells frame-major/dir-minor) and was
  diffed pixel-for-pixel against DMISharp. Method and pitfalls: `.ai/guides/porting-from-ss13.md`
  §5. Source: `Sandstorm-Station-13`
  (`icons/obj|mob/clothing/{uniforms,uniform,suits,suit,hats,head}.dmi`).
- **Soviet set (`_PS`)**: `ClothingUniformJumpsuitSoviet` (state `soviet`),
  `ClothingUniformJumpsuitSovietConscript` (state `soviet_uniform`) and
  `ClothingOuterCoatSoviet` (state `soviet_suit`); RSIs under
  `Resources/Textures/_PS/Clothing/{Uniforms/Jumpsuit,OuterClothing/Coats}`. The ushanka already
  existed upstream. Sprites are CC-BY-SA-3.0 (tg/DonkSoft set via Sandstorm).
- **Research Director's beret (`_PS`)**: `ClothingHeadHatBeretResearchDirector` (state `rdberet`),
  wired into `LockerFillResearchDirectorNoHardsuit` (so both RD lockers spawn it).
- **CentComm admiral jumpsuit**: Skyrat asset `centcom_admiral.rsi` copied from Coyote under
  `_Starlight`; prototype `ClothingUniformJumpsuitCentcomAdmiral` in the `_Starlight` jumpsuits
  file. Admin spawn only - no loadout/lathe/vendor entry.

## 14. 2026-10 AKM port (Coyote `_CS` weapons)

- **AKM (`CSWeaponAssaultRifleAKM`, `_CS`)**: prototype in
  `Resources/Prototypes/_CS/Entities/Objects/Weapons/Guns/Rifles/rifles_assault.yml` (NF .30
  assault rifle chamber + Cybersun frame), RSI
  `Resources/Textures/_CS/Objects/Weapons/Guns/Rifles/akm.rsi` (CC-BY-SA-3.0, CEV-Eris/Frontier)
  and Mono gunshot `Resources/Audio/_Mono/Weapons/Guns/SmallArms/Gunshots/ak_fire.ogg`.
  Obtainable exactly like Coyote: `WeaponCaseLongAkm` dungeon case
  (`_CS/Catalog/Fills/Items/weapon_cases_expedition.yml`), `MailCSAK` (`_CS/Mail/mail.yml`) added
  to `RandomNFMailDeliveryPool` at 0.1, and the T3 ranged dungeon weapon table
  (`_NF/Entities/Markers/Spawners/Random/Items/weapon_tables.yml`).
- **Pending Coyote `_CS` weapons** (not ported yet):
  - **`.40` heavy pistol conversion suite (`_CS/HeavyPistols` + `Ammunition/*/pistol.yml`)**:
    frames/chambers, cartridges, boxes, magazines and projectiles. Coyote uses these to convert
    `NFWeaponPistolViper`, the Universal/NFSD Universal variants and the tiered heavy pistols to
    `.40`; this fork still ships them on the Frontier `.35` chambers. The suite also brings
    `WeaponCaseShortAmmoBox*40` cases and `WeaponCaseHeavyAmmo`. (Only the heavy pistol *frames*
    are ported, for the Anaconda - the chambers/ammo are not.)
  - **Size manipulator (`WeaponSizeManipulator`)**: blocked - depends on Coyote's size system and
    `BulletSizeManipulator*` projectiles, and the size system is not ported (`.ai/UNKNOWN.md` #8).

## 15. 2026-10 Weapon batch: re-enabled Frontier guns, practice ammo, Coyote ports

- **Frontier-disabled upstream guns.** Frontier removes whole upstream weapon directories at load
  through `Resources/IgnoredPrototypes/ignoredPrototypes.yml` (`Battery`, `HMGs`, `Rifles`,
  `Pistols`, `Revolvers`, `Shotguns`, `SMGs`, `Snipers`, plus the ammunition directories), so
  individual prototypes cannot be re-enabled in place - their IDs stay reserved but the prototypes
  are gone. Guns that had no enabled Frontier replacement were copied into `_PS` with `PS` ids and
  the original stats/sprites (all CC-BY-SA-3.0 upstream assets):
  - `PSWeaponTaser` + `PSWeaponTaserSuper` (elite taser) - `_PS/.../Battery/reenabled_upstream_guns.yml`
  - `PSWeaponTeslaGun`, `PSWeaponEnergyShotgun` (fire modes restored with `_PS` spread projectiles)
  - `PSWeaponMinigun` (+ `PSCartridgeMinigun`/`PSBulletMinigun`, the upstream .10 cartridge was
    disabled in place by Frontier) - `_PS/.../HMGs/minigun.yml`
  - `PSWeaponRifleAk` (the WizDen AKMS, magazine whitelist switched to the `NFMagazineRifle30`/
    `NFCartridgeRifle30` tags) - `_PS/.../Rifles/akms.yml`
  - Projectiles needed by the above live in
    `_PS/.../Ammunition/Projectiles/reenabled_upstream_projectiles.yml`; the wizard "Summon Guns"
    pool (`Magic/event_spells.yml`) got `PSWeaponTaser`, `PSWeaponTeslaGun`,
    `PSWeaponEnergyShotgun`, `PSWeaponMinigun` and the previously commented
    `WeaponTetherGun`/`WeaponForceGun` back.
  - Pulse pistol/carbine/rifle, laser cannon, x-ray cannon, advanced/antique laser, particle
    decelerator and the revolver/SMG/shotgun/sniper families were **not** copied: Frontier already
    ships rebalanced enabled versions (`NFWeaponEnergyPistolPulse`, `NFWeaponEnergyRiflePulse`,
    `NFWeaponEnergyRifleAssaultPulseCarbine`, `NFWeaponEnergyRifleSniperCannon`,
    `NFWeaponEnergyRifleSniperXrayCannon`, `NFWeaponEnergyPistolLaserAdvanced`,
    `NFWeaponEnergyPistolLaserAntique`, `NFWeaponParticleDecelerator`, ...).
- **Practice ammo re-enabled** (Frontier removed it in #4608): the commented practice cases
  (`WeaponCaseShortAmmoBoxPractice{20,25,30,35,45,Shotgun}`), crates
  (`CrateAmmoBoxPractice*`), the `TableDungeonLootWeaponsAmmunitionPractice` table and the
  `SpawnDungeonLootAmmoPractice` marker were un-commented in place; the practice table is nested
  into `TableDungeonLootWeaponsAmmunition` (weight 0.05) and the crates into the mercenary T5
  crate group.
- **Prototype pulse rifle (`WeaponPrototypePulseRifle`, `_PS`)**: ported from Coyote (Floof),
  sprite `_PS/Objects/Weapons/Guns/Battery/prototype_pulse_rifle.rsi`, hitscan `PulseWeak` in the
  `_PS` hitscan file, case `WeaponCaseLongPrototypePulseRifleExpedition` (T5 dungeon loot) and the
  `FloofBlueprintWeaponLaserPrototypePulseRifle` blueprint (`_PS` blueprint + lathe recipe
  `WeaponPrototypePulseLaser`) added to the expedition document case table.
- **EG-4 energy revolver (`WeaponEnergyRevolver`, `_PS`)**: ported from Coyote (Goobstation),
  sprite `_PS/Objects/Weapons/Guns/Battery/erevolver.rsi`, `BulletEnergyGunMagnum` projectile,
  case `WeaponCaseShortEnergyRevolverExpedition` (T4 dungeon loot) and the
  `UplinkSecurityEnergyRevolver` NFSD uplink listing (`_PS/Catalog/security_uplink_catalog.yml`).
- **Anaconda (`WeaponPistolAnaconda` + `CSWeaponPistolAnacondaExpedition`)**: ported from Coyote
  (`_Goobstation` pistol + `BulletAnaconda` cartridge-wrapper projectile, `_CS` expedition variant
  and case). Needs the `_CS` heavy pistol **frames** (ported in
  `_CS/Entities/Objects/Weapons/Guns/HeavyPistols/base_pistol.yml`; the .40 chambers are not) and
  the new `weapon-details-class-heavy-pistol` locale string. Case in T4 dungeon loot.
- **Lollypop dispensers (`LauncherLollypopRegenerating` + Tricordazine/Omnizine/Weh/Mystery)**
  and the whole lollypop food set: copied from Coyote (`_Goobstation` food + pneumatic cannon
  files, `_Goobstation/.../lollypop.rsi`). Admin/DoNotMap content.
- **C-19r SMG (`CSWeaponSubMachineGunC19r`, `_CS`)**: ported from Coyote, `_Mono` c19r RSI and
  gunshot sound, `WeaponCaseLongC19r` (T5 dungeon loot) and `MailCSC19r` added to
  `RandomNFMailDeliveryPool` at 0.1.

## 16. 2026-10 Stechkin APS (Sandstorm BYOND port)

- Coyote does **not** have a Stechkin; Sandstorm Station 13 does
  (`code/modules/projectiles/guns/ballistic/pistol.dm`, `/obj/item/gun/ballistic/automatic/pistol/APS`:
  9mm, burst 3, semi/burst/full-auto, suppressor-capable).
- **`PSWeaponPistolStechkinAPS`** (`_PS/Entities/Objects/Weapons/Guns/Pistols/stechkin_aps.yml`):
  machine pistol frame + .35 high capacity chamber (this fork has no 9mm, so it uses .35 auto;
  Frontier has no suppressor system either), `Gun` set to SemiAuto/Burst/FullAuto with
  `shotsPerBurst: 3`.
- **Sprite**: `_PS/Objects/Weapons/Guns/Pistols/stechkin_aps.rsi` - `icon`/`base` from the DMI
  `aps` state, `bolt-open` from `aps-e`, inhands from the generic SS13 `gun` state
  (`icons/mob/inhands/weapons/guns_{left,right}hand.dmi`), belt/suit-storage sprites from the
  shared SS14 pistol art (`viper.rsi`). DMI cells were extracted with the zTXt/Pillow method
  (frame-major, direction-minor S/N/E/W; 2x2 RSI sheet layout).
- **Obtainable**: `WeaponCaseShortStechkinAPS` dungeon case (expedition variant
  `PSWeaponPistolStechkinAPSExpedition`, T2 ranged loot next to the Viper) and both WeaponryWorks
  (emagged black market) inventories, plus the DEBUG guns vendor.

## 17. 2026-10 Desert Eagle (ammo-fed Anaconda sibling)

- **`PSWeaponPistolDesertEagle`** (`_PS/Entities/Objects/Weapons/Guns/Pistols/desert_eagle.yml`):
  a normal magazine-fed heavy pistol using the Anaconda sprite (the Anaconda itself is a
  battery-fabricated gun). Parent `[NFBaseWeaponPistolChamber45, CSBaseWeaponFrameHeavyPistolCybersun]`;
  takes `NFMagazinePistol45` (8 rounds) and `NFCartridgePistol45`, inheriting the heavy pistol class,
  C2 contraband and Cybersun manufacturer details.
- **Sprites**: `_PS/Objects/Weapons/Guns/Pistols/desert_eagle.rsi`, copied from the Anaconda RSI with
  added `base`/`bolt-open` (icon copies) and a transparent `mag-0` state so the magazine-fed gun
  renders (the Anaconda has no mag/bolt states).
- **Sounds**: `Gunshots/deagle.ogg`, `MagIn/de_clipin.ogg`, `MagOut/de_clipout.ogg` and
  `Bolt/de_slideback.ogg` (rack). `Cock/de_clipin.ogg` is a byte-identical duplicate of the MagIn
  file and is currently unused.
- **Obtainable**: both WeaponryWorks emagged (black market) inventories, the DEBUG guns vendor and
  (since §18) dungeon/asteroid loot.
- **Ammo (updated in §18)**: the Desert Eagle now accepts `.50 AE`, `.44 Magnum` and `.357 Magnum`
  magazines (`CSMagazine50AE`, `CSMagazine44`/`CSMagazine44Automag`, `CSMagazine357`) as well as
  those cartridges in the chamber; those calibers were added natively in §18.

## 18. 2026-10 Fallout weapon port (coyote-bayou, 29 weapons + 11 calibers)

### Weapons

Ported from `BYOND/coyote-bayou` under the `_CS` prefix, one file per class in
`_CS/Entities/Objects/Weapons/Guns/Fallout/`:

- **Pistols**: `CSWeaponPistolAutomag` (.44 Mag), `CSWeaponPistolM93R` (9mm, burst),
  `CSWeaponPistolM9FS`, `CSWeaponPistolHiPower`, `CSWeaponPistolM1911` (.45 pistol),
  `CSWeaponPistolMakarov`, `CSWeaponPistolSkorpion` (9mm, full auto).
- **Revolvers**: `CSWeaponRevolverColtSAA` (.45 LC), `CSWeaponRevolver44` (.44 Mag),
  `CSWeaponRevolverTaurusJudge` (3x .50 shells), `CSWeaponRevolverSW45` (.45 pistol, 7 rounds).
- **SMGs**: `CSWeaponSubMachineGunUzi`, `...American180` (.22, integrally suppressed),
  `...P90` (10mm), `...MP5`, `...PPSh`.
- **Rifles**: `CSWeaponRifleM1Garand` (.30-06), `...M1A1` (10mm), `...ScarL` (5mm),
  `...AUGA10` (5mm), `...Mosin` (7.62x54R), `...SKS` (.308).
- **LMGs**: `CSWeaponLightMachineGunBAR`, `...M1919`, `...RPD`, `...DP27` (all .308).
- **Shotgun**: `CSWeaponShotgunSaiga12` (.50 shells, 8-round magazine).
- **Bows**: `CSWeaponBowComposite` (arrows), `CSWeaponCrossbowMarksman` (bolts).

- Sprites in `_CS/Objects/Weapons/Guns/Fallout/<Name>.rsi` (`icon`/`base`, `bolt-open`, `mag-0`,
  4-dir inhands and equipped sprites). Extracted from the coyote-bayou DMIs with the
  zTXt/Pillow method; `bolt-open` uses the source `-e`/`-open` state when present, equipped-back
  sprites come from `modular_coyote/icons/objects/back.dmi` when available and the shared SS14
  rifle art otherwise, belt/suit storage from the shared SS14 pistol art.
- Sounds copied from `sound/f13weapons/` into `Resources/Audio/_CS/Weapons/Fallout/`.
- Chamber bases (`CSBaseWeapon...Chamber...`) in `.../Fallout/bases.yml` set the magazine
  whitelist (`CSMagazine*` tags), examine caliber and revolver chambers.
- Fire modes/rates adapted from the BYOND rpm values (rpm / 60 = SS14 `fireRate`).

### Calibers

11 new native calibers under `_CS/Entities/Objects/Weapons/Guns/Ammunition/`:
**9mm, 10mm, .22 LR, .44 Magnum, .357 Magnum, .50 AE, .45 Long Colt, 5mm, .308, .30-06, 7.62x54R**.

- Full 7-variant families (Standard, Overpressure, Incendiary, Uranium, Practice, Rubber, EMP) for
  cartridges, projectiles and ammo boxes; magazines ship in standard, empty and all 6 variant
  versions, matching the Frontier convention.
- Cartridges reuse the upstream pistol/magnum/rifle casing RSIs with tinted tips.
- Magazine sprites extracted from `icons/fallout/objects/guns/ammo.dmi`; box sprites from the
  same DMI (one box per caliber). Magazine fill levels use the source's ammo-count states
  (`mag-0` = the source `-0` empty sprite, `mag-5` = the full sprite, intermediate counts where
  the source has them, e.g. `uzi9mm-4..32`, `762belt-20..80`, `enbloc-0..8`). The PPSh drum,
  American 180 drum and Saiga magazine have pixel-identical empty/full sprites in the source, so
  those look the same regardless of fill.
- `.45 ACP` reuses the existing `.45 pistol` family, 12 gauge reuses `.50 shells`, and the
  SCAR-L/AUG use the source's 5mm (not 5.56). Arrows/bolts reuse the existing bow ammunition.
- Tags in `_CS/tags.yml`, examine-caliber locale in `Resources/Locale/en-US/_CS/weapons/gun-examine.ftl`,
  lathe recipes (ammo boxes + empty magazines) in `_CS/Recipes/Lathes/fallout_ammo.yml`.
- **Recipe exposure** (mirrors how Frontier exposes its own ammo): standard boxes + empty
  magazines added to the `NFBasicAmmunitionProduction` tech node, pistol overpressure to
  `NFImprovedPistolAmmo`, rifle overpressure to `NFImprovedRifleAmmo`, rubber to
  `NFNonlethalAmmunition`; the same recipes added to the `NFBlueprintsMercenaryNfsd` blueprint
  pack; overpressure/incendiary/uranium boxes added to the mercenary/syndicate ammo blueprint
  disks; practice boxes added to the `NfsdPracticeStatic` lathe pack. EMP boxes stay
  research-locked exactly like Frontier's own EMP ammo.

### Loot

- `_CS/Catalog/Fills/Items/weapon_cases_fallout.yml`: one `RareWeaponCase` per gun (30, including
  the Desert Eagle) with magazines/ammunition.
- `_CS/Entities/Markers/Spawners/Random/weapon_tables_fallout.yml`: `TableDungeonLootWeaponsFallout`
  (all Fallout ports) and `TableDungeonLootWeaponsPalmtree` (AKM, C-19r, Anaconda, pulse rifle,
  energy revolver, Stechkin APS, Novalite C2).
- Nested into dungeon ranged T4 (weight 0.5) and T5 (weight 0.5) and into
  `SalvageEquipmentLegendary` (asteroid salvage, weights 0.5/0.3). 2026-10-15: the Fallout weight
  was raised from 0.15 to 0.5 (≈3x more common per eligible roll; each individual case is still
  rare, ~1/30 of that).

## 19. 2026-10 Balance + polish pass (CSS sounds, fire rates, per-gun damage)

- **Garand ping**: `CSWeaponRifleM1Garand` auto-ejects its en-bloc clip with
  `Audio/_CS/Weapons/Fallout/garand_ping.ogg`. Vanilla auto-eject triggers the moment the magazine
  drains - which is one shot early, because that last round is moved into the chamber - so
  `MagazineAmmoProviderComponent.autoEjectDeferChambered` (new field, shared) defers both the eject
  and the sound until the chambered round has been fired too. Only enabled on the Garand, so the
  C-20r/C-19r auto-eject behaviour is unchanged.
- **Bows**: the composite bow was invisible because `BaseBow` defines no sprite layers; it now has
  `unwielded`/`wielded` layers (source `composite_unloaded`/`composite_loaded` states) plus a
  `GenericVisualizer` for wielding. The marksman crossbow was missing `icon-string-drawn`
  (ERROR sprite when drawn); the state now exists and the crossbow spawns with a loaded
  `CrossbowBolt` (`ItemSlots.projectiles.startingItem`).
- **CSS sound sets**: user-provided sounds live under `Resources/Audio/_PS/Weapons/Guns/`
  (`Gunshots/{ak47,aug,awp,m4a1,p90}.ogg` plus MagIn/MagOut/Bolt/Misc extras) and are wired to
  `CSWeaponAssaultRifleAKM` (ak47), `CSWeaponRifleAUGA10` (aug), `NFWeaponRifleSniperHristov`
  (awp), `PSWeaponRifleAssaultNovaliteC2` (m4a1 - the C1 went back to stock) and
  `CSWeaponSubMachineGunP90` (p90) - gunshot, magazine in/out and rack sounds (insert/rack for the
  Hristov's internal magazine). No gun variants were created; the sounds simply replace the old ones.
- **Fire rates**: ported LMGs x4 (BAR 10, M1919 13.2, RPD 6.68, DP-27 10), rifles x2 (M1 Garand 3,
  M1A1 6, SCAR-L 3.34, AUG 3.34, Mosin 1.6, SKS 4), SMGs x2.5 (Uzi/MP5/P90 8.25, American 180/PPSh
  12.5; P90 burst 10.75); previously added AKM 6.4, Novalite 6.4 and Hristov 1.6.
- **Per-gun damage**: new `GunDamageMultiplierComponent` (`Content.Shared/_PS/Weapons`) and
  `GunDamageMultiplierSystem` (`Content.Server/_PS/Weapons`) scale projectile damage at hit time
  based on the firing weapon (`ProjectileComponent.Weapon`). Applied to AKM, AUG, Novalite and P90
  (x1.25) and Hristov (x3). Needed because SS14 damage lives on the ammunition and those guns share
  calibers with Frontier weapons.

## 20. 2026-10 Shuttle radar proximity alert (Coyote)

- **Source**: Coyote commit `087cbe50ee` ("radar and the silencing of the twinks", Neenner) - a
  Coyote-original feature built on Frontier's Monolith-ported radar-blip system; it was never
  upstreamed to Frontier/Wizden.
- **Behaviour**: a per-console toggle + radius slider (1-300 m, default 128, locked while enabled)
  on the nav screen. While enabled the console beeps when a radar contact or shuttle hull enters the
  radius, pinging faster as it gets closer (30 m distance bands, 0.25-5 s interval), and the nav
  radar draws a light-blue circle at the alert radius.
- **Server**: `ShuttleConsoleComponent` gained `ProximityAlertEnabled`/`ProximityAlertRadius` plus
  timers; `_NF/Shuttles/Systems/ShuttleSystem.cs` gained the two request handlers and
  `NfUpdateProximityAlerts` (called from the base `ShuttleSystem.Update`);
  `_NF/Radar/RadarBlipSystem` gained `TryGetNearestContactDistance` (radar blips plus a per-tick
  cache of all shuttle hulls, own grid ignored, capped at the 300 m blip render distance).
- **Shared/client**: `NavInterfaceState` gained `proximityAlertEnabled`/`proximityAlertRadius`;
  `SetProximityAlertRequest`/`SetProximityAlertRadiusRequest` in
  `_NF/Shuttles/Events/SetTrackedCoordinatesRequest.cs`; NavScreen toggle + slider; the nav control
  draws the radius circle.
- **Adaptations**: skipped Coyote's Wayfarer autopilot/emergency-brake hunks and the unrelated
  global-audio changes from the same commit; `RadarBlipSystem` keeps the Palmtree
  `SharedRadarBlipSystem` base (no jetpack-event hunks needed). Markers are `// Palmtree/Coyote`.
- **Audio**: `beep_series1.ogg` + `short_beep2.ogg` copied from Coyote's `_Mono` computer SFX set
  (Monolith PR #1647 -> Wayfarer PR #108 -> Coyote PR #1004); attribution in
  `Resources/Audio/_Mono/Effects/Computer/attributions.yml`.
- **Locale**: `shuttle-console-proximity-alert*` in `Resources/Locale/en-US/_NF/shuttles/console.ftl`.

## 21. 2026-10 Expedition auto-recovery (Coyote via Wayfarer)

Fixes round removal on failed expeditions: bodies (dead, ghosted or left behind) are returned to
the shuttle instead of being deleted with the map, and an expedition where everyone worth caring
about is dead aborts early and recovers the bodies.

- **Sources**: Coyote PRs [#470](https://github.com/ARF-SS13/coyote-frontier/pull/470) (core
  recovery + abort), [#565](https://github.com/ARF-SS13/coyote-frontier/pull/565) (SSD
  forgiveness), [#1066](https://github.com/ARF-SS13/coyote-frontier/pull/1066) (borg fix), ported
  via Wayfarer PR [#1590](https://github.com/project-wayfarer/wayfarer-14/pull/1590) (their
  modern-base adaptation).
- **Behaviour**: when a shuttle auto-departs, every `HasHadMind` mob on the map that is not on that
  shuttle is teleported aboard (beds -> chairs -> consoles -> warp points), damaged (`TendToDork`),
  extinguished and temperature-reset, with sparks and a sound. If all relevant players are dead
  (aghosts, never-mind mobs, SSD-on-arrival players and borg brains excluded), `AbortNow` announces
  and forces departure in 20 s.
- **Files**: `Content.Server/Salvage/SalvageSystem.Runner.cs` (rescue loop + `RescueDork`,
  `GetDeadLoserDestinations`, `TendToDork`, `TryTeleportToStrap`, `AbortIfWiped`, `AbortNow`,
  `UpdateSsdGoobers`), `SalvageExpeditionComponent` (`NextAutoAbortCheck`, `InitialSsdGoobers`,
  `Aborted`), `_NF/procedural/expeditions.ftl` (`salvage-expedition-abort-wipe`),
  `Resources/Audio/_CS/ExpedReturnToBed.ogg` (+ attributions). Markers are `// Palmtree/Coyote`.
- **Core patch - `HasHadMind`**: Coyote's field (`MindContainerComponent.HasHadMind`, set in
  `MindSystem.TransferTo`, commit `102b623d04e`) is not upstream; it is backported so ghosted
  corpses (mind detached) still count as player bodies for the rescue and abort scans. The
  `Access(typeof(SharedMindSystem))` attribute was dropped from the component so other types may
  write it (same as Coyote; the analyzer rejects foreign writes otherwise).
- **Adaptations**: `TendToDork` uses Coyote's original damage (alive 150 Slash/150 Heat/100
  Poison, crit 50/50/25, dead untouched) instead of Wayfarer's harsher always-damage random roll;
  the #1066 borg guard and the `FireStacksAdjustment = -1000` sign fix are applied; the deathrattle
  "insistent med implants" retrigger was skipped (Wayfarer deprecated it); the rescue scan uses
  `HasHadMind` (Coyote final) rather than Wayfarer's `HasMind`, so ghosted bodies are recovered too.
- **`AdminGhostComponent` (Coyote, `Content.Shared/_CS`)**: ported as a pure marker (Coyote commit
  `88772ac83f2`; it is not upstream) and added to the `AdminObserver` prototype, so systems can
  identify admin ghosts instead of relying on `GhostComponent.CanGhostInteract`. The abort scan
  uses it exactly like Coyote; existing systems are untouched.
- **SSD return**: `SalvageSystem.OnPlayerAttached` (global `PlayerAttachedEvent`) removes a
  reconnecting player from `InitialSsdGoobers`, so a player who was SSD at arrival and comes back
  mid-expedition counts as active crew again (the expedition keeps going; the wipe abort no longer
  triggers just because the other crewmember died).
- **Expedition deathrattle wording + FTL arrival retrigger (Palmtree)**: `RattleOnTriggerComponent`
  gained `MessagesOnExpedition` (used while the target is on a `SalvageExpeditionComponent` map,
  where coordinates are useless - "has died on an expedition") and `RetryMessages` (the "STILL
  dead/critical" variants). `RattleOnTriggerSystem` sends through a shared `SendRattleMessage`
  helper and handles `ImplantRelayEvent<RetriggerRattleEvent>`; `SharedSubdermalImplantSystem.Relays.cs`
  relays the new `_PS` event. `SalvageSystem.RescueDork` marks recovered bodies with
  `ExpedRecoveredBodyComponent` (`Content.Server/_PS/Salvage/Components`) and
  `SalvageSystem.RetriggerRecoveredRattles` (called from `OnFTLCompleted`) removes the marker and
  raises the retrigger for bodies on the arriving shuttle, so medics get the real coordinates.
  Locale: `Resources/Locale/en-US/_PS/salvage.ftl`.
- Verified with scratch integration tests (mind tracking, abort logic, aghost exclusion, SSD
  rejoin, reduced rescue damage, rescue teleport/buckle); removed afterwards per convention.
  In-game retest still worthwhile.

## 22. 2026-10 Tiered thruster set (Coyote)

Six buyable/researchable thrusters that trade max speed (`baseThrust`), acceleration
(`thrust`/`thrustPerPartLevel`) and power draw, ported from coyote-frontier's
`_NF/Entities/Structures/Shuttles/thrusters_but_cooler.yml` (created by their "Danny Kay" commits,
last touched by PR [#656](https://github.com/ARF-SS13/coyote-frontier/pull/656) "Update thruster
layers"; the file is unchanged on their current master, so there is no newer version to take).

- **Thrusters**: `SlowWeakThruster` (MicroPutt StationKeeper: 33 base / 22 thrust / 1 W),
  `SlowStrongThruster` (MicroPutt TugMaster: 200 / 132 / 250 W), `FastWeakThruster` (TurboPutt
  Kobold: 33 / 60 / 4 kW), `FastStrongThruster` (TurboPutt Dragon: 100 / 190 / 7.5 kW),
  `FasterWeakThruster` (UltraPutt Vesper: 33 / 75 / 8.5 kW) and `FasterStrongThruster` (UltraPutt
  Titan: 330 / 750 / 50 kW). Each carries `damage: Heat: 40` in its exhaust plume and the same
  small/thruster sprites as the Frontier thrusters (`_NF/Structures/Shuttles/thruster.rsi`).
- **Boards/recipes/tags**: each thruster has its own `*MachineCircuitboard` entity (`MachineBoard`
  with a Capacitor requirement and stack requirements), a `latheRecipe` (Steel/Glass/Gold/Plasma/
  Diamond depending on tier) and a `Tag` prototype - all in the same file, as in the source.
- **Integration**: `NFShuttlecraftThrust` unlocks the slow pair and `NFShuttlecraftThrustSmall` the
  fast four (`_NF/Research/techtree.yml`); all six are in the `NFEngineeringBoards` lathe pack and in
  the `NFBlueprintsEngineering` blueprint pack (required by `TechnologyTreeTests`, which asserts that
  every tech-unlocked recipe is blueprintable). Coyote's separate `CSEdisionEngineeringThrusterPack`
  was skipped - this fork has no equivalent Edison lathe pack.
- **Naming**: the base `Thruster` and Frontier's `SmallThruster` are renamed to "SpacePutt Mule" and
  "SpacePutt Mini" thruster (with descriptions) so the family reads coherently; markers are
  `# Palmtree/Coyote`.
- **Layers**: coyote PR #656 ("thruster layers for damage consistency") moved the small thruster,
  small gyroscope and the new tiers from `MachineLayer` to `MidImpassable | LowImpassable` (drops
  `Opaque`/`BulletImpassable`, so bullets no longer stop on a thruster frame). Ported as-is; the big
  `Thruster`, `ThrusterSecurity`/`GyroscopeSecurity` and the NFSD variants keep `MachineLayer`, as in
  Coyote.
- **Not ported**: Coyote's gyroscope numbers (`baseThrust` 250 / `powerLoad` 200) are *older* than
  this fork's Frontier values (650 / 500 from Frontier "Small gyro buff" #4272). Their
  `ThrusterNfsd` "NFSD Experimental Thrusters" upgrade (100/190 + Heat damage + disable-anchoring/
  tool-use parents, their commit `614764c5551`) *was* ported later — see the thruster follow-up
  section below.
- **Test**: `Content.IntegrationTests/Tests/_NF/TieredThrusterTest.cs` checks every thruster/board
  pair, the lathe recipes, the blueprint pool and the two research unlocks.

## 23. 2026-10 Armor plates (Monolith via Coyote) + thruster follow-ups

**Armor plates** are Monolith's (`_Mono`) insertable plate system, taken from Monolith's current
master rather than Coyote's older copy (Coyote's 6-plate set - durathread/ceramic/plasteel/
plastitanium/plas-uranium/steel - was superseded upstream by 4 themed plates with a reworked
stamina model; Monolith's live files were fetched from
`Monolith-Station/Monolith` and diffed against Coyote's `_Mono` folder).

- **C#** (`Content.Shared/_Mono/ArmorPlate/`): `ArmorPlateHolderComponent` (armor side: active
  plate + speed/stamina stats), `ArmorPlateItemComponent` (`maxDurability` -1 = unbreakable,
  `absorptionRatios`, `damageToPlate`, `staminaDamageMultipliers` with Raw/Absorbed/Amplified
  sources), `ArmorPlateProtectedComponent` (marker on the wearer) and `SharedArmorPlateSystem`
  (insert/remove/equip/unequip handling, `BeforeDamageChangedEvent` interception, examine verbs).
  Adaptations: `StaminaSystem` -> this fork's `SharedStaminaSystem`; the
  `args.OriginFlag != DamageableSystem.DamageOriginFlag.Explosion` check became
  `args.Origin == null` (Monolith's origin flag is not upstream here); the unused `IGameTiming`
  dependency was dropped.
- **Core helper**: `InventorySystem.TryGetContainingEntity` was ported from Monolith's
  `InventorySystem.Helpers.cs` (returns the entity wearing an equipped item) - it does not exist
  upstream in this fork.
- **Prototypes**: `_Mono/Entities/Clothing/base_plating.yml` (`ClothingArmorPlate` holder +
  pre-filled variants) and `_Mono/Entities/Clothing/OuterClothing/Armor/armor_plates.yml`
  (`ArmorPlateBlunt_Slash`, `ArmorPlatePierce`, `ArmorPlateHeat`, `ArmorPlateSpeed` +
  `ArmorPlateRepairableBase`). Adaptation: Monolith repairs plates with their `Applicating` tool
  quality (which this fork lacks), so the plates use the standard `Repairable` welding path.
- **Where plates fit**: `ClothingOuterArmorBasic` and `ClothingOuterHardsuitBase` carry
  `ClothingArmorPlate` (Monolith's two patches). Since the 2026-10-15 follow-up the whole storage
  outerwear tree also carries it (`ClothingOuterStorageBase` - jackets, coats, hoodies) plus the
  chameleon vest (`ClothingOuterChameleon`), which is Coyote's `outerweararmor` change (coyote PR
  #1059). Clothing inheritance is per-field, so jackets keep their own `Storage.grid` (3x2) and the
  holder just adds the active-plate component; the chameleon vest's explicit `UserInterface` block
  carries both the storage and chameleon UI keys.
- **Recycling (2026-10-15)**: each plate carries a `PhysicalComposition` using materials from its
  own recipe/description (trauma: plasteel/plastic/durathread; ballistic: plasteel/plastic/
  durathread; diffusion: silver/diamond/durathread; speed: brass/bluespace/durathread), bounded
  far below the lathe recipes so `MaterialArbitrageTest`'s produce >= recycle bound holds.
  `ArmorPlateTest` asserts the holder on jackets and a composition on every plate. The fork-wide
  item audit that came with this change (what was recyclable, what was made recyclable, what was
  left and why) is `.ai/workspace/recycling-audit-2026-10-15.md`.
- **Acquisition**: lathe recipes in `_Mono/Recipes/Lathes/equipment.yml` (`ArmorNF` category) and
  the two research nodes in `_Mono/Research/armor_plates.yml` (`UniversalArmorPlatesT1/T2`,
  adapted to the `NFArsenalMercenary` discipline at positions (3,45)/(5,45), the same free cells
  Coyote used). All four recipes are in the `NFMercenaryArchetypeStatic` lathe pack and the
  `NFBlueprintsMercenary` blueprint pack (required by `TechnologyTreeTests`).
  Material substitutions (Monolith materials this fork does not have): Plastitanium -> Plasteel,
  Copper -> Silver, Iridite -> Diamond.
- **Locale/assets**: `Resources/Locale/en-US/_Mono/armor-plate.ftl`,
  `Resources/Locale/en-US/_Mono/research/armor_plates.ftl` and
  `Resources/Textures/_Mono/Clothing/ArmorPlate/armor_plates.rsi` (CC-BY-SA-4.0, Onezero0/tonotom1
  per its meta.json).
- **Not ported**: Monolith's economy/arc-furnace recipes, trade crates, cargo products, loadout
  entries, points of interest and their `RepairableComponent` change (a `Qualities` list + the
  `Applicating` tool quality).
- **Test**: `Content.IntegrationTests/Tests/_Mono/ArmorPlateTest.cs` (insert plate -> absorb ->
  durability + stamina -> remove).

**Thruster follow-ups (Coyote + Wizden):**

- `ThrusterNfsd` now carries Coyote's "NFSD Experimental Thrusters" upgrade (their commit
  `614764c5551`): parents `[BaseStructureDisableAnchoring, BaseStructureDisableToolUse,
  ThrusterSecurity]` so it cannot be unanchored or pried, and a `Thruster` override with
  `baseThrust: 100`, `thrust: 190`, `thrustPerPartLevel: [247, 304, 361, 418]` and `Heat: 40`
  exhaust damage.
- Wizden's `ThrusterComponent.CanToggle` (their #42201, "make xenoborg thrusters anti-easy
  sabotage") is ported with the matching guard in `OnActivateThruster`, so a prototype can opt out
  of hand toggling. Nothing sets it to false yet (this fork has no xenoborg thrusters).

## 24. 2026-10 RPD + RCD improvements (Coyote `palm3`)

Source: the frozen Coyote checkout's `palm3` commits `ddda1d9695` ("RPD added") and `2a62f7373a`
("Fixes and Adjustments"); the two client quickfixes (`c37fe1109f`, `a2116b0102`) are superseded by
this repo's newer client code.

- **RPD** (`RPD`/`RPDEmpty`): a new tool with 19 build modes — pipes (fourway/straight/bend/
  T-junction), pumps & valves (pressure/volume/manual/signal/pneumatic), vents (injector/scrubber/
  pump/dual-port/passive), atmospheric utility (radiator, radiator bend, gas mixer/filter, canister
  port) — plus `Deconstruct`; 60 charges and it takes standard `RCDAmmo` (Coyote itself dropped the
  short-lived `RPDAmmo` entity in its own fix commit).
  - Modes: `Resources/Prototypes/_PS/RCD/rpd.yml`; the radial icons were repointed from the
    upstream `Interface/Radial/RPD` path Coyote used to `Resources/Textures/_PS/Interface/Radial/RPD/`
    and the item sprite to `Resources/Textures/_PS/Objects/Tools/rpd.rsi`.
  - Client menu categories `Piping`/`AtmosphericUtility`/`PumpsValves`/`Vents` are registered in
    `RCDMenuBoundUserInterface`; their locale keys live in `Resources/Locale/en-US/_PS/rcd.ftl`.
- **RCD improvements** (same commits):
  - `WallShuttle` construct mode (`Resources/Prototypes/_PS/RCD/rcd.yml`) added to the RCD's mode
    list, and the shuttle wall's `RCDDeconstructable` cost/delay lowered to 3/2 (`#Palmtree`).
  - `RCDComponent.InstantConstruction`: 0-delay do-afters with `EffectRCDConstruct0`. Used by
    `RCDExperimental` (admeme), which also got a wider mode list (plastitanium walls/windows from
    the old `_CS` RCD set, mining walls, wood floor).
  - **Mirror prototypes**: `RCDPrototype.MirrorPrototype` + `RCDComponent.UseMirrorPrototype`,
    flipped with the editor flip keybind (`EditorFlipObject`, default `F`) while placing, synced by
    `RCDConstructionGhostFlipEvent`; used by the RPD's gas mixer/filter
    (`GasMixerFlipped`/`GasFilterFlipped`).
  - The old code's `RcdConstructionRule.IsWallLight` ("no light spam") rule is **not** ported: it
    referenced a `WallLight` tag prototype that exists in neither checkout (the lint rejects it) and
    no shipped mode ever opted into the rule anyway.
  - `RCDDeconstructableComponent.RpdDeconstructable` (`rpd: true`): the RPD refuses to deconstruct
    tiles and only deconstructs whitelisted entities. Coyote never set the flag; this fork wires it
    on `GasPipeBase` (`deconstructable: false` + `rpd: true` + cost 1 / delay 0 / `EffectRCDConstruct0`)
    so the RPD removes pipes, pumps, valves, vents, injectors, radiators, mixers, filters and ports
    while the RCD stays off atmos entities. The RPD pass in `RCDSystem` was split so `rpd:` gates the
    RPD and `deconstructable:` gates the RCD (Coyote's single check required both flags).
- **Integration**: the engineer locker (`FillLockerAtmospherics`) and the EngiVend now stock the
  RPD; the `RPD` lathe recipe (result `RPDEmpty`) sits in `Recipes/Lathes/tools.yml`, is unlocked
  by `NFRapidConstruction` together with `RCDAmmo`, and both are in the `NFEngineering` and
  `NFBasicToolsStatic` lathe packs plus the `NFBlueprintsEngineering` blueprint pack (the latter is
  required by `TechnologyTreeTests`, which asserts every tech unlock is printable from a blueprint).
- **Deliberately not ported**: the old `_CS` RCD roster (Pirate/NFSD RCDs and their locked-door
  modes) — this repo already ships the newer Frontier pirate/shipyard RCDs. Only the four
  plastitanium modes required by `RCDExperimental` were taken.

### 24a. 2026-10 RPD second pass: pipe colours, pipe layers, missing modes

The first pass above only took the RPD's mode list. This pass brings the rest of Coyote's RPD
(`b7a81c6e4a` "NEW SHINY RPD" plus the `palm3` follow-ups), which is what made that tool good:

- **Pipe colours.** `RCDComponent.PipeColorPalette` (`Dictionary<string, Color>`, YAML
  `pipeColorPalette:` on the `RPD`) + `PipeColorKey` (default `"default"` = no paint). The RPD's
  own menu (`RPDMenu`) shows them as an always-visible strip along the bottom.
  `RCDColorChangeMessage` carries only the palette **key**; the server resolves the colour from the
  palette, so no tuple crosses the wire. Palmtree applies the colour *functionally*, not just
  cosmetically: the shared system sets the `PipeColorVisuals.Color` appearance datum and raises
  `RCDPipeColorAppliedEvent` on the spawned entity, which `AtmosPipeColorSystem` turns into
  `AtmosPipeColorComponent.Color` — so atmos monitoring consoles and the spray painter agree.
  Coyote only touched the appearance datum.
- **Pipe layer placement.** `AlignRPDAtmosPipeLayers` (placement mode) picks the atmos pipe layer
  from where the cursor sits inside the tile — the middle quarter (radius `0.25`) is `Primary`, and
  the two halves either side are `Secondary`/`Tertiary` — previews it by swapping the placer's
  `EntityType` to the layer prototype, and draws the three guide circles. `RCDSystem` repeats the
  same computation in `OnAfterInteract` and substitutes the prototype in
  `FinalizeRCDOperation` via `SharedAtmosPipeLayersSystem.TryGetAlternativePrototype`. The resolved
  layer travels in `RCDDoAfterEvent.Layer`: the operation finishes a tick later, so keeping it in
  system state let concurrent placements overwrite each other's layer (fixed after the merge).
  `RCDPrototype.NoLayers` (YAML `noLayers: true`) opts a mode out: either its entity has no
  `AtmosPipeLayersComponent` at all (`Alarm`, `Sensor`) or it has one offering no alternative
  prototypes (`ManifoldGas`, whose `AtmosPipeLayers` is `pipeLayersLocked: true`).
- **Six more modes**: `PipeHalf`, `ManifoldGas`, `PressureRegulator`, plus a new
  `SensorsMonitors` category with `Alarm` (air alarm), `Sensor` (air sensor) and `SensorPipe` (gas
  pipe sensor). Icons reuse the entity sprites, so no art was added.
- **Own UI key.** `RpdUiKey` + `RPDMenuBoundUserInterface`/`RPDMenu`, so the RPD opens its own menu
  (mode radial + colour strip) instead of the RCD's modern `SimpleRadialMenu`. The RPD has its own
  `UserInterface` entry for `RpdUiKey` and re-points `ActivatableUI.key` off the RCD's `RcdUiKey` —
  that key is what decides which menu opens (the inherited `RcdUiKey` mapping may still be present
  in `interfaces` and is harmless).
- **Deconstruct whitelist extended** to `AirAlarm` (cost 2), `AirSensor` and `GasPipeSensor`
  (cost 1 each), so the RPD can also pull down what its new sensor modes build.
- **Admin log**: the RPD logs its operations exactly like the RCD (construct tile/spawn, deconstruct
  tile/object), with the message naming the tool (`var toolName = component.IsRpd ? "RPD" : "RCD";`
  in `RCDSystem.FinalizeRCDOperation`). The PR had removed RPD logging; it was restored with the
  merge because pipe removal needs an audit trail.

Two bugs only surfaced on a live debug build, both recorded in `.ai/HAZARDS.md` §26: a
`[Dependency]`-injected `HandsSystem` crashed the client when the placer was constructed, and a
`var tileSize` (ushort integer division) snapped RPD placement to the tile corner instead of its
centre — which also broke the pipe-layer deadzone.

See `.ai/HAZARDS.md` §26 for the traps this pass introduced.

## 25. 2026-10 Salto expedition ship (Coyote `_CS`)

- Map: `Resources/Maps/_CS/Shuttles/Expedition/salto.yml` — the first `_CS` map this fork has
  (format-7 save made on engine 261, 152 entities). Every one of the 54 prototypes and all 28
  component types it uses exist here, and it is not on the Frontier ignore list.
- Shipyard: `Resources/Prototypes/_CS/Shipyard/Expedition/salto.yml` — the `Salto` vessel
  (`LL0 Salto`, Small/Expedition/AME, price 62500) plus its `gameMap` entry. It appears on the
  expedition shipyard console through `group: Expedition`; no guidebook page (Coyote has none
  either, so there is no `ShuttleMapSalto` poster to port).
- Adaptations: `CompanionInterview` was dropped (the Companion job is Coyote `_CS` content that
  needs the un-ported RPI systems), and the interview jobs use the `[ 0, 0 ]` convention every
  other shipyard file here uses (Coyote's local tweak used `[ 2, 2 ]`).
- Validation: `ShipyardTest` loads every vessel map and checks price vs appraised value
  (`MinPriceMarkup` 1.05) — both tests cover the Salto.

## 26. 2026-10 SPLURT Funny emotes (BYOND)

Source: `BYOND/S.P.L.U.R.T-Station-13/modular_splurt/code/modules/mob/living/emotes.dm`
(the old SPLURT emote file, not the newer `/tg` checkout). Ported as a single Palmtree category
rather than scattered species categories.

- **Category**: `EmoteCategory.Funny`, set to `Vocal | (1 << 16)` so mutes and mumbling still
  apply. The enum backing was widened from `ushort` to `int` (all 16 old bits were taken);
  `General` became `int.MaxValue`. The category is registered in `EmotesUIController` and
  `EmoteCategoryWindow`, with `emote-menu-category-funny = Funny`.
- **Availability**: every prototype is `available: true` with no whitelist/blacklist, so any
  species can use the chat triggers (including `*laugh2`/`*laugh3`). The radial menu only shows a
  curated 33-entry headline set (`showInWheel: false` on the long tail) because one ring cannot
  fit 116 buttons on screen; every emote still works through its trigger. The character editor's
  emote category picker can hide the whole category.
- **Content**: 116 emotes from the SPLURT file, including `Laugh2`-`Laugh6`, `Cackle`,
  `Chuckle2`, `Cry2`, `Scream2`/`Scream3`, the bababooey/fafafooey set, `Bruh`, `Speen`,
  `Waa`/`Weh2`-`Weh4`, the gachi and Ace Attorney sets, animal/meme noises, and visual
  emotes (tilt/squint/ruffle/mlem/pant/squirm/facepaw etc.).
  - Excluded because the fork already has them: `chirp`, `caw`, `mew`, `meow`, `bleat`, `yap`,
    `yip`, `howl`, `moo`, `eyebrow2` (the existing `Eyebrow` is the same vine-boom emote).
  - Excluded as sexual content (not Funny): `moan2`, `ambatukam`/`ambatukam2`, `ara`/`ara2`,
    `rizz`. `twerk` and `fart` were removed on request.
- **Files**: `_PS/Voice/speech_emotes_funny.yml` (prototypes),
  `_PS/Voice/speech_emote_sounds_funny.yml` (`SupplementalFunnyEmotes`, an abstract parent of
  `SupplementalCoyoteEmoteStuff`, so every vocal species gets the sounds; plus concrete
  `SupplementalFunnyMale`/`SupplementalFunnyFemale` for the gendered Ace Attorney lines),
  `_PS/SoundCollections/emotes_funny.yml` (random variants), `_PS/chat/emotes_funny.ftl`, and
  `Resources/Audio/_PS/Voice/Emote/Funny/**` (160 copied OGGs with an attribution file).
- **Chat text**: `*laugh2`/`*laugh3` are ordinary `chatTriggers`; all triggers were checked
  for duplicates against every other emote prototype (none). ChatSystem lowercases and trims the
  `*` prefix before the dictionary lookup, so the keys match. The chat-trigger path originally
  echoed the raw trigger ("Mike Winterbells laugh2"); `SendEntityEmote` now detects the matched
  Funny emote and sends its localized `ChatMessages` entry instead.
- **Gender split**: `VocalComponent.SexSupplementalSounds` (Palmtree) is checked before the shared
  supplemental set, so `Objection`, `HoldIt` and `TakeThat` play the SPLURT `_m`/`_f` collections
  for male/female mobs. The files are explicitly named (`ace_objection_m1..m3` / `_f1..f2`, same
  for holdit/takethat), so no manual review is needed.
- **`emotelist`**: server console/player command (`Content.Server/Chat/Commands/EmoteListCommand.cs`)
  dumps every chat-triggerable emote as `[*emote = "text"]`, three per line. The server console has
  no entity, so it passes a plain-string entity context to Fluent to get neuter pronouns without
  logging `MetaDataComponent` errors.
- Validation: full solution build, rebuilt YAMLLinter (`No errors found`), `Content.Tests`,
  `_PS|EntityTest|CharacterCreationTest` integration tests and the headless client IL check all
  pass. Scratch integration coverage confirmed `*laugh2` produces "laughs like a king." in the
  client chat history, the male/female supplemental prototypes resolve, and `emotelist` runs.
