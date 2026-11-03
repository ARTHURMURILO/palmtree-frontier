using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._PS.Body;

/// <summary>
/// Palmtree: records the first player account attached to a mob so later systems can tell whether
/// the mob's original player is still the one controlling it.
/// </summary>
public sealed class PlayerCharacterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        var playerCharacter = EnsureComp<PlayerCharacterComponent>(args.Entity);
        playerCharacter.OriginalPlayer ??= args.Player.UserId;
    }
}
