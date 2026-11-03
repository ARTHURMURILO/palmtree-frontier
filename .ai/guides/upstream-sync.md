# Guide: Upstream synchronization (Frontier)

> How to merge new upstream content into Palmtree Station without losing fork-specific work.
> This is the repository's memory for upstream syncs. Read it before every merge and update it
> afterwards.

## Remotes and architecture

- `origin` = `xTheLifex/palmtree-frontier` (this fork).
- `upstream` = `new-frontiers-14/frontier-station-14` (**Frontier Station**).
- Wizden (`space-wizards/space-station-14`) is **not** merged directly. Frontier is the
  integration layer: it performs the Wizden conflict resolution and testing, and this fork merges
  Frontier's merge commits. Pulling an unmerged Wizden branch means redoing Frontier's work.
- `RobustToolbox/` is a submodule and must not be modified. At the 2026-10-23 sync both this fork
  and Frontier point at `feb9e1db69ab97acfd07580918f5d20ffaa6b770` (`v267.3.0`).
- The fork chain is Wizden → Frontier → Palmtree. New content from Wizden arrives inside
  Frontier's periodic "upstream merge" PRs (for example Frontier PR #4749, September–October 2025).

## Current sync state

| Date | Frontier tip merged | Merge commit | Notes |
|---|---|---|---|
| 2026-10-23 | `b40510417e` (2026-10-21, includes PR #4749) | `1352fe0805` | First sync after the admin-camera/overlay-cache work; see notes below. |

Previous upstream merge point: `c9b415e52b` (Frontier `#4657`, 2026-10-12). The fork's own work is
committed on top of that; never reset or rebase away local `_PS` history to "catch up".

## Procedure

1. **Protect the working tree.** Check `git status`. If the maintainer has uncommitted work, use a
   dedicated worktree (as an OpenCode worktree or `git worktree add`) rather than merging in place.
2. **Fetch and pin the upstream tip.**
   ```bash
   git fetch upstream master
   git rev-parse upstream/master
   ```
   Record the hash in the sync log at the bottom of this file.
3. **Create a sync branch from `master`.**
   ```bash
   git switch -c upstream-sync-YYYY-MM
   git submodule update --init --recursive
   ```
4. **Merge without committing.**
   ```bash
   git merge --no-commit --no-ff upstream/master
   ```
5. **List conflicts.**
   ```bash
   git diff --name-only --diff-filter=U
   ```
   In October 2026 this was only 8 files out of ~700. Most conflicts are type/API changes, not
   whole-file choices.
6. **Resolve with the fork bias described below.** Keep both sides when possible: upstream's new
   structure plus Palmtree's behaviour, not "ours" wholesale.
7. **Audit for silent losses** (auto-merge can keep a file but drop a patch):
   ```bash
   # Any Palmtree/Coyote marker that disappeared?
   git grep -l "Palmtree" HEAD -- Content.Client Content.Server Content.Shared Resources \
     | sed 's/^HEAD://' \
     | while read -r f; do
         [ -f "$f" ] && grep -q "Palmtree" "$f" || echo "LOST/GONE $f";
       done

   # Same for Coyote.
   git grep -l "Coyote" HEAD -- Content.Client Content.Server Content.Shared Resources \
     | sed 's/^HEAD://' \
     | while read -r f; do
         [ -f "$f" ] && grep -q "Coyote" "$f" || echo "LOST/GONE $f";
       done

   # Marking sharing policy.
   echo -n "HEAD files: ";   git grep -l kindAllowance HEAD -- Resources/Prototypes | wc -l
   echo -n "merged files: "; grep -rl kindAllowance Resources/Prototypes | wc -l
   ```
   Also check `git diff --name-status HEAD --diff-filter=D` for deleted files that the fork had
   patched, and `git grep -n '^<<<<<<<'` for leftover markers.
8. **Validate** (all commands from the repository root):
   ```bash
   dotnet build SpaceStation14.sln
   dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
   dotnet run --project Content.YAMLLinter -c Debug --no-build
   dotnet run --project Content.Client -c Debug --no-build -- --headless
   dotnet test Content.Tests/Content.Tests.csproj
   dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
     --filter "FullyQualifiedName~EntityTest|FullyQualifiedName~CharacterCreationTest|FullyQualifiedName~_PS"
   ```
   For a large upstream merge, also run the full `Content.IntegrationTests` suite in the
   background. The client headless run must print `Content.Shared: Verified IL` and
   `Content.Client: Verified IL`, then exits with the expected OpenGL error (134).
9. **Commit the merge** with a `:cl:` block because an upstream sync is player-visible. Include the
   upstream tip hash and the PR(s) it contains in the commit body.
10. **Document and commit** the sync log and any new decisions in this file.
11. **Fast-forward `master`** from the sync branch if the maintainer wants the update live. Preserve
   any unrelated uncommitted work in the main worktree. Push only when asked; after a push do not
   poll CI or the changelog workflow.

## Conflict-resolution bias

1. **Palmtree-specific features win over upstream behaviour.** This includes `_PS` systems,
   consent/NSFW gating, marking/ERP extensions, Synth, Hilbert's Hotel, Bluespace Dock, the
   interaction panel, custom species and loadout additions.
2. **Keep upstream's new structure and APIs** when it is only a refactor. Add the Palmtree
   behaviour back on top instead of reverting the file to the old shape.
3. **Preserve every in-place patch marker**: `// Palmtree`, `// Coyote`, `// Floof`, `// Frontier`,
   `// _CS`, etc. Keep patches minimal and annotated.
4. **Fork prefixes stay intact.** Never delete `_PS`, `_Floof`, `_CS`, `_DEN`, `_EE`, `_DV`,
   `_White`, `_Starlight` content or assets just because upstream removed a similar file.
5. **Default to upstream for unrelated content changes** (price fixes, upstream species sprites,
   upstream antag changes) unless the fork deliberately differs. For example, upstream commented
   out `RubberStampRd` in the RD locker during this sync; take upstream's removal, but keep the
   Palmtree RD beret entry.
6. **New upstream markings need Palmtree's sharing policy.** Any new `- type: marking` with a
   `speciesRestriction` must also get:
   ```yaml
   kindAllowance: [BasicHumanlike, BasicFurry, BasicRobot, VoxLike] # Palmtree: allow all markings
   ```
   This is required for `MarkingManager.IsAllowedBySpeciesOrKindAllowance` to share markings across
   species kinds. Existing markings keep their `kindAllowance`; audit the count after merging.
7. **Do not lose the render-target cache or admin camera fix.**
   `Content.Client/Graphics/OverlayResourceCache.cs` and the per-viewport caches in
   `Content.Client/Light/*`, `Content.Client/Overlays/StencilOverlay*`,
   `Content.Client/Silicons/StationAi/StationAiOverlay.cs`, and
   `Content.Client/Atmos/Overlays/GasTileDangerousTemperatureOverlay.cs` must survive future
   merges. Upstream PR #40181 introduced the same cache; our fork additionally applies it to the
   thermal-sight overlay. If a future merge conflicts, keep the per-viewport cache, never the old
   single-target fields.
8. **Radial-menu API changes.** Upstream replaced the old RCD/RPD radial controls with
   `RadialMenuButton`/`RadialMenuButtonWithSector` and `SimpleRadialMenu`. The fork's `RCDMenu` (used
   by `RPDMenu`) must use the new classes; `RadialMenuTextureButton` and
   `RadialMenuTextureButtonWithSector` no longer exist.
9. **Database migrations**: the fork has custom `genitals`, `NsfwConsent`, height/width, leg-style
   and emote-category migrations. If upstream adds migrations, check both `Migrations/Sqlite` and
   `Migrations/Postgres` for timestamp collisions and keep both providers in sync.
10. **Changelogs**: keep `Resources/Changelog/Palmtree.yml` for fork entries. Frontier/Wizden
    histories are upstream-owned; do not reintroduce a deleted Wizden changelog file.

## High-risk files and areas

These are the places where a merge is most likely to need manual judgment:

- `Content.Shared/Humanoid/**`, `Content.Client/Humanoid/**`, markings prototypes/locale/sprites.
- `Content.Shared/Clothing/**`, `Content.Client/Clothing/ClientClothingSystem.cs`,
  clothing prototype `HideLayerClothing`/displacement maps.
- Species prototypes (`Resources/Prototypes/Species`, `Entities/Mobs/Species`, `_DV`, `_NF`, `_CS`,
  `_PS`).
- Loadouts (`Resources/Prototypes/Loadouts`, `_NF/Loadouts`, role loadouts) and profile/DB fields.
- `Content.Server/GameTicking/**` (lobby rotation, ready command, consent gate).
- `Content.Shared/Movement/Pulling/**`, `Content.Server/Body/Systems/RespiratorSystem.cs`,
  `Content.Server/Emp/EmpSystem.cs`, `Content.Server/Ghost/GhostSystem.cs` (fork patches).
- `Content.Shared/Prying/**` (upstream switched pry times to `TimeSpan`; fork's `FlatPryTime`
  remains a float and is converted at the call site).
- RCD/RPD (`Content.Client/RCD/**`, `Content.Shared/RCD/**`, `_PS` RPD prototypes).
- Shuttle radar/proximity alerts and `_NF` shuttle systems.
- Database model/migrations and profile serialization.
- `Resources/Changelog/*` and `.github` workflows.

## Sync notes: 2026-10-23 (Frontier #4749 + follow-ups)

Upstream tip `b40510417e` contains Frontier PR #4749 ("September 1st to October 1st Upstream
Merge", 263 commits / ~700 files) plus the post-merge Frontier fixes for Clugg, the ninja sword,
the psychologist bed, shuttle wall/window, the grounding rod and screen timer electronics.

Conflict decisions made in this sync:

- `ToggleReadyCommand`: adopted upstream's `LocalizedEntityCommands` shape and error messages, and
  kept Palmtree's `NsfwConsentState.Unanswered` lobby gate.
- `JobRequirementLoadoutEffect`: adopted upstream's `GameRoleLoadoutTimers` CVar and kept the
  Palmtree admin playtime bypass.
- `PryingSystem`: adopted upstream's `TimeSpan`-based `GetPryTimeModifierEvent`, kept Palmtree's
  `FlatPryTime` (converted with `TimeSpan.FromSeconds`).
- `EmotesUIController`: adopted upstream's `RadialMenuOptionBase` type, kept Palmtree/Coyote's
  category sorting and hidden-category filtering.
- `RCDMenu`/`RPDMenu`: old `RadialMenuTextureButtonWithSector` controls no longer exist; adapted
  the fork menu to `RadialMenuButton`/`RadialMenuButtonWithSector`.
- Vox tails: upstream replaced the old `VoxTailSplit` marking in `_Impstation/misc.yml` with new
  entries in `vox_parts.yml`; took upstream's replacement and added `kindAllowance` to the three
  new tail markings.
- RD locker: took upstream's `RubberStampRd` removal and kept the Palmtree
  `ClothingHeadHatBeretResearchDirector` entry.
- `development.toml`: kept both `role_whitelist = false` (Palmtree) and
  `role_loadout_timers = false` (upstream).
- `ClientClothingSystem`: upstream removed the `Content.Shared.DisplacementMap` using because it
  no longer needed it; re-added it for the fork's leg-style displacement override.
- The admin-camera/overlay-cache work was not overwritten; the upstream #40181 cache and the fork's
  thermal-overlay cache both remain.

## Open questions / follow-ups

- The full integration suite has one **pre-existing** failure on `master` before the sync:
  `NoShipyardShipArbitrage` (Bocadillo/Dragonfly ship markup rounds just under the 5% test
  threshold). It is not a merge regression; all other integration tests pass.
- In-game test the adapted RPD radial menu (nested categories and the colour strip) after the
  radial-control change; the headless build cannot verify visuals.
- Verify digitigrade clothing and the new upstream reptilian head/mask displacement together.
- Verify the new Station AI fixer console, ERT admin shuttles, explosive cord/detonator, and the
  other newly merged upstream content in a live round.
- If a later upstream merge deletes a file that contains a fork patch, reapply the patch to the
  replacement file and add a line to this guide instead of leaving a dead fork file behind.
