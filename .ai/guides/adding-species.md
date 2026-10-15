# Guide: Adding / Porting a Species

This recipe is based on the Rodentia, Anthromorph and Tajaran ports. A playable species needs a
species prototype, a mob entity (and a dummy), a body prototype + parts, base sprites, marking
support, and optionally speech/names/damage/locale.

## 1. Files to create

| Piece | Path pattern |
|---|---|
| Species + base sprites + marking points | `Resources/Prototypes/<prefix>/Species/<name>.yml` |
| Mob entity + dummy | `Resources/Prototypes/<prefix>/Entities/Mobs/Species/<name>.yml` |
| Player mob | `Resources/Prototypes/<prefix>/Entities/Mobs/Player/<name>.yml` |
| Body parts | `Resources/Prototypes/<prefix>/Body/Parts/<name>.yml` |
| Body prototype | `Resources/Prototypes/<prefix>/Body/Prototypes/<name>.yml` |
| Damage modifier set | append to an existing `Damage/modifier_sets.yml` |
| Markings | `Resources/Prototypes/<prefix>/Entities/Mobs/Customization/Markings/<name>.yml` |
| Sprites | `Resources/Textures/<prefix>/Mobs/Species/<Name>/parts.rsi`, `.../Customization/<Name>/*.rsi` |
| Locale | species name, markings, speech verbs, dataset names under `Resources/Locale/en-US/<prefix>/` |

## 2. Species prototype

Required/important fields: `id`, `name`, `roundStart`, `prototype` (mob), `sprites` (base sprite
set), `markingLimits` (marking points prototype), `dollPrototype`, `skinColoration`.

Palmtree/Floof additions:

```yaml
  kind:                     # enables kindAllowance marking sharing
  - BasicHumanlike
  - BasicFurry
  # optional:
  defaultLegStyle: Digitigrade   # default is Plantigrade
  allowDigilegDisplacement: false # default true
```

**Remove** fields this repo does not support (the linter errors on them):
`minHeight`, `defaultHeight`, `maxHeight`, `minWidth`, `defaultWidth`, `maxWidth`,
`averageHeight`, `averageWidth`, `guideBookIcon`. (`customName` **is** supported - it powers the
editor's custom species-name field - so keep it when the source uses it.)

### Base sprites (`speciesBaseSprites`)

Every layer a marking may use needs an entry. The Floof set used by the ported content:

```yaml
- type: speciesBaseSprites
  id: MobXSprites
  sprites:
    Head: MobXHead
    Chest: MobXTorso
    # ... body parts ...
    Hair: MobHumanoidAnyMarking
    FacialHair: MobHumanoidAnyMarking
    Snout: MobHumanoidAnyMarking
    HeadTop: MobHumanoidAnyMarking
    HeadSide: MobHumanoidAnyMarking
    Tail: MobHumanoidAnyMarking
    Genital: MobHumanoidAnyMarking
    TailBehind: MobHumanoidAnyMarking
    TailOversuit: MobHumanoidAnyMarking
    NeckFluff: MobHumanoidAnyMarking
    UndergarmentTop: MobHumanoidAnyMarking
    UndergarmentBottom: MobHumanoidAnyMarking
    UndershirtUnderclothes: MobHumanoidAnyMarking
    UndershirtOverclothes: MobHumanoidAnyMarking
    LLegBehind: MobHumanoidAnyMarking
    RLegBehind: MobHumanoidAnyMarking
    LFootBehind: MobHumanoidAnyMarking
    RFootBehind: MobHumanoidAnyMarking
```

- Head/Chest base sprites need sex-morph variants (`MobXHeadMale`/`Female`, `MobXTorsoMale`/`Female`)
  or they render wrong/invisible.
- Digitigrade variants go on `humanoidBaseSprite` entries as
  `altSprites: { Digitigrade: DigilegFurryTorsoMale }` (the marking must exist).
- Marking points: add at least `Genital` (and any category with required defaults). Missing
  categories behave as unlimited (`PointsLeft` returns −1).

## 3. Mob entity

- Parent `BaseMobSpeciesOrganic` (inherits the shared sprite layer maps and `ModifyUndies`).
- Add `- type: HumanoidAppearance` with `species: X`.
- Add `- type: Body` with the body prototype.
- Remove any `Needs` component (Coyote-only) and other un-ported systems.
- If the species defines its own `Sprite` (different layer order, extra parts), the full layer list
  must include all Floof layers you want to render (`TailBehind`, `Genital`, behind legs,
  `Undershirt*`, `TailOversuit`, `NeckFluff`). Copy the order from `base.yml` or an existing custom
  species (vox/harpy).
- Dummy: parent `MobHumanDummy`, `categories: [ HideSpawnMenu ]`, `HumanoidAppearance` species X.

## 4. Body and damage

- Body prototype: root torso + organ slots; reuse upstream organs (`OrganHumanBrain`, etc.) when
  possible.
- Damage set: `- type: damageModifierSet  id: X  coefficients: {...}` appended to a modifier file.
- Butchering/melee/speech/temperature components: copy from the closest existing species.

## 5. Speech / names / typing

- `Speech` + `Vocal` reference sound collections; add missing collections as separate YAML files
  (e.g. `_DV/Voice/rodentia_emote_sounds.yml`) to avoid editing shared files.
- Names: `localizedDataset` prototypes + locale entries.
- Typing indicator: add a `typingIndicator` prototype and sprite states (merge into the existing
  `speech.rsi/meta.json` rather than overwriting the directory).
- `speechVerb` prototype + locale strings.

## 6. Marking support for the species

- Set `kind` so ported Floof markings apply.
- Add the species to any marking `speciesRestriction` only when necessary — prefer `kindAllowance`.
- If porting an existing species' markings, copy the whole marking file and its RSI directory.

## 7. Verify

```
dotnet build SpaceStation14.sln
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
dotnet run --project Content.YAMLLinter -c Debug --no-build
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter "FullyQualifiedName~EntityTest"
```

`EntityTest` spawns every entity; it catches body part/prototype mistakes and the
`EnsureDefault`/default-marking loop overrun. Also check the species appears in the character editor
and that `legStyle: Digitigrade` renders correctly.

## 8. IPC / Synth (special case)

Do **not** port IPC via the Einstein Engines silicon stack. Palmtree implemented a replacement
species, **Synth** (`_PS`), which uses the marking system to look like anything and approximates IPC
stats; see `.ai/systems/ps-systems.md` and `.ai/PORTING.md` §6.
