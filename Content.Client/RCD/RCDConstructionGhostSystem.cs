using Content.Client.Hands.Systems;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;

namespace Content.Client.RCD;

/// <summary>
/// System for handling structure ghost placement in places where RCD can create objects.
/// </summary>
public sealed class RCDConstructionGhostSystem : EntitySystem
{
    private const string PlacementMode = nameof(AlignRCDConstruction);
    private const string RpdPlacementMode = nameof(AlignRPDAtmosPipeLayers); // Palmtree: RPD pipe layer placement

    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPlacementManager _placementManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    
    private Direction _placementDirection = default;
    private bool _useMirrorPrototype = false; // Palmtree: mirror prototype flip

    // Palmtree: the prototype we asked the placer to place, ignoring the layer swap
    // AlignRPDAtmosPipeLayers applies to the placer's own permission.
    private string? _placerBaseProto;

    public override void Initialize()
    {
        base.Initialize();

        // Palmtree: mirror prototype flip (ported from the old Coyote code)
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.EditorFlipObject,
                new PointerInputCmdHandler(HandleFlip, outsidePrediction: true))
            .Register<RCDConstructionGhostSystem>();
    }

    public override void Shutdown()
    {
        // Palmtree: mirror prototype flip
        CommandBinds.Unregister<RCDConstructionGhostSystem>();
        base.Shutdown();
    }

    // Palmtree: mirror prototype flip (ported from the old Coyote code)
    private bool HandleFlip(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.State == BoundKeyState.Down)
        {
            if (!_placementManager.IsActive || _placementManager.Eraser)
                return false;

            var placerEntity = _placementManager.CurrentPermission?.MobUid;

            if (!TryComp<RCDComponent>(placerEntity, out var rcd))
                return false;

            var prototype = _protoManager.Index(rcd.ProtoId);
            if (string.IsNullOrEmpty(prototype.MirrorPrototype))
                return false;

            _useMirrorPrototype = !rcd.UseMirrorPrototype;

            var useProto = _useMirrorPrototype ? prototype.MirrorPrototype : prototype.Prototype;
            CreatePlacer(placerEntity.Value, rcd, useProto, prototype.Mode);

            RaiseNetworkEvent(new RCDConstructionGhostFlipEvent(GetNetEntity(placerEntity.Value), _useMirrorPrototype));
        }

        return true;
    }
    // Palmtree end

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Get current placer data
        var placerEntity = _placementManager.CurrentPermission?.MobUid;
        var placerIsRCD = HasComp<RCDComponent>(placerEntity);

        // Exit if erasing or the current placer is not an RCD (build mode is active)
        if (_placementManager.Eraser || (placerEntity != null && !placerIsRCD))
            return;

        // Determine if player is carrying an RCD in their active hand
        if (_playerManager.LocalSession?.AttachedEntity is not { } player)
            return;

        var heldEntity = _hands.GetActiveItem(player);

        if (!TryComp<RCDComponent>(heldEntity, out var rcd))
        {
            // If the player was holding an RCD, but is no longer, cancel placement
            if (placerIsRCD)
                _placementManager.Clear();

            return;
        }
        var prototype = _protoManager.Index(rcd.ProtoId);

        // Update the direction the RCD prototype based on the placer direction
        if (_placementDirection != _placementManager.Direction)
        {
            _placementDirection = _placementManager.Direction;
            RaiseNetworkEvent(new RCDConstructionGhostRotationEvent(GetNetEntity(heldEntity.Value), _placementDirection));
        }

        // Palmtree: pick the mirror prototype when it is enabled (ported from the old Coyote code)
        var useProto = _useMirrorPrototype && !string.IsNullOrEmpty(prototype.MirrorPrototype)
            ? prototype.MirrorPrototype
            : prototype.Prototype;

        // If the placer has not changed, exit
        if (heldEntity == placerEntity && useProto == _placerBaseProto)
            return;

        // Palmtree: the RPD picks an atmos pipe layer from where the cursor sits inside the tile,
        // so it needs its own placement mode (ported from the old Coyote code)
        if (rcd.IsRpd && useProto != null && !prototype.NoLayers && prototype.Mode == RcdMode.ConstructObject)
        {
            CreateLayeredPlacer(heldEntity.Value, rcd, useProto);
            return;
        }

        // Create a new placer
        CreatePlacer(heldEntity.Value, rcd, useProto, prototype.Mode);
        // Palmtree end
    }

    private void CreatePlacer(EntityUid uid, RCDComponent component, string? prototype, RcdMode mode)
    {
        var newObjInfo = new PlacementInformation
        {
            MobUid = uid,
            PlacementOption = PlacementMode,
            EntityType = prototype,
            Range = (int) Math.Ceiling(SharedInteractionSystem.InteractionRange),
            IsTile = (mode == RcdMode.ConstructTile),
            UseEditorContext = false,
        };

        _placementManager.Clear();
        _placementManager.BeginPlacing(newObjInfo);

        _placerBaseProto = prototype;
    }

    // Palmtree: RPD pipe layer placement (ported from the old Coyote code)
    private void CreateLayeredPlacer(EntityUid uid, RCDComponent component, string prototype)
    {
        var rcdProto = _protoManager.Index(component.ProtoId);
        var newObjInfo = new PlacementInformation
        {
            MobUid = uid,
            PlacementOption = RpdPlacementMode,
            EntityType = prototype,
            Range = (int) Math.Ceiling(SharedInteractionSystem.InteractionRange),
            IsTile = rcdProto.Mode == RcdMode.ConstructTile,
            UseEditorContext = false,
        };

        _placementManager.Clear();
        _placementManager.BeginPlacing(newObjInfo);

        _placerBaseProto = prototype;
    }
}
