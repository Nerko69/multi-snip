using System.Diagnostics;
using System.Reflection;

namespace MultiSnip;

/// <summary>Reads the metadata defined in MultiSnip.csproj so it is maintained in one place.</summary>
internal static class AppInfo
{
    private static readonly Assembly Asm = typeof(AppInfo).Assembly;

    public static string Product => Asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Multi Snip";
    public static string Description => Asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "";
    public static string Company => Asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "";
    public static string Copyright => Asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
    public static string Version =>
        Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Asm.GetName().Version?.ToString() ?? "";
    public static string FileVersion => Asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "";

    public static Icon LoadIcon()
    {
        using Stream? s = Asm.GetManifestResourceStream("MultiSnip.app.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
