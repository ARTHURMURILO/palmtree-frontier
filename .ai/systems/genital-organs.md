# System: Genital Organs

> Palmtree's organ-based replacement for genital markings (2026-10). Players pick type + size per
> organ in character creation; interactions and cum/drip mechanics read organs, and the marking
> system is reused purely to draw them.

## Purpose

- Persisted, player-configured genital organs: penis, vagina, balls, breasts, butt, belly.
- Drives the interaction panel requirements (`GenitalSystem`), organ arousal visuals and semen
  volume.
- Replaces the old genital markings in the character editor; the marking prototypes remain as
  render targets only.

## Locations

| Piece | Path |
|---|---|
| Catalog prototype (`type: genitalOrgan`) | `Content.Shared/_PS/Organs/GenitalOrganPrototype.cs` |
| Catalog YAML (generated) | `Resources/Prototypes/_PS/Organs/genital_organs.yml` |
| Runtime component | `Content.Shared/_PS/Organs/GenitalOrganComponent.cs` |
| Spawn/render/sync system | `Content.Shared/_PS/Organs/GenitalOrganSystem.cs` |
| Profile data | `Content.Shared/_PS/Organs/GenitalOrganSettings.cs` |
| Profile field | `Content.Shared/Preferences/HumanoidCharacterProfile.cs` (`Genitals`) |
| Organ entities | `Resources/Prototypes/_PS/Entities/Organs/genital_organs.yml` |
| Editor tab | `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs` (`RefreshGenitals`, `BuildGenitalControls`, `OnGenitalControlsChanged`) |
| Catalog generator | `Tools/gen_genital_organ_catalog.py` |
| DB | `Profile.Genitals` string column (`Content.Server.Database/Model.cs`), migrations `*_GenitalOrgans`, `ServerDbBase.ConvertProfiles` |
| Tests | `Content.IntegrationTests/Tests/_PS/InteractionPanelTest.cs`, `Content.IntegrationTests/Tests/_PS/GenitalRenderTest.cs`, `ServerDbSqliteTests` round-trip |

## Data model

`GenitalOrganSettings` (profile + network): one `GenitalOrganData { GenitalType Type; string
Prototype; int Size; GenitalVisibility Visibility; Color? Color; Color? DetailColor; Vector2 Offset;
float Scale; float Glow; float DetailGlow }` per organ (at most one per type) plus `SemenVolume`
(1-300, default 30; the editor labels it "Fluid per climax" and it applies to female climaxes too).
Persisted as a compact string via `ToDbString`/`FromDbString` in a plain text column, e.g.
`Penis:PenisHuman:3:NeverHidden:#f2c9a0:-:0.25,-0.5:1.5:0.5,0;Vagina:VaginaHuman:1:HiddenByUnderwear;semen=42`
(older 3/4-part entries parse as `HiddenByUnderwear`, the current default, with skin colors; `-`
means "follow skin"/default; the
7th/8th/9th segments are the optional `offsetX,offsetY`, `scale` and `glow,detailGlow`). Offset,
scale and glow are applied to the organ's render marking via
`Marking.SetOffset`/`SetScale`/`SetGlow`, so organs can be transformed like markings. Organ color
pickers expose alpha (`IsAlphaVisible`).

`GenitalOrganPrototype` (catalog): `genitalType`, localized `name`, and an ordered `sizes` list.
Each size maps to a `flaccid` render marking and an optional `aroused` one. `CanArouse` is false
when no size defines an aroused marking; such organs (balls, breasts) are flaccid-only —
`SetAroused` rejects/clears the state, the in-game arousal toggle is disabled and the editor's
aroused preview skips them. The generator deliberately does not emit `aroused` for balls;
instead, where a size has an aroused (`*Alt`) sprite, that marking becomes the `flaccid`
(standard) one, so balls always show the aroused art with no state change. Legacy conversion
(`TryConvertMarking`) accepts both a ball's base marking and its `*Alt` variant. The catalog is
generated from existing marking ids by `Tools/gen_genital_organ_catalog.py`, e.g. `PenisHuman`
sizes 1-5 map to `Genital-Penis-Human-N-0` / `-N-1`, `BreastsCoyote` maps to
`GenitalBreastsRoundA..Impossible` (with `GenitalBreastsRoundSkintoned*` variants for the skin
tone toggle), `BreastsSplurt` to `PSGenitalBreasts0..17`.

The organ entity (`OrganGenital*`) carries `Organ` + `GenitalOrganComponent` and is inserted into a
custom slot on the torso (`genital_penis`, `genital_vagina`, ...). `GenitalOrganSystem` creates the
slot at runtime with `TryCreateOrganSlot` (organs in unregistered slots are invisible to body
queries).

## Flow

1. The editor's **Genitals** tab builds dropdowns from the catalog and stores choices in
   `HumanoidCharacterProfile.Genitals` via `WithGenitals`. `EnsureValid` validates prototypes and
   sizes and clamps semen volume.
2. `SharedHumanoidAppearanceSystem.LoadProfile` raises `HumanoidProfileAppliedEvent` after applying
   the profile; `GenitalOrganSystem` reacts and syncs organs. The client lobby preview override does
   not call the shared implementation, so it calls `GenitalOrganSystem.SyncFromProfile` directly and
   refreshes the sprite.
3. `SyncFromProfile` adds/updates/removes organ entities to match the profile, copies `SemenVolume`
   onto the penis organ, then `SyncRender`.
4. `SyncRender` clears the `Genital` marking category and re-adds one render marking per organ
   **that is currently exposed** (the visibility rule gates rendering directly; covered organs are
   not drawn at all, so oversized sprites cannot poke out of clothing). Markings use
   `Forced = true`, `Visible = true`, `CanToggleVisible/OtherCanToggleVisible = false` (so
   ModifyUndies adds no verbs) and colors built from the organ's primary/detail colors (skin color
   when null), following the marking's `colorLinks` so linked states share a color group.
   Organs are added in a fixed render order — butt, vagina, balls, penis, belly, breasts — so the
   penis draws over the balls, the belly draws over the groin, and the breasts are the only organ
   drawn over the belly, regardless of the order the player enabled them in the editor.
   `NeverHidden` additionally sets `Marking.RenderOverClothing` to draw above outer clothing.
   If the organ's `SkinTone` toggle is on and the selected size has a `skintoned` render marking,
   that skin-shaded variant is used instead of the tinted one (breasts).
5. The interaction panel's genital tab lists organs, toggles `Aroused` per organ
   (`InteractionGenitalArouseMessage`) and changes the visibility rule
   (`InteractionGenitalSetVisibilityMessage`); arousal swaps the render marking and visibility
   controls both exposure and whether/how the sprite draws.
6. `GenitalSystem` (interactions) answers `HasGenital`/`GetExposure` with the per-organ
   **visibility rule** (SPLURT/Sandstorm port), and `GenitalOrganSystem` re-renders on
   `DidEquipEvent`/`DidUnequipEvent` (jumpsuit) and `MarkingVisibilityChangedEvent` (undies
   toggled), so the rule stays in sync with clothing:

   | Rule | Exposed when | Sprite |
   |---|---|---|
   | Always hidden | never | not drawn |
   | Hidden by underwear (default) | no visible relevant undergarment marking and no jumpsuit | under clothing/undergarments |
   | Hidden by jumpsuit | no jumpsuit | under clothing |
   | Never hidden | always | drawn above outer clothing (`Marking.RenderOverClothing`) |

   Undergarments are the fork's undergarment markings (`UndergarmentTop` for breasts,
   `UndergarmentBottom` for the rest). The anus is still simulated (no organ): humanoids have one,
   exposed when bottomless. The `Genital` sprite layer sits **after `Tail`** in every species map
   (just before `mask`), so tails, hands and clothing no longer draw over penises/bellies; clothing
   occlusion is handled by the visibility rule above, not by draw order.

## Cum, drip and washing

| Piece | Path | Notes |
|---|---|---|
| Reagent | `Resources/Prototypes/_PS/Reagents/semen.yml` | `Semen`, white, viscous; no longer used for puddles/overlays (kept for future fluid work) |
| Drip | `Content.Shared/_PS/Organs/DrippingCumComponent.cs`, `Content.Server/_PS/Organs/DrippingCumSystem.cs` | stores units; 1 u/s into **droplet** decals (`SemenDrip1..5` only), pausing while a jumpsuit is worn |
| Ejaculation decals | `DrippingCumSystem.SpawnCumDecals` | drops `SemenPuddle1..4` scaled by `SemenVolume`; each climax adds a new decal (never replaces) |
| Decal sprites/prototypes | `Resources/Textures/_PS/Effects/decals/semen.rsi` (ported from SPLURT `effects.dmi`), `Resources/Prototypes/_PS/Decals/semen.yml` | cleanable with space cleaner; no reagent puddles |
| Washing | — | decals are removed by space cleaner (`CleanDecalsReaction`) or by the Palmtree roomba's decal-scrubbing HTN operators (`Content.Server/_PS/Roomba`); the character overlay feature was removed |

- Internal climaxes (`cumTarget: vagina`/`anus`) store the penis organ's `SemenVolume` in the
  partner's drip reservoir; `vagina` and `anus` both drip (receiver with no jumpsuit). Drips only
  ever place the small droplet decals (`SemenDrip1..5`); the full cum decals are reserved for actual
  ejaculation. The receiver gets a private popup ("trickle" or "pool, held back by your clothes"
  while a jumpsuit is worn).
- **Drip activity gate (Palmtree):** a filled mob does not drip on its own. The drip only runs after
  the mob **moved** (its position changed since the last drip check) or **performed an interaction**
  (`DrippingCumSystem.NotifyActivity`, called for both participants by `TryPerform`). A drip consumes
  the flag, so an idle mob keeps every unit inside and walking keeps the drip going. `AddSemen`
  records the mob's position, so spawning where they were filled does not count as movement.
- **Tile limit (Palmtree):** dripping pauses while the mob's tile already has
  `MaxDripsPerTile = 5` drip decals on it. The pause never consumes stored semen; it resumes when
  the mob moves to a clear tile or the floor is cleaned. Decals are counted by the tile their center
  falls in (the stored coordinate is the decal's bottom-left corner).
- **Receive capacity (Palmtree):** the reservoir holds at most
  `DrippingCumComponent.MaxCapacity = 1000` units; anything past that is lost.
- **Womb overflow (Palmtree):** when a pulse is delivered into a vagina whose reservoir already
  holds more than `DrippingCumSystem.WombOverflowThreshold = 60` units, a full cum decal is also
  dropped on the floor to represent the overflow. The 30u is still added - overflow costs the
  receiver nothing. Only the `vagina` cum target overflows (the anus does not).
- **Multi-pulse climax:** `EmitClimax` splits the stored volume into 30u pulses, capped at 10 per
  climax (`InteractionPanelSystem.ClimaxPulseVolume`/`MaxClimaxPulses`), so 30u = one pulse and the
  300u maximum = ten. The first pulse fires immediately; the rest run from the server `Update` loop
  every 0.6s (1s for female climaxes). Each pulse emits its share of the fluid, its decal/drip, the
  interaction's cum message and a moan. While a sequence is active the actor cannot start another
  climax and climaxing participants gain no lust (fail-safe).
- **Female climax:** vagina-only characters use the `FemPuddle1..4` decals (SPLURT fem sprites,
  random per pulse, no volume scaling). The pulse count comes from the same "Fluid per climax"
  setting (`GenitalOrganSystem.GetFluidVolume` falls back to the vagina organ), and the pulses are
  1 second apart.
- Receiver-side acts (`Ride`, `TakeAnal`) route the **target's** climax through the same cum target,
  so the penis owner finishing fills the actor.
- Exterior climaxes (handjob, breastfuck, frotting, **Cum on them**, manual Climax with no recent
  interaction) drop the full cum decals at the recipient. **Cum on them** is a `forceClimax`
  exterior action with no orgasm gate; the **Climax** action triggers immediately with no range or
  orgasm requirement, on yourself or a partner. Both use the same `TriggerClimax`/`EmitClimax` path.
- Each interaction can define `cumMessages` for a context-sensitive finish line (e.g. breastfuck
  "cums all over {target}'s breasts"); the generic per-target text is the fallback. The generic
  Climax reuses the last interaction's proto/target within `continueTimeout`, so the text and the
  destination both follow what you were doing.
- Decals are placed centered on the mob (the stored coordinate is the sprite's bottom-left corner,
  so placement subtracts half a tile) and every drop is a new decal, so the floor builds up a mess.
  Drips barely scatter (`DripScatter = 0.05`) while ejaculation decals scatter widely
  (`CumScatter = 0.35`). If the scattered position lands on a space tile (standing on an
  edge/corner) placement falls back to the mob's exact position. Note `GetDecalsInRange` compares
  against `coordinate + (0.5, 0.5)`, so tests query at the mob's position.
- The shower structure does not yet wash mobs (no SS14 shower system); space cleaner removes decals.

## Editor tab

Dynamic `Genitals` tab (like Flavor Text, so upstream tab indices do not change). Per organ type:
enable checkbox, Type dropdown (catalog prototypes), Size dropdown (catalog sizes), Visibility
dropdown (Always hidden / Hidden by underwear / Hidden by jumpsuit / Never hidden), a **Skin tone**
checkbox for organs whose catalog has skin-shaded variants (breasts), a collapsible **Colors**
section with primary + detail `ColorSelectorSliders` (alpha enabled) and a "Use skin
color" reset (null colors follow the skin), plus Glow/Detail glow sliders (0-100%) in the same
section; a collapsible **Transform** section with Scale and Offset X/Y sliders + spin boxes (scale
0.25-3, offset ±1, mirroring the marking picker's transform controls) and a reset button; plus a
`Fluid per climax` SpinBox. Changing anything calls `WithGenitals`, marks the profile dirty and
reloads the preview.

The MarkingPicker ignores the `Genital` category; profiles with old genital markings are converted
to organs (`GenitalOrganSettings.TryConvertMarking`) and stripped in
`HumanoidCharacterProfile.EnsureValid`.

- Colors: each organ stores `Color`/`DetailColor` (null = skin). The detail color maps to the second
  independent color group, which is the nipple layer for the Splurt breast set; the editor only
  shows the detail picker when the selected marking actually has a second group (Coyote breasts
  don't). `GenitalOrganSystem.CountColorGroups` resolves `colorLinks`.
- Transform: each organ stores a `Vector2 Offset` (applied via `Marking.SetOffset`, clamped ±2) and
  a `float Scale` (applied via `Marking.SetScale`, 0.25-3 in the editor), so organs can be nudged
  and scaled exactly like markings. The DB string keeps them in optional 7th/8th segments
  (`type:proto:size:visibility:color:detail:ox,oy:scale:glow,detailGlow:skintone`, `-` for null
  colors/zero offset/zero glow); old 4/5/6/9-segment strings still parse.
- Legacy conversion: `TryConvertMarking` walks the organ catalog and matches the marking id against
  each size's flaccid/aroused/skintoned render marking (e.g. `PSGenitalBreasts7` -> `BreastsSplurt`
  size 8, `GenitalVaginaHuman` -> `VaginaHuman`, `GenitalButt1` -> `ButtStandard` size 1; matching a
  skintoned marking also sets `SkinTone`). An organ type that is already configured is never
  overwritten.
- Preview: the Genitals tab has a **Show undergarments in preview** checkbox that toggles
  undergarment marking visibility on the preview dummy only (not saved), so organs can be inspected.
  A **Preview aroused organs** checkbox switches every configured organ on the dummy to its aroused
  render marking (also preview-only, re-applied after profile reloads). Both preview toggles must
  call `HumanoidAppearanceSystem.RefreshSprite` afterwards — `SyncRender` only updates the local
  marking set, which does not rebuild sprite layers by itself. Organ sections are split by
  `HSeparator`s. The tab is wrapped in a `ScrollContainer` (the six organ sections overflow the
  window). Local visibility toggles call `HumanoidAppearanceSystem.RefreshSprite` because the event
  bus only allows one `MarkingVisibilityChangedEvent` subscriber (owned by `GenitalOrganSystem`).

## Hazards

- The profile event is raised during `ComponentInit`; `SyncFromProfile` must check
  `BodyComponent.RootContainer` is non-null or it NREs before MapInit.
- Profiles applied before the body exists (admin respawns, deferred MapInit) are stored in
  `GenitalOrganSystem._pendingProfiles` and retried each tick; without this the character spawns
  without genitals and never recovers. A successful sync must remove the pending entry — the mob
  receives a default genital-less profile during `ComponentInit`, and a stale pending entry would
  otherwise re-sync over the real profile on the next tick and erase the organs.
- Legacy genital markings are converted to organs in `HumanoidCharacterProfile.EnsureValid` **before**
  `Genitals.Validate`; conversion never overwrites an organ type that is already configured. The
  conversion is what makes old saves/Coyote exports keep their genitals.
- Removing an organ leaves its slot entry in `BodyPartComponent.Organs`, so `TryCreateOrganSlot`
  (which uses `TryAdd`) returns false when an organ is re-enabled. `SyncFromProfile` must fall back
  to `CanInsertOrgan` and reuse the existing slot, or toggling an organ off and on in the editor
  silently stops it from ever rendering again (regression test:
  `TogglingOrgansOffAndOnRecreatesThem`).
- Removed organs must be detached with `Containers.Remove(..., reparent: false)` (the container
  event still drives `SharedBodySystem` bookkeeping) before `QueueDel`. The default
  `SharedBodySystem.RemoveOrgan` reparents to the grid/map, which spams "Failed to attach entity to
  map or grid" warnings for the lobby preview dummy (it isn't on a grid).
- `GenitalOrganSystem.SyncRender` must set `CanToggleVisible = false` on render markings or
  ModifyUndies will expose them as toggle verbs.
- **Network clients must not rebuild the organ render markings.** `SyncRender`/`SyncFromProfile`
  early-out through `CanModifyOrgans` unless the system is server-side or the mob is client-side
  (lobby preview). Robust ejects contained entities when a client detaches an entity for PVS
  (`ClientGameStateManager.Detach`), which fires `EntRemovedFromContainerMessage` →
  `SharedBodySystem.OnBodyPartRemoved` → `OrganRemovedFromBodyEvent` on the client. Rebuilding there
  found no organs and stripped the `Genital` category; because the client's `MarkingSet` **is** its
  cached component state object (generated `HandleState` assigns it by reference), the wipe stuck
  through PVS re-entry and hid genitals for observers who walked away and returned, or joined as
  ghosts. Regression test: `Content.IntegrationTests/Tests/_PS/GenitalRenderTest.cs` (fails if the
  guard is removed).
- The client `HumanoidAppearanceSystem.LoadProfile` override does not call the shared method; any
  new profile-driven visual must be applied there too (see the preview call).
- Editing the catalog by hand will be overwritten; run the generator instead.
- Migration drift: `ServerDbSqliteTests.TestNoPendingDatabaseChanges` must stay green for both
  providers after model changes.
- The DB string tail (colors + offset + scale) must stay backward compatible: old 4/5/6-segment
  entries keep parsing, null colors and zero offsets write `-` placeholders, and scale is only
  written when it differs from 1.

## Not ported / future

- Pregnancy, womb, fluids other than semen, cumflation, refractory period.
- Character cum overlays (tried and removed; the sprite layer never showed for the reporter).
  Objects/furniture overlays are not planned either.
- Per-organ fluid type selection (SPLURT has `custom_genital_fluids`); currently one `Semen` reagent.
- Real organ surgery/removal wiring; organs can be removed with body API calls but have no
  interactions with surgery steps.
