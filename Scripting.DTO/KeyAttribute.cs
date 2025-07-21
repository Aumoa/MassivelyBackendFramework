#if SCRIPTING
using System;

namespace MessagePack;

[AttributeUsage(AttributeTargets.Property)]
public class KeyAttribute : Attribute
{
    public KeyAttribute(int x)
    {
    }
}
#endif
