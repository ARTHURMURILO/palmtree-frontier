# MODsuits + Proteans port plan

**Status:** Feature checkpoint ready for in-game testing and polish (2026-10-20). MODsuit runtime, crew/special themes and recipes, construction/research, worn art and the Protean core are implemented. Remaining optional work: the wider module/art library and servo engineering mode. Digitigrade suit deployment is covered by the existing displacement maps; unique Protean/anthro sprite variants are not present in the available source checkout. Critical organs (brain/heart/stomach) still require refactory repair instead of field healing. Proteans use the Synth physiology marker (no respiration/metabolism), pressure immunity, environmental temperature/fire handling, Synth reboot repair and source-specific toxin/radiation immunity; Synth damage resistance and forced prying remain intentionally excluded.
**Scope:** Port TG MODsuits and SPLURT-TG Proteans into Palmtree/Frontier, with anthro/digitigrade support.
**Touched areas:** `Content.Shared/_PS/Modsuits`, `Content.Server/_PS/Modsuits`,
`Content.Client/_PS/Modsuits`, `Content.Shared/_PS/Protean`, `Content.Server/_PS/Protean`,
`Content.Client/_PS/Protean`, `Resources/Prototypes/_PS/Modsuits`,
`Resources/Prototypes/_PS/Protean`, `Resources/Textures/_PS/Modsuits`,
`Resources/Textures/_PS/Protean`, locale, research, lathes, tests, `.ai`.

This is the fail-safe plan for returning to the work with no prior conversational memory.
It is intentionally detailed about architecture, batch order, omissions and hazards.

## 1. What changed from the previous plan

The MODsuit plan stays largely the same. The Protean plan changes substantially:

- Proteans use the **SlimeForm-style polymorph model**, not a full player body stored inside a
  clothing item.
- The original body is parked on the polymorph paused map with all gear except the suit.
- The player's mind moves into an invisible `MobProteanSuitController`.
- The suit remains a normal wearable MODsuit item.
- The controller is parented to the suit and follows it, including when another player wears it.
- On unfold, the controller is detached to the grid before `PolymorphSystem.Revert`, then the
  original body is restored and the suit is re-equipped.
- `ProteanSuitComponent` no longer stores the living body as an occupant. It stores the controller,
  original body, assimilated suit, theme, skin and lock state.
- MODsuit runtime must be completed before Protean core.

Everything else remains: TG is canonical for MODsuits, SPLURT-TG is canonical for Protean, ERP
content is excluded, unsupported systems are omitted and documented, and anthro/digitigrade art is
in scope.

## 2. Source of truth and scope

| Area | Source | Decision |
|---|---|---|
| MODsuits | TG master, commit `000a99fb` | Canonical behavior and stats |
| Protean | SPLURT-TG, commit `59f9043a` | Canonical species/abilities |
| MODsuit art | TG, plus SPLURT/Skyrat/Sandstorm variants | Port base art and available anthro/digi states |
| Protean art | Zubbers `protean.dmi` plus existing humanoid bases | Port organs/effects; use humanoid body bases |
| ERP | - | Excluded |

TG has roughly 34 theme datums, 61 pre-equipped control variants and ~88 module families. These are
represented by a small number of C# systems and data-driven prototypes, not 61 suit classes.

All new code and assets go under `_PS`:

```text
Content.Shared/_PS/Modsuits
Content.Server/_PS/Modsuits
Content.Client/_PS/Modsuits
Resources/Prototypes/_PS/Modsuits
Resources/Textures/_PS/Modsuits

Content.Shared/_PS/Protean
Content.Server/_PS/Protean
Content.Client/_PS/Protean
Resources/Prototypes/_PS/Protean
Resources/Textures/_PS/Protean
```

Do not extend `ToggleableClothingComponent`. MODsuits need their own multi-part, module, power and
UI systems.

## 3. MODsuit architecture

### Core components

| Component | Responsibility |
|---|---|
| `ModsuitControlComponent` | Back-slot control: active/sealed/open, wearer, parts, core, modules, theme/skin, actions |
| `ModsuitPartComponent` | Deployed helmet/chestplate/gauntlet/boot; links to control, slot and sealed state |
| `ModsuitCoreComponent` | Power source; standard core uses a battery, infinite/Protean variants override |
| `ModsuitModuleComponent` | Base module data: type, complexity, power cost, cooldown, incompatibilities, active |
| `ModsuitThemePrototype` | Theme stats: armor, complexity, charge drain, slowdown, inbuilt modules, default skin |
| `ModsuitSkin` / `ModsuitPartSkin` | Per-theme visual data, sealed/unsealed states, species variants |

### Systems

- `SharedModsuitSystem`: deployment, retraction, part linking, slot handling, clothing visuals.
- `ModsuitPowerSystem`: charge drain, core checks, low-power shutdown.
- `ModsuitSealSystem`: seal/unseal, pressure/temperature/fire protection, layer hiding.
- `ModsuitModuleSystem`: install/uninstall, complexity, actions, toggle/use/active modules.
- `ModsuitConstructionSystem`: shell/core/part/plating assembly.
- `ModsuitUiSystem` + client XAML BUI: suit panel, modules, charge, complexity, skins.
- Module behavior systems under `Content.Server/_PS/Modsuits/Modules`.

### Deployment model

The control keeps four generic part entities in internal containers:

```text
MODsuit control (back slot)
  |-- core container
  |-- modules container
  |-- parts container
  |     |-- helmet
  |     |-- chestplate
  |     |-- gauntlets
  |     `-- boots
  `-- overslot container
```

On deploy:

1. Check the suit is on the back slot, inactive and has a core.
2. Move conflicting head/outer/gloves/shoes items into the overslot container or drop them.
3. Equip the four parts.
4. Set active state and grant suit actions.
5. Apply theme armor, slowdown and part visuals.

On retract, reverse the process and restore overslot items if possible.

Parts are generic; themes/skins apply visuals and stats. This avoids 61 suit classes.

**Reuse note:** each part also carries the existing `AttachedClothingComponent` (the hardsuit
helmet/suit attachment component). That component provides the normal "cannot be taken off/used as a
loose item" protection; `ModsuitPartComponent` only adds multi-part control wiring. Do not duplicate
the attached-clothing removal logic in MODsuit systems.

### Sealing and protection

- Seal state is per-part, with a suit-wide seal action.
- Sealed helmet/chestplate receive `PressureProtection` and `TemperatureProtection`.
- Theme armor, fire/acid resistance, radiation modifiers and explosion resistance come from theme data.
- Sealed chestplate/helmet hide configured humanoid layers.
- Do **not** hide `TailBehind` or `TailOversuit`; anthro tails must remain visible.
- Sealed suit protects against pressure and temperature but does **not** create oxygen. Wearers still
  use internals/tanks, matching hardsuit behavior. Suit storage can hold tanks.
- Helmet open state forces unsealed helmet; sealing closes it.
- Low charge forces unseal and disables modules.

### Modules

Modules are item entities with `ModsuitModuleComponent` plus behavior components. Generic types:

- passive
- toggle
- usable
- active/selected

The generic system handles complexity, charge, cooldown, incompatibility, activation, action
granting and UI state. Behavior systems handle effects:

- storage -> adds storage to the control/suit
- flashlight -> toggles light
- magboots -> adds/toggles `MagbootsComponent` on boots
- health analyzer -> adds analyzer behavior
- injector/defibrillator -> adapt target medical components
- visor -> adds HUD overlay behavior
- welding -> welder behavior
- tether/rad protection/constructor/t-ray/etc. -> target systems
- jetpack -> custom ion-jetpack behavior using suit charge; stock gas jetpack only if gasless
  variant exists
- security/service/supply modules -> adapt only where the target system exists

Module actions are granted by the control and removed on uninstall, retraction or suit deletion.

### UI

- Use a XAML `ModsuitWindow`, not TGUI/React.
- Sections: suit status, charge, complexity, parts, modules, skins.
- Messages: activate, seal, open helmet, select module, toggle/use module, eject core, select skin.
- Normal wearer opens the suit UI through an item action.
- A Protean controller opens the same UI through the linked suit; a
  `BoundUserInterfaceCheckRangeEvent` handler returns `Pass` for the linked controller.
- If cross-entity opening is unreliable, add a compact "Suit" tab inside the Protean window.

### Construction and acquisition

Lathe prints:

- MOD shell
- standard core
- construction helmet/chestplate/gauntlets/boots
- theme plating
- modules

Assembly uses the TG shell sequence: core -> screwdriver -> helmet -> chestplate -> gauntlets ->
boots -> wrench -> screwdriver -> plating. Result is a control unit with parts stored inside, theme
applied and core installed.

No finished suits need to be lathe results; construction plus plating covers all themes.

## 4. Protean architecture

### Components and prototypes

- `MobProteanSuitController`: invisible controller mob.
- `ProteanSuitForm`: polymorph prototype.
- `ProteanControllerComponent`: links controller, original body and suit.
- `ProteanSuitComponent`: links suit, original body, controller, stored suit, stored theme, stored
  skin and lock state.
- `ProteanComponent`: species marker/owner component on the original body.
- `ProteanCoreComponent`: suit core powered by refactory metal.
- `ProteanBrainComponent`: dead/repair state and metamorphic control.
- `ProteanRefactoryComponent`: metal storage/metabolism.
- `ProteanOrchestratorComponent`: missing-organ movement penalty.
- `ProteanServoModuleComponent`: servo subroutines.
- `ProteanUiSystem` / `ProteanWindow`: XAML UI.
- `ProteanFoldAction`: custom action that drops the suit and calls `PolymorphEntity`.

### Fold lifecycle

```text
[MobProtean + suit on back]
        |
        | 1. Validate suit is owned, inactive and removable
        | 2. Drop suit to the ground
        | 3. PolymorphEntity(body, ProteanSuitForm)
        v
Original body -> paused map
Mind          -> MobProteanSuitController
Suit          -> normal wearable MODsuit
Controller    -> parented to the suit
```

`ProteanSuitForm`:

```yaml
entity: MobProteanSuitController
inventory: None
transferName: true
transferDamage: false
revertOnDeath: false
revertOnCrit: false
revertOnDelete: true
forced: true
effectProto: EffectProteanToSuit
```

- `inventory: None` deliberately leaves all other gear on the original body.
- Only the suit is dropped, because it must become the wearable object.
- `forced: true` prevents the generic revert action from being granted.
- `PolymorphableComponent` is added to `MobProtean` for cooldown/last-morph state, but it has no
  innate polymorph. The custom fold action does the suit handling first.

### Controller

`MobProteanSuitController` must be minimal and safe:

- `MindContainerComponent`
- `ActionsComponent`
- `UserInterfaceComponent`
- `DoAfterComponent`
- `EyeComponent` / `ContentEyeComponent`
- no sprite layers
- no fixtures/collision
- no hands
- no `DamageableComponent`
- no `MobStateComponent` unless testing proves one is required
- remove `InputMover`, `MobMover` and movement-speed components after polymorph so the player cannot
  move the controller independently
- parented to the suit with `Transform.SetParent`, not inserted into an inventory container

The controller camera follows the suit. It only provides actions and UI.

### Unfold lifecycle

1. Ensure the suit is inactive.
2. Remove the suit from any wearer, hand or storage container.
3. Drop it at the current location.
4. Detach the controller to the grid/map at the suit coordinates.
5. Call `PolymorphSystem.Revert(controller)`.
6. Original body is restored at the suit location.
7. Equip the suit on the Protean's back.
8. Apply the reform status effect.

Step 4 is mandatory. `PolymorphSystem.Revert` restores the body to the controller's parent. If the
controller is still parented to the suit, the body would be parented to the suit item.

### Suit worn by another player

- The suit remains a normal MODsuit back item.
- The controller stays parented to it and follows.
- The wearer gets normal suit protection and controls.
- `ProteanSuitComponent.Locked` prevents the wearer from removing or deactivating the suit.
- The Protean owner can unlock it through the Protean UI.
- The owner can open the normal suit panel through the range-check bypass.
- Do not use `PilotedClothingSystem` for normal Protean behavior; it makes a contained pilot control
  the wearer, which is not what Protean folding does.

### Suit deletion

- Suit parts and controls are effectively indestructible in v1.
- Subscribe to `EntityTerminatingEvent` on the suit.
- If a controller exists:
  1. detach it to the grid;
  2. call `Revert` before the suit finishes terminating;
  3. restore the original body at the suit's last coordinates;
  4. if reversion fails, mark the core dead and restore the body in a safe state.
- Never rely on `PolymorphSystem`'s delete path while the controller is still parented to a
  terminating suit.

### Protean suit behavior

- Default theme based on standard MODsuit, with a large storage module.
- Integrated `ProteanCoreComponent`; cannot be removed with a wrench or emagged.
- Refactory metal is reported as core charge; source semantics treat metal as a gate rather than a
  consumed battery. Keep initially and document balance changes.
- No standard cell; no ethereal/soul/plasma cores.
- Suit is not removable by the Protean owner while outside.
- Non-owner wearers can remove it normally unless locked.
- Stripping the suit from the Protean owner is blocked; storage can be emptied normally.
- If the species is lost, force-unfold if needed, drop stored suit, delete the Protean suit and
  clear references.

### Protean survival and repair

- Critical damage before ghosting forces a fold.
- Fully heal the body, mark the brain dead, delete the refactory, then fold. This mirrors the
  source's "alive body with dead core" state.
- While dead, the suit cannot activate/seal and the owner cannot unfold.
- Open the suit with a screwdriver and insert a new refactory:
  - 10-second do-after;
  - install into the original body;
  - start a 5-minute repair timer;
  - on completion, clear dead state and heal.
- Pen memory reset:
  - 10-second do-after;
  - admin log;
  - owner message and 1-minute sleep.
  - Target has no true memory system; document this as a roleplay/logout effect.

### Other Protean abilities

| Ability | Plan |
|---|---|
| Metal storage/refactory | Port; metal replaces hunger; passive drain and healing |
| Low power | Port; slows movement, reduces metal drain, blocks suit activation, only outside suit |
| Heal | Port; 30s, costs 6 metal, heals body/limbs/organs |
| Suit assimilation | Ported theme-level: absorb another MODsuit, store the original, apply its theme/skin. Module transfer is deferred. |
| Suit ejection | Port; restore stored suit/theme/modules |
| Plating assimilation | Port; copy theme, consume plating |
| Lock suit | Port; owner-controlled lock on non-owner wearer |
| Servo module | Port movement and engineering modes; medical mode omitted with surgery |
| Alter form | Defer; optional character-editor integration |
| Wearer disguise | Omit; not required for core Protean |
| Cargo organ/vessel packs | Optional; organs via techfab first |
| Memory reset | Port as message/log/sleep |

### Species/body

- `MobProtean` + `MobProteanDummy`, round-start species.
- Body prototype with nanite head/chest/arms/legs, optional digitigrade.
- Organs: brain/core, orchestrator, refactory, eyes, ears, tongue, liver.
- Reject non-Protean organs in core slots; allow robotic/nanomachine organs.
- Missing refactory causes damage; missing orchestrator causes knockdown/slowdown.
- Use target organ damage where available; simplify full organ health/rejection and document.
- Traits: no hunger, no breath, rad/toxin immune, no wounds, no virus, synthetic/robotic, custom
  death handling.
- Speech/typing/names: synthetic/nanite flavor using existing systems.
- Markings: reuse humanoid/furry base sprites and `kind` allowances; add tail layers and
  digitigrade `altSprites`.
- No unique full-body RSI required; source only supplies organ/effect art. Use nanite coloration and
  existing humanoid bases.

### Species spawn integration

- Ensure suit creation/equipment on `PlayerSpawnCompleteEvent` after the job outfit.
- Add a minimal `SpeciesChangedEvent` to `SharedHumanoidAppearanceSystem.SetSpecies`
  (marked `// Palmtree`) or handle species changes through an idempotent `ProteanSystem` check.
- If the player already has a back item, move it into the suit's large storage if possible,
  otherwise drop it.
- If the job outfit contains a MODsuit, assimilate it optionally; otherwise store/drop it.
- Add the Protean interface action to the body and controller.
- When folded, the body action is dormant; the controller gets its own action.

## 5. Content scope

### MODsuit themes

Batch 2 crew themes:

- standard
- civilian
- engineering
- atmospheric
- advanced
- mining
- medical
- rescue
- research
- security
- safeguard
- magnate
- cosmohonk

Deferred/special:

- portable suit (special slot layout)
- loader (loader mechanics omitted)
- syndicate, elite, nuclear, infiltrator, interdyne, enchanted, ninja, prototype, glitch,
  responsory, apocryphal, corporate, chrono, debug, administrative

Special suits are Batch 6. Port only those whose dependency systems exist. Chrono/timeline, ninja
mechanics and similar content may be admin-only or omitted.

### Modules

Port order:

1. Core/general: storage, flashlight, light variants, magboots, ion jetpack, welding, tether, rad
   protection, t-ray, constructor, signlang radio, status readout, thermal regulator, longfall,
   shock absorber, recycler, EMP shield, DNA lock, joint torsion, fishing glove, hat stabilizer,
   plasma stabilizer.
2. Engineering: atmospheric mister, head protector, advanced magboots, welding variants.
3. Medical: health analyzer, injector, defibrillator, organizer, quick carry, patient transport,
   thread ripper only if surgery exists.
4. Visor: diagnostic, medical, meson, night, security, thermal.
5. Security: holster, magnetic harness, megaphone, pepper shoulders, projectile dampener, quick
   cuff, shooting assistant, shove blocker, active sonar.
6. Service/supply: bikehorn, selfcleaner, microwave beam, balloon, waddle, clamp, drill, orebag,
   magnet, hydraulic, GPS, disposal connector.
7. Science: reagent scanner; anomaly modules only if reasonable.
8. Antag/ninja: energy shield, chameleon, medbeam, noslip, power kick, springlock, stealth, energy
   net, hacker, weapon recall, adrenaline boost - only where dependencies exist.

### Protean content

- Species, body, organs, integrated suit.
- Polymorph fold/unfold.
- Metal/refactory, low power, heal.
- Assimilation/ejection, lock.
- Critical withdrawal/repair, memory reset.
- Servo module.
- Optional cargo organs/vessel and alter form.
- Guidebook/locale/admin spawns.

## 6. Techfab and research integration

### MODsuits

Use **dynamic lathe packs**, not static packs. Static packs bypass research gating.

| Techfab | Pack contents |
|---|---|
| EngineeringTechFab | shell, cores, construction parts, plating, general/engineering modules |
| MedicalTechFab | medical modules, Protean refactory/orchestrator |
| SecurityTechFab | security modules |
| ServiceTechFab | service modules |
| Cargo/SalvageTechFab | supply modules |
| EngineeringTechFab or appropriate | science/general modules if no science techfab exists |

Research nodes use existing NF disciplines:

- `NFEngineering`: base MODsuit construction, engineering/atmospheric plates, utility modules.
- `NFMedical`: medical plate/modules and Protean organs.
- `NFArsenalNfsd` / `NFArsenalMercenary`: security modules and special suits.
- `NFScience`: research/advanced modules.
- Cross-prerequisites where appropriate.

Group recipes; do not create one node per module unless necessary. Each node's `recipeUnlocks` must
match the dynamic packs.

### Protean

- Suit is not crafted; it spawns with the species.
- Refactory/orchestrator and optional organs are printed at MedicalTechFab (or EngineeringTechFab)
  after research.
- Servo module recipe under the appropriate engineering/science node.
- Cargo organ/vessel packs optional; no cargo-only dependency for basic repair.

Test recipes are unavailable before research and printable after. Do not add suit recipes to static
packs.

## 7. Art plan

### MODsuits

- Convert TG `mod_clothing.dmi`, `mod_modules.dmi`, `mod_construction.dmi` to RSI under
  `Resources/Textures/_PS/Modsuits`.
- Preserve all states, directions, delays and metadata.
- Port action/module icons into `_PS` action/interface RSIs.
- Add theme skins via `ModsuitSkin` visual data.
- Anthro/digitigrade:
  - Merge Skyrat/Sandstorm `mod_clothing.dmi` states where available.
  - Use `ClothingVisuals` keys `${slot}-${speciesId}` for species-specific states.
  - Use `LegDisplacementModsuit` for digitigrade boots/chestplate via `LegDisplacementPrototype`.
  - Do not use `IGNORE_DIGITIGRADE` on MOD boots.
  - Leave `TailBehind` and `TailOversuit` visible; test Tajaran, Rodentia, Anthromorph and other
    tailed species.
- Crew themes get full anthro/digi coverage; special themes fall back to base art where source
  variants do not exist, documented.

### Protean

- Port `protean.dmi` organ/effect states: refactory, orchestrator, core, eyes/ears/tongue/liver,
  to/from suit effects.
- Use existing humanoid/furry body bases for the species.
- Add digitigrade `altSprites` for Protean body parts.
- Keep organ/effect art in `_PS/Protean`.
- Add reform/low-power/servo status alert icons.
- No ERP art.

### Volume control

34 themes x 4 parts x sealed/unsealed x species variants is a large art surface. Do not block
runtime on complete art:

1. Batch 1 uses standard/engineering art only.
2. Batch 2 adds crew themes.
3. Batch 3 adds species variants.
4. Batch 6 adds special themes.
5. Missing variants use base states and are listed for follow-up.

## 8. Revised batch plan

| Batch | Work | Exit criteria |
|---|---|---|
| **1. MODsuit vertical slice** | Components/systems, standard + engineering themes, four parts, core, deploy/retract, seal, 5 modules, BUI, construction, tests | Assemble standard suit, wear/deploy/seal, use flashlight/storage/magboots, retract cleanly |
| **2. Crew suits + acquisition** | All crew themes/plating, construction recipes, dynamic lathe packs, research nodes, base art | Every crew theme craftable through techfab after research |
| **3. Crew modules + species art** | Supported modules, anthro/digi art, tail/digi validation | Module install/use/UI works; crew suits render correctly on humanoid, anthro and digitigrade species |
| **4. Protean core** | Species/body/organs, integrated suit, polymorph controller, fold/unfold, metal, power, Protean UI, spawn integration | Round-start Protean folds/unfolds; body inventory preserved; suit wearable by another; no ghost |
| **5. Protean advanced** | Assimilation/ejection, lock, low power, heal, critical fold/repair, memory reset, servo module, suit deletion | All source non-ERP mechanics pass edge tests |
| **6. Special MODsuits** | Antag/special themes and supported modules, uplink/admin distribution; omissions documented | Special themes/plating/recipes load; unsupported modules documented |
| **7. Hardening** | Remaining art, balance, guidebook, locale, docs, full validation | Build/lint/tests/typecheck clean; manual gameplay pass |

Batch 3 may be split into 3a modules and 3b art. Protean must not start until Batch 1 runtime and
Batch 2 base suit construction are stable.

Current checkpoint (2026-10-20): **feature checkpoint.** Batch 1 runtime is usable; Batch 2 crew
themes/plating/research and worn TG art are in; Batch 3 retains the supported storage/flashlight/
magboots/servo modules and digitigrade validation; Batch 4 Protean core/advanced (species, body/organs,
fold/unfold, UI, lock, assimilation/ejection with module transfer, plating assimilation, low power,
heal, memory reset, critical repair, core/metal bound suit power, missing-organ penalties and suit
cleanup) passes tests. Batch 6 adds 18 special themes/plating recipes and their research/pack wiring.
The remaining work is optional breadth: the wider module/art library, unique Protean/anthro art
variants, servo engineering mode and unsupported modules/mechanics.

## 9. Explicit omissions and adaptations

Document in `.ai/PORTING.md` and `.ai/HAZARDS.md`.

| Omitted/adapted | Reason |
|---|---|
| Lustwish theme/suit, hypnotic visor, Zubbers remote companion | ERP-specific source content |
| MODlink and AI movement/control | No corresponding AI movement system |
| Timeline modules and chrono suit mechanics | No timeline system |
| Pathfinder module | No pathfinder target system |
| Loader/ash/sphere mechanics | No loader/ash/sphere systems |
| Candela spool | Beacons not ported |
| Surgery-dependent modules | No surgery system; medical modules adapted or omitted |
| Soul/ethereal/plasma cores | No corresponding species/core systems |
| Jump jet z-level movement | Target lacks z-levels; omit or adapt |
| Portable suit special slot layout | Defer until generic part layout supports it |
| Suit part destruction/repair | V1 parts effectively indestructible; control deletion handled |
| Suit paint kit | Optional hardening feature |
| Protean alter form/character editor | Optional; defer |
| Protean wearer disguise | Omit; not required |
| Protean cargo vessel/dead body printing | Optional; organs via techfab first |
| True Protean memory wipe | Target has no memory system; use message/log/sleep |
| Full Protean organ health/surgery | Simplify; document differences |
| Protean OOC escape | Not ported; ensure lock has owner unlock/admin rescue path |
| Station antag integration for special suits | Existing uplink/merc/admin paths only |

## 10. Validation

Run after each batch:

```bash
dotnet build SpaceStation14.sln
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
dotnet run --project Content.YAMLLinter -c Debug --no-build
dotnet test Content.Tests/Content.Tests.csproj
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~EntityTest|FullyQualifiedName~CharacterCreationTest|FullyQualifiedName~_PS"
dotnet run --project Content.Client -c Debug --no-build -- --headless
```

Add:

- `ModsuitTest`
  - deploy/retract all parts
  - slot conflict/overslot restore
  - seal adds/removes pressure and temperature protection
  - core charge and low-power shutdown
  - module install/use/toggle/cooldown/complexity/incompatibility
  - construction sequence
  - research gating and techfab availability
- `ProteanTest`
  - original body parked with inventory intact
  - controller mind transfer and parent link
  - suit wearable by another player
  - suit panel access from controller
  - unfold restores body and equips suit
  - suit deletion restores body
  - lock, assimilation/ejection, low power, heal
  - critical fold/refactory repair/memory reset
  - disconnect/reconnect while folded
- `EntityTest` for every new prototype.
- Manual checks for tails, digitigrade, sealed layers, UI and module actions.

## 11. Main risks and mitigations

| Risk | Mitigation |
|---|---|
| Controller/revert parent bug | Detach controller to grid before `Revert`; test ground, worn and stored suit |
| Suit deletion deletes/orphans controller | Handle suit termination first; detach and revert; guard `PolymorphSystem` parent deletion |
| Controller receives movement input | Remove `InputMover`/`MobMover`; no fixtures; no sprite; parent to suit |
| Suit UI not openable from controller | `BoundUserInterfaceCheckRangeEvent` pass for linked controller; fallback compact suit tab |
| Suit worn by another player | Test lock, UI, controller follow, suit removal and owner unfold |
| Part slot conflicts | Overslot container; atomic deploy failure; never half-equip parts |
| Art volume | Crew-first; base fallback; separate art batches |
| Research gating bypassed by static packs | Use `dynamicPacks` only; test recipes before/after unlock |
| Protean organ/surgery assumptions | Use target organ hooks; simplify and document |
| Client sandbox/typecheck | No prohibited shared/client APIs; run headless client validation |

## 12. Default decisions

1. Existing NF techfabs and disciplines, no new research discipline.
2. Dynamic recipe packs, never static, for research-gated MODsuit content.
3. Crew suits first; special/antag suits in Batch 6.
4. Overslot container for displaced head/outer/gloves/shoes items; restore on retract.
5. Suit parts effectively indestructible in v1; document part destruction as deferred.
6. Protean polymorph controller as described, with suit deletion handling.
7. No ERP content, no alter-form/disguise in the first pass, optional cargo vessel later.

## 13. Batch 1 implementation notes

Initial files are under `Content.Shared/_PS/Modsuits`, `Content.Server/_PS/Modsuits` and
`Resources/Prototypes/_PS/Modsuits`.

The first vertical slice deliberately uses placeholder part visuals until the TG RSI conversion is
done. The deployment, seal, power, module and UI plumbing must compile and be testable before art is
added.

Batch 1 progress:

- [x] Plan recorded in `.ai/ideas/modsuit-protean-port.md`
- [x] Core components/prototypes
- [x] Deployment/retraction
- [x] Seal/unseal
- [x] Core power
- [x] Module base + storage/flashlight/magboots modules
- [x] BUI (XAML control panel, toggle/seal messages, module/part state)
- [x] Construction shell/plating assembly and techfab/research recipes
- [ ] Remaining first-batch modules
- [x] Basic integration and acquisition tests

Implemented anchors:

- `Content.Shared/_PS/Modsuits/Components/ModsuitControlComponent.cs`
- `Content.Shared/_PS/Modsuits/Components/ModsuitPartComponent.cs`
- `Content.Shared/_PS/Modsuits/Components/ModsuitCoreComponent.cs`
- `Content.Shared/_PS/Modsuits/Components/ModsuitModuleComponent.cs`
- `Content.Shared/_PS/Modsuits/Components/ModsuitStorageModuleComponent.cs`
- `Content.Shared/_PS/Modsuits/Components/ModsuitFlashlightModuleComponent.cs`
- `Content.Shared/_PS/Modsuits/Prototypes/ModsuitThemePrototype.cs`
- `Content.Shared/_PS/Modsuits/ModsuitUi.cs`
- `Content.Shared/_PS/Modsuits/SharedModsuitSystem.cs`
- `Content.Server/_PS/Modsuits/ModsuitSystem.cs`
- `Content.Server/_PS/Modsuits/ModsuitModuleSystem.cs`
- `Content.Server/_PS/Modsuits/ModsuitConstructionSystem.cs`
- `Content.Client/_PS/Modsuits/`
- `Resources/Prototypes/_PS/Modsuits/`
- `Resources/Prototypes/_PS/Recipes/Lathes/modsuit.yml`
- `Resources/Prototypes/_PS/Recipes/Lathes/Packs/modsuit.yml`
- `Resources/Prototypes/_PS/Research/modsuit.yml`
- `Resources/Prototypes/_PS/Damage/modsuit.yml`
- `Resources/Textures/_PS/Modsuits/Clothing/mod_clothing.rsi` (TG worn art, one PNG per state)
- `Resources/Locale/en-US/_PS/modsuit.ftl`
- `Content.IntegrationTests/Tests/_PS/ModsuitTest.cs`
- `Content.Shared/_PS/Protean/`
- `Content.Server/_PS/Protean/ProteanSystem.cs`
- `Content.Server/_PS/Protean/ProteanRefactorySystem.cs`
- `Content.Server/_PS/Protean/ProteanUiSystem.cs`
- `Content.Client/_PS/Protean/`
- `Resources/Prototypes/_PS/Protean/`
- `Resources/Prototypes/_PS/Species/protean.yml`
- `Content.IntegrationTests/Tests/_PS/ProteanTest.cs`

Batch 1 verified with:

```bash
dotnet build SpaceStation14.sln --no-restore
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj --no-restore
dotnet run --project Content.YAMLLinter -c Debug --no-build
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ModsuitTest"
dotnet run --project Content.Client -c Debug --no-build -- --headless
```

The headless client reaches `Content.Shared: Verified IL` / `Content.Client: Verified IL` and then fails on
the expected unavailable OpenGL binding.

Known gaps in the current slice:

- Part/core/module visuals use placeholder hardsuit/glove/boot RSIs until the TG RSI conversion.
- No lathe/research recipes yet.
- No jetpack, medical or security modules yet.
- Module removal is implemented as a server helper but has no UI/verb yet.
- `Open` state is present but no screwdriver interaction yet.
- `ModsuitSystem.Update` dirties the core charge every active tick; optimize later if network cost shows up.
- Both systems rely on the access patches listed in `.ai/HAZARDS.md`.
- DMI-to-RSI conversion requires the DMISharp/BYOND atlas order (frame-major, then direction, row-major by sheet columns). The worn TG RSI was generated with one PNG per state and 2x2 direction grids for static states; keep this in mind if regenerating module overlays.

## 14. Batch 4 progress

- [x] `MobProtean`/`MobProteanDummy` and the `Protean` species prototype
- [x] Custom humanoid `Protean` body prototype using human body parts and Protean synthetic organs
- [x] `ProteanComponent`, `ProteanSuitComponent`, `ProteanControllerComponent`
- [x] `MobProteanSuitController` and the `ProteanSuitForm` polymorph prototype
- [x] Fold/unfold runtime using the parked-map polymorph path
- [x] Species spawn auto-equips the integrated suit, dropping a backpack if the back slot is occupied
- [x] Fold/unfold integration test (`Content.IntegrationTests/Tests/_PS/ProteanTest.cs`)
- [x] Refactory metal store and passive drain
- [x] Suit lock action and non-owner removal/toggle block
- [x] Integrated suit cannot be removed by its owner through normal inventory actions
- [x] Low power and direct damage healing
- [x] Non-critical organ and limb regeneration
- [x] Refactory organ techfab recipes
- [x] Protean UI (metal, fold/unfold, suit lock, ejection, low power, self-repair, servo)
- [x] Suit assimilation/ejection (theme + non-storage module transfer)
- [x] Refactory organ recipes and MedicalTechFab research unlock
- [x] Critical withdrawal/refactory repair and five-minute repair timer
- [x] Memory reset via pen do-after (sleep/log; no true memory storage)
- [x] Basic servo movement mode + UI
- [ ] Servo engineering mode (no target action-speed system)
- [x] Deployed parts use `AttachedClothingComponent` for removal protection
- [x] Suit destruction cleanup
- [x] Species-loss cleanup
- [x] Synth physiology core (no breathing/metabolism, pressure immunity, temperature/fire handling, reboot repair)
- [x] Source Protean toxin/radiation immunity and no-DNA handling
- [ ] Source traits with no target system (virus, wounds, madness, dismemberment)
- [x] Missing refactory/orchestrator penalties
- [x] Integrated ProteanCore / plating assimilation
- [x] Sealed MODsuit helmet hides hair and head layers
- [x] Digitigrade MODsuit deployment via existing leg displacement maps
- [ ] Unique Protean/anthro sprite variants (source art unavailable)

Current Prototype anchors:

- `Content.Shared/_PS/Protean/Components/ProteanComponent.cs`
- `Content.Shared/_PS/Protean/Components/ProteanSuitComponent.cs`
- `Content.Shared/_PS/Protean/Components/ProteanControllerComponent.cs`
- `Content.Shared/_PS/Protean/Components/ProteanOrganComponents.cs`
- `Content.Shared/_PS/Protean/ProteanEvents.cs`
- `Content.Server/_PS/Protean/ProteanSystem.cs`
- `Content.Server/_PS/Protean/ProteanRefactorySystem.cs`
- `Content.Server/_PS/Protean/ProteanUiSystem.cs`
- `Content.Client/_PS/Protean/`
- `Resources/Prototypes/_PS/Protean/controller.yml`
- `Resources/Prototypes/_PS/Protean/suit.yml`
- `Resources/Prototypes/_PS/Protean/actions.yml`
- `Resources/Prototypes/_PS/Protean/body.yml`
- `Resources/Prototypes/_PS/Protean/organs.yml`
- `Resources/Prototypes/_PS/Protean/ui.yml`
- `Resources/Prototypes/_PS/Protean/status_effects.yml`
- `Resources/Prototypes/_PS/Protean/servo_status_effect.yml`
- `Content.Shared/_PS/Protean/Components/ProteanServoModuleComponent.cs`
- `Content.Client/_PS/Protean/ProteanBoundUserInterface.cs`
- `Content.Client/_PS/Protean/ProteanWindow.xaml` / `.xaml.cs`
- `Resources/Prototypes/_PS/Species/protean.yml`
- `Resources/Locale/en-US/_PS/protean.ftl`
