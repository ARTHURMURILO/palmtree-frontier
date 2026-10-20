namespace Content.Shared._PS.Modsuits;

/// <summary>
/// A physical MODsuit piece. The control owns these entities while retracted and equips them while active.
/// </summary>
public enum ModsuitPartSlot : byte
{
    Helmet,
    Chestplate,
    Gauntlets,
    Boots,
}

/// <summary>
/// How a MODsuit module is triggered by the wearer.
/// </summary>
public enum ModsuitModuleType : byte
{
    Passive,
    Toggle,
    Usable,
    Active,
}
