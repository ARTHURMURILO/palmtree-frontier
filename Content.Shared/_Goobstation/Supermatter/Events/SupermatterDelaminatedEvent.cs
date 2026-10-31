namespace Content.Shared._Goobstation.Supermatter.Events;

public sealed class SupermatterDelaminatedEvent(EntityUid supermatter) : EntityEventArgs
{
    public readonly EntityUid Supermatter = supermatter;
}
