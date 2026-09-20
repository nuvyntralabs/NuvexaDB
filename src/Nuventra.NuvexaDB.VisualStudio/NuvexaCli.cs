namespace Nuventra.NuvexaDB.VisualStudio;

/// <summary>
/// Resolves the <c>nuvexa</c> CLI: VSIX-bundled <c>cli/nuvexa</c> first, then PATH.
/// The Visual Studio tool window stays in-process; this is for terminals and pack layout.
/// </summary>
public static class NuvexaCli
{
    public static string FileName => OperatingSystem.IsWindows() ? "nuvexa.exe" : "nuvexa";

    public static string Resolve()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "cli", FileName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        return "nuvexa";
    }
}
