using System.Reflection;

namespace AstraFinance.App;

/// <summary>Static application metadata.</summary>
public static class AppInfo
{
    public const string Developer = "Aprotoos";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
