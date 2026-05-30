using SQLMigration;

namespace NoraeBook.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__NoraeBook")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
    }
}
