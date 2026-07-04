using SQLMigration;

namespace SecretGate.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__SecretGate")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
    }
}
