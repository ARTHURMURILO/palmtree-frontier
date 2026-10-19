using System;
using System.Numerics;
using Content.Shared._PS.ShipCombat;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._PS.ShipCombat;

/// <summary>
/// Palmtree: draws ship weapon beams.
/// </summary>
/// <remarks>
/// The server broadcasts <see cref="ShipBeamEffectEvent"/> with a wide PVS range, so beams render far
/// outside the normal PVS bubble (ship combat is larger scale). The visuals reuse the vanilla
/// hitscan effect entity: a stretched, unshaded sprite that fades out with the same flick animation.
/// </remarks>
public sealed class ShipBeamEffectSystem : EntitySystem
{
    /// <summary>The vanilla hitscan effect entity: one unshaded sprite layer and an animation player.</summary>
    private static readonly EntProtoId BeamEffectProto = "HitscanEffect";

    /// <summary>The beam sprite, drawn horizontally: the entity is scaled along X to the shot length.</summary>
    private static readonly SpriteSpecifier.Rsi BeamSprite = new(
        new ResPath("Objects/Weapons/Guns/Projectiles/projectiles.rsi"),
        "beam_heavy");

    [Dependency] private readonly AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ShipBeamEffectEvent>(OnBeamEffect);
    }

    private void OnBeamEffect(ShipBeamEffectEvent ev)
    {
        var from = _xform.ToMapCoordinates(GetCoordinates(ev.From));
        var to = _xform.ToMapCoordinates(GetCoordinates(ev.To));

        var delta = to.Position - from.Position;
        var length = delta.Length();
        if (length < 0.5f)
            return;

        var mid = from.Position + delta / 2f;
        var ent = Spawn(BeamEffectProto.Id, new MapCoordinates(mid, from.MapId));

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _xform.SetWorldRotationNoLerp(ent, delta.ToWorldAngle());

        // The beam sprite is horizontal, so it is stretched along X to the length of the shot.
        _sprite.SetScale((ent, sprite), new Vector2(length, 1f));
        _sprite.LayerSetSprite((ent, sprite), EffectLayers.Unshaded, BeamSprite);
        _sprite.LayerSetRsiState((ent, sprite), EffectLayers.Unshaded, BeamSprite.RsiState);
        sprite[EffectLayers.Unshaded].AutoAnimated = false;
        sprite[EffectLayers.Unshaded].Visible = true;

        _animPlayer.Play(ent, new Animation
        {
            Length = TimeSpan.FromSeconds(0.48f),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = EffectLayers.Unshaded,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(BeamSprite.RsiState, 0f),
                    },
                },
            },
        }, "ship-beam-effect");
    }
}
