# UNKNOWN

Questions that could not be answered confidently from the working tree. Treat these as
"verify before relying on" items. Each entry says what is known and what is missing.

## Deployment / operations

1. **Production `server_config.toml` is not in the repo.** Presets, map, auth mode, DB engine,
   admin API token and `nf.server_auth_list` are unknown.
2. **Live server topology.** Whether the Palmtree network is multi-server and how it connects is not
   in-repo.
3. **Postgres in production?** SQLite is the code default; no evidence either way in-repo.

## Ported-content behavior

5. **IPC direction.** IPC is replaced by the Palmtree **Synth** species (`_PS`), which reuses the
   marking system and approximates IPC stats without the Einstein Engines silicon stack. Whether a
   full silicon implementation (battery/radio/EMP) is ever wanted is unknown. See
   `.ai/systems/ps-systems.md` and `.ai/PORTING.md` §6.
6. **Consent.** Whether/when the Floof consent system will be ported is undecided. Until then the
   account-level **NSFW age gate** (lobby prompt, `nsfwtoggle`, account column) gates genital
   rendering, the editor tab, semen decals, the interaction panel and ERP emotes/sounds, while
   `ModifyUndies` relies on the per-marking `otherCanToggleVisible` opt-in.
7. **Traits.** 46 BodyType/Horny/Scent trait ids from character exports are not ported and are
   silently dropped on import. Whether they will be added later is unknown.
8. **Size system.** Character `height`/`width` are first-class (profile, DB, editor sliders, client
   `sprite.Scale`). Whether the Coyote size *manipulation* system will be ported is unknown.
9. **Marking visibility persistence.** `Marking.Visible` is session-only; genital markings reset to
    hidden after respawn/relog. Whether this should persist is undecided.
10. **`RenderOverClothing`** is ported and required by the organ renderer (`NeverHidden` organs set
    it; `HumanoidAppearanceSystem` consumes it). Directional marking offsets (`MarkingOffsetFront`,
    ...) remain unported. Whether future content needs them is unknown.
11. **Digitigrade displacement coverage.** Leg displacement was ported (`LegDisplacementPrototype` +
    `HumanoidAppearanceComponent.LegDisplacements`), but clothing coverage is partial (same maps as
    Coyote); whether every outfit looks right on digitigrade legs is untested in-game.

## Runtime behavior hidden in the engine

12. **Engine internals not line-traced**: handshake/encryption, PVS budget algorithms, physics
    solver details, map chunk serialization, UI layout/rendering.
13. **Sandbox whitelist coverage**: `RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml` is large;
    whether a specific BCL API is allowed was only checked for the APIs used by this port
    (`string.Create` is blocked; `float.ToString(IFormatProvider)` and `CultureInfo` are allowed).
14. **Engine upgrade risk**: which content code depends on undocumented engine behavior is not fully
    enumerable without an upgrade attempt.

## Content/gameplay reachability

15. **Which upstream content is reachable under live NF presets.** Upstream presets have no rules,
    but individual systems/events can still be attached by admins.
16. **`_PS` weapon balance/reachability.** Ported weapons are defined but not audited for spawn
    placement/vendors; whether they should be obtainable and how is undecided.
17. **Bayou clothing reachability.** The `CoyoteJumpsuit` subgroup was wired into the same job
    groups as the old repo; whether all of them are used on the live server is unknown.
18. **`CoyoteShoes` group**: the old repo had a separate shoes group for `LoadoutFootProtectors`;
    this port instead added the loadout to `ContractorShoes`. Behavior should match, but the group
    layout differs from the old server.
19. **`_Starlight` vs `_StarLight` casing**: two prototype directories exist; whether one is legacy
    is unknown.

## Moderation / ERP

20. **Data protection posture.** No consent freetext is stored; character flavor text exposure
    behavior (if any) is upstream and unaudited.
21. **Censor implementation** (`SimpleCensor`, `RegexCensor`) exists upstream but is not wired to
    live chat paths.

## Repo/tooling

22. **CI gating**: which GitHub workflows run on this branch and whether they match local commands
    is unverified.
23. **`Content.Docfx`** is not in the solution; its output/docs coverage was not assessed.
24. **Map renderer viewer JSON consumers** are external tooling not in this repo.

## Documentation gaps

25. `.ai/` docs prefer symbol names; line numbers will drift. Update docs when architecture shifts.
26. The generic Frontier system docs (`systems/*.md`) are summaries; deep NF internals should be
    verified against `_NF` source when relied upon.
