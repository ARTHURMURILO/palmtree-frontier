# EVENTS

> How the engine's event bus works and the events that matter most here. Verify against
> `RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.*.cs`.

## Dispatch model

| Raise call | Reaches |
|---|---|
| `RaiseLocalEvent<T>(msg)` | system handlers `SubscribeLocalEvent<T>` (broadcast) |
| `RaiseLocalEvent<T>(uid, msg)` | **directed** handlers `SubscribeLocalEvent<TComp, TEvent>` on `uid` |
| `RaiseLocalEvent<T>(uid, msg, broadcast: true)` | both directed and broadcast |
| `RaiseNetworkEvent(msg, filter)` | client/server network subscriptions (`SubscribeNetworkEvent`) |
| `RaiseAllEvent` | all systems, no ordering guarantees |

Key gotcha: `RaiseLocalEvent<T>(msg)` does **not** call component handlers. Many systems depend on
this; if a handler "doesn't fire", check the raise overload.

Subscriptions happen in `Initialize()` and are locked afterwards. Use `Subs.*` helpers for CVar and
C# events so they unsubscribe on shutdown.

## Lifecycle events

| Event | When |
|---|---|
| `ComponentInit` / `ComponentStartup` / `ComponentShutdown` / `ComponentRemove` | component lifecycle |
| `MapInitEvent` | entity is initialized on a map (not re-run on deserialization unless forced) |
| `EntityTerminatingEvent` | entity is being deleted |
| `PlayerAttachedEvent` / `PlayerDetachedEvent` | session attaches/detaches to an entity |
| `RoundStartingEvent` / `RoundStartedEvent` / `RoundEndTextAppendEvent` / `RoundRestartCleanupEvent` | round flow |
| `GameRunLevelChangedEvent` | lobby ↔ in-round ↔ post-round |
| `RulePlayerSpawningEvent` / `RulePlayerJobsAssignedEvent` | spawning hooks |
| `GameRuleAddedEvent` / `GameRuleStartedEvent` / `GameRuleEndedEvent` | rule lifecycle |
| `PrototypesReloadedEventArgs` | prototype hot reload (use this, not `PrototypeReloadEvent`) |
| `AfterAutoHandleStateEvent` | client finished applying auto-networked component state |

There is no `RoundEndedEvent`; use `RoundEndMessageEvent` + `GameRunLevelChangedEvent`.

## Events used by the ported systems

| Event | Consumer | Purpose |
|---|---|---|
| `GetVerbsEvent<Verb>` | `ModifyUndiesSystem` | Adds show/hide verbs for markings with the toggle opt-in |
| `ModifyUndiesDoAfterEvent` | `ModifyUndiesSystem` | Completes the visibility toggle |
| `GetItemActionsEvent` | `SharedConcealableClothingSystem` | Grants concealment action to equipped clothing |
| `ToggleClothingConcealmentEvent` | `SharedConcealableClothingSystem` | Toggles concealment |
| `EquipmentVisualsUpdatedEvent` / `GetEquipmentVisualsEvent` | client concealable system | Removes/clears equipment layers while concealed |
| `ImplantImplantedEvent` | server concealable system | Registers the implant's concealment category |
| `EntGotRemovedFromContainerMessage` | server concealable system | Removes the category when the implant leaves |
| `ExaminedEvent` / `ExamineCompletedEvent` | examine pipeline | No ERP contributors registered in this repo |
| `TickerLobbyStatusEvent` | `LobbyState` | Updates lobby UI + background |
| `AfterAutoHandleStateEvent` | client humanoid system | Re-renders on marking/state changes |
| `FlatpackUnpackAttemptEvent` | `FlatpackPlacementSystem`; raised by `SharedFlatpackSystem` | By-ref veto before unpacking: set `Cancelled` + `Message` (built from the footprint reason key) |
| `FlatpackUnpackedEvent` | `FlatpackPlacementSystem` (anchor), `TurbineAssemblySystem` (site works) | Broadcast on the spawned entity after unpack |

Both flatpack events are declared in `Content.Shared/_PS/Construction/FlatpackEvents.cs`. The
attempt event is raised directed on the flatpack by `Content.Shared/Construction/SharedFlatpackSystem.cs`,
which pops the vetoer's `Message`; the server footprint handler is
`Content.Server/_PS/Construction/FlatpackPlacementSystem.cs`, and the `CanPlace` check it shares
with the client ghost is `Content.Shared/_PS/Construction/SharedFlatpackPlacementSystem.cs`.

## Marking-related event/data notes

- Marking changes are not events; they mutate `MarkingSet` on `HumanoidAppearanceComponent` and
  `Dirty` the component. The client re-renders via `AfterAutoHandleStateEvent`.
- `SetMarkingVisibility` is called directly by `ModifyUndies` after the do-after.
- Profile changes flow through preferences messages, not gameplay events.

## Gotchas

- Paused entities are skipped by `EntityQueryEnumerator<T>`; use `AllEntityQueryEnumerator<T>` if
  you need them.
- `Dirty(uid, component)` is required after mutating networked state; nested mutations inside
  auto-networked collections still rely on a component dirty.
- Directed events for components require the component to be on the target entity.
- Do not subscribe outside `Initialize()` (subscription lock).
- Shared attack/action hooks are predicted: they run on the client, can replay after a server
  state, and run again on the server. Client-only visuals must be spawned from a client
  override guarded by `Timing.IsFirstTimePredicted`, and server relays must target
  `Filter.PvsExcept(user)`; clients should also ignore the relay for `IPlayerManager.LocalEntity`
  and dedupe delayed duplicates. See `.ai/HAZARDS.md` §30.
