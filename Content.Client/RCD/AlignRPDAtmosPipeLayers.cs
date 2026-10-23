using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Hands.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.Utility;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.Placement.PlacementManager;

namespace Content.Client.RCD;

/// <summary>
/// RPD placement mode. Unlike the plain RCD placer, the atmos pipe layer is chosen from where
/// inside the tile the cursor sits: the middle quarter is the primary layer, and the two halves
/// either side of it are the secondary and tertiary layers.
/// </summary>
/// <remarks>
/// Palmtree: ported from the old Coyote code.
/// </remarks>
public sealed class AlignRPDAtmosPipeLayers : PlacementMode
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IEntityNetworkManager _entityNetwork = default!;

    // Resolved through the entity system manager, not injected: HandsSystem carries no
    // [RegisterDependency], so a [Dependency] field on it throws UnregisteredDependencyException
    // when the placement manager constructs this mode. Same approach as AlignRCDConstruction.
    private readonly HandsSystem _hands;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly SharedAtmosPipeLayersSystem _pipeLayersSystem;
    private readonly SpriteSystem _spriteSystem;
    private readonly RCDSystem _rcdSystem;

    private const float SearchBoxSize = 2f;
    private const float MouseDeadzoneRadius = 0.25f;
    private const float PlaceColorBaseAlpha = 0.5f;
    private const float GuideRadius = 0.1f;
    private const float GuideOffset = 0.21875f;

    private EntityCoordinates _mouseCoordsRaw = default;
    private AtmosPipeLayer _currentLayer = AtmosPipeLayer.Primary;
    private float? _currentEyeRotation;
    private readonly Color _guideColor = new(0, 0, 0.5785f);

    public AlignRPDAtmosPipeLayers(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);

        _hands = _entityManager.System<HandsSystem>();
        _mapSystem = _entityManager.System<SharedMapSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _rcdSystem = _entityManager.System<RCDSystem>();
        _pipeLayersSystem = _entityManager.System<SharedAtmosPipeLayersSystem>();

        ValidPlaceColor = ValidPlaceColor.WithAlpha(PlaceColorBaseAlpha);
    }

    public override void Render(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalSession?.AttachedEntity is not { } player ||
            !_entityManager.TryGetComponent<TransformComponent>(player, out var xform) ||
            !_transformSystem.InRange(xform.Coordinates, MouseCoords, SharedInteractionSystem.InteractionRange))
        {
            return;
        }

        var gridUid = _transformSystem.GetGrid(MouseCoords);

        if (gridUid == null || !_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return;

        // Draw the three layer guides unless we are in line/grid placing mode
        if (pManager.PlacementType == PlacementTypes.None)
        {
            var gridRotation = _transformSystem.GetWorldRotation(gridUid.Value);
            var worldPosition = _mapSystem.LocalToWorld(gridUid.Value, grid, MouseCoords.Position);
            var direction = (_eyeManager.CurrentEye.Rotation + gridRotation + Math.PI / 2).GetCardinalDir();
            var multi = (direction == Direction.North || direction == Direction.South) ? -1f : 1f;

            args.WorldHandle.DrawCircle(worldPosition, GuideRadius, _guideColor);
            args.WorldHandle.DrawCircle(worldPosition + gridRotation.RotateVec(new Vector2(multi * GuideOffset, GuideOffset)), GuideRadius, _guideColor);
            args.WorldHandle.DrawCircle(worldPosition - gridRotation.RotateVec(new Vector2(multi * GuideOffset, GuideOffset)), GuideRadius, _guideColor);
        }

        base.Render(args);
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        _mouseCoordsRaw = ScreenToCursorGrid(mouseScreen);
        MouseCoords = _mouseCoordsRaw.AlignWithClosestGridTile(SearchBoxSize, _entityManager, _mapManager);

        var gridId = _transformSystem.GetGrid(MouseCoords);

        if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var mapGrid))
            return;

        CurrentTile = _mapSystem.GetTileRef(gridId.Value, mapGrid, MouseCoords);

        // TileSize is a ushort, so this must be a float: with `var` the `/ 2` below is integer
        // division and evaluates to 0, which snaps placement to the tile corner instead of its centre.
        float tileSize = mapGrid.TileSize;
        GridDistancing = tileSize;

        var tileCenter = new Vector2(CurrentTile.X + tileSize / 2, CurrentTile.Y + tileSize / 2);

        MouseCoords = pManager.CurrentPermission!.IsTile
            ? new EntityCoordinates(MouseCoords.EntityId, tileCenter)
            : new EntityCoordinates(MouseCoords.EntityId, tileCenter + pManager.PlacementOffset);

        // Work out the layer from the cursor offset from the centre of the tile. Measure from the
        // true centre, not the (admin-offset) placement point, to match the server's calculation.
        var mouseCoordsDiff = _mouseCoordsRaw.Position - tileCenter;
        var newLayer = AtmosPipeLayer.Primary;

        if (mouseCoordsDiff.Length() > MouseDeadzoneRadius)
        {
            var gridRotation = _transformSystem.GetWorldRotation(gridId.Value);
            var rawAngle = new Angle(mouseCoordsDiff);
            var eyeRotation = _eyeManager.CurrentEye.Rotation;
            var direction = (rawAngle + eyeRotation + gridRotation + Math.PI / 2).GetCardinalDir();
            newLayer = (direction == Direction.North || direction == Direction.East)
                ? AtmosPipeLayer.Secondary
                : AtmosPipeLayer.Tertiary;
        }

        // Only track the layer / forward the eye rotation when the player is in range and holding an RPD
        if (_playerManager.LocalSession?.AttachedEntity is { } player &&
            _entityManager.TryGetComponent<TransformComponent>(player, out var xform) &&
            _transformSystem.InRange(xform.Coordinates, MouseCoords, SharedInteractionSystem.InteractionRange) &&
            _hands.GetActiveItem(player) is { } heldEntity &&
            _entityManager.HasComponent<RCDComponent>(heldEntity))
        {
            _currentLayer = newLayer;
            UpdateEyeRotation(heldEntity, _eyeManager.CurrentEye.Rotation);
        }

        UpdatePlacer(_currentLayer);
    }

    /// <summary>
    /// The player's eye rotation is not networked, but the server needs it to resolve the same
    /// pipe layer the client is previewing, so forward it whenever it changes.
    /// </summary>
    private void UpdateEyeRotation(EntityUid heldEntity, Angle eyeRotation)
    {
        if (_currentEyeRotation == eyeRotation.Theta)
            return;

        _currentEyeRotation = (float) eyeRotation.Theta;
        _entityNetwork.SendSystemNetworkMessage(new RPDEyeRotationEvent(_entityManager.GetNetEntity(heldEntity), _currentEyeRotation));
    }

    private void UpdatePlacer(AtmosPipeLayer layer)
    {
        if (pManager.CurrentPermission?.EntityType == null)
            return;

        if (!_protoManager.TryIndex<EntityPrototype>(pManager.CurrentPermission.EntityType, out var currentProto))
            return;

        if (!currentProto.TryGetComponent<AtmosPipeLayersComponent>(out var atmosPipeLayers, _entityManager.ComponentFactory))
            return;

        if (!_pipeLayersSystem.TryGetAlternativePrototype(atmosPipeLayers, layer, out var newProtoId))
            return;

        if (!_protoManager.TryIndex<EntityPrototype>(newProtoId, out var newProto))
            return;

        pManager.CurrentPermission.EntityType = newProtoId;

        if (!newProto.TryGetComponent<SpriteComponent>(out var sprite, _entityManager.ComponentFactory))
            return;

        var textures = new List<IDirectionalTextureProvider>();

        foreach (var spriteLayer in sprite.AllLayers)
        {
            if (spriteLayer.ActualRsi?.Path != null && spriteLayer.RsiState.Name != null)
                textures.Add(_spriteSystem.RsiStateLike(new SpriteSpecifier.Rsi(spriteLayer.ActualRsi.Path, spriteLayer.RsiState.Name)));
        }

        pManager.CurrentTextures = textures;
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        var player = _playerManager.LocalSession?.AttachedEntity;

        if (!_entityManager.TryGetComponent<TransformComponent>(player, out var xform))
            return false;

        if (!_transformSystem.InRange(xform.Coordinates, position, SharedInteractionSystem.InteractionRange))
        {
            InvalidPlaceColor = InvalidPlaceColor.WithAlpha(0);
            return false;
        }

        InvalidPlaceColor = InvalidPlaceColor.WithAlpha(PlaceColorBaseAlpha);

        if (_hands.GetActiveItem(player!.Value) is not { } heldEntity)
            return false;

        if (!_entityManager.TryGetComponent<RCDComponent>(heldEntity, out var rcd))
            return false;

        var gridUid = _transformSystem.GetGrid(position);

        if (!_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var mapGrid))
            return false;

        var tile = _mapSystem.GetTileRef(gridUid.Value, mapGrid, position);
        var posVector = _mapSystem.TileIndicesFor(gridUid.Value, mapGrid, position);

        if (_stateManager.CurrentState is not GameplayStateBase screen)
            return false;

        var target = screen.GetClickedEntity(_transformSystem.ToMapCoordinates(_mouseCoordsRaw));

        return _rcdSystem.IsRCDOperationStillValid(heldEntity, rcd, gridUid.Value, mapGrid, tile, posVector, target, player.Value, false);
    }
}
