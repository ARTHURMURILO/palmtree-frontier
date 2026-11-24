using Content.Client._PS.Weapons.Melee;
using Content.Shared._PS.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Melee;

public sealed partial class MeleeWeaponSystem
{
    [Dependency] private WhipLashVisualSystem _whipLash = default!;

    /// <summary>
    /// Palmtree: spawns the local rope lash for light attacks with a whip.
    /// </summary>
    protected override void DoWhipLash(EntityUid user, EntityUid weapon, MapCoordinates target, WhipComponent whip)
    {
        // This effect is client-side; only play it on the first prediction, not on replay.
        if (!Timing.IsFirstTimePredicted)
            return;

        _whipLash.SpawnLash(user, weapon, whip, target);
    }
}
