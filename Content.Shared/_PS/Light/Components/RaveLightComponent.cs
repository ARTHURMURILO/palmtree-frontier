using Robust.Shared.GameStates;

namespace Content.Shared._PS.Light.Components;

/// <summary>
/// Palmtree: makes a light fixture continuously pulse between its normal brightness and fully off.
/// The pulse is a triangle wave: 100%, 90%, ... 0%, then immediately 90%, 100%, 90%, ...
/// The fixture owns the effect; its bulb only determines the light's color.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RaveLightComponent : Component
{
    /// <summary>
    /// The pulse periods a screwdriver cycles through, in seconds per full bright-dim-bright cycle.
    /// </summary>
    public static readonly float[] Periods = { 0.5f, 1f, 2f };

    /// <summary>
    /// Length of one full pulse cycle in seconds. Changed with a screwdriver.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Period = 1f;

    /// <summary>
    /// Peak point-light energy. Kept on the fixture so the bulb cannot change the pulse.
    /// If zero, the light's current energy is captured when it first pulses.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxEnergy;

    /// <summary>
    /// Phase offset in cycles. Used to make otherwise identical lights pulse out of sync.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Phase;

    /// <summary>
    /// Client-side cache of the point light's original energy, used when <see cref="MaxEnergy"/> is zero.
    /// </summary>
    [ViewVariables]
    public float CachedBaseEnergy;
}
