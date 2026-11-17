// SPDX-FileCopyrightText: 2025 jhrushbe <capnmerry@gmail.com>
// SPDX-License-Identifier: CC-BY-NC-SA-3.0

using System.Numerics;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
///     Animates <see cref="SpriteComponent.Offset"/> for an overspeeding turbine.
/// </summary>
/// <remarks>
///     The offset is rebuilt every frame from a fast deterministic noise plus a couple of high
///     frequency sines. That gives the broken-bearing rattle of a blade set that is no longer in
///     balance, while staying within a couple of pixels so the machine still reads as bolted to its
///     tile. It is purely visual; the transform and physics footprint never move.
/// </remarks>
public sealed class TurbineShakeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, Vector2> _baseOffsets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TurbineShakeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TurbineShakeComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, TurbineShakeComponent component, ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _baseOffsets[uid] = sprite.Offset;
    }

    private void OnShutdown(EntityUid uid, TurbineShakeComponent component, ComponentShutdown args)
    {
        if (_baseOffsets.Remove(uid, out var baseOffset) && TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetOffset((uid, sprite), baseOffset);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = (float) _timing.CurTime.TotalSeconds;

        var query = EntityQueryEnumerator<TurbineShakeComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var shake, out var sprite))
        {
            if (!_baseOffsets.TryGetValue(uid, out var baseOffset))
            {
                baseOffset = sprite.Offset;
                _baseOffsets[uid] = baseOffset;
            }

            var intensity = Math.Clamp(shake.Intensity, 0f, 1f);
            if (intensity <= 0f)
            {
                _sprite.SetOffset((uid, sprite), baseOffset);
                continue;
            }

            var seed = uid.Id * 0.731f;

            // A new noise sample roughly every other frame: fast enough to read as an uncontrolled
            // rattle, but not so fast that it turns into a static blur at 60 fps.
            var noiseX = Noise((int) (now * 42f), uid.Id * 17 + 3);
            var noiseY = Noise((int) (now * 42f), uid.Id * 29 + 11);

            // Two high-frequency sines keep a little mechanical rhythm underneath the noise.
            var sineX = MathF.Sin(now * 131f + seed) * 0.55f + MathF.Sin(now * 47f + seed * 1.7f) * 0.45f;
            var sineY = MathF.Sin(now * 157f + seed * 2.3f) * 0.5f + MathF.Sin(now * 67f + seed * 3.1f) * 0.5f;

            var offset = new Vector2(
                (noiseX * 0.035f + sineX * 0.025f) * intensity,
                (noiseY * 0.03f + sineY * 0.018f) * intensity);

            _sprite.SetOffset((uid, sprite), baseOffset + offset);
        }
    }

    private static float Noise(int step, int seed)
    {
        var value = (uint) (step * 374761393 + seed * 668265263);
        value = (value ^ (value >> 13)) * 1274126177;
        return ((value ^ (value >> 16)) & 0xFFFF) / 32768f - 1f;
    }
}
