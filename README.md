<div align="center">
<img alt="Palmtree Station" height="280" src="Resources/Textures/_PS/Logo/logo.png" />
</div>

# Palmtree Station

**Palmtree Station** is a private, community-run [Space Station 14](https://github.com/space-wizards/space-station-14)
server. It runs on a customized [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14)
codebase — itself a fork of Space Station 14 — on the
[Robust Toolbox](https://github.com/space-wizards/RobustToolbox) engine.

This repository is the server's content pack. It is public so players and contributors can see
exactly what runs on the server. Palmtree Station is not affiliated with Frontier Station, Wizard's
Den, or any other server.

Fork history: Space Station 14 → Frontier Station → **Palmtree Station** (previously based on
Coyote Sector). Palmtree-specific content lives under the `_PS` prefix.

## Features

### Frontier gameplay

- Hub outpost, shipyard, ship deeds and ship purchasing
- Economy, banking and cargo contracts, plus Palmtree's RIX bank transfers
- Salvage, expeditions, sectors, pirates and NFSD content
- **Bluespace Dock** and **Ship Registrar** for persistent ship storage

### Character and roleplay

- **Hilbert's Hotel**: private rooms with shareable codes, loaded on demand
- **Interaction panel**: data-driven social interactions with per-player consent
- An **18+ account gate**; NSFW content is opt-in and consent is enforced
- Extended markings, genital organs and an undies/ERP toggle layer (Coyote and Floof ports)
- Species including Synth, Anthromorph, Tajaran, Vulpkanin, Harpy, Rodentia and Resomi
- Concealable clothing through a backpack implant, and rings on gloves

### Engineering and content

- Custom atmospherics: heat pump, temperature gate/pump, heat exchanger and atmos meter, all
  pipe-layer aware and constructible in-game
- A **circuitry editor** with Lua scripting
- Palmtree weapons, grenades and expedition loot
- Lobby backgrounds and a music rotation
- NanoChat group chats and an emoji picker

## Building

Requirements: the .NET 9 SDK and Python 3. The engine is a git submodule, so clone with submodules
or run the setup script below.

1. Clone the repository:

   ```shell
   git clone https://github.com/xTheLifex/palmtree-frontier.git
   cd palmtree-frontier
   ```

2. Initialize the submodule and engine:

   ```shell
   python RUN_THIS.py
   ```

3. Build the solution:

   ```shell
   dotnet build
   ```

See the [Space Station 14 development docs](https://docs.spacestation14.com/en/general-development/setup.html)
for the full toolchain. The server itself is private; a local build is for development and testing.

## Contributing

Contributions are welcome from the community. Read [CONTRIBUTING.md](CONTRIBUTING.md) first. In
short:

- New Palmtree content goes under `_PS`; ported content keeps its origin prefix (`_NF`, `_CS`,
  `_Floof`, `_DEN`, ...).
- Mark edits to upstream files with a `// Palmtree`, `# Palmtree`, `// Coyote` or `# Coyote` comment
  so future upstream merges stay manageable.
- Do not use GitHub's web editor to submit changes.
- Player-visible changes need a `:cl:` block in the commit message.

## License

The project as a whole is licensed under the [AGPLv3](LICENSE-AGPLv3.txt). Code inherited from
Space Station 14 is used under the [MIT license](LICENSE-MIT.txt). Assets keep their individual
licenses; see the `meta.json` and `attributions.yml` files next to each asset.

Read [LEGAL.md](LEGAL.md) for the full attribution table.

Palmtree Station is a fork of [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14),
which is a fork of [Space Station 14](https://github.com/space-wizards/space-station-14). All credit
for the original game and for Frontier's content belongs to their respective authors.
