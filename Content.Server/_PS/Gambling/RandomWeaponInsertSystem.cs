using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Gambling;

/// <summary>
/// Adds "Insert randomly" / "Fill sequence" / "Shoot self" verbs to shotguns.
/// The fill verbs pull a random number of shells (never fewer than <see cref="MinShells"/>, never
/// more than the gun can hold) from any ammo box the interacting character is holding or that lies
/// within <see cref="Radius"/> tiles, and report how many of each shell type were loaded.
/// "Shoot self" is the proof-of-concept Buckshot Roulette action: it fires the loaded shell
/// straight into the person doing the shooting.
/// </summary>
public sealed class RandomWeaponInsertSystem : EntitySystem
{
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float Radius = 2f;
    private const string ShotgunTag = "ShellShotgun";

    /// <summary>
    /// The fewest shells a fill inserts. Guns that cannot hold this many do not offer the verbs.
    /// </summary>
    private const int MinShells = 2;

    /// <summary>
    /// Per-gun cooldown for the "Shoot self" verb. The vanilla firing path enforces
    /// <c>GunComponent.NextFire</c> in <c>AttemptShoot</c>, which we bypass to be able to hit the
    /// shooter, so we keep our own timer instead.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextSelfShot = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<GunComponent, EntityTerminatingEvent>(OnGunTerminating);
        SubscribeLocalEvent<GunComponent, ComponentShutdown>(OnGunShutdown);
    }

    private void OnGunTerminating(EntityUid uid, GunComponent comp, ref EntityTerminatingEvent args)
    {
        _nextSelfShot.Remove(uid);
    }

    private void OnGunShutdown(EntityUid uid, GunComponent comp, ComponentShutdown args)
    {
        _nextSelfShot.Remove(uid);
    }

    private void OnGetVerbs(EntityUid uid, BallisticAmmoProviderComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        if (args.Hands is not { } hands)
            return;
        if (comp.MayTransfer) // It's an ammo box, not a gun.
            return;
        if (!TryComp<GunComponent>(uid, out var gun)) // Must be a gun!
            return;
        if (comp.Whitelist?.Tags?.Any(t => t.Id == ShotgunTag) != true) // Only shotguns
            return;

        AddShootSelfVerb(uid, comp, gun, args);

        // The fill verbs always load at least two shells, so a gun that cannot hold (or take) that
        // many does not offer them.
        if (comp.Capacity < MinShells)
            return;
        if (FreeCapacity(comp) < MinShells)
            return;

        var pool = GatherPool(uid, comp, args.User, hands);
        if (TotalAvailable(pool) < MinShells)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ammo-fill-random"),
            Act = () => FillRandom(uid, comp, args.User, hands),
        });

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ammo-fill-sequence"),
            Act = () => FillSequence(uid, comp, args.User, hands),
        });
    }

    private void AddShootSelfVerb(EntityUid gunUid, BallisticAmmoProviderComponent provider, GunComponent gun, GetVerbsEvent<AlternativeVerb> args)
    {
        var loaded = provider.Entities.Count + provider.UnspawnedCount > 0;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ammo-shoot-self"),
            Disabled = !loaded,
            Message = loaded ? null : Loc.GetString("ammo-shoot-self-empty"),
            Act = () => ShootSelf(gunUid, provider, gun, args.User),
        });
    }

    /// <summary>
    /// Fires the next shell straight into the user. The gun is passed to <c>Shoot</c> as the
    /// projectile's shooter (instead of the user) so <c>ProjectileComponent.IgnoreShooter</c> does
    /// not skip the very person we are trying to hit.
    /// </summary>
    private void ShootSelf(EntityUid gunUid, BallisticAmmoProviderComponent provider, GunComponent gun, EntityUid user)
    {
        if (provider.Entities.Count + provider.UnspawnedCount <= 0)
            return;

        if (_nextSelfShot.TryGetValue(gunUid, out var next) && next > _timing.CurTime)
            return;

        var cooldown = gun.FireRateModified > 0f
            ? TimeSpan.FromSeconds(1f / gun.FireRateModified)
            : TimeSpan.FromSeconds(1d);
        _nextSelfShot[gunUid] = _timing.CurTime + cooldown;

        var userXform = Transform(user);

        var ammo = new List<(EntityUid?, IShootable)>();

        // Honour clumsy fumbles and anything else gating a normal shot before taking a shell, like
        // the vanilla firing path does: a cancelled shot must not consume the shell (the handlers
        // do not need the ammo list).
        var beforeShot = new SelfBeforeGunShotEvent(user, (gunUid, gun), ammo);
        RaiseLocalEvent(user, beforeShot);
        if (beforeShot.Cancelled)
            return;

        RaiseLocalEvent(gunUid, new TakeAmmoEvent(1, ammo, userXform.Coordinates, user, willBeFired: true));
        if (ammo.Count == 0)
            return;

        // Grab the shell name before firing: some cartridges delete themselves on spawn, and we want
        // the chat line to say exactly what went off.
        var (shellUid, _) = ammo[0];
        var shellName = shellUid is { } shell
            ? Name(shell)
            : Loc.GetString("ammo-shoot-self-unknown-shell");

        // The projectile spawns inside the user and the user is its victim, not its shooter, so it
        // registers the point-blank hit on the first physics step. The aim offset keeps the shot
        // direction non-degenerate; the shell lands either way.
        var to = userXform.Coordinates.Offset(userXform.LocalRotation.ToVec());
        _gun.Shoot(gunUid, gun, ammo, userXform.Coordinates, to, out _, user: null);

        var shotEvent = new GunShotEvent(user, ammo);
        RaiseLocalEvent(gunUid, ref shotEvent);

        SendEmote(user, Loc.GetString("ammo-shoot-self-emote", ("gun", gunUid), ("shell", shellName)));
    }

    /// <summary>
    /// Broadcasts a third-person action line to chat, so load/fire actions are on the public record
    /// and cannot be quietly faked during RP.
    /// </summary>
    private void SendEmote(EntityUid source, string message)
    {
        _chat.TrySendInGameICMessage(source, message, InGameICChatType.Emote,
            ChatTransmitRange.Normal, hideLog: false, ignoreActionBlocker: true);
    }

    /// <summary>
    /// Collects ammo boxes the character is holding or that lie within <see cref="Radius"/> tiles,
    /// that still have shells to give and that could feed this gun.
    /// </summary>
    private List<EntityUid> GatherPool(EntityUid gun, BallisticAmmoProviderComponent target, EntityUid user, HandsComponent hands)
    {
        var set = new HashSet<EntityUid> { gun };
        foreach (var held in _hands.EnumerateHeld((user, hands)))
        {
            set.Add(held);
        }

        var coords = Transform(user).Coordinates;
        foreach (var ent in _lookup.GetEntitiesInRange<BallisticAmmoProviderComponent>(coords, Radius, LookupFlags.Uncontained))
        {
            set.Add(ent.Owner);
        }

        set.Remove(gun);
        var pool = new List<EntityUid>();
        foreach (var box in set)
        {
            if (TryComp<BallisticAmmoProviderComponent>(box, out var comp) &&
                comp.MayTransfer &&
                GetAvailable(box) > 0 &&
                WhitelistsOverlap(target.Whitelist, comp.Whitelist))
            {
                pool.Add(box);
            }
        }

        return pool;
    }

    /// <summary>
    /// Pulls a single shell out of <paramref name="box"/> and feeds it into the gun through the
    /// vanilla insertion path. Returns the inserted shell's prototype id and a short display label,
    /// or null if the box is empty or the shell does not fit the gun.
    /// </summary>
    private (string Id, string Label)? TryPullShell(EntityUid box, EntityUid gun, BallisticAmmoProviderComponent target, EntityUid user)
    {
        var before = target.Entities.Count + target.UnspawnedCount;

        var ammo = new List<(EntityUid?, IShootable)>();
        RaiseLocalEvent(box, new TakeAmmoEvent(1, ammo, Transform(box).Coordinates, user));

        foreach (var (ent, _) in ammo)
        {
            if (ent is not { } shell)
                continue;

            var id = MetaData(shell).EntityPrototype?.ID ?? $"entity:{shell.Id}";
            var label = ShortShellName(Name(shell));

            // InteractUsing runs OnBallisticInteractUsing, which handles the whitelist check,
            // container insert, sound and appearance for us. If it refuses the shell the gun's
            // ammo count is unchanged and we report failure so the caller stops trying.
            _interaction.InteractUsing(user, shell, gun, Transform(gun).Coordinates, checkCanInteract: false, checkCanUse: false);
            return target.Entities.Count + target.UnspawnedCount > before ? (id, label) : null;
        }

        return null;
    }

    /// <summary>
    /// Loads a random number of shells (between <see cref="MinShells"/> and the free capacity),
    /// guaranteeing at least one shell of every kind present in the pool, then tops the rest up
    /// from uniformly random boxes.
    /// </summary>
    private void FillRandom(EntityUid gun, BallisticAmmoProviderComponent target, EntityUid user, HandsComponent hands)
    {
        var pool = GatherPool(gun, target, user, hands);
        if (pool.Count == 0)
            return;

        var maxInsert = Math.Min(FreeCapacity(target), TotalAvailable(pool));
        if (maxInsert < MinShells)
            return;

        var remaining = _random.Next(MinShells, maxInsert + 1);
        var tally = new Dictionary<string, (string Label, int Count)>();

        // Guarantee one shell of each distinct kind (e.g. lethal + blank) before filling randomly.
        // Shuffling first randomises which kind lands in which magazine slot.
        _random.Shuffle(pool);
        var seeded = new HashSet<string>();
        foreach (var box in pool)
        {
            if (remaining <= 0)
                break;

            var proto = PeekProto(box);
            if (proto == null || !seeded.Add(proto))
                continue;

            if (TryPullShell(box, gun, target, user) is { } shell)
            {
                AddTally(tally, shell);
                remaining--;
            }
        }

        while (remaining > 0)
        {
            var candidates = pool.Where(box => GetAvailable(box) > 0).ToList();
            if (candidates.Count == 0)
                break;

            var box = _random.Pick(candidates);
            if (TryPullShell(box, gun, target, user) is { } shell)
            {
                AddTally(tally, shell);
                remaining--;
            }
            else
            {
                pool.Remove(box); // Incompatible ammo; stop trying this box.
            }
        }

        if (tally.Count > 0)
            SendEmote(user, Loc.GetString("ammo-fill-random-emote", ("gun", gun), ("shells", FormatShellTally(tally))));
    }

    /// <summary>
    /// Loads a random number of shells (between <see cref="MinShells"/> and the free capacity) as a
    /// repeated cycle of the available shell kinds. Each kind appears once or twice in the cycle,
    /// rolled randomly, so a two-kind fill can be A-B-A-B..., A-B-B-A-B-B..., A-A-B-A-A-B..., etc.
    /// instead of always alternating. The kind order is shuffled too, so the whole pattern varies
    /// between loads.
    /// </summary>
    private void FillSequence(EntityUid gun, BallisticAmmoProviderComponent target, EntityUid user, HandsComponent hands)
    {
        var pool = GatherPool(gun, target, user, hands);
        if (pool.Count == 0)
            return;

        // Group every box by the shell prototype it hands out.
        var groups = new Dictionary<string, List<EntityUid>>();
        foreach (var box in pool)
        {
            var proto = PeekProto(box);
            if (proto == null)
                continue;

            if (!groups.TryGetValue(proto, out var boxes))
            {
                boxes = new List<EntityUid>();
                groups[proto] = boxes;
            }

            boxes.Add(box);
        }

        if (groups.Count == 0)
            return;

        var maxInsert = Math.Min(FreeCapacity(target), TotalAvailable(pool));
        if (maxInsert < MinShells)
            return;

        var kinds = groups.Keys.ToList();
        _random.Shuffle(kinds);

        // Build the cycle used for the whole load: each kind repeats once or twice, e.g. A-B or
        // A-B-B. The kind order is shuffled and the repeats are rolled per fill, so the sequence is
        // a random one of these patterns instead of the same alternation every time.
        var cycle = new List<string>();
        foreach (var kind in kinds)
        {
            var repeats = _random.Next(1, 3); // 1 or 2
            for (var i = 0; i < repeats; i++)
                cycle.Add(kind);
        }

        var remaining = _random.Next(MinShells, maxInsert + 1);
        var tally = new Dictionary<string, (string Label, int Count)>();
        var liveKinds = new HashSet<string>(kinds);
        var index = 0;

        // Every iteration either inserts a shell, drops a box, or drops a kind, so this always terminates.
        while (remaining > 0 && liveKinds.Count > 0)
        {
            var kind = cycle[index % cycle.Count];

            if (!liveKinds.Contains(kind))
            {
                index++;
                continue;
            }

            var boxes = groups[kind];
            var candidates = boxes.Where(box => GetAvailable(box) > 0).ToList();

            if (candidates.Count == 0)
            {
                liveKinds.Remove(kind);
                index++;
                continue;
            }

            var box = _random.Pick(candidates);
            if (TryPullShell(box, gun, target, user) is { } shell)
            {
                AddTally(tally, shell);
                remaining--;
                index++;
            }
            else
            {
                boxes.Remove(box); // Incompatible ammo; stop trying this box.
            }
        }

        if (tally.Count > 0)
            SendEmote(user, Loc.GetString("ammo-fill-sequence-emote", ("gun", gun), ("shells", FormatShellTally(tally))));
    }

    private int FreeCapacity(BallisticAmmoProviderComponent target)
    {
        return Math.Max(0, target.Capacity - (target.Entities.Count + target.UnspawnedCount));
    }

    private int TotalAvailable(List<EntityUid> pool)
    {
        var total = 0;
        foreach (var box in pool)
        {
            total += GetAvailable(box);
        }

        return total;
    }

    private int GetAvailable(EntityUid box)
    {
        if (!TryComp<BallisticAmmoProviderComponent>(box, out var comp))
            return 0;

        return comp.Entities.Count + comp.UnspawnedCount;
    }

    /// <summary>
    /// The shell prototype a box will yield next, or null when it has nothing to give.
    /// </summary>
    private string? PeekProto(EntityUid box)
    {
        if (!TryComp<BallisticAmmoProviderComponent>(box, out var comp))
            return null;

        if (comp.Entities.Count > 0)
            return MetaData(comp.Entities[^1]).EntityPrototype?.ID;

        return comp.Proto?.Id;
    }

    /// <summary>
    /// Shells are named "shell (.50 practice)"; chat reads better as ".50 practice". Anything that
    /// does not follow that pattern is left alone.
    /// </summary>
    private static string ShortShellName(string name)
    {
        const string prefix = "shell (";
        if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(')'))
            return name[prefix.Length..^1];

        return name;
    }

    /// <summary>
    /// Adds one pulled shell to the per-type tally, keyed by prototype id.
    /// </summary>
    private static void AddTally(Dictionary<string, (string Label, int Count)> tally, (string Id, string Label) shell)
    {
        if (tally.TryGetValue(shell.Id, out var entry))
            tally[shell.Id] = (entry.Label, entry.Count + 1);
        else
            tally[shell.Id] = (shell.Label, 1);
    }

    /// <summary>
    /// Formats "3 .50 buckshot and 2 .50 practice" from the per-type tally.
    /// </summary>
    private string FormatShellTally(Dictionary<string, (string Label, int Count)> tally)
    {
        var parts = tally.Values
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Select(entry => Loc.GetString("ammo-fill-shell-count", ("count", entry.Count), ("shell", entry.Label)))
            .ToList();

        if (parts.Count == 0)
            return string.Empty;

        var result = parts[0];
        for (var i = 1; i < parts.Count; i++)
        {
            var key = i == parts.Count - 1 ? "ammo-fill-shell-list-last" : "ammo-fill-shell-list-mid";
            result = Loc.GetString(key, ("left", result), ("right", parts[i]));
        }

        return result;
    }

    /// <summary>
    /// Whether two whitelists share at least one tag. Used as a cheap "can this box feed this gun"
    /// filter so we never pull shells the gun will reject. Null-safe: unknown = allow.
    /// </summary>
    private static bool WhitelistsOverlap(EntityWhitelist? a, EntityWhitelist? b)
    {
        if (a?.Tags == null || b?.Tags == null)
            return true;

        foreach (var tag in a.Tags)
        {
            if (b.Tags.Contains(tag))
                return true;
        }

        return false;
    }
}
