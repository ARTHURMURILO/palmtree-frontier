# DATA_FLOW

> How data moves between systems. Focused on flows that matter for this fork; engine internals are
> summarized, not line-traced.

## Round flow

1. `GameTicker.InitializeLobbyBackground` selects/rotates the lobby background (30s timer, Palmtree).
2. Lobby → `StartRound`: preset (`NFAdventure`/`NFPirate`/`NFTest`) → `GamePreset` → rules.
3. Map load, `RoundStartingEvent`, `RoundStartAttemptEvent`, `RulePlayerSpawningEvent`.
4. Job assignment (`StationJobsSystem`) and per-player `SpawnPlayerMob` (NF bank/loadout hooks).
5. `RoundStartedEvent` → gameplay; periodic systems tick.
6. `EndRound` → `RoundEndTextAppendEvent` → `RoundRestartCleanupEvent` → back to lobby.
7. Round-scoped state resets on `RoundRestartCleanupEvent`.

## Player connect → character → mob

1. Client connects; `ClientGameTicker` receives lobby status (round state, lobby background).
2. Character editor (`HumanoidProfileEditor`) edits `HumanoidCharacterProfile`; markings via
   `MarkingPicker` (`MarkingSet`); loadouts via `RoleLoadout` groups.
3. Server receives profile (net message) → `ServerPreferencesManager` persists to DB (jsonb profile).
4. Spawn: `StationSpawningSystem.SpawnPlayerMob` creates the species mob, applies loadouts
   (`_NF` contractor groups), then `HumanoidAppearanceSystem.LoadProfile`.
5. `LoadProfile` builds `MarkingSet`, `EnsureSpecies` (kind-aware), sets `LegStyle` and other
   component fields, `Dirty` → auto-networked state.
6. Client `AfterAutoHandleStateEvent` → `UpdateSprite` → layers/markings render.

## Marking data flow (detailed)

```
YAML marking prototype ──► MarkingManager cache (by category, by id)
Profile (List<Marking>) ──► MarkingSet (per-humanoid, component, auto-networked)
   ├─ EnsureSpecies/EnsureSexes/EnsureDefault (server & client preview)
   ├─ MarkingPicker edits (colors, scale/offset, leg style, rank, remove)
   └─ ModifyUndies flips Marking.Visible at runtime
Client renderer:
   speciesBaseSprites + LegStyle ──► base layers (altSprites)
   MarkingSet ──► ApplyMarking per sprite:
       bodyPart / layering ──► target layer
       colorLinks ──► colors
       scale/offset ──► layer transform
       Category2Layer ──► HiddenBaseLayers
       genital clamp ──► below jumpsuit/outerClothing
```

Persistence format (DB string): `markingId@#rrggbb,...[@scale,offsetX,offsetY][@g...][@m<flags>][@c<name>]`.

## UI flow

- BUI pattern: server `ActivatableUISystem` + `UserInterfaceComponent` YAML → client BUI class keyed
  by `ClientType` → `SetUiState`/state messages → `UpdateState`.
- Lobby/character editor: `LobbyState` (screen) + `HumanoidProfileEditor` (control) + `MarkingPicker`
  (control). Profile changes are local until saved; preview uses the dummy entity via `LoadProfile`.
- Lobby background: server status event → `ClientGameTicker.LobbyStatusUpdated` →
  `LobbyState.UpdateLobbyBackground` → crossfade in `FrameUpdate`.

## Chat/speech flow (summary)

Client chat input → `ChatSystem` (server) validates, applies accents/sanitization, raises
`ChatMessageEvent`-style events → `MsgChatMessage` to clients → `ChatManager`/`ChatBox` renders.
Emotes use `ChatSystem.Emote`; radio uses headset/radio systems with encryption channels. No
consent/censor contributors are registered in this repo.

## Damage/body flow (summary)

`DamageableSystem` applies damage to `DamageableComponent` via damage types/sets; `BodySystem`
maps part damage; `MobStateSystem` transitions states; `BloodstreamSystem`/`RespiratorSystem`
handle internals; species damage sets come from `damageModifierSet` prototypes. Ported species
(Rodentia/Anthromorph/Tajaran) use standard body prototypes and damage sets.

## Economy flow (Frontier)

Bank accounts (`BankSystem`) hold credits; ATM/console BUIs move money; shipyard/market/cargo
consume/produce money; loadouts and missions may pay out. NF code lives under `_NF` and is mostly
independent of the marking layer.

## Persistence flow

- On profile save/character switch: profile JSON (including markings) → `IServerDbManager` →
  `Profile` row (markings as jsonb string list).
- The NSFW age gate stores a single `Preference.NsfwConsent` column (account-level); adding the
  full consent system later would add account/character tables.
- Admin logs are DB-backed (`LogType` numeric values are load-bearing).
