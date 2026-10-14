using Robust.Shared.GameStates;

namespace Content.Shared._PS.Power;

/// <summary>
/// Palmtree: marks a fuel generator whose point light should only be lit while the generator is
/// actually running (e.g. the B.L.Y.A.T.M.A.N. generator's purple cone light). The prototype keeps
/// full control of the light's colour/mask/radius; <c>GeneratorRunningLightSystem</c> only toggles
/// <c>Enabled</c> to match <see cref="Content.Shared.Power.Generator.FuelGeneratorComponent.On"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GeneratorRunningLightComponent : Component;
