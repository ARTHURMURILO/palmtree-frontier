namespace Content.Shared._Goobstation.Supermatter.Events;

[ByRefEvent]
public readonly record struct SupermatterConsumedEvent(EntityUid Supermatter);
