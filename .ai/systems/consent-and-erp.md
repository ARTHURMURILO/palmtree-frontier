# System: Consent & ERP-Adjacent Content

> Palmtree is an adult-RP-permitting server (maintainer context). The repo has an **account-level
> NSFW age gate** (Palmtree, 2026-10) but **not** the full Floof consent system. This document
> records what actually exists, what does not, and what is gated behind the age gate.

## NSFW age gate (exists)

Every account starts as **unanswered** and is treated as non-consenting (no NSFW content at all)
until the player answers the one-time lobby prompt. The answer is permanent and can only be changed
with the `nsfwtoggle <user> <true/false>` console command (`AdminFlags.Admin`).

| Piece | Path |
|---|---|
| Tri-state enum | `Content.Shared/_PS/Consent/NsfwConsentState.cs` (`Unanswered`/`Denied`/`Allowed`) |
| Account flag | `PlayerPreferences.NsfwConsent` (sent in `MsgPreferencesAndSettings`) |
| Answer message | `Content.Shared/Preferences/MsgUpdateNsfwConsent.cs` (client → server, only accepted while unanswered) |
| Save path | `IServerPreferencesManager.SetNsfwConsentAsync` → `ServerDbBase.SaveNsfwConsentAsync` |
| DB | `Preference.NsfwConsent` nullable bool (`nsfw_consent` column), migrations `NsfwConsent` (Sqlite + Postgres) |
| Lobby prompt | `Content.Client/_PS/Consent/NsfwConsentOverlay.xaml(.cs)` added by `Content.Client/Lobby/LobbyState.cs` |
| Client gate | `Content.Client/_PS/Consent/NsfwGate.cs` (static, fail-closed, `Changed` event) |
| Command | `Content.Server/_PS/Commands/NsfwToggleCommand.cs` (autocomplete: session names + booleans) |
| Locale | `Resources/Locale/en-US/_PS/nsfw-consent.ftl` |

What the gate controls (anything but `Allowed` disables all of it):

- **Genital sprites**: client `HumanoidAppearanceSystem.ApplyMarking` early-outs for
  `HumanoidVisualLayers.Genital`. Covers organ render markings, legacy genital markings, remote
  players, ghosts, your own body and the lobby preview. `NsfwGate.Changed` rebuilds every humanoid
  sprite (same mechanism as the censor-nudity CVar), so admin flips apply live.
- **Semen decals**: `DecalPrototype.Nsfw` (`nsfw: true` on `SemenDrip*`, `SemenPuddle*`,
  `FemPuddle*`) is skipped in `Content.Client/Decals/Overlays/DecalOverlay.cs`.
- **Genitals editor tab**: `HumanoidProfileEditor.RefreshGenitals` never builds the tab and removes
  it if present. Organ data already saved in a profile is preserved, just hidden.
- **Interaction panel**: `InteractionPanelSystem.OpenPanel` refuses (verb hidden too) and
  `HasConsent` ANDs the account flag, so a non-consenting player can also never be the *target* of a
  sexual interaction. There is deliberately no partial panel. Non-sexual lewd entries
  (`InteractionPrototype.NonSexual`, currently only `Kiss`) skip the account flag for emote/sound
  visibility and target checks; the voluntary per-session consent toggle still applies.
- **ERP emotes/sounds**: `SendErpEmote` and the lewd branch of `PlayInteractionSound` drop
  non-consenting listeners (non-sexual entries exempt).
- **Joining**: `toggleready` is rejected while unanswered; declining does not block joining.

Behavior notes:

- The prompt is answered exactly once. `MsgUpdateNsfwConsent` is ignored unless the account is
  `Unanswered`; the DB never stores "unanswered" again (admin commands cannot reset it).
- Fail-closed everywhere: prefs not loaded, unknown session, or a missing state = non-consenting.
  Mobs without a session (NPCs, tests) keep the legacy consenting behavior.
- Account-level only: it is not per-character and not a per-player "I don't want X right now"
  system.
- Genital *data* (marking ids) still reaches non-consenting clients in component state; only the
  rendering is skipped. There is no per-client state filtering in the engine.
- Guests (no stored prefs) answer every login.

## Consent status: full Floof system not ported

There is still **no** `Content.Shared/Consent`, `Content.Server/Consent`, `Content.Client/Consent`,
`Resources/Prototypes/consent.yml`, consent DB tables or a consent CVar in this repository — only
the age gate above.

Consequences:

- `ModifyUndies` defaults to self-only; per-marking `OtherCanToggleVisible` is the only gate for
  other players. It defaults to off, including for undergarments and genitals, so other players
  cannot reveal anything unless the owner opted in.
- The interaction panel still has the **session-only** lewd-interaction toggle
  (`InteractionStateComponent.Consent`, default on) for consenting players who want to opt out
  moment-to-moment; it is not persisted and is separate from the account age gate.
- No system gates anything on `NSFWDescriptions`, `SizeManipulation`, `Transformation`, `Vore`,
  `Digestion`, etc. (most of those systems are not ported either).
- Character exports still contain `consentToggles`, `accountConsentFreetext` and
  `characterConsentFreetext` (the old Floof schema). The importer ignores these unknown fields; they
  are not stored or enforced.

The old Coyote checkout (see `.ai/file_paths.md`) has the complete Floof consent system; `.ai/PORTING.md` §8 and
`guides/porting-from-coyote.md` describe how to port it if requested. It requires DB migrations
(two tables + a profile column), which the maintainer explicitly deferred.

## ERP features that DO exist here

| Feature | Location | Notes |
|---|---|---|
| NSFW age gate (account flag, lobby prompt, `nsfwtoggle`) | see above | Gates genitals, semen decals, editor tab, interaction panel and ERP emotes/sounds |
| Genital markings (core library, butts/bellies, Palmtree breasts) | `Resources/Prototypes/Entities/Mobs/Customization/Markings/genitals.yml`, `butts_and_bellies.yml`, `_PS/.../genitals.yml` | `bodyPart: Genital`, start hidden, render under clothing, hidden entirely without the age gate |
| Show/hide verbs for undergarments (and any marking the player opts in) | `Content.Server/_Floof/ModifyUndies/*` | Per-marking opt-in (`canToggleVisible` default off except undergarments, `otherCanToggleVisible` default off), 1s do-after, `marking-toggle-*` locale |
| Marking visibility state | `SharedHumanoidAppearanceSystem.SetMarkingVisibility` | Flips `Marking.Visible`; session-only |
| Interaction panel (Sandstorm port) | `Content.{Shared,Server,Client}/_PS/Interactions/*`, `Resources/Prototypes/_PS/Interactions/`, `Resources/Audio/_PS/Interactions/` | Verb + Ctrl+Shift-click; kiss/hug/fuck/blowjob etc. gated on the account age gate + a session consent toggle (default on), organ-based genitals (see `systems/genital-organs.md`), lust/moans/climax, semen volume + drip + washable cum overlay; no pregnancy/refractory — see `systems/interaction-panel.md` |
| Genital organs + cum | `Content.Shared/_PS/Organs/*`, `Content.Server/_PS/Organs/*`, `Resources/Prototypes/_PS/Organs/`, `Resources/Prototypes/_PS/Reagents/semen.yml`, `Resources/Textures/_PS/Effects/cum.rsi` | Character-creation organ picks (penis/vagina/balls/breasts/butt/belly + semen volume) persisted in the profile; SPLURT cum overlay and Sandstorm-style drip; wash with water/space cleaner |
| Undergarment/over-garment marking library | `undergarments.yml`, `underovergarments.yml` | 218 + 67 markings, Floof sprites |
| Genital-under-clothing ordering | `Content.Client/Humanoid/HumanoidAppearanceSystem.ApplyMarking` | Clamps below `jumpsuit`/`outerClothing` |
| Digitigrade legs / base markings / kind sharing | see `systems/marking-and-appearance.md` | General customization, used heavily by ERP characters |

## ERP-adjacent features NOT ported

| Feature | Old location | Status |
|---|---|---|
| Per-player consent toggles (UI/DB) | `Content.{Shared,Server,Client}/Consent`, `consent.yml` | Deferred (replaced in practice by the account age gate) |
| CustomExamine (NSFW descriptions) | `_Floof/Examine` | Not ported |
| Vore / Digestion | `Content.Server/FloofStation/VoreSystem.cs` | Not ported |
| Scent / sniff | `_CS/SniffAndSmell` | Explicitly excluded |
| Aphrodisiac visibility | `_CS/AphroLacedVisibility`, `ServerAphrodisiacChecker` | Explicitly excluded |
| Horny examine quirks + BodyType traits | `_CS/HornyQuirks`, `body_type_traits.yml`, `Traits.yml` | Not ported (46 trait ids dropped on import) |
| Size manipulation | `_CS/Body/Systems/Size*` | Not ported; character `height`/`width` are supported through the profile/editor/client scale instead |
| RPI economy | `_CS/RolePlayIncentiveServer` | Not ported |

## Examine pipeline (where ERP text used to land)

Client examine request → server range/occlusion check (`ExamineSystemShared.CanExamine`) →
`ExamineSystemShared.GetExamineText` builds text from metadata + `ExaminedEvent` subscribers →
`ExamineCompletedEvent`. In this repo no ERP contributors are registered (CustomExamine/HornyQuirks/
Scent are absent); the pipeline itself is upstream.

## If you are asked to extend consent

Follow `.ai/guides/porting-from-coyote.md`, porting from the old repo:

1. Shared: `PlayerConsentSettings`, `ConsentTogglePrototype`, `SharedConsentSystem`, `MsgUpdateConsent`.
2. Server: `ServerConsentManager`, `ConsentSystem`; register the manager in `ServerContentIoC`.
3. Client: `ClientConsentManager`, `ConsentUiController`, consent window XAML.
4. Prototypes/locale: `consent.yml`, `_Floof` locale.
5. DB: add `ConsentSettings`/`ConsentToggle` tables and `Profile.CharacterConsentFreetext`, then
   regenerate **both** SQLite and Postgres migrations.
6. Re-gate `ModifyUndies` on the target's `GenitalMarkings` consent (the old code does this in
   `ModifyUndiesSystem.AddModifyUndiesVerb`) and replace the interaction panel's session
   `InteractionStateComponent.Consent` check with the consent system
   (`systems/interaction-panel.md`).

## Unknowns

- Whether `NSFWDescriptions`/CustomExamine should come with consent.
- Whether visibility toggles should persist across sessions once consent exists.
- Whether the age gate should also hide the nude *base body* sprites (breasts/chest shading);
  currently it only hides the genital layer, matching the "no genitals" requirement.
