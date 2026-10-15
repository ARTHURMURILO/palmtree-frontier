# CONVENTIONS

> Observed patterns, plus explicit inconsistencies. Follow the surrounding code; when in doubt,
> prefer the pattern used by the module you are editing.

## Code organization

- One system per feature area; shared logic in `Content.Shared` as `SharedXSystem`, side-specific
  subclasses in `Content.Server`/`Content.Client` (`ServerXSystem`, `ClientXSystem`).
- Components are data-only: `[RegisterComponent]`, `[DataField]`, `[AutoGenerateComponentState]` +
  `[AutoNetworkedField]` for replicated state.
- Dependencies via `[Dependency] private readonly X _x = default!;`; managers via IoC.
- Subscriptions in `Initialize()` only (subscription lock); use `Subs.CVar/Subs.Local/...` helpers
  for auto-unsubscribing handlers.
- Events are records/classes in shared; directed vs broadcast matters (see `.ai/EVENTS.md`).
- Prefer `ProtoId<T>`/`EntProtoId` over raw strings for prototype references.

## Palmtree-specific conventions

- New Palmtree code goes under `_PS` in the matching project. Ported fork code keeps its prefix.
- **Asset placement law:** any asset the fork adds — audio, textures/`.rsi`, UI art, fonts — goes
  under the matching `_PS` resource folder (`Resources/Audio/_PS/`, `Resources/Textures/_PS/`,
  `Resources/Locale/en-US/_PS/`, ...). Only content ported from another source keeps that source's
  prefix (`_CS` for Coyote/coyote-bayou content, `_NF` for Frontier, `_DV`, `_EE`, `_White`,
  `_Starlight`, ...). Never drop new files into upstream folders (`Resources/Audio/Weapons/...`,
  `Resources/Textures/Objects/...`, etc.); repoint the reference (`path:`, `insertSound:`,
  `soundGunshot:`, sprite `sprite:`) at the `_PS` copy instead. Move existing files with `git mv`
  so history is preserved.
- Core-file patches are minimal, annotated with a comment (`// Palmtree: ...`) and should not
  reorder existing code unless the feature requires it.
- Marking prototypes are data; marking behavior changes go through `MarkingManager`/renderer, never
  through per-prototype C#.
- ERP content is explicit and adult, but code should stay neutral: there is no per-toggle consent
  system (only the account NSFW age gate), and
  `ModifyUndies` is gated by the per-marking `canToggleVisible`/`otherCanToggleVisible` opt-ins.

## Prototype conventions

- IDs are PascalCase; fork prefixes (`_NF`, `_CS`, `_PS`) are folder-level, not ID-level.
- Marking locale keys: `marking-<id>` and `marking-<id>-<state>`.
- Loadout locale keys: `loadout-name-<id>` when used; group names `loadout-group-*`.
- Fork marking files commonly use `kindAllowance` + `speciesRestriction` together; copy the pattern
  rather than inventing new restrictions.
- `hidden: true` keeps helper markings out of the editor list; `altSprites` marks leg variants.
- Comments in ported files sometimes carry provenance (`# Coyote:`, `# Palmtree/Floof`); keep them.
- Weapon prototypes carry a `suffix:` naming their source: fork-made guns use `Palmtree`, ports use
  `Coyote Bayou` (BYOND `coyote-bayou` Fallout set), `Coyote Frontier` (SS14 `COYOTE` additions),
  or `Sandstorm`; upstream Frontier guns keep the inherited `Frontier`. When porting a weapon, set
  the suffix in the same commit.

## Observed inconsistencies / quirks

- Prefix casing varies: `_Starlight` (prototypes) vs `_StarLight` (some prototype dirs) vs
  `_Starlight` (locale); `_DV` vs `DeltaV` texture paths; `_EE` vs `_EinsteinEngines`.
- Some marking files live under core `Resources/Prototypes/Entities/Mobs/Customization/Markings/`
  even though they are Floof/Coyote content (`genitals.yml`, `undergarments.yml`).
- The engine uses namespace collisions for partial classes (`GameTicker`); grep partials before
  adding members.
- `Content.Shared/Humanoid/Markings/MarkingManager.cs` and `MarkingsSet.cs` both contain
  species-filtering logic; keep them in sync (kind-aware check).
- `Resources/manifest.yml` metadata and `identifier.sqlite` are stale artifacts.

## Commits and changelogs

- Player-visible changes (content, balance, UI, fixes) carry a `:cl:` block at the end of the commit
  message: `:cl:` on its own line, then one `- add|remove|tweak|fix: message` line per change.
- The commit-changelog workflow appends those entries to `Resources/Changelog/Palmtree.yml`; do not
  write entries into the upstream `Changelog.yml`/`Frontier.yml`/`Maps.yml`/`Admin.yml` files.
- Internal refactors, documentation and CI changes do not need a changelog entry.
- Commit subjects are short and imperative; rationale and porting notes go in the body.

## Formatting/tooling

- C# formatting is enforced by `.editorconfig`; nullable warnings are errors in some projects.
- YAML is 2-space indented; prototype files are lists of documents (`- type: ...`).
- Fluent keys are lowercase/kebab; duplicate keys across all locale files are load errors.
- Tests: NUnit; unit tests in `Content.Tests`, pooled server+client integration tests in
  `Content.IntegrationTests`.
