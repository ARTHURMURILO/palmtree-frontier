using System.Linq;
using System.Numerics;
using Content.Client.Hands.Systems;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ComponentTrees;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Funkystation.Construction;

/// <summary>
///     Draws a ghost of the machine a multi-tile flatpack will unpack into while the player hovers
///     the flatpack with a pulsing tool. The ghost is green when the footprint fits and red when it
///     does not, and turns with the flatpack so the player can line it up before committing.
/// </summary>
public sealed class FlatpackPlacementGhostSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SpriteTreeSystem _spriteTree = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedFlatpackPlacementSystem _placement = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly Color ValidColor = new(48, 255, 48, 128);
    private static readonly Color InvalidColor = new(255, 48, 48, 128);
    private static readonly EntProtoId GhostPrototype = "FlatpackPlacementGhost";
    private static readonly ProtoId<ToolQualityPrototype> PulsingQuality = "Pulsing";

    private EntityUid? _ghost;
    private EntProtoId? _ghostEntity;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_player.LocalEntity is not { } player)
        {
            Clear();
            return;
        }

        if (FindPulsingTool(player) == null)
        {
            Clear();
            return;
        }

        if (!_input.MouseScreenPosition.IsValid)
        {
            Clear();
            return;
        }

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId == MapId.Nullspace)
        {
            Clear();
            return;
        }

        var flatpack = FindFlatpack(mouse);
        if (flatpack is not { } target)
        {
            Clear();
            return;
        }

        if (_containers.IsEntityInContainer(target))
        {
            Clear();
            return;
        }

        var xform = Transform(target);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            Clear();
            return;
        }

        if (!TryComp<FlatpackFootprintComponent>(target, out var footprint) ||
            !TryComp<FlatpackComponent>(target, out var flatpackComp) ||
            flatpackComp.Entity is not { } entity)
        {
            Clear();
            return;
        }

        var position = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var direction = xform.LocalRotation.GetCardinalDir();
        var valid = _placement.CanPlace((target, footprint), gridUid, grid, position, out _);

        if (_ghost is not { } ghost || _ghostEntity != entity)
        {
            ClearGhost();
            ghost = CreateGhost(target, entity, _map.GridTileToLocal(gridUid, grid, position), direction);
            if (ghost == EntityUid.Invalid)
            {
                Clear();
                return;
            }

            _ghost = ghost;
            _ghostEntity = entity;
        }

        _transform.SetCoordinates(ghost, _map.GridTileToLocal(gridUid, grid, position));
        _transform.SetLocalRotation(ghost, direction.ToAngle());

        if (TryComp<SpriteComponent>(ghost, out var sprite))
            _sprite.SetColor((ghost, sprite), valid ? ValidColor : InvalidColor);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearGhost();
    }

    /// <summary>
    ///     Finds a pulsing tool in any hand, not just the active one: the ghost is a preview, and
    ///     players often keep the multitool in the off-hand while lining a flatpack up.
    /// </summary>
    private EntityUid? FindPulsingTool(EntityUid player)
    {
        if (!TryComp<HandsComponent>(player, out var hands))
            return null;

        foreach (var hand in hands.Hands.Keys)
        {
            var held = _hands.GetHeldItem((player, hands), hand);
            if (held is { } item && _tool.HasQuality(item, PulsingQuality.Id))
                return item;
        }

        return null;
    }

    private EntityUid? FindFlatpack(MapCoordinates mouse)
    {
        // Use the sprite tree rather than physics: the flatpack is a small floor item whose physics
        // fixture can go to sleep, and the sprite tree is what the client uses to click things.
        // Query a point, not a tile-sized box: a 1x1 box would trigger in the whole 3x3 around the
        // flatpack. With approximate bounds off, only sprites actually under the cursor match.
        var bounds = Box2.CenteredAround(mouse.Position, new Vector2(0.05f, 0.05f));
        foreach (var entry in _spriteTree.QueryAabb(mouse.MapId, bounds, approx: false))
        {
            if (HasComp<FlatpackFootprintComponent>(entry.Uid) && HasComp<FlatpackComponent>(entry.Uid))
                return entry.Uid;
        }

        return null;
    }

    private EntityUid CreateGhost(EntityUid flatpack, EntProtoId entity, EntityCoordinates coords, Direction direction)
    {
        if (!_proto.TryIndex(entity, out EntityPrototype? targetProto) ||
            !targetProto.Components.ContainsKey("Sprite"))
        {
            return EntityUid.Invalid;
        }

        var ghost = Spawn(GhostPrototype, coords);
        var ghostSprite = EnsureComp<SpriteComponent>(ghost);
        var ghostXform = Transform(ghost);
        ghostXform.LocalRotation = direction.ToAngle();

        var dummy = Spawn(entity, MapCoordinates.Nullspace);
        if (!TryComp<SpriteComponent>(dummy, out var targetSprite))
        {
            Del(dummy);
            QueueDel(ghost);
            return EntityUid.Invalid;
        }

        _appearance.OnChangeData(dummy, targetSprite);
        _sprite.CopySprite((dummy, targetSprite), (ghost, ghostSprite));
        for (var i = 0; i < ghostSprite.AllLayers.Count(); i++)
        {
            ghostSprite.LayerSetShader(i, "unshaded");
        }

        ghostSprite.Offset = targetSprite.Offset;
        Del(dummy);
        return ghost;
    }


    private void Clear()
    {
        ClearGhost();
    }

    private void ClearGhost()
    {
        if (_ghost is { } ghost)
            QueueDel(ghost);
        _ghost = null;
        _ghostEntity = null;
    }
}
