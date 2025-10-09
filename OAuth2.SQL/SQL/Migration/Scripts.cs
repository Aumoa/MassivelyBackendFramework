using SQLMigration;

namespace OAuth2.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__OAuth2")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
    }
}
