using System.Reflection;

namespace AI;

public class ToolsProviderOptions
{
    internal List<Assembly> Assemblies { get; } = [];

    public ToolsProviderOptions AddAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
        return this;
    }

    public ToolsProviderOptions AddAssemblyOf<T>()
    {
        Assemblies.Add(typeof(T).Assembly);
        return this;
    }
}
