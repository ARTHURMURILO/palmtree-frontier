# HUMAN-PROVIDED CONTEXT

The following information was provided by the project maintainer.

Treat it as **context and guidance**, not as proof of implementation.

If this information conflicts with the source code:

* The source code determines what the project currently does.
* The human-provided context describes intended architecture, historical decisions, terminology, or
  information that may not be obvious from the source.
* Record meaningful discrepancies in `.ai/UNKNOWN.md` or `.ai/HAZARDS.md`.

## Project Context

This is a Space Station 14 codebase. The game has different servers each having their own codebase,
or using someone else's codebase.

Our server is named **Palmtree Station**, and thus we've chosen `_PS` as our prefix.

This repository is based on **Frontier Station** (New Frontier), a Space Station 14 fork. Frontier is
actively maintained, unlike our previous base, **Coyote Sector** (`_CS`), which was abandoned in 2026.

We are in the middle of a migration: the old codebase lives in a separate checkout
(the `COYOTE` checkout, see `.ai/file_paths.md`; branch `palm3`) and only specific things are being ported here:

* Palmtree Station specific content (loading screens, the concealable backpack implant, weapons, etc.)
* Coyote's genital markings and marking system, plus the Floof marking infrastructure those depend on
* Missing species our characters use (Rodentia, Anthromorph, Tajaran) and the markings/loadouts
  referenced by exported character files

Everything else (the full Floof consent system, scent, size manipulation, vore, RPI economy, needs, traits, IPC) is
deliberately **not** ported at this time. (The account-level NSFW age gate *is* implemented — see
`.ai/systems/consent-and-erp.md`.) See `.ai/PORTING.md`.

Space Station 14 (SS14) is a remake of Space Station 13 (SS13).

## Terminology

SS14 means Space Station 14. SS13 means Space Station 13.

The SS14 official maintainers are referred to as Wizard's Den or simply "Wizden". Thus if you see
mentions of wizden or wizden code, that's what it refers to.

RobustToolbox is the game's engine.

RP stands for Roleplay. ERP stands for Erotic Roleplay, which is allowed in Palmtree.

BYOND is the platform/engine of the original game, SS13. It's only historically relevant, as
RobustToolbox is the engine of our project, a remake of SS13.

LRP stands for Low-Roleplay, and MRP and HRP for Medium and High RP respectively. These refer to the
level of roleplay of a server. Where as LRP is basically no roleplay involved at all, just mechanical
gameplay. And HRP is almost exclusively roleplay focused, putting things aside such as objectives or
antagonism in favor of character writing and development.

"emag" is often a nickname to the Syndicate's Cryptographic Sequencer, the famous antagonist tool of
hacking devices into doing stuff.

NanoTrasen (NT) is usually the corporation associated with the player's station in Space Station
13/14. In Palmtree and Coyote, they are not the main station anymore, and the universe expands beyond
just a corporation station. The players therefore live on **Nash Station** with **NFSD** as their
law enforcement.

NFSD stands for New Frontier Sheriff's Department. They're the local police.

Syndicate is the rival corporation of NT. They're usually the ones sending nukeops and other
antagonists to normal rounds of the game.

## Architecture Known to the Maintainer

The actual game code is split between `Content.Client` for the client code and `Content.Server` for
the server side code. These are the focus of adding any gameplay features.

The other projects such as `Content.Replay` etc, are hardly ever touched.

The game uses ECS. Everything in the game is an entity, and those entities have components.
Components do not contain behaviour. Only data. Systems therefore act upon said components.

A collection of components that make up an entity or a thing is called a Prototype. For example
`Resources/Prototypes/Entities/Objects/Tools/toolbox.yml` contains a definition for a toolbox.
`ToolboxBase` is therefore a prototype. `ToolboxMechanical` is a child of `ToolboxBase`, and is a
separate entity that can be spawned in. Prototypes are like Prefabs in engines like Unity.

A prototype may also define things aside from game entities, such as loading screen music and
backgrounds.

The `Resources` folder is where all sounds, prototypes and textures reside.

The game uses its own image format known as .rsi which is basically just a folder, with spritesheets
inside, corresponding to a state, and a json file defining this state. This is a very similar concept
to SS13's .dmi format (dream maker image) for the BYOND engine.

## Historical Context

The game's engine name, Robust Toolbox, is also a funny reference to SS13, where the controls were so
bad in the original game and hard to master that anyone capable of being lethal in PvP encounters and
actually good at it were called robust. And the toolbox was a common strong weapon.

SS13 codebases usually suffer from technical debt. In fact SS13 is pretty much the definition of
technical debt. SS14 however tries to be a bit cleaner. But the coders of the code we used, Coyote,
are not the brightest. Finding misplaced files, misnamed resources or straight up inconsistency is
not unexpected.

As of 2026, Coyote abandoned the SS14 scene, leaving that code entirely frozen. Palmtree Station now
maintains this Frontier-based fork instead.

## Important Constraints

Our additions must be placed under `_PS` folders, much like the rest of the codebases do. This allows
for modularity and organization between what code is ours and what isn't ours.

Content ported from other forks (`_Floof`, `_CS`, `_DEN`, `_EE`, `_White`, `_Starlight`) keeps its
original prefix so future merges and provenance stay clear.

The engine RobustToolbox is never to be modified.

## Things That Are Intentionally Weird

Nobody but Wizden can modify the engine. And nobody but them understands it well.

IPC is not ported. It is replaced by the Palmtree **Synth** species (`_PS`), which reuses the marking
system so players can look like any species while keeping IPC-like stats. If true silicon IPCs are
ever wanted, they should be built on the upstream silicon systems instead of the Einstein Engines
stack.

The Floof per-toggle consent system is not ported. The account-level NSFW age gate (one-time lobby
prompt, `nsfwtoggle`, what it gates) is documented in `.ai/systems/consent-and-erp.md`.
`ModifyUndies` is gated per marking: the owner
can toggle by default, other players only when the marking has `otherCanToggleVisible` (default off).
