using Content.Server._PS.Circuitry.Components;
using Robust.Shared.Timing;

namespace Content.Server._PS.Circuitry.Systems;

/// <summary>
/// Palmtree: drains the assembly's power cell for idle module draw. Movement and actions are
/// charged by the systems that perform them through <see cref="CircuitAssemblySystem.TryConsumePower"/>.
/// </summary>
public sealed class CircuitPowerSystem : EntitySystem
{
    [Dependency] private readonly CircuitAssemblySystem _assembly = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CircuitAssemblyComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (now < component.NextIdlePowerDraw)
                continue;

            component.NextIdlePowerDraw = now + TimeSpan.FromSeconds(1);
            _assembly.RefreshPowerState(uid, component);

            if (!component.Powered || component.IdlePowerDraw <= 0f)
                continue;

            if (!_assembly.TryConsumePower(uid, component.IdlePowerDraw, component))
                _assembly.RefreshPowerState(uid, component);
        }
    }
}
