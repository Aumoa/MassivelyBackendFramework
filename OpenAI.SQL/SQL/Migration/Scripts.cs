using SQLMigration;

namespace OpenAI.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__OpenAI")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
        yield return new _2__Add_message_role();
        yield return new _3__Add_session_removed_at();
    }
}
