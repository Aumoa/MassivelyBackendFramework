namespace SQLMigration;

public interface IScripts
{
    IEnumerable<IScript> GetScripts();
}
