using SQLMigration;

namespace GatewayServer.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__GatewayServer")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
    }
}
