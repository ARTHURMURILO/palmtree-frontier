using Robust.Shared.GameStates;

namespace Content.Shared._PS.Modsuits;

/// <summary>
/// A deployed MODsuit part. Owned by a <see cref="ModsuitControlComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ModsuitPartComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Control;

    [DataField, AutoNetworkedField]
    public ModsuitPartSlot Slot;

    [DataField, AutoNetworkedField]
    public bool Sealed;
}
