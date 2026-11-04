using Robust.Shared.GameStates;

namespace Content.Shared._PS.Light.Components;

/// <summary>
/// Palmtree: marks a powered light fixture as flickering.
/// The fixture owns the effect; the bulb inside it only determines the light's
/// color and point-light values. A rave light takes priority over flickering.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FlickeringLightComponent : Component;
