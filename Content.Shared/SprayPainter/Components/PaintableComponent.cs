using Content.Shared.SprayPainter.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.SprayPainter.Components;

/// <summary>
/// Marks objects that can be painted with the spray painter.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PaintableComponent : Component
{
    /// <summary>
    /// Group of styles this airlock can be painted with, e.g. glass, standard or external.
    /// Set to null to make an entity unpaintable.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<PaintableGroupPrototype>? Group;

    /// <summary>
    /// Palmtree: the style last applied with the spray painter (the entity prototype id written to
    /// <see cref="PaintableVisuals.Prototype"/> in the appearance data).
    /// </summary>
    /// <remarks>
    /// The chosen style lives in <c>AppearanceComponent.AppearanceData</c>, which is runtime-only and
    /// not serialized: a painted door (or airlock, crate, canister, locker...) silently lost its paint
    /// when the ship it was on was stored and retrieved. This field persists the choice; the style is
    /// re-applied on startup (<see cref="SharedSprayPainterSystem"/>).
    /// </remarks>
    [DataField]
    public string? PaintedStyle;
}
