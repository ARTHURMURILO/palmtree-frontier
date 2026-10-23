# HAZARDS

> Things a future AI agent can easily get wrong. None of these are bugs to "fix" as part of documentation.

## 1. Client sandbox type checking (high risk for new shared/client code)

The client runs the engine's IL type checker (`res.typecheck`). Only whitelisted BCL APIs are allowed
in `Content.Shared` and `Content.Client`. A violation crashes the client at startup with
`Assembly Content.Shared failed type checks`, not a compile error.

Observed failure: `string.Create(IFormatProvider, ref DefaultInterpolatedStringHandler)` is blocked.
`float.ToString(IFormatProvider)`, `CultureInfo`, `System.Single` (`All: True`) and `Enum`
(`All: True`) are allowed; most `string` members are individually whitelisted.
Whitelist lives in `RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml`.

Also blocked: **collection expressions targeting `List<T>`** (`List<X> Foo = ["a", "b"];`). The
compiler lowers non-empty list collection expressions to `CollectionsMarshal.SetCount`, which the
sandbox rejects (`Sandbox violation: Access to method not allowed: ...CollectionsMarshal.SetCount`).
Empty `[]` and array targets (`X[]`) are fine; use `new() { "a", "b" }` for lists.

To verify locally without a display:
`dotnet run --project Content.Client -c Debug --no-build -- --headless` → look for
`Content.Shared: Verified IL` / `Content.Client: Verified IL`; the process then dies on OpenGL,
which is expected. There is no sandbox check in `Content.IntegrationTests`.

## 2. Marking filters must be kind-aware

There are several places that filter markings by species; all must use
`MarkingManager.IsAllowedBySpeciesOrKindAllowance`:

- `MarkingManager.MarkingsByCategoryAndSpecies(AndSex)`
- `MarkingManager.IsValidMarking`
- `MarkingManager.CanBeApplied` (both overloads)
- `MarkingsSet.EnsureSpecies`

Using `SpeciesRestrictions.Contains(species)` directly silently strips markings shared through
`kindAllowance` (e.g. `FeroxiTorsoCountershadingF` on a Vulpkanin).

Also remember `onlyWhitelisted` logic: a marking counts as whitelisted if it has
`speciesRestriction` **or** `kindAllowance`.

## 3. `MarkingsSet.EnsureDefault` loop

The loop must be `while (points.Points > 0 && index < points.DefaultMarkings.Count)`. Using `||`
indexes past `DefaultMarkings` whenever points > defaults (Rodentia uses 999 points with one default
marking) and crashes entity spawning. It also skips categories that already have markings (so
imported characters are not given defaults on top).

## 4. Fork-patched upstream files

Frontier and this port edit upstream files in place, marked with `// Frontier`, `// Coyote`,
`// Palmtree`, `// Floof`, `// _CS`, etc. Never assume a core file is untouched upstream. Grep for
markers before editing. Keep patches minimal and annotated.

Notable in-place patches added by this port:

- `Resources/Prototypes/Entities/Clothing/Back/backpacks.yml` — `ConcealableClothing` on
  `ClothingBackpack`.
- `Content.Server/GameTicking/GameTicker.LobbyBackground.cs` — 30s rotation timer + status broadcast.
- `Content.Client/Lobby/LobbyState.cs` + `LobbyGui.xaml(.cs)` — background crossfade.
- Species YAML (`Resources/Prototypes/Species/*`, `_DV`, `_NF`, `Nyanotrasen`, `_CS`) — new layers,
  `kind`, `altSprites`.
- `Resources/Prototypes/_NF/Loadouts/**` — `CoyoteJumpsuit` subgroups and extra loadout entries.
- `Content.Shared/Movement/Pulling/Systems/PullingSystem.cs` — mass-based pull slowdown for heavy
  pulled entities (`// Palmtree`).
- `Content.Server/Body/Systems/RespiratorSystem.cs` — skips entities with `SynthComponent`.
- `Content.Server/Emp/EmpSystem.cs` — `EmpPulse` skips entities the lookup returned after they
  were deleted in the same tick (Frontier's grid-immunity check used `Transform()` and threw on
  them) (`// Palmtree`).
- `Content.Client/Clothing/ClientClothingSystem.cs` — leg-style displacement override.
- All species-restricted markings — `kindAllowance` added (3,050 occurrences across 84 files as of
  2026-10; recount with `grep -rl kindAllowance Resources/Prototypes | wc -l`), so markings are
  shared across species kinds like Coyote did.
- `Content.Server/Ghost/GhostSystem.cs` — `GetPlayerWarps` also lists disconnected (SSD) characters
  (`// Palmtree`): the ghost warp menu is built from `IPlayerManager.Sessions`, so a character whose
  mob stays in the round after a disconnect never appeared. Minds with a `MindComponent.UserId` and no
  session behind them are now included while their mob is alive or critical.
- `Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs` — its `[Access]` list also names
  `SharedBluespaceDockSystem` (`// Palmtree`), the shared helper both ship consoles use to create
  deeds. The analyzer blocks foreign writes to the component, and a *shared* component cannot name a
  server-side system in the attribute, so the write helper lives in the shared system (2026-10).

## 5a. `SharedMapSystem.TilesEnumerator` recurses per skipped tile

`TilesEnumerator.MoveNext` ends with `return MoveNext(out tile)` for tiles it skips, and
`GetLocalTilesEnumerator` defaults to `ignoreEmpty: true` — so enumerating a large area of an
empty-ish grid costs **one stack frame per tile** and stack-overflows the process. Any overlay that
enumerates tiles for a viewport-sized box must clamp it (`box.Intersect(grid.LocalAABB)`, see
`Content.Client/Shuttles/FtlArrivalOverlay.cs`) or pass `ignoreEmpty: false`.

## 5b. The XAML compile can be skipped after adding new `.xaml` files

`CompileRobustXaml` (XamlIL, `RobustToolbox/MSBuild/XamlIL.targets`) runs `AfterCompile` with
`Inputs="@(IntermediateAssembly);@(ReferencePathWithRefAssemblies)"` and
`Outputs="$(IntermediateOutputPath)XAML/doot"` — **new `.xaml` files are not inputs**, so the target
can skip and the window's compiled XAML never lands in the assembly. The `[GenerateTypedNameReferences]`
fields still compile (they come from the source generator), so the build looks fine and the client
crashes at runtime with `No precompiled XAML found for <Type>, make sure to specify Class or name
your class the same as your .xaml`.

- The failure is visible only as a bare `MSB3073: The command "... /t:CompileRobustXaml ..." exited
  with code 1` in the build output — treat it as a real XAML error, never as a transient MSBuild
  hiccup (the actual message is swallowed by the nested build).
- To force a recompile: delete `Content.Client/obj/<Config>/XAML/doot` (and recreate the directory if
  you also removed `obj/<Config>/XAML/`, which holds the task's `original.dll`/`references`).
- To verify: `strings bin/Content.Client/Content.Client.dll | grep "<Namespace>.<Window>.xaml"` — the
  compiled XAML is an embedded resource named `<type full name>.xaml`.
- Common invalid XAML properties: `LineEdit` has **no** `MaxLength` (clamp in code instead); check
  `RobustToolbox/Robust.Client/UserInterface/Controls/*.cs` before using an unfamiliar property.

## 5. Deliberate namespace collisions for partial classes

Fork code extends upstream classes from `_NF`/`_PS` folders using the upstream namespace, e.g.
`Content.Server/_NF/GameTicking/GameTicker.NFSpawning.cs` declares
`namespace Content.Server.GameTicking; // Intentionally colliding namespaces`. Compile errors like
"partial class already has member" usually mean a duplicate hook in an unexpected folder. Always
`grep -rn "partial class GameTicker"`.

## 6. Engine is off-limits

`RobustToolbox/` is a submodule and the maintainer context says only Wizden modifies the engine. If a
fix seems to require engine changes, document it as an engine dependency instead.

## 7. Engine APIs that differ from public documentation

This engine revision (v267.3.0) differs from commonly documented SS14 APIs:

- **No `[EventHandler]` attribute** — explicit `Subscribe*` calls only.
- **No `[DependsOn]`** — use `UpdatesBefore/UpdatesAfter` in `Initialize`.
- **No `GetPVSInterest`**; PVS uses `SessionSpecific`, `SendOnlyToOwner`, `PvsOverrideSystem`,
  `ExpandPvsEvent`, filters.
- **No `PrototypeReloadEvent`** — use `PrototypesReloadedEventArgs`.
- **No `EntityDeletedEvent`** bus event; only the C# `EntityManager.EntityDeleted` event.
- **No `RoundEndedEvent`**; use `RoundEndMessageEvent` + `GameRunLevelChangedEvent`.
- **No `RoundIdle`**; run levels are `PreRoundLobby`, `InRound`, `PostRound`.
- Map format is **7**.
- `EntityQueryEnumerator<T>` skips paused entities; use `AllEntityQueryEnumerator<T>` to include them.
- Component/field renames observed during the port: `SoundOnTrigger` → `EmitSoundOnTrigger`,
  `OnUseTimerTrigger` → `TimerTrigger`, `ImplantImplantedEvent.Implanted` is now non-nullable.
  Old Coyote prototypes may use the old names.

## 8. Broadcast vs directed events

`RaiseLocalEvent<T>(msg)` never calls `SubscribeLocalEvent<TComp,TEvent>` handlers. If a handler
"doesn't fire", check the raise overload and whether the event is `[ComponentEvent]`.

## 9. Subscription lock

Subscribe only in `Initialize()`. Subscribing later throws "Subscription locked." after startup. Use
`Subs` helpers for CVar/C# event handlers so they auto-unsubscribe on shutdown.

## 10. YAMLLinter staleness and strictness

- Runtime prototype loading logs and skips per-file errors; a broken YAML may only appear as a
  missing entity at runtime.
- `dotnet run --project Content.YAMLLinter --no-build` runs the previously built output **including
  its copied `Content.Shared.dll`**. After C# changes (new enum values, new prototype fields), rebuild
  the linter or the solution first, or you will get bogus errors like
  `Requested value 'BaseLegs' was not found`.
- Unknown prototype fields are errors in the linter, even though runtime deserialization of
  non-prototype data (e.g. character exports) ignores them.
- `ProtoId` fields are validated: an `altSprites` entry pointing at a marking that does not exist
  fails the linter.

## 11. Locale duplicates

Adding a locale key that already exists anywhere in `Resources/Locale/<culture>` fails prototype
loading with `already exist entry of type: Message`. When porting locale files, check for keys that
already exist (the ported `_Floof/anthro.ftl` already contained the leg-style keys that were also
added to `markings-picker.ftl`; the duplicate was removed).

**Fluent (`.ftl`) has no inline comments.** Text after a value is part of the translated string, so
`chat-radio-traffic = Shortband # Palmtree...` renders the comment in-game; it has to be removed by
hand. Comments are only allowed on their own `#` line (common in this repo), never at the end of a
value line.

## 12. DB and persistence

- `LogType` numeric values are persisted; do not renumber.
- Migrations exist twice (SQLite + Postgres); update both or a provider breaks.
- Character markings are stored in `Profile.Markings` (jsonb) as strings:
  `markingId@#rrggbb,...` with optional `@scale,offsetX,offsetY`, `@g...`, `@m<flags>` and
  `@c<name>` suffixes. The parser tolerates every older format and defaults to
  `CanToggleVisible=false`, `OtherCanToggleVisible=false` when the flags segment is absent (old saves
  have no flags segment, so non-undergarment markings become non-toggleable; undergarments are
  re-enabled by `MarkingsSet.ApplyDefaultTogglePermission`). Changing
  this format requires updating `Marking.ToString` **and** `Marking.ParseFromDbString`.
- No Floof consent tables exist; the only consent-related schema is `Preference.NsfwConsent`
  (nullable bool, migration `NsfwConsent`) for the Palmtree age gate.

## 13. ERP/marking semantics

- Genital markings start hidden: `SharedHumanoidAppearanceSystem.AddMarking` sets `Visible = false`
  for `MarkingCategories.Genital`. Visibility is session state (not persisted in the DB string);
  characters get hidden genitals again after respawn/relog. `ModifyUndies` flips `Marking.Visible`
  via `SetMarkingVisibility`.
- `ModifyUndies` has no consent gate: other players can toggle a marking only when its
  `otherCanToggleVisible` opt-in is set (default off). The old consent-gated behavior was
  intentionally dropped. The account NSFW age gate gates *rendering* (client-side `Genital` skip),
  not these verbs.
- Base markings (`Base*` categories) hide the species base layer via `HiddenBaseLayers`; the list is
  rebuilt on every `UpdateLayers` and must be recomputed before hiding.
- Profile application: `SharedHumanoidAppearanceSystem.LoadProfile` must use the
  `AddMarking(uid, Marking marking, colors, ...)` overload. The `(string, colors)` overload creates a
  fresh marking and silently drops `scale`, `offset` and `glow` (symptom: editor preview shows the
  values, in-game they are defaults).
- Interaction panel (`systems/interaction-panel.md`):
  - The panel BUI is opened on the **actor**, not the target: this engine has one BUI state per
    `(entity, key)`, so binding it to the target would share state between different users.
  - `InteractionPrototype` requirement/flag lists are **strings** parsed with `Enum.TryParse`; a typo
    silently drops the requirement (no linter check).
  - Genital exposure follows the per-organ visibility rule (Always hidden / Hidden by underwear /
    Hidden by jumpsuit / Never hidden); the `Genital` marking flags are only used for rendering.
    Missing `InteractionStateComponent` means "consent on / hears lewd sounds" (defaults are the
    absence of state). The account-level NSFW age gate is checked separately and fail-closed
    (`InteractionPanelSystem.IsNsfwAllowed`); session-less mobs keep the legacy consenting default.
  - "Never hidden" sets `Marking.RenderOverClothing`, which the client uses to place the genital
    layer above `outerClothing`; "Always hidden" skips the render marking entirely. The `Genital`
    sprite layer must stay below `UndergarmentBottom` in species sprite maps or bottom underwear
    will not occlude organs.
  - `SharedPopupSystem.PopupCursor` broadcasts on the server; requirement failures use
    `PopupEntity(message, subject, actor)` so only the actor sees them.
  - Normal interaction sounds use PVS but lewd sounds are hand-filtered to sessions with
    `LewdSounds` enabled; do not route normal sounds through the lewd filter.
- Genital organs (`systems/genital-organs.md`):
  - `GenitalOrganSystem.SyncFromProfile` runs from a profile event that also fires during
    `ComponentInit`; guard on `BodyComponent.RootContainer != null` or it NREs before MapInit.
  - The client `HumanoidAppearanceSystem.LoadProfile` override does not call the shared method, so
    the lobby preview needs its own `SyncFromProfile` + `UpdateSprite` call; remote players get
    networked organ entities and the server-side render markings.
  - Organ render markings are added with `CanToggleVisible = false`; otherwise ModifyUndies exposes
    them as toggle verbs. The catalog is generated (`Tools/gen_genital_organ_catalog.py`), do not
    hand-edit `genital_organs.yml`.
  - Visibility gates rendering, not just layer overlap: covered organs are removed from the
    `Genital` marking category. Keep the `DidEquipEvent`/`DidUnequipEvent` (jumpsuit) and
    `MarkingVisibilityChangedEvent` (undies) subscriptions in `GenitalOrganSystem` or the sprite
    desyncs from clothing.
  - `Profile.Genitals` is a text column; changing `GenitalOrganSettings.ToDbString` requires updating
    `FromDbString`, `ServerDbBase.ConvertProfiles` and both providers' data (no schema change needed
    for format tweaks, but migration drift tests must stay green).
  - The semen drip uses `DecalPrototype`s (`SemenDrip*`/`SemenPuddle*`) via `DecalSystem`, not
    reagent puddles. It needs a grid with `DecalGridComponent` (no decals in space) and the decals
    are only removable while `Cleanable` (space cleaner's `CleanDecalsReaction`, or the `_PS`
    roomba's `CleanDecalsOperator`). Drips only place
    `SemenDrip*`; `SpawnCumDecals` places `SemenPuddle*`. Decals are placed at the mob's position
    with scatter and each drop is a **new** decal (no upgrading/replacing); scattered positions that
    land on space fall back to the mob's exact position. `GetDecalsInRange` compares against
    `coordinate + (0.5, 0.5)`, so queries must account for that offset.
  - Never raise `MarkingVisibilityChangedEvent` (or call `SyncRender`) while enumerating
    `MarkingSet.Markings`: the handler rebuilds the `Genital` category and mutates the dictionary.
    `SetMarkingVisibility` raises after its loop; the editor snapshots undergarment markings before
    toggling them.
  - The event bus allows only one subscription per (component, event) pair across all systems.
    `GenitalOrganSystem` owns `MarkingVisibilityChangedEvent`; the client `HumanoidAppearanceSystem`
    must use its `RefreshSprite` method for local preview toggles instead of subscribing too.
  - `GenitalOrganSystem` retries profiles applied before the body parts exist (admin respawns,
    deferred MapInit) via `_pendingProfiles`; dropping the retry makes characters spawn genital-less.
    A successful `OnProfileApplied` must **clear the pending entry**: mobs get a default
    genital-less profile event during `ComponentInit` before the real profile arrives, and a stale
    pending entry re-syncs on the next tick and wipes the configured organs (regression test:
    `StalePendingProfileDoesNotWipeGenitals`).

## 14. Configuration defaults differ from upstream

- `game.defaultpreset = "nfpirate"`, `game.map = "Frontier"`, `game.map_pool = "NFMapPool"`.
- Upstream game presets have `rules: []` — Traitor/Nukeops/Wizard are inert by default.
- `audio.lobby_music_collection` default was changed from `NFLobbyMusic` to `PSLobbyMusic`.
- `game.destination_file` triggers a one-shot data dump and shuts the server down.

## 15. State/global mutable traps

- `IoCManager` is per-thread; systems/managers are process singletons, not per round. Reset
  round-scoped state on `RoundRestartCleanupEvent`.
- `ServerPreferencesManager` caches profiles per user.
- `GameTicker` is a large partial class; prefer a system + rule over adding cross-concern logic.
- Prototype reloads mutate live entities; handlers must be idempotent.

## 16. Content-authoring hazards (carried over from the old docs)

- **Species Head/Chest base sprites require sex-morph variants** (`HeadMale`/`HeadFemale`/
  `TorsoMale`/`TorsoFemale`) or they render wrong/invisible.
- **`MarkingPoints.Required` is effectively unused.**
- **Dungeon rooms are copied by prototype ID only** — atlas entity DataFields are lost.
- **Salvage-magnet asteroid configs are commented out** while code still references them; only debris
  offers work.
- **`RandomHumanoidSpawner` markers self-delete on MapInit** — one-shot.
- **Admin permission cache is built once** in `AdminManager.Initialize()`; commands registered later
  become server-console-only for players.
- **Engine `savegrid`/`savemap` are server-console-only** and write to `data/`, never `Resources/`.

## 17. Stray repository artifacts

- `Resources/manifest.yml` window title/logo may not match Palmtree branding.

## 18. Runtime-loaded maps and server popups (Hilbert's Hotel / SS13 port findings)

- **`SharedPopupSystem.PopupClient` is a no-op on the server** — it exists for client-side
  prediction. Server code must use `PopupEntity(message, entity, recipient)` or the popup silently
  never appears (all hotel popups were invisible until this was fixed).
- **Never pause a runtime-loaded map with a player body on it.** `SetPaused(map, true)` also sets
  `EntityPaused` on the body; an admin ghost returning to a paused room froze the player. Keep maps
  live and delete them after a grace period instead.
- **`GravityComponent.Enabled` defaults to `false`.** Generated maps must set
  `Gravity: enabled: true, inherent: true` or players float.
- **Machines default to requiring APC power.** Runtime rooms have no power grid; emit
  `ApcPowerReceiver: needsPower: false` (resolve inheritance through prototype parents) or use
  `AlwaysPowered*` prototypes.
- **Props are unanchored unless their prototype is a structure.** Map generators should emit
  `anchored: True` on every placed prop.
- **`MapLoaderSystem.TryLoadMap` returns a paused, uninitialized map.** Call
  `SharedMapSystem.InitializeMap` yourself; do not pass `InitializeMaps = true` and then call it
  again (it throws).
- **YAMLLinter does not validate map files.** Missing or abstract prototypes only fail at load
  ("Missing prototype for map: X"); integration tests that load every map are the gate.
- **`savemap` writes to the server user-data dir** (`bin/Content.Server/data`) and map loading
  prefers user data over `Resources` ("Reading map user data instead of content"). Copy saved files
  back and delete stale user-data copies.
- **Rotating a `noRot` entity in a map file** triggers a load-time `DebugAssertException`.
- **Preview maps may be paused; gameplay maps must not.** The Bluespace Dock's orphan-ship preview
  calls `MapSystem.SetPaused(mapId, true)` on a throwaway map with no player bodies (safe); never do
  this to a map a character can stand on (see the hotel rule above).
- Full method and SS13 concept mapping: `.ai/guides/porting-from-ss13.md`.

## 19. Fork port batch hazards (2026-10)

- **Never run a blind word replacement over locale files.** Renaming "spesos" also rewrote FTL
  placeables (`{$spesos}` became `{$space roubles}`) and message keys
  (`forensic-reward-amount-speso-only`); the client then crashes at startup with a Fluent parse
  error. Always grep for `$` placeables and `^key` after bulk edits, and run the YAMLLinter (which
  boots a client and parses every `.ftl`).
- **Commenting out every child of an `entityTable` leaves `children:` null** and crashes prototype
  deserialization (`NullNotAllowedException`). Use `table: !type:NoneSelector` instead.
- **`EmoteCategory` is now a `ushort` flag set.** Emotes in species categories (Vulp, Felinid, ...)
  do not carry the `Vocal` flag, so `HasFlag(EmoteCategory.Vocal)` checks in muting/mumble systems
  no longer match them — this matches Coyote and is intentional, but new code should not assume
  "vocal emote" means `Vocal` flag only (`EmoteCategory.Sex` is `Vocal|Hands`).
- **DB changes need both migrations.** After editing `Content.Server.Database/Model.cs`, run
  `dotnet ef migrations add <Name> --context SqliteServerDbContext|PostgresServerDbContext
  --project Content.Server.Database --startup-project Content.Server.Database` (the design-time
  factories live in the DB project); commit the migration, designer and snapshot files.
- **Integration tests must `await pair.CleanReturnAsync()`** or the pair is dirty-disposed and the
  test is reported as skipped, not failed.
- **`_DV/Recipes/Lathes/misc.yml` already existed** (CassetteTape/TapeRecorder recipes); adding the
  water vapor tank recipe required appending, not overwriting (the lathe packs reference those ids).

## 20. BYOND gun sprites and `MagazineVisuals` (2026-10)

- **BYOND encodes ammo/bolt in the `icon_state` name**: `<name>[-<max_ammo>][-e][-f]` plus `_open`
  hand sprites (`-e` = no chambered round, `-<max_ammo>` = magazine attached, `-f` = folded stock).
  Examples: `rpd` (drum, chambered), `rpd-e` (open/empty), `rpd-100`, `uzi-30`, `M38closed80`.
  World art lives in `icons/fallout/objects/guns/{ballistic,bar,ammo}.dmi` and
  `modular_coyote/icons/objects/{automatic,mgs,rifles}.dmi` (the `gen_guns.py` port script is not
  in this repo). Parse sheet states with `state = "([^"]+)"\s*\n\s*dirs = (\d+)...`.
- **The port cropped instead of scaling.** `bar.dmi` is 40x32 and the `.rsi` frame is 32x32, so an
  early generator pass chopped the barrel off. This was later fixed by resizing `BAR.rsi` to 40x32
  (the `meta.json` now says 40x32). When a source is wider than the frame, resize the `.rsi` `size`
  (and pad the 32x32 inhand/equipped cells symmetrically so the center - and the held position -
  does not shift).
- **`MagazineVisuals` mapping**: `base`/`bolt-open` must use the *magazine-less* states, the
  `mag-N` overlay uses the *with-magazine* states, and `zeroVisible: false` unless `mag-0` really
  draws an empty magazine. A child prototype can add `- type: MagazineVisuals` with its own
  `steps`/`zeroVisible` (fields merge). If `base` has the mag and `bolt-open` does not, the magazine
  only appears when the player racks the gun (RPD bug). With `steps >= 2` + `zeroVisible: true`,
  `mag-0` is drawn for *partial* magazines as well, so it must be a copy of the with-magazine art;
  a blank `mag-0` makes the magazine vanish after the first shot (2026-10 QA: American180, AUGA10,
  DP27, M1A1, P90, Saiga12 and ScarL shipped with blank mag art and inherited `steps: 1`).
- **Fallout gun sprite QA (2026-10)**: the `gen_guns.py` generator left the following traps; the
  states were regenerated from the coyote-bayou DMIs and these rules must hold for future edits:
  - `equipped-BACKPACK`/`equipped-SUITSTORAGE` were the AK/viper placeholder art for most guns. The
    per-weapon worn art lives in `modular_coyote/icons/objects/back.dmi` under the gun's own name
    (`bar`, `dp`, `m1garand`, `mosin`, `mp5`, `ppsh`, `rpd`, `saiga12`, `scarl`, `sks`, `crossbow0`).
    M1919/M1A1 use the un-wired `browlmg`/`mcar` states (inferred - review before relying on them);
    no worn art exists for AUGA10, American180, P90, Uzi or the composite bow (still AK art).
  - `guns_lefthand/righthand` mixes dedicated held art with generic/hands-only states (`gun`,
    `shotgun`, `rifle`, `R84`). Dedicated states: `american`, `M38closedmag`, `crossbow`, `m90`,
    `rpd`, `aug`, `coltwalker`, `44colt`, `45revolver`, `judge`, `deagle`, `glock`, `pistolchrome`;
    `ww2gunsleft/right.dmi` holds the two-handed art for `dp`, `m1garand`, `mosin`, `mp5`,
    `ppsh`, `saiga12`, `scarl`, `sks`, `uzi`, `skorpion`. Before the QA pass the M1919, M1A1, PPSh
    and M1 Garand all shared the generic `rifle` art and the DP-27 used `R84`.
  - **The coyote BAR has no held art**: its `inhand_icon_state` is `BAR`, which does not exist in
    any onmob DMI, and `ww2gunsleft:bar` is actually the DP-27 drawing minus its bipod. The BAR
    keeps the `ww2gunsleft/right:bar` art for now - an accepted known mismatch; a proper BAR held
    art does not exist in the source.
  - **Magazine-less world states hide behind `-e` names**: for the DP-27 the pan-less art is
    `mgs.dmi:dp-e`/`dp_open` (the plain `dp`/`dp-47` states still contain it), and for the Saiga-12
    the magazine-less art is `ballistic.dmi:saiga-e`/`automatic.dmi:saiga12_open` (`saiga-8-e`
    keeps the magazine and only drops the chambered round). Base/bolt-open use these; `mag-0`/
    `mag-1` keep the with-magazine art so removing the magazine is visible.
  - **DMIs use the `(0,128,128,0)` colour key** for transparency; zero those pixels' RGB on
    conversion. A state missing from `guns_righthand` (e.g. `crossbow`) is mirrored from the left.
  - M1919 belt tiers are `mag-0` = empty belt, `mag-1..4` = 20/40/60/80 rounds (`M38closed0/20/40/
    60/80`); `base`/`bolt-open` are `M38closed-empty`/`M38open-empty`.


## 21. Expedition auto-recovery / ever-mind tracking (2026-10)

- **`HasHadMind` is Coyote-only.** `MindContainerComponent.HasHadMind` was backported from Coyote
  (commit `102b623d04e`) for expedition body recovery; it is **not** upstream Wizden/Frontier. It is
  set in `MindSystem.TransferTo` on attach, detach and ghost creation.
- **The `Access` analyzer blocks foreign writes.** The component's
  `[Access(typeof(SharedMindSystem))]` attribute had to be dropped (Coyote did the same); with it,
  any write to `HasHadMind` from another system or from tests fails the build with `RA0002`
  ("Tried to perform a 'Write' ... despite only having 'Read' access"). Removing `Access` lets every
  assembly write the component's fields - keep that in mind when adding more fields to it.
- **`FireStacksAdjustment` is negative.** `ExtinguishEvent.FireStacksAdjustment` documents "should
  be a negative number"; Wayfarer's port still had the old `+1000` bug, fixed here to `-1000`.
- **`AdminGhostComponent` is Coyote-only** (ported as a `Content.Shared/_CS` marker and added to
  the `AdminObserver` prototype). New systems that need to identify admin ghosts should use it
  rather than `GhostComponent.CanGhostInteract` (a general ghost flag); existing `CanGhostInteract`
  behaviour is unchanged.
- **`PlayerAttachedEvent` is raised directed + broadcast.** A global `SubscribeLocalEvent<
  PlayerAttachedEvent>` handler only fires when the event is raised with `broadcast: true` (the
  engine's own attach path does this); raising it directed-only in tests silently skips global
  subscribers.
- **Event-bus subscriptions are locked at runtime.** Scratch integration tests cannot
  `SubscribeLocalEvent`; assert via components or invoke private handlers by reflection instead.
  `RemCompDeferred` also needs a tick (`RunTicksSync`) before the component is gone.

## 22. Paths, drip state and record lookups (2026-10)

- **`IWritableDirProvider` only accepts rooted `ResPath`s.** `WritableDirProvider.GetFullPath`
  asserts `path.IsRooted` and throws `ArgumentException: Path must be rooted`; a relative path like
  `new ResPath("exported_characters")` therefore kills any command that writes to `UserData` (this
  is what broke `exportcharacters`). Build paths with
  `ResPath.FromRelativeSystemPath(dir).ToRootedPath()` and print the physical location with
  `root.ToRelativePath()`.
- **`Identity.Name` follows the identity entity, not the metadata name.** Mob prototypes carry an
  `IdentityComponent` whose `identity` entity is renamed on the *next* tick after an
  `EntityRenamedEvent` (`IdentitySystem.Update`). Tests that rename a mob with
  `MetaDataSystem.SetEntityName` and then immediately look it up by name (e.g. via
  `WantedStatusSystem`) get `"identity"`; either run a tick first or give the mob its ID card.
- **`DrippingCumComponent` has server-only runtime fields** (`Ready`, `LastPosition`) that are not
  `DataField`s and are not replicated; only `Amount` is networked. The drip is gated on movement or
  `NotifyActivity` (interactions), and pausing on a full tile never consumes stored semen - keep
  that invariant when touching `DrippingCumSystem.Update`.
- **Decal tiles are `floor(coordinates + 0.5)`.** `Decal.Coordinates` is the decal's bottom-left
  corner; `DecalSystem.GetDecalsInRange(grid, position, range)` compares `position` against
  `coordinates + (0.5, 0.5)`, so query at the mob's position and treat `+0.5` as the decal center.
- **`JobTrackingComponent` has an `Access` attribute** (`SharedJobTrackingSystem`, `SharedGameTicker`);
  foreign *writes* fail the build with `RA0002` (tests cannot set `Job`/`Active`). Read the job from
  the character's general station record (`WantedStatusSystem.TryGetJob`) instead - the ID console
  keeps `JobPrototype` in sync.

## 23. Spawn-time component changes and the full-suite tests (2026-10)

The fork's CI (`build-test-debug.yml`) runs the **entire** `Content.IntegrationTests` suite with
`NUnit.MapWarningTo=Failed`; the local pipeline in the skill only runs `_PS|EntityTest|CharacterCreationTest`,
so these fail only in CI:

- **`PrototypeSaveTest.UninitializedSaveTest`** spawns every map-savable prototype on an
  uninitialized map and fails if `ComponentInit` changed any serialized component data. The codebase
  convention: **declare what init creates in the prototype** (e.g. a `ContainerContainer` entry for
  `ToggleableClothing`/`Storage`, a `SolutionContainerManager` for edible items), or move the
  data-modifying handler to `MapInitEvent` (which does not run for uninitialized maps). Container
  mappings **replace** parent mappings on inheritance - re-declare parent containers when overriding
  (`ClothingOuterHardsuitBase` must repeat `storagebase` from `ClothingArmorPlate`). Fixed families:
  hardsuit/EVA `storagebase`, wrapped lollipop `food` solution, `_CS` PDA blip cartridge
  (`BlipCartridgeSystem` now inits on `MapInit`).
- **`ItemSpriteTest.AllItemsHaveSpritesTest`** requires every non-abstract prototype with
  `ItemComponent` to have a sprite. Body-part entities that are never held should not inherit
  `BaseItem` (the `_PS` genital organs); real items need a sprite or a visual parent
  (`PalmGrenadeFlash` was missing `GrenadeFlash`).
- **`GuideEntryPrototypeTests.ValidatePrototypeContents`** parses every guidebook entry; the
  Chemicals page embeds every reagent/reaction and legitimately exceeds the client's 25k per-frame
  style update limit, so the test raises the `ui` sawmill level while it runs (the warning is a
  client perf signal, not a content error).
- **`FailAndStartPresetTest`** readies up; the Palmtree NSFW consent gate blocks `toggleready` until
  the prompt is answered, so the test answers it via `IClientPreferencesManager.UpdateNsfwConsent`
  first.
- **`RSI Validator` / `RGA schema validator`** (separate workflows) allow only the CC/CC0 set
  (RSI: no `Custom`; attributions: `Custom` is allowed if the copyright describes the license, and
  `source` may be `"NA"`). Run them locally:
  `python RobustToolbox/Schemas/validate_rsis.py Resources/` and yamale with
  `RobustToolbox/Schemas/rga.yml` + `rga_validators.py`.

## 24. Saving live grids: `mapInit: true` skips MapInit on load (2026-10-16)

- A grid saved while it is **live** gets `mapInit: true` on every entity (`EntitySerializer` writes
  it for `EntityLifeStage.MapInitialized`). On load, `EntityDeserializer` only bumps the life stage
  for those entities - **`MapInitEvent` does not fire for any of them** (see
  `EntityDeserializer.StartEntities/StartupEntity` and the `PostMapInit` handling). Official ship
  files do not carry the flag, so they load as pre-init and get MapInit exactly once.
- Consequence: anything normally registered/configured in MapInit is missing on a retrieved ship.
  Known cases: `DeviceNetworkSystem` never joins `DeviceNetworkComponent`s (buttons, logic, doors,
  cannons - all silently dead), `GunSignalControlSystem` never adds shuttle-cannon sink ports, and
  `SharedGunSystem.OnMapInit` never runs `RefreshModifiers`, so `GunComponent`'s modified values
  (fire rate, projectile speed, angles) stay at zero: **saved guns refuse to fire and emitter/
  A.P.E. bolts spawn with no velocity and freeze in place**. The bluespace dock compensates with
  `RestoreMapInitRegistration` in `BluespaceDockSystem` - extend that when a new
  MapInit-registered ship system shows up.
- **Never "fix" this by re-raising `MapInitEvent` on the loaded entities.** Live entities already
  had their spawn-time init; running it again re-fires every `StorageFill`/`ContainerFill` and
  duplicates (or spills) whatever players have in their containers. The bluespace dock test
  `BluespaceDockDeviceLinkTest` locks in both halves: device links work after retrieval, and an
  emptied-and-refilled toolbox keeps exactly its stored contents.
- Device network packets are processed **one tick after** being queued
  (`DeviceNetworkSystem.SwapQueues`), so tests (and any code that checks a signal's effect) must run
  a tick between `InvokePort`/`SendSignal` and the assertion.
- **Mid-flight projectiles are never saved and never left behind when a ship is stored, deleted or
  sold** (`BluespaceDockSystem.DeleteProjectiles`): a stored bolt would come back frozen, and a
  projectile left in the world keeps `EntityUid` references to its weapon/shooter, which errors
  during game state serialization once that entity is deleted. Files written before this rule are
  cleaned on load. The same applies to any future system that deletes a grid with guns on it.
- `SetTiles` with **diagonally adjacent** tiles splits the grid (`GridSplitEvent`): entities on the
  split-off part end up on a new grid without the original's components (deed, brand). Test setups
  must use orthogonally adjacent tiles.

## 25. Saving live grids: one unserializable field fails the whole ship (2026-10-18)

- Every `[DataField]` type needs a `[DataDefinition]`. A live grid save serializes **all** components
  on the grid, so a single field of a type without one throws
  `No data definition found for type X when writing` (logged as `Caught exception while serializing
  component X of entity Y`, then `Caught exception while trying to serialize grid ...`). Nothing is
  written (the file only appears after a successful serialization), the dock reports
  `bluespace-dock-error-save-failed`, and the ship cannot be stored until the offending item is
  removed. **When a ship "won't store", grep the server log for `No data definition found`.**
- Fixed offenders (2026-10-18):
  - `PlantAnalyzerComponent.PlantInfo` - the "last scanned plant" memory is runtime UI state (and
    holds a `NetEntity`), so it is deliberately **not** a `[DataField]` any more.
  - `DoAfterId` - **in-place core patch** (`Content.Shared/DoAfter/DoAfter.cs`, marked Palmtree): it
    is a `[DataDefinition]` now. Nine components (`ClimbingComponent`, `MagicMirrorComponent`,
    `GraveComponent`, `EnvelopeComponent`, `CanEscapeInventoryComponent`, ...) store one as a
    `[DataField]`, so any grid saved while someone was climbing/digging/scanning on it failed the
    same way. Keep this in mind when a new component stores a `DoAfterId`.
- A failed save is not side-effect free: `DeviceListSystem.OnMapSave` (and
  `NetworkConfiguratorSystem`/`FollowerSystem`) mutate the world in `BeforeSerializationEvent` and
  only `DeviceListSystem` mutates *permanently* - it prunes device-list entries that point at
  another map and never restores them, so a failed store can break a ship's device links. Anything
  else that hooks serialization and mutates state needs an `AfterSerializationEvent` counterpart.
- **Dead radar targets log `Can't resolve MetaDataComponent`**: `RadarConsoleComponent.TargetEntity`
  is a saved `EntityUid` that outlives the ship it points at (stored, sold, deleted). Every console
  state refresh (`ShuttleConsoleSystem.GetNavState`, `RadarConsoleSystem.UpdateState` - both run for
  all consoles when any ship docks) then calls `GetNetEntity` on a dead entity and logs the error.
  The dock clears targets when it deletes a ship and both systems drop dead targets before using
  them (`SharedRadarConsoleSystem.ClearTargetIfDead/ClearTargetsPointingAt`).
- `RestoreMapInitRegistration` (section 24) runs **per entity in a try/catch**: one entity that
  throws must not skip the registration of every entity after it. It also re-links every
  `ExtensionCableReceiverComponent` without a provider - a loaded machine whose APC link is missing
  shows "un-powered" until a player deconstructs and rebuilds it.

## 26. RCD/RPD port notes (2026-10-18)

- **The RPD deconstruct whitelist is opt-in per entity** (`RCDDeconstructableComponent.RpdDeconstructable`,
  YAML `rpd: true`). The RPD refuses to deconstruct tiles outright. In `RCDSystem` the two flags are
  independent gates: `rpd:` for the RPD, `deconstructable:` for the RCD (Coyote's original single
  check required both). The whole atmos family inherits `rpd: true` + `deconstructable: false` from
  `GasPipeBase` (pipes, pumps, valves, vents, injectors, radiators, mixers, filters, ports). Note
  the sub-floor caveat: plain pipes under a floor tile keep `SubFloorHide.BlockInteractions` (true),
  so they can only be clicked - built, wrenched or RPD'd - where they sit on exposed plating.
- **Mirror prototypes are split client/server state.** The ghost placer
  (`RCDConstructionGhostSystem`) picks the `MirrorPrototype` from its local `_useMirrorPrototype`
  flag, toggled while placing with the flip keybind (`EditorFlipObject`, default `F`); the spawn in
  `RCDSystem.FinalizeRCDOperation` uses `RCDComponent.UseMirrorPrototype`, which is synced by
  `RCDConstructionGhostFlipEvent` only when the RCD is in the sender's active hand. When a mode
  without a `mirrorPrototype:` is selected the fallback is the normal prototype, and the flip flag
  survives the switch (toggling again on a mirror mode re-applies it).
- **`InstantConstruction` skips the do-after delay, not the do-after**: it forces delay 0 and the
  `EffectRCDConstruct0` fx, but validity/charge checks still run every tick (and it zeroes the
  delay on deconstruct operations too).

### RPD second pass (pipe colours + pipe layers)

- **Every new `rpd: true` entity needs an `fx:`.** The deconstruct path in `RCDSystem.OnAfterInteract`
  does `effectPrototype = destructible.Effect`, and `RCDDeconstructableComponent.Effect` defaults to
  null — `Spawn(null, ...)` on an fx-less target. The `GasPipeBase` block already sets
  `fx: EffectRCDConstruct0`; the air alarm / air sensor / gas pipe sensor blocks added with the
  sensor RPD modes do too.
- **Which menu opens is decided by `ActivatableUI.key`, not by `UserInterface.interfaces`.** The
  `RPD` prototype adds its own `UserInterface` entry for `enum.RpdUiKey.Key` and re-points
  `ActivatableUI.key` off the RCD's `RcdUiKey`, otherwise the item keeps opening the RCD's radial.
  The inherited `RcdUiKey` mapping may still be merged into `interfaces` and is harmless —
  `ActivatableUI` is what picks the menu.
- **Pipe colours travel as a palette key, never as a tuple/`Color` payload.** Palette lives on the
  entity (`pipeColorPalette:` on the `RCD` component, networked so the menu can draw swatches),
  `RCDColorChangeMessage` carries the key, and the shared system resolves it. Applying the colour is
  two-sided on purpose: the appearance datum (`PipeColorVisuals.Color`) drives the sprite, while
  `RCDPipeColorAppliedEvent` → `AtmosPipeColorSystem.SetColor` updates `AtmosPipeColorComponent` for
  the atmos monitoring console and the spray painter. Appearance alone leaves those two out of sync.
- **`LastKnownEyeRotation` is server-only working state: do not network or `Dirty()` it.** The
  placement mode forwards the local eye rotation (not networked) so the server can resolve the
  layer. The field is `[ViewVariables]`-only, deliberately not an `[AutoNetworkedField]`: dirtying
  on every change would network the component each time the player turns, and no client code reads
  it (the client recomputes the layer in `AlignRPDAtmosPipeLayers`).
- **`MapGridComponent.TileSize` is a `ushort`, so `tileSize / 2` is integer division and equals 0.**
  `1 / 2 == 0`, so `CurrentTile.X + tileSize / 2` lands on the tile **corner**, not its centre. Every
  placer in the engine writes `float tileSize = mapGrid.TileSize; //convert from ushort to float`
  for exactly this reason (`AlignTileAny`, `AlignTileDense`, `AlignTileEmpty`, `AlignRCDConstruction`,
  `AlignAtmosPipeLayers`). Writing `var tileSize = ...` compiles fine, passes every static check, and
  then silently misplaces everything by half a tile at runtime — this shipped once and had to be
  found by playtesting. It bites twice in the RPD: the ghost snaps to the corner, *and*
  `RCDSystem.OnAfterInteract` measures the layer deadzone from the corner, so almost every click
  reads as "off centre" and the tool silently prefers the secondary/tertiary layer. Check any new
  `tileSize` with `grep -n "tileSize = mapGrid.TileSize"`.
- **Coyote's `- new Vector2(0.5f, 0.5f)` bias in the layer diff was a workaround for the above**, not
  a deliberate offset: with the integer-division bug the "centre" was the corner, and subtracting
  another half tile nudged it back toward the middle. With `float tileSize` the tile centre is
  genuinely `index + 0.5`, so the bias must stay **out**. The real requirement is that
  `AlignRPDAtmosPipeLayers` (raw cursor vs snapped centre) and `RCDSystem.OnAfterInteract` (with
  `args.ClickLocation`) compute the *same* centre; both use `tile.X + tileSize / 2` with a float
  `tileSize`.
- **`[Dependency]` works on a `PlacementMode` for managers only, never for a bare `EntitySystem`.**
  `PlacementMode` instances are built through `PlacementManager.BeginPlacing` →
  `CreateInstanceImpl`, and `IoCManager.InjectDependencies` throws `UnregisteredDependencyException`
  for anything the container does not know about. `Content.Client.Hands.Systems.HandsSystem` carries
  no `[RegisterDependency]`, so `[Dependency] readonly HandsSystem _hands` compiles and then crashes
  the client the moment the placer is constructed. Resolve systems off the entity manager instead —
  `_hands = _entityManager.System<HandsSystem>();` — as `AlignRCDConstruction` does. Managers
  (`IEntityManager`, `IPrototypeManager`, `IPlayerManager`, `IEyeManager`, `IStateManager`, ...) are
  fine as `[Dependency]`; a `Logger.GetSawmill(...)` static is the way to log from this class, since
  there is no `Log` property outside `EntitySystem`.
- **`noLayers: true` is for modes with no layer to pick.** Mode entities need an
  `AtmosPipeLayersComponent` whose `alternativePrototypes` actually vary; `GasPipeManifold` has
  `pipeLayersLocked: true`, and wallmounts/sensors have no component at all. The ghost system also
  gates the layered placer on `RcdMode.ConstructObject`, so deconstruct previews keep the plain
  placer.
- **Do not recreate the RPD placer every tick.** `AlignRPDAtmosPipeLayers` rewrites the placer's own
  `CurrentPermission.EntityType` to the layer prototype, so `RCDConstructionGhostSystem` tracks the
  *base* prototype it asked for (`_placerBaseProto`) — comparing against
  `CurrentPermission.EntityType` (Coyote's check) is always unequal and rebuilt the placer at
  60 Hz.

## 27. Absolute round-time cooldowns do not survive a round (2026-10-19)

- `UseDelayInfo.EndTime`, `GunComponent.NextFire` and `MeleeWeaponComponent.NextAttack` are
  `TimeSpan`s on the **round clock** (`IGameTiming.CurTime`), which restarts at zero every round. A
  live ship save kept those absolute values, so after a round restart every affected entity was "on
  cooldown" until the new round clock reached the old time: a ship stored 30 minutes into a round had
  unusable buttons, switches and toolbelts for the first 30 minutes of the next round (same-round
  store/retrieve looked fine, which is what made this look random).
- The symptom misleads: `InteractionActivate` (left click) and `UseInHandInteraction` (E) silently
  bail when `UseDelaySystem.IsDelayed`, while **right-click verbs bypass the check** - so a button
  could be toggled from its verb menu while normal clicks and E did nothing.
- `BluespaceDockSystem.TrySaveShipFile` calls `ResetRuntimeCooldowns` (on save *and* load): all
  `UseDelay` delays, `SharedGunSystem.ResetCooldown` for guns, melee `NextAttack`,
  **AME `LastUpdate`/`NextUpdate`/`EffectCooldown`** (`AmeControllerSystem.ResetTimers` - a retrieved
  AME used to sit idle until the new round clock reached the old time), thruster `NextFire`
  (`ThrusterSystem.ResetFireCooldown`), hydro tray `NextUpdate`/`LastCycle`, and power-transmission
  `NextDeposit`/`AccumulatedEnergy`. **Any new component that stores an absolute `TimeSpan`/`GameTick`
  and gates an action needs a reset there too.** Relative counters are fine
  (`DoorComponent.SecondsUntilStateChange` stores a relative number of seconds). Note `Dirty()` must
  not be called on non-networked components (the AME controller is one).
- Regression test: `BluespaceDockSaveTest.SavedShipForgetsActionCooldowns` (fails without the reset).

## 28. Emote categories and chat triggers (2026-10)

- **`EmoteCategory` is an `int` flag set now, not `ushort`.** All 16 old bits were taken and
  SPLURT's `Funny` category needed a new one. `General` is `int.MaxValue`; `Funny` is
  `Vocal | (1 << 16)` (not just `1 << 16`, or mutes/mumbling stop applying). Keep new categories
  as flags and update `EmotesUIController.EmoteGroupingInfo` **and**
  `EmoteCategoryWindow.CategoryTooltips` or the wheel throws on a missing dictionary key.
- Category persistence is by enum **name** (`ServerDbBase` joins/splits `HiddenEmoteCategories`),
  so widening the backing type does not need a DB migration. `Sex`/`Vocal`/`Invalid` are stripped
  on save and forced visible.
- **`*trigger` is just `EmotePrototype.ChatTriggers`.** ChatSystem strips the `*`, lowercases and
  trims punctuation before the dictionary lookup. A trigger must be unique across every emote
  prototype (duplicates only log a warning and the first emote wins), and a prototype with no
  trigger cannot be used from chat even if it is in the wheel.
- Funny emote sounds are inherited: `SupplementalFunnyEmotes` (abstract, `_PS`) is the parent of
  `SupplementalCoyoteEmoteStuff`, which is the `VocalComponent` default. Adding an emote to the
  wheel without adding it to that `sounds` dictionary still works for chat but is silent.
- FTL chat messages can use Fluent pronoun helpers (`{POSS-ADJ($entity)}`, `{SUBJECT($entity)}`);
  plain HTML tags and `[user.p_their()]`-style DM macros are not valid in SS14 locale strings.
- **Abstract prototypes are not instantiated and cannot be `Index<T>()`-ed.** Use them only as
  `parent:` data (`SupplementalFunnyEmotes`); a `ProtoId` field/default that points at an abstract
  prototype fails at runtime with `UnknownPrototypeException` even though the linter accepts it.
  `SupplementalFunnyMale`/`Female` are therefore concrete `emoteSounds` prototypes.
- The chat-trigger path (`*trigger`) is separate from `TryEmoteWithChat`: without special handling
  it sends the raw trigger text, not `ChatMessages`. `SendEntityEmote` now replaces the action with
  the localized message for `EmoteCategory.Funny` only.
- `emotelist` runs from the server console with no attached entity; pass a string as the Fluent
  `entity` argument (neuter fallback) instead of `EntityUid.Invalid`, which logs
  `MetaDataComponent` KeyNotFound errors for pronoun functions.

## 28. Saving live grids: runtime-only fields are the main failure source (2026-10-22)

- The same rule as §25 keeps surfacing: every `[DataField]` must be writable. Two shapes that look
  harmless but are not:
  - **Dictionary keys that are mappings.** `SmartFridgeComponent.ContainedEntries` is a
    `Dictionary<SmartFridgeEntry, HashSet<NetEntity>>`; `SmartFridgeEntry` is a `[DataRecord]`
    struct, so the key serializes as a *mapping*, and a dictionary key must be a value node. The
    save threw `Caught exception while serializing component SmartFridge`; it only failed once the
    fridge was used (empty dictionary = no keys). Dictionary keys must be scalars (`string`, `int`,
    `ProtoId`, ...).
    - **`[DataField] EntityUid?` holding the map.** `StoreComponent.StartingMap` ("the map the store was
    originally from, used to block refunds if the map is changed") is a direct reference to the map
    entity. Saving a *grid* truncates the map (it is the grid's parent), and a reference to it makes
    the component serialization fail: every PDA (they all carry a Store) failed every strict save.
    Clear such fields before serializing and re-derive them on load (`ClearRuntimeMapReferences`).
  - **Session-scoped refs in persisted collections** (`NetEntity` sets/maps): even when they
    serialize, they describe a round. Prefer `[ViewVariables]`/`[AutoNetworkedField]` plus a rebuild
    from real entity state (`SmartFridgeComponent.ContainedEntries` is rebuilt from the container).
- **A save must never be the only copy of the store decision.** `TrySaveShipFile` falls back to
  `EntityExceptionBehaviour.IgnoreEntityAndChildren` and records what it skipped in the file's
  `droppedEntities` section; loading spawns fresh copies (`TrySpawnDroppedEntities`). Container
  contents are re-inserted into their original container (owner prototype + position + container id
  are recorded); nested containers are not covered. When anchoring a re-created entity, check
  `xform.Anchored` first - prototypes with an anchored Transform are already anchored on spawn and a
  second `AnchorEntity` trips the engine's snap-grid assert. The strict attempt still logs an ERRO
  (the engine does), so a "ship stored with skipped entities" shows up in the log as
  `entity_serializer: Caught exception` followed by
  `bluespace_dock: ... falling back to a save that skips them`.
- **The store strips before it knows the file is good.** Any new failure point after
  `StripStationReferences`/`UndockShuttle` must go through `RestoreStoreFailure` (station refs, deed,
  docking to the *grid it was docked to*). A stripped ship has no station member/bound components, so
  its station-bound machinery and power look "soft-broken" until it is restored.
