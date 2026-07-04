using SQLMigration;

namespace MasterServer.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__MasterServer")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
        yield return new _2__GatewayBackendRoutePolicy();
        yield return new _3__GatewayClientSecretCredentials();
        yield return new _4__BackendPacketManifest();
        yield return new _5__BackendPacketManifestVerifierProgram();
    }
}
