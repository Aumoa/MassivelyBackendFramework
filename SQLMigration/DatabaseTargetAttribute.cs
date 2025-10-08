namespace SQLMigration;

[AttributeUsage(AttributeTargets.Class)]
public class DatabaseTargetAttribute(string databaseName) : Attribute
{
    public readonly string DatabaseName = databaseName;
}
