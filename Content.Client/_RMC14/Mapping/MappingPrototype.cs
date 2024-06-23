﻿using Robust.Shared.Prototypes;

namespace Content.Client._RMC14.Mapping;

public sealed class MappingPrototype
{
    public readonly IPrototype? Prototype;
    public readonly string Name;
    public List<MappingPrototype>? Parents;
    public List<MappingPrototype>? Children;

    public MappingPrototype(IPrototype? prototype, string name)
    {
        Prototype = prototype;
        Name = name;
    }
}
