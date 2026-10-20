# Body-shape displacement: breasts

**Status:** idea / not implemented
**Scope:** optional body-shape clothing warping for characters with breast genital organs

## Goal

When a character has a breast organ, clothing on `jumpsuit` / `outerClothing` should be warped
outward around the chest so it follows the character's configured breast size instead of clipping
through the organ marking.

## Why it is feasible

The repository already has:

- `DisplacementData` and `DisplacementMapSystem` for applying a displacement texture to a clothing
  sprite layer with the `DisplacedDraw` shader.
- `GenitalOrganComponent` with networked `Size` and `OrganPrototype` fields.
- `GenitalOrganSystem.TryGetOrgan(mob, GenitalType.Breasts, ...)` and `GetOrgans`.
- `ClientClothingSystem` already resolves a displacement map for each clothing slot.

## Proposed approach

1. Add a prototype (working name `bodyDisplacement`) keyed by body-shape type, organ prototype,
   size bucket and clothing slot, or a small dedicated breast-displacement prototype.
2. On the client, after resolving the normal leg displacement, query the wearer's breast organ.
3. If a matching displacement map exists, use it for `jumpsuit` and `outerClothing`.
4. Refresh clothing visuals when the breast organ is added/removed/resized, or when clothing is
   equipped.
5. Keep the feature data-driven so other body shapes (belly, butt, etc.) could reuse it later.

## Limitations

- Displacement only warps pixels; it does not create cloth folds, shadows or physically accurate
  stretching. Tight jumpsuits/MODsuits will look better than loose coats.
- A generic map is a compromise across garments and directions. Per-garment maps or size buckets
  give better results.
- SPLURT/Coyote has many breast sizes (up to 18 entries). Use a small number of curated buckets
  rather than one map per size unless the art can be generated cheaply.
- Automatic generation from breast marking alpha masks is possible but quality and consistency are
  uncertain; hand-adjusted masks for 4-6 buckets are safer.
- Need to handle underwear/clothing occlusion, organ removal, species sprites and network updates.

## Recommendation

Prototype 3 size buckets (small/medium/large) on `jumpsuit` and `outerClothing` first. If that
looks acceptable, expand to more buckets and other body-shape organs.
