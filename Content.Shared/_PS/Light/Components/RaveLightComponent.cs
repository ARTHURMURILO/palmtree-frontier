using Robust.Shared.GameStates;

namespace Content.Shared._PS.Light.Components;

/// <summary>
/// Palmtree: makes a light continuously pulse between its normal brightness and fully off.
/// The pulse is a triangle wave: 100%, 90%, ... 0%, then immediately 90%, 100%, 90%, ...
/// Can be put on a bulb (so any fixture it is inserted into pulses) or directly on a fixture.
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
    /// Peak point-light energy. If zero, the light's energy is captured when it first pulses.
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
