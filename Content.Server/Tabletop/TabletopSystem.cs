using Content.Server.Popups;
using Content.Server.Tabletop.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Tabletop;
using Content.Shared.Tabletop.Components;
using Content.Shared.Tabletop.Events;
using Content.Shared.Verbs;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Tabletop
{
    [UsedImplicitly]
    public sealed partial class TabletopSystem : SharedTabletopSystem
    {
        [Dependency] private readonly SharedMapSystem _map = default!;
        [Dependency] private readonly EyeSystem _eye = default!;
        [Dependency] private readonly ViewSubscriberSystem _viewSubscriberSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
        [Dependency] private readonly MetaDataSystem _meta = default!;

        /// <summary>
        /// Palmtree: maximum number of placed pieces per board. Bounds the entity spam that
        /// originally forced this feature off.
        /// </summary>
        private const int MaxHolograms = 50;

        /// <summary>
        /// Palmtree: sanitized placeholder spawned for every item placed on a board.
        /// </summary>
        private static readonly EntProtoId HologramPrototype = "PalmtreeTabletopHologram";

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<TabletopStopPlayingEvent>(OnStopPlaying);
            SubscribeLocalEvent<TabletopGameComponent, ActivateInWorldEvent>(OnTabletopActivate);
            SubscribeLocalEvent<TabletopGameComponent, ComponentShutdown>(OnGameShutdown);
            SubscribeLocalEvent<TabletopGamerComponent, PlayerDetachedEvent>(OnPlayerDetached);
            SubscribeLocalEvent<TabletopGamerComponent, ComponentShutdown>(OnGamerShutdown);
            SubscribeLocalEvent<TabletopGameComponent, GetVerbsEvent<ActivationVerb>>(AddPlayGameVerb);
            SubscribeLocalEvent<TabletopGameComponent, InteractUsingEvent>(OnInteractUsing);

            SubscribeNetworkEvent<TabletopRequestTakeOut>(OnTabletopRequestTakeOut);

            InitializeMap();
        }

        private void OnTabletopRequestTakeOut(TabletopRequestTakeOut msg, EntitySessionEventArgs args)
        {
            if (args.SenderSession is not { } playerSession)
                return;

            if (!TryGetEntity(msg.TableUid, out var table))
                return;

            if (!TryComp(table.Value, out TabletopGameComponent? tabletop) || tabletop.Session is not { } session)
                return;

            if (!TryGetEntity(msg.Entity, out var entity))
                return;

            if (!HasComp<TabletopHologramComponent>(entity.Value))
            {
                _popupSystem.PopupEntity(Loc.GetString("tabletop-error-remove-non-hologram"), table.Value, playerSession);
                return;
            }

            // Check if player is actually playing at this table
            if (!session.Players.ContainsKey(playerSession))
                return;

            // Only remove pieces that actually belong to this board.
            if (!session.Entities.Remove(entity.Value))
                return;

            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{playerSession:player} removed tabletop piece {ToPrettyString(entity.Value)} from {ToPrettyString(table.Value)}");

            QueueDel(entity.Value);
        }

        private void OnInteractUsing(EntityUid uid, TabletopGameComponent component, InteractUsingEvent args)
        {
            if (!_cfg.GetCVar(CCVars.GameTabletopPlace))
                return;

            if (!HasComp<HandsComponent>(args.User))
                return;

            if (component.Session is not { } session)
                return;

            if (!HasComp<ItemComponent>(args.Used))
                return;

            // Palmtree: cap placed pieces per board so a malicious client cannot flood the tabletop map.
            var holograms = 0;
            foreach (var sessionEntity in session.Entities)
            {
                if (HasComp<TabletopHologramComponent>(sessionEntity))
                    holograms++;
            }

            if (holograms >= MaxHolograms)
            {
                _popupSystem.PopupEntity(Loc.GetString("tabletop-cant-add-more"), uid, args.User);
                return;
            }

            var originalMeta = MetaData(args.Used);
            var protoId = originalMeta.EntityPrototype?.ID;

            // Palmtree: spawn a sanitized dummy instead of a copy of the item. Copying arbitrary
            // item prototypes let their components (storage, explosives, tools, etc.) into the
            // tabletop map, which was the original crash/abuse vector. The client mirrors the
            // source sprite from appearance data instead.
            var hologram = Spawn(HologramPrototype, session.Position.Offset(-1, 0));

            // Make sure the entity can be dragged and can be removed, move it into the board game world and add it to the Entities hashmap
            EnsureComp<TabletopDraggableComponent>(hologram);
            EnsureComp<TabletopHologramComponent>(hologram);
            session.Entities.Add(hologram);

            _meta.SetEntityName(hologram, originalMeta.EntityName);

            if (protoId != null)
                _appearance.SetData(hologram, TabletopItemVisuals.Prototype, protoId);

            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(args.User):player} placed {ToPrettyString(args.Used):subject} onto tabletop board {ToPrettyString(uid)}");

            _popupSystem.PopupEntity(Loc.GetString("tabletop-added-piece"), uid, args.User);
        }

        protected override void OnTabletopMove(TabletopMoveEvent msg, EntitySessionEventArgs args)
        {
            if (args.SenderSession is not { } playerSession)
                return;

            if (!TryComp(GetEntity(msg.TableUid), out TabletopGameComponent? tabletop) || tabletop.Session is not { } session)
                return;

            // Check if player is actually playing at this table
            if (!session.Players.ContainsKey(playerSession))
                return;

            base.OnTabletopMove(msg, args);
        }

        /// <summary>
        /// Add a verb that allows the player to start playing a tabletop game.
        /// </summary>
        private void AddPlayGameVerb(EntityUid uid, TabletopGameComponent component, GetVerbsEvent<ActivationVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            if (!TryComp(args.User, out ActorComponent? actor))
                return;

            var playVerb = new ActivationVerb()
            {
                Text = Loc.GetString("tabletop-verb-play-game"),
                Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/die.svg.192dpi.png")),
                Act = () => OpenSessionFor(actor.PlayerSession, uid)
            };

            args.Verbs.Add(playVerb);
        }

        private void OnTabletopActivate(EntityUid uid, TabletopGameComponent component, ActivateInWorldEvent args)
        {
            if (args.Handled || !args.Complex)
                return;

            // Check that a player is attached to the entity.
            if (!TryComp(args.User, out ActorComponent? actor))
                return;

            OpenSessionFor(actor.PlayerSession, uid);
        }

        private void OnGameShutdown(EntityUid uid, TabletopGameComponent component, ComponentShutdown args)
        {
            CleanupSession(uid);
        }

        private void OnStopPlaying(TabletopStopPlayingEvent msg, EntitySessionEventArgs args)
        {
            CloseSessionFor(args.SenderSession, GetEntity(msg.TableUid));
        }

        private void OnPlayerDetached(EntityUid uid, TabletopGamerComponent component, PlayerDetachedEvent args)
        {
            if(component.Tabletop.IsValid())
                CloseSessionFor(args.Player, component.Tabletop);
        }

        private void OnGamerShutdown(EntityUid uid, TabletopGamerComponent component, ComponentShutdown args)
        {
            if (!TryComp(uid, out ActorComponent? actor))
                return;

            if(component.Tabletop.IsValid())
                CloseSessionFor(actor.PlayerSession, component.Tabletop);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<TabletopGamerComponent>();
            while (query.MoveNext(out var uid, out var gamer))
            {
                if (!Exists(gamer.Tabletop))
                    continue;

                if (!TryComp(uid, out ActorComponent? actor))
                {
                    RemComp<TabletopGamerComponent>(uid);
                    return;
                }

                if (actor.PlayerSession.Status != SessionStatus.InGame || !CanSeeTable(uid, gamer.Tabletop))
                    CloseSessionFor(actor.PlayerSession, gamer.Tabletop);
            }
        }
    }
}
