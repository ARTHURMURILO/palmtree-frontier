# System: Interaction Panel (Sandstorm port)

> Ported from **Sandstorm-Station-13** (BYOND) `modular_sand/code/datums/interactions/` and
> `tgui/packages/tgui/interfaces/MobInteraction/`. Scope: the "core tame" interaction set only;
> lewd interactions are gated by a session consent toggle and resolved from genital **organs**
> (they replaced the old genital markings; see `systems/genital-organs.md`).

## Purpose

Lets a player open a tabbed panel on another character (or themselves) and perform data-driven
social/lewd interactions: chat emote + sound + lust/moaning/climax, with search, favorites and
auto-repeat. Sandstorm's organ-based genitals, pregnancy, refractory period, vore and extreme
content are intentionally not ported.

## Locations

| Piece | Path |
|---|---|
| Prototype (`type: interaction`) | `Content.Shared/_PS/Interactions/InteractionPrototype.cs` |
| Enums/flags | `Content.Shared/_PS/Interactions/InteractionEnums.cs` |
| Marker component | `Content.Shared/_PS/Interactions/InteractionPanelComponent.cs` |
| UI key/state/messages | `Content.Shared/_PS/Interactions/InteractionUi.cs` |
| Genital resolver (organs) | `Content.Shared/_PS/Interactions/GenitalSystem.cs` |
| Server system | `Content.Server/_PS/Interactions/InteractionPanelSystem*.cs` |
| Server runtime state | `Content.Server/_PS/Interactions/InteractionStateComponent.cs` |
| Client BUI/window | `Content.Client/_PS/Interactions/InteractionBoundUserInterface.cs`, `InteractionWindow.xaml(.cs)` |
| Prototypes | `Resources/Prototypes/_PS/Interactions/interactions.yml` |
| Sound collections | `Resources/Prototypes/_PS/SoundCollections/interactions.yml` |
| Audio | `Resources/Audio/_PS/Interactions/*` (+ `attributions.yml`) |
| Locale | `Resources/Locale/en-US/_PS/interactions/{interactions,ui}.ftl` |
| Mobs | `InteractionPanel` + `enum.InteractionUiKey.Key` added to the organic humanoid `UserInterface` in `Resources/Prototypes/Entities/Mobs/Species/base.yml` |
| Keybind | `InteractWithEntity` (`Content.Shared/Input/ContentKeyFunctions.cs`, `Content.Client/Input/ContentContexts.cs`, `Resources/keybinds.yml`, `KeyRebindTab`) |
| Tests | `Content.IntegrationTests/Tests/_PS/InteractionPanelTest.cs` |
| NSFW age gate | `Content.Shared/_PS/Consent/NsfwConsentState.cs`, `Content.Client/_PS/Consent/*`, `IServerPreferencesManager.SetNsfwConsentAsync` — see `systems/consent-and-erp.md` |

## How it works

1. **Opening.** Right-click verb `interaction-verb-open` ("Interact with...") or the
   `InteractWithEntity` keybind (Ctrl+Shift+left click, `mod1: Control`, `mod2: Shift`). The server
   `PointerInputCmdHandler` in `InteractionPanelSystem` mirrors `PullController`'s server-side
   binding. Range is 6 tiles. **Clicking empty space opens the panel on yourself**, which is how the
   self (masturbation) actions are reached; the panel can also target yourself through the verb on
   your own body.
2. **Binding.** The BUI is opened on the *actor*, not the target
   (`_ui.OpenUi(user, InteractionUiKey.Key, user)`). This engine version has one state per
   `(entity, key)`, so binding to the actor avoids users sharing a state; the target travels in
   `InteractionBoundUserInterfaceState.Target`. The target must carry
   `InteractionPanelComponent` (organic humanoids).
3. **Requirements.** `InteractionPrototype.UserRequirements`/`TargetRequirements` are lists of
   `InteractionRequirements` names (parsed in code to avoid YAML flag-enum issues; the C# field is
   `List<string>`). `Mouth` fails on ingestion blockers (mask), `Hands` needs a `HandsComponent`.
   Genital flags resolve through `GenitalSystem`, which now reads **genital organs** (see
   `systems/genital-organs.md`), not markings:
   - Presence comes from `GenitalOrganComponent`s on the mob (penis, vagina, balls, breasts, plus
     butt/belly which have no interaction requirements yet).
   - Exposure follows the organ's SPLURT-style visibility rule (Always hidden / Hidden by underwear /
     Hidden by jumpsuit / Never hidden), checked against the jumpsuit slot and visible undergarment
     markings.
   - The anus is simulated (no organ): humanoids always have one, exposed when bottomless.
   - Both `Exposed` and `Unexposed` in one list means "any state, but must have the genital".
   - `InteractionFlags.UserIsTarget` marks self-only actions; `InteractionFlags.SelfOrOther`
     (used by the manual Climax) allows both self and other targets.
4. **Consent.** Two layers:
   - **Account NSFW age gate (Palmtree)**: every account starts non-consenting (unanswered). Until
     the player answers the lobby prompt, `OpenPanel` refuses to open the panel (and the right-click
     verb is not added), so the panel is all-or-nothing — there is no partial panel. The account
     flag is also ANDed into `HasConsent`, so a non-consenting player can never be the *target* of a
     sexual interaction either. Entities without a player session (NPCs, test mobs) keep the legacy
     consenting behavior.
   - **Session toggle**: `InteractionStateComponent.Consent` (session-only, default **on**) gates
     every `InteractionType.Lewd` entry for both actor and target (including `nonSexual` ones).
     Turning it off in the Preferences tab blocks others (and yourself). Per-marking
     `OtherCanToggleVisible` remains the gate for others revealing genitals. There is still no
     persisted per-toggle consent system beyond the account age gate.
   - **Non-sexual entries**: `InteractionPrototype.NonSexual` (set on `Kiss`) skips the *account*
     gate for emote/sound visibility and for the target check; the session toggle above still
     applies. The panel lock for non-consenting actors is not bypassed.
   - An admin flipping a player to non-consenting closes their open panel and stops auto-repeat from
     the server `Update` loop.
5. **Effects.** `TryPerform` sends an emote through `ChatSystem.TrySendInGameICMessage`
   (`InGameICChatType.Emote`, `ChatTransmitRange.Normal`) for normal interactions, or builds a
   purple-wrapped message (`#c060ff`) and sends it through `SendErpEmote` for lewd ones (the
   standard emote path strips markup). It plays the prototype's sound (lewd sounds are filtered
   to sessions in range whose `LewdSounds` is on; normal sounds use PVS), applies
   intro/continuing message selection (same interaction + target within `ContinueTimeout`), then
   applies lust. Participants are **not** turned to face each other (Palmtree removed the Sandstorm
   auto-facing). See "Range and visibility (PVS)" below for who can read/hear what.
6. **Arousal.** `EnsureState` randomizes tolerance (75-200) and potency (10-25) per mob once. Lust
   decays 1/s on read. Lewd interactions add 20 by default (self actions 30/20, ass slap 10; kiss
   sets at least 10) and arouse the
   involved organs (which swaps their render markings). Optional per-player lust-gain and
   moaning-chance multipliers live in the Preferences tab. Moans use Sandstorm's quadratic bezier
   between `tolerance*1.5` and `tolerance*3`, with the "You struggle to not orgasm!" self-popup;
   climax at `tolerance*3` resets lust and plays `final_*` sounds. Repeating the same interaction
   uses `continueMessages` when provided.
7. **Climax fluids / drip.** `InteractionPrototype.CumTarget` (`vagina`/`anus`/`mouth`/`exterior`)
   maps a climax: internal endings store the penis organ's `SemenVolume` in the partner's
   `DrippingCumComponent` (1 u/s **droplet** decals while not wearing a jumpsuit; drips never grow
   into the full puddle decals), exterior endings drop the full `SemenPuddle*` decals on the ground.
   A null cum target (manual Climax with no recent interaction) is treated as an exterior climax on
   the actor. Receiver-side acts (`Ride`, `TakeAnal`) reuse the same cum target for the **target's**
   climax, so the penetrator finishing fills the actor. Each interaction can define
   `cumMessages` for a context-sensitive finish line (e.g. breastfuck "cums all over {target}'s
   breasts"); the generic per-target text is the fallback. The **Cum on them** action is a
   `forceClimax` exterior action with no orgasm gate; the **Climax** action (`forceClimax`) has no
   range or orgasm requirement, is usable on self or others, and reuses the last interaction's cum
   target when it has none of its own. Both go through the same `TriggerClimax`/`EmitClimax` code.
   **Multi-pulse climax:** a climax splits `SemenVolume` into 30u pulses (max 10, so 300u = 10
   pulses). The first pulse fires immediately; the rest follow every 0.6s from the server `Update`
   loop, each placing a decal/drip, sending the cum message and moaning. While
   `InteractionStateComponent.ClimaxPulsesLeft > 0` the actor cannot start another climax
   (`CanPerform`/`TryPerform` reject `ForceClimax`) and climaxing participants gain no lust from
   interactions (fail-safe against re-triggering). See `systems/genital-organs.md`.
8. **Auto-repeat.** The server `Update` loop repeats the active interaction every `AutoPace`
   (Sandstorm's `interaction_speeds`: 4/2/1/0.8/0.5 s) without the 0.5 s cooldown, stopping on any
   requirement failure, target deletion or range loss. The panel state is pushed only when something
   happens (performed interaction, climax pulse, message); the once-per-second refresh that used to
   keep lust bars and attribute lines live was **reverted** because it rebuilt the window and reset
   the controls the player was typing into. Reintroduce it only with a partial state update that
   leaves inputs alone.
9. **Favorites** are session-only (`InteractionStateComponent.Favorites`) because this fork has no
   profile storage for them.
10. **Admin logs.** Every performed interaction (and every manual climax) is written to the admin
   logs as `LogType.Interaction` with both participants and the interaction prototype id/name.
   Auto-repeat continuations are not logged again, so a chain produces one log entry.

## Range and visibility (PVS)

The interaction panel measures range in **world tiles**. The engine's **PVS** (*Potentially
Visible Set*) is per-client interest management, not line of sight:

- `net.pvs_range` (CVar, default **25**) is the radius in which the server sends entities
  to a client. `Filter.AddPlayersByPvs(origin, multiplier)` is literally
  `AddInRange(origin, 25 * multiplier)` - it does **not** raycast, so **walls never block it**.
- The port originally filtered lewd emotes with `AddPlayersByPvs(source, 2f)` = **50 tiles**,
  which let players read ERP emotes through walls from across the station. A stopgap lowered the
  multiplier to `0.12f` (~3 tiles). `SendErpEmote` now uses an explicit `ErpEmoteRange = 4f` on
  top of `ExamineSystemShared.InRangeUnOccluded` (same map + distance + occluder raycast - the
  same check behind whispering and subtle chat), plus the existing "no ghosts" rule. Non-consenting
  accounts (NSFW age gate) are dropped too, unless the interaction is `nonSexual`.
- Performing interactions was already line-of-sight-checked: opening/using one goes through
  `SharedInteractionSystem.InRangeUnobstructed(user, target, range)` with `InteractionRange = 6f`,
  so an interaction cannot be started through a wall.
- Lewd **sounds** (`SoundRange = 4f`) are distance-filtered but not wall-blocked - SS14 audio has
  no occlusion (gunshots and speech are audible through walls too). Recipients are also filtered by
  the per-listener `LewdSounds` toggle and the account NSFW age gate (non-`nonSexual` entries only).
  Normal (non-lewd) interaction emotes use the regular chat path (`ChatTransmitRange.Normal` ->
  `VoiceRange = 10`) and are visible through walls like every other emote.

## Content

- Prototypes are defined in `Resources/Prototypes/_PS/Interactions/interactions.yml`.
- Ported normal interactions: handshake, pat, cheer, high-five, headpat, salute, fistbump, pinky
  promise, bird, hold hand, hug (hug is new; Sandstorm had the sound but no datum).
- Ported lewd interactions: kiss, titgrope, blowjob, oral (cunnilingus), handjob, finger,
  finger ass, fuck (vaginal), fuck anal, breastfuck, frotting, tribadism, nipple suck, ass slap,
  ride + take anal (receiver-side penetration, the target's climax fills the actor), self actions
  (jerk off, finger yourself, finger your rear, grope your own breasts), cum on them (manual
  exterior action) and climax (manual orgasm button, usable on self or others at any time).
- Not ported: breastfeeding, feet (footjob/footfuck/grinding/licking), facefuck/throatfuck,
  rimjob, mounting/thigh smothering, nuts-to-face/nut smack, cursed (ear/eye) content,
  and everything depending on pregnancy/refractory period/multi-orgasm gating/estrous traits.
- Audio: 47 ogg files copied from Sandstorm's `modular_sand/sound/interactions/` plus
  `sound/weapons/slap.ogg`; see `Resources/Audio/_PS/Interactions/attributions.yml`.

## Organs (implemented)

The old genital markings were replaced by player-configured **genital organs** in character
creation; `GenitalSystem` now reads `GenitalOrganComponent`s. The marking library survives as the
render backend (hidden from the editor, stripped from old profiles). Semen volume and the drip live
in the same system. Full details: `systems/genital-organs.md`.

## Hazards

- `SharedPopupSystem.PopupCursor` broadcasts on the server; requirement failures must use
  `PopupEntity(message, subject, actor)` so only the acting player sees them.
- BUI state is per entity, not per user — do not bind this panel to the target without adding a
  per-user channel.
- Requirement/flag lists are strings (see `InteractionPrototype`); a typo silently drops the
  requirement. Add entries carefully and keep names in sync with `InteractionRequirements`/
  `InteractionFlags`.
- Exposure is now the jumpsuit rule on organs; the `Genital` marking flags no longer drive
  requirements. Do not reintroduce marking-based checks in requirement code.
- Lewd emotes must go through `SendErpEmote` (purple); the standard chat path strips markup. Both
  `SendErpEmote` and the lewd sound filter drop sessions attached to a `GhostComponent`, so
  observers (including admin ghosts) never receive ERP text or sounds. Do **not** filter these with
  `Filter.AddPlayersByPvs` - it is a bare radius (`net.pvs_range` x multiplier) that ignores
  walls; use `ErpEmoteRange` + `InRangeUnOccluded` (see "Range and visibility (PVS)").
- The entity event bus allows **one subscription per (component, event) pair across all systems**.
  `MarkingVisibilityChangedEvent` is owned by `GenitalOrganSystem`; the client
  `HumanoidAppearanceSystem` must not subscribe to it. Local preview refreshes go through
  `HumanoidAppearanceSystem.RefreshSprite`.
- The NSFW age gate is **account-level and fail-closed**: `TryGetCachedPreferences` missing/not
  loaded or any state other than `Allowed` blocks NSFW. Do not gate on the entity's session
  consent alone, and keep the `nonSexual` exemption limited to tame entries (`Kiss`).
- `IsNsfwAllowed(EntityUid)` requires an `ActorComponent` session; session-less mobs (NPCs, tests)
  intentionally stay consenting. Any new sexual system must call the same helper rather than
  reimplementing the lookup.

## Validation

- `Content.IntegrationTests/Tests/_PS/InteractionPanelTest.cs` covers the interaction requirements/
  consent/cooldowns, the internal-climax drip, legacy marking conversion and stale-profile
  persistence.
- `ServerDbSqliteTests` round-trips the persisted `Genitals` string (`CharlieCharlieson`) and
  guards migration drift.
- The standard pipeline (solution build, YAMLLinter, unit tests, targeted integration tests,
  headless client IL check) is expected to pass.
