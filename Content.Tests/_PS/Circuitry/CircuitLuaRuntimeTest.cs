#nullable enable
using System.Linq;
using Content.Server._PS.Circuitry.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._PS.Circuitry;

[TestFixture]
public sealed class CircuitLuaRuntimeTest
{
    [Test]
    public void EventQueueDropsOldestWhenFlooded()
    {
        var runtime = new CircuitLuaRuntime();
        var source = new EntityUid(1);

        for (var i = 0; i < CircuitLuaRuntime.MaxQueuedEvents + 5; i++)
            runtime.QueueEvent(new CircuitEvent("signal", source, i.ToString(), null));

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Events, Has.Count.EqualTo(CircuitLuaRuntime.MaxQueuedEvents));
            Assert.That(runtime.Events.Peek().Message, Is.EqualTo("5"));
            Assert.That(runtime.Events.Last().Message, Is.EqualTo((CircuitLuaRuntime.MaxQueuedEvents + 4).ToString()));
        });
    }
}
