#if SCRIPTING
using System;

namespace MessagePack;

[AttributeUsage(AttributeTargets.Class)]
public class MessagePackObjectAttribute : Attribute
{
}
#endif
