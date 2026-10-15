# System: Markings & Humanoid Appearance

> The heart of this fork's customization layer: a Floof/Coyote-derived marking system grafted
> additively onto Frontier's upstream marking code. Read `.ai/HAZARDS.md` §2–§3 and §13 first.

## Purpose

Player character customization beyond upstream SS14: shared markings across species groups
(`kindAllowance`), layered sprites, color links, per-marking scale/offset, digitigrade leg styles,
base-layer replacement markings (species adaptors, furry chests) and genital markings with
show/hide verbs.

## Locations

| Piece | Path |
|---|---|
| Marking data model | `Content.Shared/Humanoid/Markings/Marking.cs` |
| Prototype definition | `Content.Shared/Humanoid/Markings/MarkingPrototype.cs` |
| Category enum + conversions | `Content.Shared/Humanoid/Markings/MarkingCategories.cs` |
| Marking set (per-humanoid storage) | `Content.Shared/Humanoid/Markings/MarkingsSet.cs` |
| Prototype cache/filters | `Content.Shared/Humanoid/Markings/MarkingManager.cs` |
| Coloring (incl. color links) | `Content.Shared/Humanoid/Markings/MarkingColoring.cs` |
| Layer enum | `Content.Shared/Humanoid/HumanoidVisualLayers.cs` |
| Layer helpers | `Content.Shared/Humanoid/HumanoidVisualLayersExtension.cs` |
| Leg style enum | `Content.Shared/Humanoid/HumanoidLegStyle.cs` |
| Species prototype | `Content.Shared/Humanoid/Prototypes/SpeciesPrototype.cs` |
| Base sprite prototypes | `Content.Shared/Humanoid/Prototypes/HumanoidSpritePrototypes.cs` |
| Humanoid component | `Content.Shared/Humanoid/HumanoidAppearanceComponent.cs` |
| Profile appearance | `Content.Shared/Humanoid/HumanoidCharacterAppearance.cs` |
| Shared server logic | `Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs` |
| Client renderer | `Content.Client/Humanoid/HumanoidAppearanceSystem.cs` |
| Marking picker UI | `Content.Client/Humanoid/MarkingPicker.xaml(.cs)` |
| Character editor wiring | `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs` |
| Undies/genital verbs | `Content.Server/_Floof/ModifyUndies/*`, `Content.Shared/_Floof/ModifyUndiesDoAfterEvent.cs` |
| Marking prototypes | `Resources/Prototypes/Entities/Mobs/Customization/Markings/*`, `_Floof/.../markings/*`, `_PS/.../genitals.yml`, `_DV`, `_DEN`, `_EE`, `_Starlight`, `_White` |
| Sprites | `Resources/Textures/Mobs/Customization/*`, `Floof/Mobs/Customization/*`, `_PS`, `_CS`, `_DV`, `_DEN`, `_EE`, `_Starlight`, `_White` |
| Locale | `Resources/Locale/en-US/preferences/ui/markings-picker.ftl`, `_Floof/anthro.ftl`, `_CS/genitals.ftl`, `_CS/undies*.ftl`, `_PS/genitals.ftl`, prefix marking locales |

## Data model

`MarkingPrototype` (YAML `type: marking`) fields:

| Field | Meaning |
|---|---|
| `id` | Marking id; locale key `marking-<id>` (and `marking-<id>-<state>` per sprite) |
| `bodyPart` | `HumanoidVisualLayers` the marking defaults to |
| `markingCategory` | `MarkingCategories` used for editor grouping and points |
| `speciesRestriction` | Explicit species allowlist (optional) |
| `kindAllowance` | Species `kind` allowlist (Palmtree/Floof) — see below |
| `sexRestriction` | Optional `Sex` restriction |
| `sprites` | List of `SpriteSpecifier.Rsi` states; one color per sprite |
| `layering` | `Dictionary<string, HumanoidVisualLayers>`: sprite state → layer override |
| `colorLinks` | `Dictionary<string, string>`: child state → parent state (inherits color; hidden from color picker) |
| `altSprites` | `Dictionary<HumanoidLegStyle, ProtoId<MarkingPrototype>>`: leg-style variants |
| `hidden` | Hide from the editor list (used via another marking) |
| `baseLayerSprite` | Sprite used when a base marking replaces a species base layer |
| `followSkinColor` / `forcedColoring` / `coloring` | Coloring behavior (upstream + Floof `coloring` types) |
| `shader` | Optional layer shader (Impstation) |

`Marking` (runtime instance) fields: `markingId`, colors, `visible`, `forced`, plus Palmtree
extensions `scale`, `offsetX/offsetY`, `glowLevels` (per-color 0..1, legacy `glow` scalar) and the
per-marking toggle settings `customName`, `canToggleVisible` (default false; undergarments default
true), `otherCanToggleVisible` (default false). `Marking.ToString()` / `Marking.ParseFromDbString()` define the DB format:

```
markingId@#rrggbb,#rrggbb,...[@scale,offsetX,offsetY][@g0.5,1,...][@m3][@cCustom Name]
```

Optional segments: transform (only when scale/offset differ from defaults), glow (`g`, only when any
glow is non-zero), visibility flags (`m`, only when the marking differs from
`CanToggleVisible = true` / `OtherCanToggleVisible = false`; bit 1 = self-toggle, bit 2 = others) and
custom name (`c`, only when set; `@` in the name is written as `_` so it cannot break the format).
Old strings remain valid and default to self-toggleable, not other-toggleable. `Equals` includes
scale/offset/glow/visible and the visibility settings. Change `ToString` and `ParseFromDbString`
together.

`HumanoidAppearanceComponent` additions: `HiddenBaseLayers` (`List<HumanoidVisualLayers>`) and
`LegStyle`. `HumanoidCharacterAppearance` adds `LegStyle` (persisted in the profile and exported
characters).

## kindAllowance (cross-species markings)

- `SpeciesPrototype.kind` is a list of strings (`BasicHumanlike`, `BasicFurry`, `BasicRobot`,
  `VoxLike`, `Resomi`, ...). Most playable species carry one or two kinds.
- `MarkingPrototype.kindAllowance` lets a marking be used by any species whose kind intersects.
- The rule: allowed if `speciesRestriction` is null, or contains the species, or
  `kindAllowance` intersects `species.kind`.
- Implemented in `MarkingManager.IsAllowedBySpeciesOrKindAllowance(SpeciesPrototype, MarkingPrototype)`.
  **Every filter path must call it** (see HAZARDS §2).
- `onlyWhitelisted` species (e.g. some forks) require either `speciesRestriction` or
  `kindAllowance` to be present.
- This repo adds `kindAllowance: [BasicHumanlike, BasicFurry, BasicRobot, VoxLike]` to **all**
  species-restricted markings (915 across 39 files), mirroring Coyote: any species with a matching
  kind can wear them. New species-restricted markings should include it.

## Digitigrade legs & clothing displacement

- `HumanoidCharacterAppearance.LegStyle` is chosen in the marking picker (`Plantigrade`/`Digitigrade`)
  and persisted/exported with the profile.
- Species base sprites use `altSprites` (e.g. `MobSynthLLeg → DigilegSynthlizLegLeft`); markings use
  their own `altSprites` (species adaptors → `DigilegPaw*`/`DigilegFurry*`) via
  `HumanoidAppearanceSystem.GetMarkingForLegStyle` (client). Anthromorph/Reptilian species base
  sprites were missing `altSprites` entirely (digitigrade did nothing) and got them for torso, legs
  and feet; the vulpkanin tail markings got their `altSprites → DigiVulpTail*` mappings.
- The shared `BaseMobSpeciesOrganic` `Sprite` layer list was missing the behind-leg layers
  (`RLegBehind`/`LLegBehind`/`LFootBehind`/`RFootBehind`); they were added in the correct draw
  order. `LayerMapReserve` appends unknown layers at the end otherwise, so the digitigrade behind
  sprites would draw over clothing. Species also need a `speciesBaseSprites` entry
  (`LLegBehind: MobHumanoidAnyMarking`) for markings to be visible on those layers
  (`BaseLayers[layer].AllowsMarkings`) — Anthromorph already had them, Reptilian got them.
- Clothing accommodation: `LegDisplacementPrototype` (`_CS/LegDisplacement.yml`) +
  `HumanoidAppearanceComponent.LegDisplacements` (default `LegDisplacementDigitigrade`).
  `ClientClothingSystem.RenderEquipment` calls `HumanoidAppearanceSystem.GetDisplacementForLegStyle`
  to override jumpsuit/shoes/outerClothing displacement maps when the species allows it
  (`SpeciesPrototype.AllowDigilegDisplacement`).

## Layers

`HumanoidVisualLayers` now includes the Floof layers: `Genital`, `TailBehind`, `TailOversuit`,
`NeckFluff`, `RLegBehind`/`LLegBehind`, `RFootBehind`/`LFootBehind`, `UndershirtUnderclothes`,
`UndershirtOverclothes`, `TailExtras`, `Disregard`.

Layer maps must exist in two places for a species to render a marking on that layer:

1. The mob's `Sprite` component layers (`Resources/Prototypes/Entities/Mobs/Species/base.yml` for
   the shared organic base + any species with a custom `Sprite`).
2. The species' `speciesBaseSprites` prototype (`Genital: MobHumanoidAnyMarking`, etc.). The client
   requires `BaseLayers[layer].AllowsMarkings` to render markings there.

`HumanoidVisualLayersExtension.Sublayers` ties related layers together (behind legs, tail layers,
undershirts) so hiding a parent hides its sublayers.

## Rendering pipeline (client)

1. `HumanoidAppearanceSystem.UpdateSprite` → `UpdateLayers` → `ApplyMarkingSet` → `UpdateLayersAgain`.
2. `LoadProfile` adds profile markings through the `Marking`-preserving `AddMarking` overload so
   scale/offset/glow survive the trip from profile to component; the `(string, colors)` overload
   creates a fresh marking and drops them (see HAZARDS).
2. `UpdateLayers` clears and rebuilds `BaseLayers` from the species' `speciesBaseSprites`, applying
   `altSprites` for the current `LegStyle` (base-layer variant), then `SetLayerData`.
3. `ApplyMarkingSet` iterates every marking; for each:
   - `GetMarkingForLegStyle` swaps in an `altSprites` variant when one matches the leg style.
   - `ApplyMarking` computes per-sprite `layerSlot` (BodyPart or `layering` override), creates layers
     named `<markingId>-<state>`, applies scale/offset, color (with `colorLinks`), visibility and
     displacement, and clamps genital layers below `jumpsuit`/`outerClothing`.
     When a color's glow is > 0 it also creates a companion `<markingId>-<state>-glow` layer with the
     `unshaded` shader, splitting alpha so the composed alpha is preserved.
   - Base markings (`Base*` categories) add their target layer to `HiddenBaseLayers` via
     `MarkingCategoriesConversion.Category2Layer`.
4. `UpdateLayersAgain` hides every layer in `HiddenBaseLayers` (species adaptors replacing body
   parts).
5. Genital markings are hidden by default (`AddMarking` sets `Visible = false` for the Genital
   category) and toggled at runtime through `ModifyUndies`.

## ModifyUndies (marking toggle verbs)

- `ModifyUndiesComponent` (server) is attached to `BaseMobSpeciesOrganic` and `BaseSpeciesDummy`.
  It is a marker component only; there is no body-part allowlist anymore.
- `ModifyUndiesSystem` adds a verb for **every marking** whose `CanToggleVisible` (owner) or
  `OtherCanToggleVisible` (other players) is set. Defaults are off for the owner except
  undergarments (which default on), and off for others; players opt in/out per marking in the
  editor. New markings get the default from `MarkingPrototype.AsMarking()`, and
  `MarkingsSet.EnsureValid`/`EnsureSpecies` normalize old saves (`ApplyDefaultTogglePermission`).
- The per-marking opt-in doubles as consent for other players (the account NSFW age gate only
  gates genital *rendering* and the editor tab, not these verbs).
- The verb starts a 1s do-after (`ModifyUndiesDoAfterEvent`), then calls
  `SharedHumanoidAppearanceSystem.SetMarkingVisibility`, which flips `Marking.Visible` and dirties
  the component (auto-networked `MarkingSet`). Visibility is session state, not persisted.
- Verb/popup text uses `CustomName` when set, otherwise `marking-<id>`.
- Icons: `Resources/Textures/Interface/VerbIcons/{undies,bra,underpants,love}.png` (or the first
  marking sprite); locale in `Resources/Locale/en-US/_Floof/markings/modify_undies.ftl`.

## Editor UI

`MarkingPicker`:
- Enumerates `MarkingCategories`; categories with markings for the current species appear as buttons.
- `GetMarkings` uses `MarkingManager.MarkingsByCategoryAndSpeciesAndSex` (kind-aware).
- Collapsible "Adjust position/size" controls edit scale (0.25–3.0) and offset X/Y (−1..1) via
  sliders + spin boxes, persisted through `Marking.SetScale/SetOffset`.
- Collapsible "Marking settings" controls edit the custom name and the self/other toggle
  permissions, persisted through `Marking.SetCustomName`, `CanToggleVisible` and
  `OtherCanToggleVisible`.
- Each color has a **Glow** slider/spin box (0–100%) persisted through `Marking.SetGlow`; glowing
  markings render an `unshaded` companion layer.
- Marking color selectors expose an **alpha** slider (`ColorSelectorSliders.IsAlphaVisible`), and the
  stored color keeps its alpha through the DB string/export (`Color.ToHex()` is 8-digit RGBA). The
  picker must not strip alpha when loading colors (`MarkingPicker.OnUsedMarkingSelected`).
- Hair/facial hair/eye/skin colors go through `HumanoidCharacterAppearance.ClampColor`, which must
  preserve the alpha channel (`new Color(RByte, GByte, BByte, AByte)`); stripping it silently undid
  the hair picker's alpha slider.
- SlimePerson: `MobSlimeMarkingFollowSkin` no longer sets `markingsMatchSkin`, so slime hair/facial
  hair keep their own colors, and `SlimeNose` lost `forcedColoring` — all slime markings are
  recolorable (per the user's request; the body itself still uses the skin color).
- Leg Style selector (`Plantigrade`/`Digitigrade`) raises `OnLegStyleChanged`; the profile editor
  stores it with `HumanoidCharacterAppearance.WithLegs` and re-renders the preview.
- Color selectors skip states present in `colorLinks` (they inherit a parent's color).
- `HumanoidProfileEditor` wires `OnMarkingColorChange`, `OnMarkingRankChange`, `OnLegStyleChanged`.

## Species integration

To make markings work for a species:

1. `kind` list on the species prototype.
2. `Genital`/`TailBehind`/other layers in `speciesBaseSprites` (value `MobHumanoidAnyMarking`).
3. Sprite layer maps in the mob's `Sprite` (shared base covers most species; custom sprite species
   need the maps added).
4. `Genital` marking points entry (avoids missing-key surprises in `MarkingsByCategoryAndSpecies`).
5. `altSprites` on `humanoidBaseSprite` entries for digitigrade variants (optional).

Rodentia/Anthromorph/Tajaran were adapted this way; see `.ai/guides/adding-species.md`.

Marking-point limits decide which markings survive `EnsureValid` on import/load (over-limit entries
are dropped, in list order). Human, Vulpkanin and Reptilian use Coyote's 35-per-category limits
(Tail/HeadTop 999 on Vulpkanin) so Coyote character exports keep their tails and cross-species
markings; Anthromorph is already 999 everywhere.

## Height / width

`HumanoidCharacterProfile.Height`/`Width` are visual multipliers (standard `1`, clamped to
`0.5`-`2` — the sliders max out at double the standard size). They are set by the **Height**/
**Width** sliders in the editor's Appearance tab (`CHeightSlider`/`CWidthSlider`), copied to
`HumanoidAppearanceComponent.Height`/`Width` (networked) by `LoadProfile`, and applied on the client
as `SpriteComponent.Scale = (Width, Height)` in `HumanoidAppearanceSystem.UpdateSprite`. Persisted
in the `height`/`width` profile DB columns (`HeightWidth` migration, default 1).

## Genital content

- **Superseded by organs (2026-10).** The genital marking library is now a render backend for
  player-configured genital organs: the MarkingPicker ignores the `Genital` category, profiles with
  genital markings have them converted to organs and stripped in `HumanoidCharacterProfile.EnsureValid`
  (`GenitalOrganSettings.TryConvertMarking` matches the marking against the organ catalog), and
  `GenitalOrganSystem` adds forced, non-toggleable render markings on the `Genital` layer from the
  organ catalog. See `systems/genital-organs.md`.
- Core library: `Resources/Prototypes/Entities/Mobs/Customization/Markings/genitals.yml` (379),
  `butts_and_bellies.yml` (19); Palmtree breasts `_PS/.../genitals.yml` (18).
- Sprites: `Resources/Textures/Mobs/Customization/Markings/Genital/{balls,belly,breasts,butt,cocks,vagina}.rsi`
  and `Resources/Textures/_PS/Mobs/Customization/Markings/Genital/breasts.rsi`.
- `RenderOverClothing` was **not** ported (no prototypes use it); genital sprites are clamped below
  jumpsuit/outer clothing, which is also the organ exposure rule (no jumpsuit = exposed).

## Dependencies

Species/marking prototypes, `MarkingManager` IoC singleton, profile/preferences, client sprite
system, `DoAfter`, actions/verbs (ModifyUndies), `MarkingColoring`.

## Tests

- `Content.Tests/Shared/Humanoid/MarkingSerializationTest.cs` — DB string round-trip (defaults,
  transform, legacy format, clamping).
- `EntityTest`/`CharacterCreationTest` cover spawning and profile application broadly.

## Unknowns

- Whether marking visibility should persist across sessions (currently session-only).
- Whether `RenderOverClothing`/directional marking offsets should be ported for future content.
