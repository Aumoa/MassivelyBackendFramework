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
        yield return new _11__AddChatLogAttachments();
        yield return new _12__AddChatLogMessageReferences();
        yield return new _13__AddChannelNotes();
        yield return new _14__AddAppointmentItems();
        yield return new _15__AddClaudeInstructions();
        yield return new _16__AddAiSkills();
        yield return new _17__AddDiscordUserPermissions();
        yield return new _18__AddAutoResponseSettings();
        yield return new _19__AddAutoResponseEventDiagnostics();
        yield return new _20__AddImageGenerationWorkflows();
        yield return new _21__AddImageGenerationWorkflowNames();
        yield return new _22__AddAmbientChatContextSettings();
    }
}
