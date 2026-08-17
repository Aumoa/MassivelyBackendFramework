using SQLMigration;

namespace OAuth2.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__OAuth2")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
        yield return new _2__Add_client();
        yield return new _3__Add_account_sub();
        yield return new _4__Add_email_verify();
        yield return new _5__Add_client_user_group();
        yield return new _6__Fix_client_user_group_unique_constraint();
        yield return new _7__Add_api_key();
        yield return new _8__Change_api_key_to_user_key();
        yield return new _9__Add_api_key_scope_and_client_restriction();
        yield return new _10__Add_client_default_scopes();
        yield return new _11__Add_oauth_grant();
        yield return new _12__Add_account_updated_at();
        yield return new _13__Add_account_picture();
        yield return new _14__Add_account_role();
    }
}
