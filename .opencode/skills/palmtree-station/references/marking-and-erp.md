# Marking & ERP system reference

Deep dive: `.ai/systems/marking-and-appearance.md`; ERP status: `.ai/systems/consent-and-erp.md`.

## Files

| Piece | Path |
|---|---|
| Data model | `Content.Shared/Humanoid/Markings/Marking.cs` |
| Prototype | `Content.Shared/Humanoid/Markings/MarkingPrototype.cs` |
| Categories/conversions | `Content.Shared/Humanoid/Markings/MarkingCategories.cs` |
| Set/filters | `Content.Shared/Humanoid/Markings/MarkingsSet.cs`, `MarkingManager.cs` |
| Layer enums | `Content.Shared/Humanoid/HumanoidVisualLayers.cs`, `HumanoidLegStyle.cs` |
| Profile/appearance | `Content.Shared/Humanoid/HumanoidCharacterAppearance.cs`, `HumanoidAppearanceComponent.cs` |
| Shared logic | `Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs` |
| Client renderer | `Content.Client/Humanoid/HumanoidAppearanceSystem.cs` |
| Editor UI | `Content.Client/Humanoid/MarkingPicker.xaml(.cs)`, `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs` |
| Undies verbs | `Content.Server/_Floof/ModifyUndies/*`, `Content.Shared/_Floof/ModifyUndiesDoAfterEvent.cs` |
| Leg displacement | `Content.Shared/_CS/Humanoid/LegDisplacementPrototype.cs`, `Resources/Prototypes/_CS/LegDisplacement.yml` |
| Content | `Resources/Prototypes/Entities/Mobs/Customization/Markings/*`, `_Floof/.../markings/*`, `_PS/.../genitals.yml`, `_DV`, `_DEN`, `_EE`, `_Starlight`, `_White` |

## Prototype fields

`bodyPart` (layer), `markingCategory`, `speciesRestriction`, **`kindAllowance`**,
`sexRestriction`, `sprites`, `layering` (state → layer), `colorLinks` (child → parent),
`altSprites` (leg style → marking), `hidden`, `baseLayerSprite`, `shader`, coloring fields.

### kindAllowance (critical)

- Allowed if `speciesRestriction` is null/contains species, **or** `kindAllowance` intersects
  `SpeciesPrototype.Kind`.
- This repo adds `kindAllowance: [BasicHumanlike, BasicFurry, BasicRobot, VoxLike]` to all
  species-restricted markings (Coyote behavior). Always include it on new restricted markings.
- Every filter must call `MarkingManager.IsAllowedBySpeciesOrKindAllowance`:
  `MarkingManager.MarkingsByCategoryAndSpecies(AndSex)`, `IsValidMarking`, both `CanBeApplied`,
  and `MarkingsSet.EnsureSpecies`. Direct `SpeciesRestrictions.Contains` silently strips
  kind-shared markings (this bug shipped once).

## Runtime instance data & persistence

`Marking` carries colors, `visible`, `forced`, `scale`, `offsetX/offsetY`, `glowLevels` (+ legacy
`glow`) and the toggle settings `customName`, `canToggleVisible` (default false; undergarments
default true), `otherCanToggleVisible` (default false). DB string format
(`Marking.ToString`/`ParseFromDbString`):

```
markingId@#rrggbb,...[@scale,offsetX,offsetY][@g0.5,1,...][@m3][@cCustom Name]
```

Transform segment omitted when default; glow (`g`) omitted when all zero; visibility flags (`m`,
bit 1 = self, bit 2 = others) omitted at defaults; custom name (`c`) omitted when empty (`@` is
sanitized to `_`). Old strings remain valid and default to self-toggleable. Change `ToString` and
`ParseFromDbString` together.

**Profile load**: `SharedHumanoidAppearanceSystem.LoadProfile` must use
`AddMarking(uid, Marking marking, colors, ...)` so scale/offset/glow survive. The
`(string, colors)` overload creates a fresh marking and drops them.

## Rendering (client)

- `UpdateLayers` rebuilds `BaseLayers` from species `speciesBaseSprites`, applying base-sprite
  `altSprites` for the current `LegStyle`.
- `ApplyMarkingSet` swaps markings via `GetMarkingForLegStyle` (marking `altSprites`), then
  `ApplyMarking`:
  - `layering` routes sprites to arbitrary layers; `colorLinks` copies colors (and glow).
  - scale/offset applied per layer; glow creates `<id>-<state>-glow` with `unshaded` shader and
    alpha split; hidden/removed markings clean up glow layers.
  - genital layers clamp below `jumpsuit`/`outerClothing`.
  - `Base*` categories add to `HiddenBaseLayers`; `UpdateLayersAgain` hides them.
- `RemoveMarking` removes both base and `-glow` layers.

## Genital markings & ModifyUndies

- Genital markings: `bodyPart: Genital`, `markingCategory: Genital`, start hidden
  (`AddMarking` sets `Visible = false` for the category), toggled by `ModifyUndies`.
- `ModifyUndiesSystem` adds verbs for **any** marking whose `CanToggleVisible` (owner) or
  `OtherCanToggleVisible` (others) is set. Defaults: owner off except undergarments (on), others off.
  The per-marking opt-in is the consent gate for others (the account NSFW age gate gates rendering,
  not these verbs). It starts a 1s do-after,
  then `SharedHumanoidAppearanceSystem.SetMarkingVisibility` flips `Marking.Visible` and dirties.
  Verb and popup text uses `CustomName` when set.
- Visibility is session state, not persisted; toggle settings are persisted in the DB string.

## Interaction panel (Sandstorm port)

- `Content.{Shared,Server,Client}/_PS/Interactions/*`; prototypes `type: interaction` in
  `Resources/Prototypes/_PS/Interactions/interactions.yml`; sounds in `Resources/Audio/_PS/Interactions/`.
- The **NSFW age gate** (account flag, lobby prompt, `nsfwtoggle`) gates the whole panel: no panel
  until the player answers, no sexual interactions on non-consenting targets, and ERP emotes/sounds
  skip non-consenting listeners. `InteractionPrototype.NonSexual` (only `Kiss`) is exempt from the
  account gate; the session toggle still applies. Full details: `.ai/systems/consent-and-erp.md`.
- Genital requirements read **organs** (`Content.Shared/_PS/Organs/*`); exposure is the jumpsuit rule.
  See `.ai/systems/genital-organs.md`.
- Lewd interactions need both parties' session `InteractionStateComponent.Consent` (default on); the
  actor opens the panel with the right-click verb or Ctrl+Shift-click. Ctrl+Shift-clicking **empty
  space** opens the self panel for masturbation actions; the manual **Climax** works on self or
  others at any time.
- Per-organ visibility rules (Always hidden / Hidden by underwear / Hidden by jumpsuit / Never
  hidden) are changeable in the panel and editor and persist in the profile; exposure and rendering
  both follow them (`Never hidden` draws over clothing, covered organs are not drawn at all).
- Organ colors (primary + detail, alpha enabled) and transforms (offset + scale + glow) are picked
  per organ in the editor (sliders mirror the marking picker); they persist in the DB string and
  apply via `Marking.SetOffset`/`SetScale`/`SetGlow`. The editor also has a **Preview aroused
  organs** toggle and per-organ `HSeparator`s. Balls and breasts are **flaccid-only** (`CanArouse`
  false) — their arousal toggle is disabled and arousal is rejected. Ball sizes that have an
  aroused (`*Alt`) sprite render that sprite as their standard one, so they show the aroused art
  with no state change. A manual
  **Climax** action exists alongside **Cum on them**. Old genital
  markings are converted to organs on load/import (`GenitalOrganSettings.TryConvertMarking`), so
  Coyote exports keep their genitals. Human/Vulpkanin/Reptilian marking-point limits match Coyote
  (35 per category, 999 tails/head-top on Vulpkanin) so cross-species exports keep every marking.
- Character **Height/Width** sliders (Appearance tab) scale the whole sprite; range 0.5-2 (max
  double the standard), persisted in the `height`/`width` DB columns.
- Climaxes are multi-pulse: the "Fluid per climax" setting is split into 30u pulses (max 10, so
  300u = 10). Each pulse places a decal/drip, sends the cum text and moans; while pulsing the actor
  cannot start another climax and gains no lust from interactions. Females use the SPLURT
  `FemPuddle*` decals (random per pulse) with 1-second pulses; males use `SemenPuddle*` at 0.6s.
- Female ejaculate decals come from SPLURT (`fem1..4` in `_PS/Effects/decals/semen.rsi`), and are
  chosen at random per pulse. Decals are centered on the character (placement subtracts the decal's
  half-tile bottom-left offset), so cum/drip/ejaculate no longer appear up by the head.
- SlimePerson markings can all be recolored (no `markingsMatchSkin`/`forcedColoring` lock), and
  hair/facial hair alpha is preserved (`ClampColor`).
- Receiver-side acts (`Ride`, `TakeAnal`) let a vagina/anus owner take the target's penis and route
  the target's climax into themselves.
- Lust decays 1/s, random tolerance/potency per mob, moans use a bezier chance and climax plays
  `final_*`; lust gain is doubled from Sandstorm (default 20/action). `cumTarget` on the prototype
  drives where the fluid goes: drips only place small droplet decals, actual ejaculation places the
  full `SemenPuddle*` decals (each drop a new decal, scattered where the mob stands). Interactions
  can define `cumMessages` for context-sensitive finish lines. **Cum on them** and **Climax** have
  no orgasm/range gate and share the same climax code. Open panels refresh once per second. Purple
  ERP emotes go through `SendErpEmote`. Full doc: `.ai/systems/interaction-panel.md`; hazards:
  `.ai/HAZARDS.md` §13.

## Digitigrade legs & clothing

- Leg style is chosen in the marking picker; persisted in `HumanoidCharacterAppearance.LegStyle`.
- Base sprites and markings swap via `altSprites` (e.g. Synth → `DigilegSynthliz*`, adaptors →
  `DigilegPaw*`/`DigilegFurry*`).
- Clothing displacement: `LegDisplacementPrototype` (`_CS/LegDisplacement.yml`) +
  `HumanoidAppearanceComponent.LegDisplacements` (default `LegDisplacementDigitigrade`) +
  `ClientClothingSystem.RenderEquipment` → `HumanoidAppearanceSystem.GetDisplacementForLegStyle`,
  gated by `SpeciesPrototype.AllowDigilegDisplacement`. Coverage is partial (same as Coyote).

## Species requirements for markings

1. `kind` list on the species prototype.
2. Layer entries in `speciesBaseSprites` (`Genital`, `TailBehind`, `TailOversuit`, `NeckFluff`,
   behind legs, `Undershirt*`, `TailExtras`, `SnoutCover`, ...).
3. Layer maps in the mob `Sprite` (`Entities/Mobs/Species/base.yml` covers most; custom-sprite
   species need them added).
4. `Genital` marking-points entry.
5. Optional `altSprites` on `humanoidBaseSprite` entries for digitigrade.

## Adding a marking

`.ai/guides/adding-markings.md` + skill `species-and-loadouts.md`. Checklist: RSI states exist,
locale `marking-<id>` / `marking-<id>-<state>`, layer exists in species, kindAllowance present,
lint green.
