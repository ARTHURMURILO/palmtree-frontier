using Content.Shared.Movement.Systems;

namespace Content.Shared._PS.Roomba;

/// <summary>
/// Palmtree: shared rules for roombas that both sides need to agree on.
/// </summary>
public sealed class SharedRoombaSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // A roomba is a harmless little appliance: it never shoves mobs around. This is the same
        // trick prone mobs use (StandingStateSystem) - cancelling the target-side collision means
        // nobody pushes themselves away from a roomba. It has to live in shared because clients
        // predict their own mob pushes and send them to the server.
        SubscribeLocalEvent<RoombaComponent, AttemptMobTargetCollideEvent>(OnAttemptMobTargetCollide);
    }

    private void OnAttemptMobTargetCollide(Entity<RoombaComponent> ent, ref AttemptMobTargetCollideEvent args)
    {
        args.Cancelled = true;
    }
}
