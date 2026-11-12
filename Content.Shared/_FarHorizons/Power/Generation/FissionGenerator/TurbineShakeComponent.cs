// SPDX-FileCopyrightText: 2025 jhrushbe <capnmerry@gmail.com>
// SPDX-License-Identifier: CC-BY-NC-SA-3.0

using Robust.Shared.GameStates;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     High-frequency, sprite-only vibration for a turbine that has gone past its safe overspeed.
/// </summary>
/// <remarks>
///     The generic <see cref="Content.Shared.Jittering.JitteringComponent"/> is deliberately not
///     used here: its minimum amplitude is about a tenth of a tile, and at turbine scale that reads
///     as the machine sliding off its footprint rather than the blades rattling inside it. This
///     component only carries an intensity; the client system turns that into a few pixels of
///     deterministic vibration, so the transform, pipe nodes and ship never move.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TurbineShakeComponent : Component
{
    /// <summary>0 is barely visible; 1 is a failing blade set at full overspeed.</summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Intensity;
}
