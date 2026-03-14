using SQLMigration;

namespace OpenAI.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__OpenAI")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
    }
}
