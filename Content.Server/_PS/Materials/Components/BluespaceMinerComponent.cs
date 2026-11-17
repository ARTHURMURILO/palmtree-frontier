using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.ViewVariables;

namespace Content.Server._PS.Materials.Components;

/// <summary>
/// Palmtree: a passive machine that produces ores into a linked ore silo.
/// Ported from the SPLURT bluespace miner and adapted to the ore silo network.
/// </summary>
/// <remarks>
/// The defaults deliberately only include the basic ores (iron, quartz, coal, silver, gold and
/// salt). Rare ores such as plasma, uranium, diamond and bananium are not mined unless an admin
/// adds them to <see cref="OreWeights"/> through View Variables.
/// </remarks>
[RegisterComponent]
public sealed partial class BluespaceMinerComponent : Component
{
    /// <summary>
    /// Whether the miner is currently producing.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    /// <summary>
    /// How many seconds between mining cycles. Values less than or equal to zero are treated as
    /// one second. Kept as a float so it can be edited directly through View Variables.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float SecondsPerCycle = 6f;

    /// <summary>
    /// How many material units are produced per cycle. One ore is 100 units.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int AmountPerCycle = 100;

    /// <summary>
    /// Relative weights for the ore selected each cycle. Only basic ores by default.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<ProtoId<MaterialPrototype>, float> OreWeights = new()
    {
        ["RawIron"] = 20f,
        ["RawQuartz"] = 20f,
        ["Coal"] = 20f,
        ["RawSilver"] = 8f,
        ["RawGold"] = 3f,
        ["RawSalt"] = 8f,
    };

    /// <summary>
    /// Sound played when a mining cycle completes.
    /// </summary>
    [DataField]
    public SoundSpecifier? MiningSound = new SoundPathSpecifier(
        "/Audio/Effects/Cargo/ping.ogg",
        AudioParams.Default.WithVolume(-8f));

    /// <summary>
    /// When the next mining cycle is due. Runtime state, not persisted.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextMiningTime;
}
