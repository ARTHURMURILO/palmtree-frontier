using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

// Palmtree: shared events for the reactor deployment flatpacks. They live in the core construction
// namespace so Content.Server/_NSV13 can veto a multi-entity unpack before it spawns anything and
// receive the freshly spawned machine afterwards. Upstream only had the instant-unpack path.
namespace Content.Shared.Construction;

/// <summary>
///     Raised on a flatpack just before it unpacks, so that multi-entity assemblies can veto the placement.
/// </summary>
[ByRefEvent]
public struct FlatpackUnpackAttemptEvent
{
    public readonly EntityUid Flatpack;
    public readonly EntityUid User;
    public readonly EntityUid Grid;
    public readonly Vector2i Position;

    public bool Cancelled;
    public string? Message;

    public FlatpackUnpackAttemptEvent(EntityUid flatpack, EntityUid user, EntityUid grid, Vector2i position)
    {
        Flatpack = flatpack;
        User = user;
        Grid = grid;
        Position = position;
    }
}

/// <summary>
///     Raised on the freshly spawned entity once a flatpack has unpacked.
/// </summary>
public sealed class FlatpackUnpackedEvent : EntityEventArgs
{
    public readonly EntityUid Flatpack;
    public readonly EntityUid User;
    public readonly EntityUid Spawned;

    public FlatpackUnpackedEvent(EntityUid flatpack, EntityUid user, EntityUid spawned)
    {
        Flatpack = flatpack;
        User = user;
        Spawned = spawned;
    }
}

[Serializable, NetSerializable]
public sealed partial class FlatpackUnpackDoAfterEvent : SimpleDoAfterEvent
{
}
