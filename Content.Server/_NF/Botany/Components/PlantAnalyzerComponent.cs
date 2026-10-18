using Content.Shared._NF.PlantAnalyzer;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;

namespace Content.Server.Botany.Components;

/// <summary>
///    After scanning, retrieves the target Uid to use with its related UI.
/// </summary>
[RegisterComponent]
public sealed partial class PlantAnalyzerComponent : Component
{
    [DataDefinition]
    public partial struct PlantAnalyzerSetting
    {
        [DataField]
        public bool AdvancedScan;

        [DataField]
        public float ScanDelay;

        [DataField]
        public float AdvScanDelay;
    }

    [DataField, ViewVariables]
    public PlantAnalyzerSetting Settings = new();

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public DoAfterId? DoAfter;

    [DataField]
    public SoundSpecifier? ScanningEndSound;

    /// <summary>
    ///    Info on the last scanned plant, if any
    /// </summary>
    /// <remarks>
    ///    Palmtree: runtime UI state, deliberately not a <c>[DataField]</c>. It is shown in the analyzer's
    ///    window and is rebuilt by the next scan, but it cannot be persisted: the struct has no data
    ///    definition, so a save (the bluespace dock's ship files, for instance) of any grid holding a
    ///    plant analyzer throws "No data definition found for type
    ///    PlantAnalyzerScannedPlantInformation" and fails. Its <c>NetEntity</c> target would also go
    ///    stale across a save/load.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public PlantAnalyzerScannedPlantInformation? PlantInfo;
}
