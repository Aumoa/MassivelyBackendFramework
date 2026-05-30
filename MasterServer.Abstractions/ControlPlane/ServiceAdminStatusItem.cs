using System;

namespace MasterServer.ControlPlane;

public sealed class ServiceAdminStatusItem
{
    public ServiceAdminStatusItem(string group, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            throw new ArgumentException("Status group is required.", nameof(group));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Status name is required.", nameof(name));
        }

        Group = group;
        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Group { get; }

    public string Name { get; }

    public string Value { get; }
}
