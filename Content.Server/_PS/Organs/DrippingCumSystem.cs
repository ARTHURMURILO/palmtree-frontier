using System.Numerics;
using Content.Server.Decals;
using Content.Shared._PS.Organs;
using Content.Shared.Decals;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._PS.Organs;

/// <summary>
/// Drains stored semen (<see cref="DrippingCumComponent"/>) into SPLURT-style droplet decals, 10
/// units per second while the mob is bottomless; the drip pauses while they wear a jumpsuit.
/// </summary>
/// <remarks>
/// Drips only ever make the small droplet sprites (<c>SemenDrip1..5</c>). The full cum decals
/// (<c>SemenPuddle1..4</c>) are reserved for actual ejaculation via <see cref="SpawnCumDecals"/>.
/// Every drop is a new decal placed where the mob actually stands (with a little scatter), so the
/// floor builds up a mess instead of one decal being upgraded. Decals are cleanable (space cleaner).
/// A mob only starts dripping after it moved or performed an interaction (see
/// <see cref="NotifyActivity"/>); once started, the drip keeps running every second even while the
/// mob stands still, until the reservoir is empty. Dripping pauses on a tile that already has too
/// many drips on it. Pausing never consumes stored semen: it waits until the mob can drip again.
/// </remarks>
public sealed class DrippingCumSystem : EntitySystem
{
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly TimeSpan DripInterval = TimeSpan.FromSeconds(1);

    /// <summary>How many units of stored semen each drip consumes (10u per drip).</summary>
    private const double DripAmount = 10;

    /// <summary>Droplet decal ladder. Drips never escalate to the full puddle decals.</summary>
    private static readonly string[] DripDecals =
    [
        "SemenDrip1",
        "SemenDrip2",
        "SemenDrip3",
        "SemenDrip4",
        "SemenDrip5",
    ];

    /// <summary>How far a drip droplet can scatter from the mob's exact position (barely any).</summary>
    private const float DripScatter = 0.05f;

    /// <summary>How far an ejaculation decal can scatter (a proper mess).</summary>
    private const float CumScatter = 0.35f;

    /// <summary>Maximum number of drips allowed on a single tile before dripping pauses.</summary>
    private const int MaxDripsPerTile = 20;

    /// <summary>How far the mob has to move between drip checks for it to count as activity.</summary>
    private const float MovementThreshold = 0.06f;

    /// <summary>Semen inside a vagina above this amount overflows with a floor decal on each pulse.</summary>
    public const double WombOverflowThreshold = 60;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<DrippingCumComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            // A zeroed reservoir (e.g. just cleaned) should never leave a ghost drip.
            if (comp.Amount <= 0)
            {
                RemCompDeferred<DrippingCumComponent>(uid);
                continue;
            }

            if (now < comp.NextDrip)
                continue;

            comp.NextDrip = now + DripInterval;

            // Sandstorm pauses the drip while the groin is covered; this fork simplifies that to jumpsuits.
            if (_inventory.TryGetSlotEntity(uid, "jumpsuit", out _))
                continue;

            // Palmtree: stored semen only starts dripping once the mob moves or acts. That starter
            // is a latch: after the first movement/interaction the drip keeps running every second
            // even while standing still, until the reservoir is empty.
            CheckMovement(uid, comp);
            if (!comp.Ready)
                continue;

            // Palmtree: a tile that is already full of drips pauses the drip instead of eating the
            // stored semen, so it resumes when the mob moves (or the floor is cleaned).
            if (CountDripsOnTile(uid) >= MaxDripsPerTile)
                continue;

            if (!TryDrip(uid, comp))
                continue;

            comp.Amount = Math.Max(0, comp.Amount - DripAmount);
            comp.Dripped += 1;
            Dirty(uid, comp);

            if (comp.Amount <= 0)
                RemCompDeferred<DrippingCumComponent>(uid);
        }
    }

    /// <summary>
    /// Palmtree: starts the drip when the mob performed an interaction.
    /// </summary>
    public void NotifyActivity(EntityUid uid)
    {
        if (TryComp<DrippingCumComponent>(uid, out var comp))
            comp.Ready = true;
    }

    /// <summary>
    /// Palmtree: starts the drip when the mob moved since the last check. Once set, the latch stays
    /// on so the drip continues while the mob stands still.
    /// </summary>
    private void CheckMovement(EntityUid uid, DrippingCumComponent comp)
    {
        var position = Transform(uid).LocalPosition;
        if ((position - comp.LastPosition).LengthSquared() > MovementThreshold * MovementThreshold)
            comp.Ready = true;

        comp.LastPosition = position;
    }

    /// <summary>
    /// Palmtree: number of drip decals already on the tile the mob is standing on. Drips placed by
    /// this system are centered on the mob's tile (the stored coordinate is the decal's bottom-left
    /// corner), so a decal's tile is <c>floor(coordinate + 0.5)</c>.
    /// </summary>
    private int CountDripsOnTile(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid)
            return 0;

        var position = xform.LocalPosition;
        var tileX = MathF.Floor(position.X);
        var tileY = MathF.Floor(position.Y);

        var count = 0;
        foreach (var (_, decal) in _decals.GetDecalsInRange(grid, position, 1f, IsDripDecal))
        {
            if (MathF.Floor(decal.Coordinates.X + 0.5f) == tileX &&
                MathF.Floor(decal.Coordinates.Y + 0.5f) == tileY)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsDripDecal(Decal decal)
    {
        return decal.Id.StartsWith("SemenDrip", StringComparison.Ordinal);
    }

    /// <summary>Drops one more droplet under the mob. Returns false when it cannot (e.g. in space).</summary>
    private bool TryDrip(EntityUid uid, DrippingCumComponent comp)
    {
        var stage = Math.Clamp((int) (comp.Dripped / 2.0), 0, DripDecals.Length - 1);
        return TryPlaceDecal(uid, DripDecals[stage], DripScatter);
    }

    /// <summary>
    /// Ejaculation decals: drops a full SPLURT cum decal where the target stands, scaled by how much
    /// semen the penis organ produces. Never replaces an existing decal.
    /// </summary>
    public void SpawnCumDecals(EntityUid uid, double volume)
    {
        var decalId = volume switch
        {
            >= 75 => "SemenPuddle4",
            >= 50 => "SemenPuddle3",
            >= 25 => "SemenPuddle2",
            _ => "SemenPuddle1",
        };

        TryPlaceDecal(uid, decalId, CumScatter);
    }

    /// <summary>
    /// Female ejaculation decals: drops a random SPLURT fem decal where the mob stands.
    /// Never replaces an existing decal.
    /// </summary>
    public void SpawnFemDecals(EntityUid uid)
    {
        var decalId = _random.Pick(new[] { "FemPuddle1", "FemPuddle2", "FemPuddle3", "FemPuddle4" });
        TryPlaceDecal(uid, decalId, CumScatter);
    }

    /// <summary>Adds a decal centered on the mob's position with scatter; existing decals are kept.</summary>
    private bool TryPlaceDecal(EntityUid uid, string decalId, float scatter)
    {
        var xform = Transform(uid);

        if (xform.GridUid is not { } grid)
            return false;

        // Decals are drawn with their bottom-left corner at the stored coordinate, while mob
        // sprites are centered on their transform. Shift by half a tile so the decal center lands
        // on the character instead of up-right of them (near the head).
        var pos = xform.LocalPosition;
        var centered = pos - new Vector2(0.5f, 0.5f);
        var scattered = centered +
                        new Vector2(_random.NextFloat(-scatter, scatter), _random.NextFloat(-scatter, scatter));

        if (_decals.TryAddDecal(decalId, new EntityCoordinates(grid, scattered), out _, cleanable: true))
            return true;

        // Scatter (or the shift itself, when standing on a tile edge) can land on space; retry
        // without scatter, then fall back to the mob's tile center so a drop is never lost.
        if (_decals.TryAddDecal(decalId, new EntityCoordinates(grid, centered), out _, cleanable: true))
            return true;

        var tileCorner = new Vector2(MathF.Floor(pos.X), MathF.Floor(pos.Y));
        return _decals.TryAddDecal(decalId, new EntityCoordinates(grid, tileCorner), out _, cleanable: true);
    }

    /// <summary>Adds semen to the mob's reservoir, creating the drip if needed.</summary>
    public void AddSemen(EntityUid uid, double amount)
    {
        if (amount <= 0 || !HasComp<HumanoidAppearanceComponent>(uid))
            return;

        var comp = EnsureComp<DrippingCumComponent>(uid);

        // Palmtree: the reservoir holds at most 1000u (anything past that is wasted).
        comp.Amount = Math.Min(comp.Amount + amount, DrippingCumComponent.MaxCapacity);

        // Slight randomness so multiple recipients don't drip in lockstep.
        if (comp.NextDrip == default)
        {
            comp.NextDrip = _timing.CurTime + TimeSpan.FromSeconds(_random.NextDouble());

            // Remember where the mob was filled so spawning there does not count as movement.
            comp.LastPosition = Transform(uid).LocalPosition;
        }

        Dirty(uid, comp);
    }

    public double GetSemen(EntityUid uid)
    {
        return TryComp<DrippingCumComponent>(uid, out var comp) ? comp.Amount : 0;
    }

    /// <summary>Removes all stored semen immediately, stopping the drip (the Clean yourself action).</summary>
    public void ClearSemen(EntityUid uid)
    {
        RemComp<DrippingCumComponent>(uid);
    }
}
