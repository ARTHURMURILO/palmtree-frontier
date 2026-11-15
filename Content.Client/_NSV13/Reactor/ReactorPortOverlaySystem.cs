// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._NSV13.Reactor;
using Content.Shared.Examine;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._NSV13.Reactor;

/// <summary>
///     Shows the reactor's connection faces for a few seconds after the local player examines it.
/// </summary>
/// <remarks>
///     The trigger is the examine response, which only ever reaches the client that asked for it,
///     so the arrows are private to the examiner by construction - no networking, no filtering,
///     nothing for other players to see. The response also carries the entity, so no extra verb or
///     keybind is needed: examining the reactor is the whole interaction.
/// </remarks>
public sealed class ReactorPortOverlaySystem : EntitySystem
{
    /// <summary>How long the arrows stay up after an examine, in seconds.</summary>
    private const float ShowTime = 8f;

    /// <summary>How long the arrows take to dim out at the end, in seconds.</summary>
    private const float FadeTime = 1.2f;

    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private ReactorPortOverlay _overlay = default!;
    private EntityUid? _target;
    private float _remaining;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new ReactorPortOverlay(EntityManager, _cache, _xform);
        _overlayMan.AddOverlay(_overlay);

        SubscribeNetworkEvent<ExamineSystemMessages.ExamineInfoResponseMessage>(OnExamineResponse);
    }

    private void OnExamineResponse(ExamineSystemMessages.ExamineInfoResponseMessage msg)
    {
        if (!TryGetEntity(msg.EntityUid, out var uid) || !HasComp<ReactorPortLayoutComponent>(uid))
            return;

        _target = uid;
        _remaining = ShowTime;

        _overlay.Target = uid;
        _overlay.Alpha = 1f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_target is not { } uid)
            return;

        _remaining -= frameTime;

        if (_remaining <= 0f || Deleted(uid))
        {
            _target = null;
            _overlay.Target = null;
            return;
        }

        _overlay.Alpha = Math.Clamp(_remaining / FadeTime, 0f, 1f);
    }
}
