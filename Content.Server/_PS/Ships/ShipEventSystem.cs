// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._NF.Shipyard.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Radio;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._PS.Ships;

/// <summary>
///     Ship-scoped alarms: sign a message with the vessel it came from and put it on the Shortband,
///     so a machine failure reaches its own ship and the ships close enough to hear it, and nothing
///     else.
/// </summary>
/// <remarks>
///     Palmtree has no station. Everything a crew owns is a grid, and several crews may be flying
///     side by side. The supermatter used to announce through the station-wide path, which on this
///     fork reaches every vessel and every trade post in the sector. Every helper here resolves the
///     source entity's <see cref="TransformComponent.GridUid"/> first and only ever names that grid.
/// </remarks>
public sealed class ShipEventSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;

    /// <summary>
    ///     Channel every engine alarm is broadcast on: the Shortband (<c>Traffic</c>).
    /// </summary>
    /// <remarks>
    ///     An engine failure is a problem for the ship it happens on, not a sector-wide event. The
    ///     Shortband is the fork's range-degraded channel: a neighbouring vessel hears it, garbled
    ///     with distance, and anything further out does not hear it at all.
    /// </remarks>
    public static readonly ProtoId<RadioChannelPrototype> EngineChannel = "Traffic";

    /// <summary>
    ///     Resolve the grid an entity belongs to. Anchored machinery, mobs and loose items all
    ///     resolve to the ship they are standing on.
    /// </summary>
    public bool TryGetGrid(EntityUid uid, out EntityUid grid)
    {
        grid = default;

        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        if (xform.GridUid is not { } gridUid || !HasComp<MapGridComponent>(gridUid))
            return false;

        grid = gridUid;
        return true;
    }

    /// <summary>
    ///     Best-effort display name for the vessel an entity is standing on.
    /// </summary>
    /// <remarks>
    ///     Deeds live on the captain's ID card and point at the grid they were purchased for, so
    ///     the lookup runs id-card -> grid. Only <em>purchased</em> ships have one; anything
    ///     spawned by an admin or placed by a map falls through to the grid's own name, and
    ///     finally to a generic "unregistered" string. The alarm must never fail to announce
    ///     just because nobody filed the paperwork.
    /// </remarks>
    public string GetShipName(EntityUid uid)
    {
        if (!TryGetGrid(uid, out var grid))
            return Loc.GetString("ship-event-name-unknown");

        var deeds = EntityQueryEnumerator<ShuttleDeedComponent>();
        while (deeds.MoveNext(out _, out var deed))
        {
            if (deed.ShuttleUid != grid)
                continue;

            var full = ShipyardSystem.GetFullName(deed);
            if (!string.IsNullOrWhiteSpace(full))
                return full;
        }

        var name = MetaData(grid).EntityName;
        if (!string.IsNullOrWhiteSpace(name) && name != "Unknown")
            return name;

        return Loc.GetString("ship-event-name-unknown");
    }

    /// <summary>
    ///     Broadcast an engine alarm on the Shortband, and nowhere else.
    /// </summary>
    /// <remarks>
    ///     Deliberately narrower than a station announcement: no direct chat fallback, because that
    ///     fallback is a server-side line of text that reads as a station-wide announcement - the
    ///     exact thing this exists to avoid. The message goes out as ordinary radio traffic, so it
    ///     reaches whoever is actually listening to the Shortband: the ship's own intercoms and
    ///     shortband headsets, plus any vessel close enough to pick it up.
    /// </remarks>
    public void EngineAnnounce(EntityUid source, string message)
    {
        var text = Loc.GetString("ship-event-announcement", ("ship", GetShipName(source)), ("message", message));
        _radio.SendRadioMessage(source, text, EngineChannel, source);
    }
}
