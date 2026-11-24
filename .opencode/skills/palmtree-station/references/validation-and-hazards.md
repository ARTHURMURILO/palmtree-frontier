# Validation & hazards reference

Full hazard list: `.ai/HAZARDS.md`. This file is the operational summary.

## Validation pipeline

```bash
# 1. Build everything (catches C# errors, including tests)
dotnet build SpaceStation14.sln

# 2. Rebuild the linter AFTER any C# change, then lint.
#    --no-build alone reuses stale Content.Shared.dll copies and reports bogus enum errors.
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
dotnet run --project Content.YAMLLinter -c Debug --no-build   # expect "No errors found"

# 3. Unit tests
dotnet test Content.Tests/Content.Tests.csproj

# 4. Integration tests (targeted; EntityTest spawns every entity)
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~EntityTest|FullyQualifiedName~CharacterCreationTest|FullyQualifiedName~_PS"

# 5. Client sandbox typecheck without a display
dotnet run --project Content.Client -c Debug --no-build -- --headless
# expect Content.Shared/Content.Client "Verified IL"; then an OpenGL error is expected (exit 134)
```

Integration tests are slow (each pooled server+client pair ~20–90 s); run targeted filters rather
than the whole suite when iterating.

## Hazards

### Sandbox (client)
`Content.Shared`/`Content.Client` assemblies are IL-verified against
`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml`. `string.Create(IFormatProvider, ...)` is
blocked and crashes the client at startup (`Assembly Content.Shared failed type checks`). Use
`float.ToString(CultureInfo.InvariantCulture)` (Single and CultureInfo are whitelisted). Verify with
the headless run.

### Stale YAMLLinter
`dotnet run --project Content.YAMLLinter --no-build` executes the previously built output, including
its own copy of `Content.Shared.dll`. New enum values/prototype fields appear "not found" until the
linter is rebuilt.

### Marking filters
Use `MarkingManager.IsAllowedBySpeciesOrKindAllowance` everywhere; direct species checks strip
kind-shared markings. `MarkingsSet.EnsureSpecies` and `MarkingManager` both contain filter logic.
All species-restricted markings must carry `kindAllowance`
(`[BasicHumanlike, BasicFurry, BasicRobot, VoxLike]`).

### `EnsureDefault`
`MarkingsSet.EnsureDefault` must use `while (points.Points > 0 && index < points.DefaultMarkings.Count)`
and skip categories that already have markings. `||` overruns `DefaultMarkings` and crashes spawning
(Rodentia uses 999 points with one default).

### Profile load / glow
`LoadProfile` must use `AddMarking(uid, Marking marking, colors, ...)`; the `(string, colors)`
overload creates a fresh marking and drops scale/offset/glow (editor preview shows values, in-game
they reset).

### Prediction and client-only effects
Shared attack/action hooks (`AttemptAttack` and similar) run on the client for prediction, can
replay after a server state, and run on the server. Spawn vfx from a client override guarded by
`Timing.IsFirstTimePredicted`; relay only to others with `Filter.PvsExcept(user)`; ignore the relay
for `IPlayerManager.LocalEntity`; and dedupe by user + `IGameTiming.RealTime` because a delayed
event can arrive after the local effect ended. Hook once after the swing loop, not inside it.
Local effect entities need `FrameUpdate` + clamped `RealTime` and a `TimedDespawnComponent` fallback.
Symptom: "out and back, then out and back" or several ropes at once. See `.ai/HAZARDS.md` §30.

### Body speed
`SharedBodySystem.UpdateMovementSpeed` averages `MovementBodyPart` values from leg entities and calls
`MovementSpeedModifierSystem.ChangeBaseSpeed`. Set speed on the **leg parts**; a mob-level
`MovementSpeedModifier` alone is overwritten at spawn. Very high speeds destabilize client prediction
(keep species speeds moderate).

### In-place core patches (keep annotated, keep minimal)
| Patch | File |
|---|---|
| Concealable `ClothingBackpack` component | `Resources/Prototypes/Entities/Clothing/Back/backpacks.yml` |
| Lobby rotation (30s) | `Content.Server/GameTicking/GameTicker.LobbyBackground.cs` |
| Lobby crossfade | `Content.Client/Lobby/LobbyState.cs`, `LobbyGui.xaml(.cs)` |
| Synth respiration skip | `Content.Server/Body/Systems/RespiratorSystem.cs` (+ `SynthComponent`) |
| Mass-based pull slowdown | `Content.Shared/Movement/Pulling/Systems/PullingSystem.cs` |
| Leg displacement in clothing | `Content.Client/Clothing/ClientClothingSystem.cs` |
| Species/marking layers, kinds, altSprites | species YAML + `Entities/Mobs/Species/base.yml` |

### Content authoring
- Unknown prototype fields are **lint errors**; unknown fields in exported character YAML are ignored.
- Duplicate Fluent keys in a culture are load errors; check before copying locale.
- Fluent (`.ftl`) has **no inline comments**: anything after a value, including `# ...`, becomes part
  of the translated string. Only standalone `#` lines are comments; never annotate a value line.
- `ProtoId` fields (`altSprites`, loadout items, effects) are validated by the linter.
- Missing RSI states can pass lint and fail at runtime; copy complete `.rsi` directories.
- `EntityTest` catches body/prototype spawn errors; run it after species/content changes.
- Locale keys for marking categories: `markings-category-<Category>` (all `MarkingCategories` values
  must have one; `Base*` and `TailExtras` were once missing).
- Engine renames to watch when porting: `SoundOnTrigger`→`EmitSoundOnTrigger`,
  `OnUseTimerTrigger`→`TimerTrigger`, `noSpawn`→`categories: [ HideSpawnMenu ]`.

### Process
- Do not commit unless asked; leave changes for the maintainer's in-game test.
- Stage specific paths rather than `git add -A`; the tree can contain untracked artifacts.
- Update `.ai/` in the same change when architecture/behavior shifts.
