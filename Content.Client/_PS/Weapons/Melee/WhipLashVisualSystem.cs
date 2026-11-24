using System.Numerics;
using Content.Shared._PS.Weapons.Melee;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Client._PS.Weapons.Melee;

/// <summary>
/// Moves whip lash visuals from the wielder towards the strike point and back again.
/// The rope itself is drawn by <see cref="Content.Client.Physics.JointVisualsOverlay"/>.
/// </summary>
public sealed class WhipLashVisualSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _lastLash = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WhipLashEvent>(OnWhipLash);
    }

    private void OnWhipLash(WhipLashEvent ev)
    {
        // The attacker's client already spawned a predicted lash; do not stack a second rope.
        if (_player.LocalEntity is { } local && GetEntity(ev.User) == local)
            return;

        var user = GetEntity(ev.User);
        var weapon = GetEntity(ev.Weapon);

        if (!TryComp<WhipComponent>(weapon, out var whip))
            return;

        SpawnLash(user, weapon, whip, _transform.ToMapCoordinates(GetCoordinates(ev.Target)));
    }

    public void SpawnLash(EntityUid user, EntityUid weapon, WhipComponent whip, MapCoordinates target)
    {
        if (!TryComp(user, out TransformComponent? userXform) ||
            userXform.MapID != target.MapId ||
            userXform.MapID == MapId.Nullspace)
        {
            return;
        }

        var start = _transform.GetWorldPosition(userXform);
        var direction = target.Position - start;
        var distance = direction.Length();

        if (distance <= 0f)
            return;

        // Suppress delayed prediction/server duplicates for the same attack.
        var now = _timing.RealTime;
        if (_lastLash.TryGetValue(user, out var last) &&
            now - last < TimeSpan.FromSeconds(0.9))
        {
            return;
        }

        if (TryComp<MeleeWeaponComponent>(weapon, out var melee) &&
            distance > melee.Range)
        {
            distance = melee.Range;
        }

        var end = start + direction.Normalized() * distance;

        // Only ever show one lash per wielder at a time.
        // Identical duplicate events are ignored; a genuinely new attack replaces the old lash.
        var lashes = EntityQueryEnumerator<WhipLashVisualComponent>();
        while (lashes.MoveNext(out var existingUid, out var existing))
        {
            if (existing.User != user || EntityManager.IsQueuedForDeletion(existingUid))
                continue;

            if ((existing.Target - end).LengthSquared() < 0.01f)
                return;

            QueueDel(existingUid);
        }

        var lash = Spawn(null, new MapCoordinates(start, target.MapId));
        _lastLash[user] = now;

        var visuals = AddComp<JointVisualsComponent>(lash);
        visuals.Sprite = whip.RopeSprite;
        visuals.Target = GetNetEntity(user);
        visuals.OffsetB = new Vector2(0f, 0.5f);
        Dirty(lash, visuals);

        var lashVisual = AddComp<WhipLashVisualComponent>(lash);
        lashVisual.User = user;
        lashVisual.Target = end;
        lashVisual.Map = target.MapId;
        lashVisual.StartTime = _timing.RealTime;

        // Scale the lash timing with distance so 12-tile lashes do not snap out faster than 6-tile ones.
        lashVisual.ExtendDuration = Math.Clamp(distance / 60f, 0.08f, 0.2f);
        lashVisual.RetractDuration = lashVisual.ExtendDuration * 1.2f;

        // Safety net in case the visual system is ever skipped.
        var despawn = EnsureComp<TimedDespawnComponent>(lash);
        despawn.Lifetime = lashVisual.ExtendDuration + lashVisual.HoldDuration + lashVisual.RetractDuration + 0.1f;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<WhipLashVisualComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var lash, out _))
        {
            if (Deleted(lash.User) ||
                !TryComp(lash.User, out TransformComponent? userXform) ||
                userXform.MapID != lash.Map)
            {
                QueueDel(uid);
                continue;
            }

            var elapsed = MathF.Max(0f, (float)(_timing.RealTime - lash.StartTime).TotalSeconds);
            var extend = lash.ExtendDuration;
            var hold = lash.HoldDuration;
            var retract = lash.RetractDuration;

            float progress;
            if (elapsed < extend)
            {
                progress = Easings.OutQuart(elapsed / extend);
            }
            else if (elapsed < extend + hold)
            {
                progress = 1f;
            }
            else if (elapsed < extend + hold + retract)
            {
                progress = 1f - Easings.InQuart((elapsed - extend - hold) / retract);
            }
            else
            {
                QueueDel(uid);
                continue;
            }

            var start = _transform.GetWorldPosition(userXform);
            _transform.SetWorldPosition(uid, Vector2.Lerp(start, lash.Target, progress));
        }
    }
}
