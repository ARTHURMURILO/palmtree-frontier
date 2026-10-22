using Robust.Shared.GameStates;

namespace Content.Shared._PS.Light.Components;

/// <summary>
/// Palmtree: marks a light bulb (or a light fixture) as flickering.
/// When a marked bulb is inserted into a powered light, the server enables the fixture's
/// existing blinking visuals. A rave light takes priority over flickering.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FlickeringLightComponent : Component;
