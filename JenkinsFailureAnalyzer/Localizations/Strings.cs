using System.Globalization;
using System.Resources;

namespace JenkinsFailureAnalyzer.Localizations;

internal static class Strings
{
    private static readonly ResourceManager s_ResourceManager = new("JenkinsFailureAnalyzer.Localizations.Strings", typeof(Strings).Assembly);

    public static string AccessDeniedDescription => GetString(nameof(AccessDeniedDescription));
    public static string AccessDeniedEyebrow => GetString(nameof(AccessDeniedEyebrow));
    public static string AccessDeniedTitle => GetString(nameof(AccessDeniedTitle));
    public static string AnalysisResult => GetString(nameof(AnalysisResult));
    public static string AppTitle => GetString(nameof(AppTitle));
    public static string Build => GetString(nameof(Build));
    public static string CapturedContent => GetString(nameof(CapturedContent));
    public static string Commit => GetString(nameof(Commit));
    public static string EmptyDescription => GetString(nameof(EmptyDescription));
    public static string EmptyTitle => GetString(nameof(EmptyTitle));
    public static string ErrorTitle => GetString(nameof(ErrorTitle));
    public static string FailedStage => GetString(nameof(FailedStage));
    public static string Job => GetString(nameof(Job));
    public static string LikelyCauses => GetString(nameof(LikelyCauses));
    public static string Logout => GetString(nameof(Logout));
    public static string NotFoundDescription => GetString(nameof(NotFoundDescription));
    public static string NotFoundTitle => GetString(nameof(NotFoundTitle));
    public static string RecentAnalyses => GetString(nameof(RecentAnalyses));
    public static string Received => GetString(nameof(Received));
    public static string Refresh => GetString(nameof(Refresh));
    public static string Reload => GetString(nameof(Reload));
    public static string RemoteAddress => GetString(nameof(RemoteAddress));
    public static string SelectAnalysis => GetString(nameof(SelectAnalysis));
    public static string SuggestedActions => GetString(nameof(SuggestedActions));
    public static string Summary => GetString(nameof(Summary));
    public static string Unknown => GetString(nameof(Unknown));

    private static string GetString(string name)
    {
        return s_ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
    }
}
