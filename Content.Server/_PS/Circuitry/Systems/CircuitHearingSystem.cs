using Content.Server._PS.Circuitry.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Damage;
using Content.Shared.Emoting;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;

namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: queues local speech, emotes, damage, attacks and projectile hits for assemblies.
/// Radio and device-link signals are handled by <see cref="CircuitRadioSystem"/>.
/// </summary>
public sealed class CircuitHearingSystem : EntitySystem
{
    [Dependency] private readonly LuaBrainSystem _lua = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<EmotePerformedEvent>(OnEmote);
        SubscribeLocalEvent<CircuitAssemblyComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<CircuitAssemblyComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<CircuitAssemblyComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnSpoke(EntitySpokeEvent ev)
    {
        if (ev.Channel != null)
            return;

        QueueForNearby(
            ev.Source,
            "speech",
            ev.ObfuscatedMessage ?? ev.Message);
    }

    private void OnEmote(EmotePerformedEvent ev)
    {
        QueueForNearby(ev.Source, "emote", Loc.GetString(ev.Emote.Name), ev.Emote.ID);
    }

    private void OnDamageChanged(EntityUid uid, CircuitAssemblyComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        QueueForAssembly(
            uid,
            new CircuitEvent(
                "damage",
                args.Origin ?? uid,
                "damage",
                null,
                Damage: BuildDamage(args.DamageDelta)));
    }

    private void OnAttacked(EntityUid uid, CircuitAssemblyComponent component, AttackedEvent args)
    {
        QueueForAssembly(
            uid,
            new CircuitEvent(
                "attacked",
                args.User,
                Name(args.Used),
                null,
                Item: args.Used));
    }

    private void OnProjectileHit(EntityUid uid, CircuitAssemblyComponent component, ref ProjectileHitEvent args)
    {
        QueueForAssembly(
            uid,
            new CircuitEvent(
                "projectile",
                args.Shooter ?? uid,
                "projectile",
                null,
                Damage: BuildDamage(args.Damage)));
    }

    private void QueueForNearby(EntityUid source, string type, string message, string? emote = null)
    {
        var sourcePos = _transform.GetMapCoordinates(source);

        foreach (var runtime in _lua.Runtimes)
        {
            if (!TryComp<CircuitAssemblyComponent>(runtime.Assembly, out var component))
                continue;

            if (!component.Capabilities.HasFlag(CircuitCapability.Sensor) ||
                !component.Powered ||
                _mobState.IsDead(runtime.Assembly) ||
                source == runtime.Assembly ||
                !runtime.Program.IsRunning)
            {
                continue;
            }

            var assemblyPos = _transform.GetMapCoordinates(runtime.Assembly);
            if (assemblyPos.MapId != sourcePos.MapId ||
                (assemblyPos.Position - sourcePos.Position).Length() > component.SensorRange)
            {
                continue;
            }

            runtime.QueueEvent(new CircuitEvent(type, source, message, null, Emote: emote));
        }
    }

    private void QueueForAssembly(EntityUid assembly, CircuitEvent ev)
    {
        if (!TryComp<CircuitAssemblyComponent>(assembly, out var component) ||
            !component.Powered ||
            _mobState.IsDead(assembly))
        {
            return;
        }

        var runtime = _lua.GetRuntime(assembly);
        if (runtime is null || !runtime.Program.IsRunning)
            return;

        runtime.QueueEvent(ev);
    }

    private static Dictionary<string, float>? BuildDamage(DamageSpecifier? damage)
    {
        if (damage == null)
            return null;

        var result = new Dictionary<string, float>();
        foreach (var (type, amount) in damage.DamageDict)
            result[type] = amount.Float();

        return result;
    }
}
