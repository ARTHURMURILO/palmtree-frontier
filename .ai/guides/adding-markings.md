# Guide: Adding Markings

Markings are prototypes of `type: marking`, cached by `MarkingManager`, stored per humanoid in a
`MarkingSet`, and rendered by the client `HumanoidAppearanceSystem`. See
`.ai/systems/marking-and-appearance.md` for the full model.

## 1. Minimal marking

```yaml
- type: marking
  id: MyMarkingId
  bodyPart: Chest              # HumanoidVisualLayers
  markingCategory: Chest       # MarkingCategories
  speciesRestriction: [Vulpkanin]          # optional explicit allowlist
  kindAllowance: [BasicHumanlike, BasicFurry] # optional shared allowlist
  sprites:
  - sprite: _PS/Mobs/Customization/MyPack/markings.rsi
    state: my_state
```

Checklist:

- [ ] `.rsi` exists with every referenced state (`meta.json` lists them).
- [ ] Locale keys `marking-MyMarkingId` and `marking-MyMarkingId-my_state` exist in a `.ftl`.
- [ ] The layer (`Chest` here) exists in the species' `speciesBaseSprites` and in the mob's `Sprite`
      layer maps, or the marking silently does not render.
- [ ] Category has a `markingPoints` entry on the species, or the category is intentionally unlimited
      (missing entries return −1 / unlimited).

## 2. Cross-species markings (`kindAllowance`)

`kindAllowance` lets a marking be used by species that share a `kind` with it, even when
`speciesRestriction` lists other species. Species kinds currently in use: `BasicHumanlike`,
`BasicFurry`, `BasicRobot`, `VoxLike`. Some files also list `Resomi`/`Avali` in `kindAllowance`;
no species carries those kinds, so those entries are inert.

Rules:

- A marking with no `speciesRestriction` is allowed everywhere.
- Allowed if the species is listed, or kinds intersect.
- **Never** write `SpeciesRestrictions.Contains(...)` directly; use
  `MarkingManager.IsAllowedBySpeciesOrKindAllowance` (HAZARDS §2).

## 3. Layered markings (`layering`, `colorLinks`)

`layering` routes individual sprite states to arbitrary layers, e.g. breasts whose `BEHIND` sprites
draw behind the body:

```yaml
  layering:
    m_breasts_pair_0_FRONT_primary: Genital
    m_breasts_pair_0_BEHIND_primary: TailBehind
  colorLinks:
    m_breasts_pair_0_BEHIND_primary: m_breasts_pair_0_FRONT_primary
```

- Values are `HumanoidVisualLayers` (strongly typed; no runtime `Enum.Parse`).
- `colorLinks` maps child → parent; the child is hidden from the color picker and inherits the
  parent's color. This is required when the same visual element is split across layers.

## 4. Leg-style variants (`altSprites`)

```yaml
  altSprites:
    Digitigrade: MyMarkingDigitigrade
```

The client swaps to the referenced marking when the humanoid's `LegStyle` matches (falling back to
`Digitigrade` when the current style has no exact entry). Used by species adaptors and digileg
markings. The referenced prototype must exist or the YAMLLinter fails.

## 5. Base-layer replacement markings (`Base*` categories)

Species adaptors use categories such as `BaseChest`, `BaseArms`, `BaseLegs`, `BaseHead` with
`bodyPart` pointing at the body layer. When applied, `MarkingCategoriesConversion.Category2Layer`
hides the species base layer (via `HiddenBaseLayers`). Add `baseLayerSprite` when the marking also
needs to supply the replacement base sprite for a leg style. `hidden: true` keeps helper markings out
of the editor list.

## 6. Genital markings

Follow the same pattern with:

```yaml
  bodyPart: Genital
  markingCategory: Genital
```

- Genital markings start hidden (`AddMarking` sets `Visible = false` for the category) and are
  toggled by `ModifyUndies`.
- Prefer `layering` (`Genital` front, `TailBehind` behind) and `colorLinks` as the ported libraries do.
- The `Genital` layer must exist on the species (see `.ai/guides/adding-species.md`).

## 7. Sprites and DB format

- Runtime instance fields persist as
  `markingId@#rrggbb,...[@scale,offsetX,offsetY][@g0.5,...][@m3][@cCustom Name]`; only change
  `Marking.ToString` and `ParseFromDbString` together, keeping the legacy format parseable.
- Scale is clamped to 0.1–4.0 (editor 0.25–3.0); offsets to ±2 (editor ±1).
- Glow is per-instance, not a prototype field: each color gets a 0–100% glow slider in the editor,
  persisted as the `g` segment. Glowing markings render an `unshaded` companion layer.

## 8. Verify

```
dotnet build SpaceStation14.sln
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
dotnet run --project Content.YAMLLinter -c Debug --no-build
```

Common errors and causes:

| Error | Cause |
|---|---|
| `Requested value 'X' was not found` | enum value missing, or stale YAMLLinter assembly |
| `No MarkingPrototype found with id ...` | `altSprites` target missing |
| `already exist entry of type: Message` | duplicate locale key |
| marking loads but does not render | layer missing from species base sprites/sprite maps, or `BaseLayers[layer].AllowsMarkings` false |
| marking disappears on load | species filter stripped it (kind-aware check missing somewhere) |
