using Robust.Shared.Prototypes;

namespace Content.Server.Construction.Components;

/// <summary>
///     Lets a deployed machine be packed back into the flatpack it came from.
///     Only the flatpack-spawned variants should carry this, so machines placed on maps cannot
///     mint free flatpacks.
/// </summary>
[RegisterComponent, Access(typeof(PackableIntoFlatpackSystem))]
public sealed partial class PackableIntoFlatpackComponent : Component
{
    /// <summary>
    ///     The flatpack prototype to spawn when this machine is packed up.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Flatpack = default!;

    /// <summary>
    ///     How long packing the machine up takes.
    /// </summary>
    [DataField]
    public TimeSpan PackDelay = TimeSpan.FromSeconds(5);
}
