using Content.Server.EUI;
using Content.Shared._PS.ShipRegistry;
using Content.Shared.Eui;
using Robust.Shared.Physics.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._PS.ShipRegistry;

/// <summary>
/// Palmtree: the ship preview EUI. It is the admin camera's mechanism, minus the admin powers: a
/// hidden camera entity is spawned at the ship, subscribed to the viewer's session (which also
/// force-sends the entity, overriding its visibility layer) and deleted when the window closes.
/// </summary>
public sealed partial class ShipPreviewEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedViewSubscriberSystem _viewSubscriber = default!;
    private readonly SharedPhysicsSystem _physics = default!;
    private readonly SharedMapSystem _map = default!;

    private static readonly EntProtoId CameraProtoId = "ShipPreviewCamera";

    private readonly EntityUid _target;
    private readonly Action? _onClosed;
    private EntityUid? _camera;

    /// <param name="target">The grid to look at.</param>
    /// <param name="onClosed">
    /// Called when the window closes, so a caller that owns the previewed grid (the orphan restore
    /// tool) can clean it up.
    /// </param>
    public ShipPreviewEui(EntityUid target, Action? onClosed = null)
    {
        IoCManager.InjectDependencies(this);

        _onClosed = onClosed;
        _viewSubscriber = _entityManager.System<SharedViewSubscriberSystem>();
        _physics = _entityManager.System<SharedPhysicsSystem>();
        _map = _entityManager.System<SharedMapSystem>();

        _target = target;
    }

    public override void Opened()
    {
        base.Opened();

        _camera = CreateCamera(_target, Player);
        StateDirty();
    }

    public override void Closed()
    {
        base.Closed();

        _entityManager.DeleteEntity(_camera);
        _onClosed?.Invoke();
    }

    public override EuiStateBase GetNewState()
    {
        var name = _entityManager.GetComponent<MetaDataComponent>(_target).EntityName;
        var size = _physics.GetWorldAABB(_target).Size;
        return new ShipPreviewEuiState(_entityManager.GetNetEntity(_camera!.Value), name, size, _timing.CurTick);
    }

    private EntityUid CreateCamera(EntityUid target, Robust.Shared.Player.ICommonSession observer)
    {
        // Center the camera on the ship rather than on the grid's origin.
        var aabb = _physics.GetWorldAABB(target);
        var mapId = _entityManager.GetComponent<TransformComponent>(target).MapID;
        var mapUid = _map.GetMap(mapId);
        var camera = _entityManager.SpawnAttachedTo(CameraProtoId, new EntityCoordinates(mapUid, aabb.Center));

        // Allow the user to see entities near the camera and force-send the camera itself.
        _viewSubscriber.AddViewSubscriber(camera, observer);

        return camera;
    }
}
