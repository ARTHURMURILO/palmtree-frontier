using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.ViewVariables;

namespace Content.Server._PS.Body;

/// <summary>
/// Palmtree: marks a mob that has been controlled by a player at least once and remembers which
/// player that was. Used by <see cref="Content.Server.Body.Systems.RespiratorSystem"/> so a
/// character does not start breathing again when its original player logs off or switches to
/// another mob, even if another client (for example an admin) takes the body over.
/// </summary>
[RegisterComponent]
public sealed partial class PlayerCharacterComponent : Component
{
    /// <summary>
    /// The account that first attached to this mob.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public NetUserId? OriginalPlayer;
}
