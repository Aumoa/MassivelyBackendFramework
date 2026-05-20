using SQLMigration;

namespace NPay.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__NPay")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
        yield return new _2__AddAllowGuestExpenseEdit();
    }
}
