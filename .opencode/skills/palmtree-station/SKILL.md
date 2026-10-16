---
name: Palmtree Station (Frontier fork)
description: Work on the Palmtree Station Frontier fork — porting Coyote/Floof and SS13 (BYOND) content, the marking/ERP systems, species (Synth, Rodentia, Anthromorph, Tajaran), loadouts, Hilbert's Hotel, and the build/lint/test/typecheck pipeline. Use for any change in this Space Station 14 repository.
---

# Palmtree Station development

This repository is a **Frontier Station** (New Frontier) fork with Palmtree-specific content and a
targeted port of Coyote/Floof ERP + marking systems. The old base lives in a separate checkout
(the `COYOTE` checkout — see `.ai/file_paths.md`) and is the source for ported content.

## Ground rules

1. **Never modify `RobustToolbox/`** (engine submodule). Build on engine APIs.
2. **New Palmtree code goes under `_PS`** (`Content.Server/_PS`, `Content.Shared/_PS`,
   `Content.Client/_PS`, `Resources/Prototypes/_PS`, ...). Ported fork code keeps its original prefix
   (`_Floof`, `_CS`, `_DEN`, `_EE`, `_White`, `_Starlight`, `_DV`).
   **Assets follow the same law:** new audio/textures/UI go under `Resources/Audio/_PS`,
   `Resources/Textures/_PS`, etc.; ported content keeps its source prefix (`_CS`, `_NF`, ...).
   Never add new files to upstream resource folders — repoint references instead.
3. **Do not commit unless the user asks.** The maintainer tests in-game first and often wants changes
   left in the working tree.
4. `.ai/` is the repository's memory. Read it before large changes and update it in the same change
   when architecture shifts.
5. Preserve upstream/fork patch markers (`// Frontier`, `// Coyote`, `// Palmtree`, `// Floof`) and
   keep core-file patches minimal.
6. **Changelogs**: player-visible changes carry a `:cl:` block in the commit message
   (`:cl:` on its own line, then `- add|remove|tweak|fix: message` lines). The commit workflow
   appends it to `Resources/Changelog/Palmtree.yml`; never write entries into the upstream changelog
   files. Details: `.ai/guides/changelogs.md`.
7. **Never await CI/workflow runs after a push.** Once `git push` succeeds, report it and stop.
   Do not poll or watch GitHub Actions (`gh run list`, `gh run watch`, `gh api .../actions/runs`,
   waiting for the changelog bot, etc.) — the changelog append and CI are asynchronous and are the
   maintainer's to watch. Checking the workflow status is not part of any task unless the maintainer
   explicitly asks for it.

## Start here

1. `git status` + `git branch --show-current`. Work happens on `master` (the `ps-erp` port branch was
   merged and deleted).
2. Read `.ai/README.md` (navigation) and the relevant `.ai/systems/*.md` or `.ai/guides/*.md`.
3. For a port task, read `references/porting-playbook.md` in this skill.
4. For SS13/BYOND content or `.dmm` map ports, read `references/porting-from-ss13.md` and
   `.ai/guides/porting-from-ss13.md`.
5. For marking/ERP/species work, read `references/marking-and-erp.md` and
   `references/species-and-loadouts.md`.

## Workflows

### Porting content from the Coyote checkout (`COYOTE`)

- Find the source by prototype id / symbol, classify every field against this repo, adapt renames,
  copy prototypes + `.rsi` + audio + locale, then wire groups/layers.
- Full method, adaptation table and pitfalls: `references/porting-playbook.md`.
- Useful scripts: `scripts/check_character_refs.py`, `scripts/copy_referenced_assets.py`.

### Porting SS13 (BYOND) content and `.dmm` maps

- Sources: `.../BYOND/S.P.L.U.R.T-tg` (newest, condo Hilbert's Hotel), `.../BYOND/S.P.L.U.R.T-Station-13`
  (old), `.../BYOND/Sandstorm-Station-13`. Fork additions live in `modular_*` folders.
- Convert rooms with `Tools/convert_dmm_room.py <in.dmm> <out.yml> --name "..."`; it emits SS14
  format-7 maps with air, gravity, anchoring and self-powered machines.
- Translation table, map-format details, sprite caveats and hazards:
  `references/porting-from-ss13.md` (full: `.ai/guides/porting-from-ss13.md`).
- Worked example: Hilbert's Hotel (`_PS/HilbertHotel`, `.ai/systems/hilbert-hotel.md`).

### Markings, ERP, appearance

- The marking system is Floof/Coyote-derived and lives in `Content.Shared/Humanoid/Markings`,
  `Content.Shared/Humanoid`, `Content.Client/Humanoid`, with verbs in `Content.Server/_Floof`.
- Key features: `kindAllowance` sharing, `layering`/`colorLinks`, `altSprites` (digitigrade),
  per-marking scale/offset/glow, genital markings, `ModifyUndies` (per-marking toggle opt-in), leg displacement.
- Details and hazards: `references/marking-and-erp.md`.

### Species

- Species roster and the **Synth** IPC replacement: `references/species-and-loadouts.md`.
- When adding/porting a species follow `.ai/guides/adding-species.md`; remember `kind`, all marking
  layers, sex-morph base sprites and `altSprites`.

### Loadouts

- Loadout + item + group wiring + locale: `.ai/guides/adding-loadouts.md`.
- A loadout is unreachable until it is listed in a `loadoutGroup`.

## Validation pipeline (always run after changes)

```bash
dotnet build SpaceStation14.sln
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj   # REQUIRED after C# changes
dotnet run --project Content.YAMLLinter -c Debug --no-build # expect "No errors found"
dotnet test Content.Tests/Content.Tests.csproj              # unit tests
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~EntityTest|FullyQualifiedName~CharacterCreationTest|FullyQualifiedName~_PS"
# client sandbox typecheck (no display needed):
dotnet run --project Content.Client -c Debug --no-build -- --headless
# expect: Content.Shared / Content.Client "Verified IL", then an expected OpenGL error (exit 134)
```

`references/validation-and-hazards.md` explains each gate and the traps.

## Top hazards (read the full list before editing core files)

- **Client sandbox**: `Content.Shared`/`Content.Client` are IL-verified. `string.Create(IFormatProvider, ...)`
  is blocked; use `float.ToString(CultureInfo.InvariantCulture)`. Verify with the headless run.
- **Stale YAMLLinter**: `--no-build` uses the previously built output, including a copied
  `Content.Shared.dll`. Rebuild the linter after C# changes or you get bogus enum errors.
- **kindAllowance**: every species-restricted marking must carry
  `kindAllowance: [BasicHumanlike, BasicFurry, BasicRobot, VoxLike]`, and every marking filter must
  use `MarkingManager.IsAllowedBySpeciesOrKindAllowance` (including `MarkingsSet.EnsureSpecies`).
- **Profile load**: use the `Marking`-preserving `AddMarking(uid, Marking marking, colors, ...)`
  overload; the `(string, colors)` overload drops scale/offset/glow.
- **`MarkingsSet.EnsureDefault`** must loop with `&&` (not `||`) and skip categories that already
  have markings.
- **Body speed** comes from leg `MovementBodyPart` values, not only the mob's
  `MovementSpeedModifier`.
- **Locale duplicates** are load errors; unknown prototype fields are lint errors. Fluent (`.ftl`)
  has no inline comments — text after a value (even `# ...`) becomes part of the translation.
- **Server popups**: `SharedPopupSystem.PopupClient` is a no-op on the server (it exists for client
  prediction). Server code must use `PopupEntity(message, entity, recipient)` or popups silently
  never appear.
- **Runtime-loaded maps**: never pause a map with a player body on it (`EntityPaused` freezes the
  body, e.g. after admin ghosting). Generated rooms need `Gravity: enabled/inherent` (defaults to
  false), `needsPower: false` machines, `anchored: true` props and a serialized `GridAtmosphere`.
  See `references/porting-from-ss13.md`.
- **In-place core patches** exist for: base `ClothingBackpack` concealment, lobby rotation/crossfade,
  respiration skip (`SynthComponent`), mass-based pull slowdown, leg displacement in
  `ClientClothingSystem`, and species/marking YAML.

## References

- `.ai/README.md` — full documentation map
- `.ai/systems/marking-and-appearance.md` — marking system deep dive
- `.ai/systems/ps-systems.md` — lobby, concealable clothing, weapons, Synth
- `.ai/systems/consent-and-erp.md` — what ERP exists and what does not (no consent system)
- `.ai/PORTING.md` — what was ported, what was excluded, and why
- `.ai/HAZARDS.md`, `.ai/UNKNOWN.md` — traps and open questions
- This skill: `references/porting-playbook.md`, `references/porting-from-ss13.md`,
  `references/marking-and-erp.md`, `references/species-and-loadouts.md`,
  `references/validation-and-hazards.md`
