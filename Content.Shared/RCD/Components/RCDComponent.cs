using Content.Shared.Atmos.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Shared.RCD.Components;

/// <summary>
/// Main component for the RCD
/// Optionally uses LimitedChargesComponent.
/// Charges can be refilled with RCD ammo
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RCDSystem))]
public sealed partial class RCDComponent : Component
{
    /// <summary>
    /// List of RCD prototypes that the device comes loaded with
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<RCDPrototype>> AvailablePrototypes { get; set; } = new();

    /// <summary>
    /// Sound that plays when a RCD operation successfully completes
    /// </summary>
    [DataField]
    public SoundSpecifier SuccessSound { get; set; } = new SoundPathSpecifier("/Audio/Items/deconstruct.ogg");

    /// <summary>
    /// The ProtoId of the currently selected RCD prototype
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<RCDPrototype> ProtoId { get; set; } = "Invalid";

    // Palmtree: RCD improvements ported from the old Coyote code (mirror prototype, RPD, instant construction)
    /// <summary>
    /// Indicates if a mirrored version of the construction prototype should be used (if available)
    /// </summary>
    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool UseMirrorPrototype = false;

    /// <summary>
    /// Indicates whether this is an RCD or an RPD
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsRpd { get; set; }

    /// <summary>
    /// Indicates if this RCD/RPD should construct instantly.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool InstantConstruction { get; set; }

    /// <summary>
    /// Palmtree: admin/experimental RCD mode. Construction and deconstruction ignore charges
    /// entirely (no cost, no gain and no loss), and deconstruction accepts any anchored fixture or
    /// tile instead of only entities carrying <see cref="RCDDeconstructableComponent"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Unrestricted { get; set; }

    /// <summary>
    /// Palmtree: multiplier applied to RCD/RPD operation delays. 2 is twice as fast (half the
    /// delay); var-edit it or set it per prototype for faster tools. Values at or below zero are
    /// ignored.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float SpeedMultiplier = 1f;

    /// <summary>
    /// Pipe colours the RPD's colour strip offers, keyed by palette name. Mirrors the spray
    /// painter's pipe palette so an RPD-tinted pipe looks like a spray painted one.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, Color> PipeColorPalette = new();

    /// <summary>
    /// The palette key of the pipe colour the RPD paints newly constructed pipes with.
    /// "default" means the RPD does not paint at all.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string PipeColorKey = "default";

    /// <summary>
    /// Palmtree: the atmos pipe layer the RPD player is currently aiming at. The client sends it
    /// whenever its cursor crosses a layer boundary; the server's click location is snapped to the
    /// tile centre, so it cannot reproduce the client's offset on its own.
    /// </summary>
    /// <remarks>
    /// Server-only working state: it is never dirtied, so it deliberately stays out of the
    /// component state.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public AtmosPipeLayer LastKnownPipeLayer { get; set; } = AtmosPipeLayer.Primary;
    // Palmtree end

    /// <summary>
    /// The direction constructed entities will face upon spawning
    /// </summary>
    [DataField, AutoNetworkedField]
    public Direction ConstructionDirection
    {
        get => _constructionDirection;
        set
        {
            _constructionDirection = value;
            ConstructionTransform = new Transform(new(), _constructionDirection.ToAngle());
        }
    }

    private Direction _constructionDirection = Direction.South;

    /// <summary>
    /// Returns a rotated transform based on the specified ConstructionDirection
    /// </summary>
    /// <remarks>
    /// Contains no position data
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public Transform ConstructionTransform { get; private set; }

    // Frontier: ship-based RCDs
    /// <summary>
    /// Frontier - Shipyard RCD
    /// A flag that limits RCD to the authorized ships.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsShipyardRCD;
    // End Frontier: ship-based RCDs
}
