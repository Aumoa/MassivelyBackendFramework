using SQLMigration;

namespace DiscordBot.SQL.Migration;

[DatabaseTarget("MassivelyBackendFramework__DiscordBot")]
public partial class Scripts : IScripts
{
    public IEnumerable<IScript> GetScripts()
    {
        yield return new _1__Init();
        yield return new _2__AddChatLogSearch();
        yield return new _3__AddAllowedChannels();
        yield return new _4__AddClaudeSettings();
        yield return new _5__AddToolSettings();
        yield return new _6__AddChatLogImages();
        yield return new _7__AddAllowedChannelRequests();
        yield return new _8__AddAppointments();
        yield return new _9__AddAppointmentHasTime();
        yield return new _10__AddAppointmentChannelIndex();
    }
}
