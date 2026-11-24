using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Weapons.Melee;

/// <summary>
/// Sent by the server to clients in PVS when another player lashes with a whip.
/// </summary>
[Serializable, NetSerializable]
public sealed class WhipLashEvent : EntityEventArgs
{
    public NetEntity User;
    public NetEntity Weapon;
    public NetCoordinates Target;

    public WhipLashEvent(NetEntity user, NetEntity weapon, NetCoordinates target)
    {
        User = user;
        Weapon = weapon;
        Target = target;
    }
}
