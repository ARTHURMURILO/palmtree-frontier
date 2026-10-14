using Robust.Shared.GameStates;

namespace Content.Shared._PS.Traits;

/// <summary>
/// Palmtree: marker for characters with the Theobromine intolerance trait. Theobromine (found in
/// chocolate, coffee and tea) poisons and nauseates them; everyone else metabolizes it harmlessly.
/// Previously this was enforced by the <c>Animal</c> metabolizer type, i.e. by species.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TheobromineIntoleranceComponent : Component;
