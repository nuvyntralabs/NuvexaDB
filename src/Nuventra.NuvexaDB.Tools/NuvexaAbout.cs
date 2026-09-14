using System.Diagnostics;
using System.Reflection;
using Nuventra.NuvexaDB;

namespace Nuventra.NuvexaDB.Tools;

/// <summary>Shared About copy for Data Studio, Visual Studio, and other hosts.</summary>
public static class NuvexaAbout
{
    public const string Product = "NuvexaDB";
    public const string Studio = "Nuvexa Data Studio";
    public const string Author = "Niladri Prasad Padhy / Nuventra";
    public const string Organization = "Nuvyntra Labs";
    public const string License = "MIT";
    public const string Website = "https://nuvyntralabs.github.io/";
    public const string GitHub = "https://github.com/nuvyntralabs/NuvexaDB";
    public const string NuGet = "https://www.nuget.org/packages/Nuventra.NuvexaDB";
    public const string Summary =
        "Embedded NoSQL database for .NET and .NET MAUI. One portable .nvx file, BSON pages, optional AES-256-GCM, and NQL (Nuvexa Query Language).";
    public const string AboutUs =
        "NuvexaDB is built by Niladri Prasad Padhy (Nuventra) and published with the MauiEssentials catalog under Nuvyntra Labs. The desktop workbench is Nuvexa Data Studio. Visual Studio and VS Code / Cursor share the same browse and NQL session.";

    public static string ProductVersion
    {
        get
        {
            var asm = typeof(NuvexaDatabase).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus < 0 ? info : info[..plus];
            }

            return asm.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public const int FormatVersion = 2;

    public static string Headline(string surface) =>
        string.IsNullOrWhiteSpace(surface) ? Studio : $"{Studio} — {surface}";

    public static string PlainText(string surface) =>
        $"""
        {Headline(surface)}
        {Product} {ProductVersion}   ·   .nvx format {FormatVersion}   ·   {License}

        {Summary}

        {AboutUs}

        Author: {Author}
        Organization: {Organization}
        Website: {Website}
        GitHub: {GitHub}
        NuGet: {NuGet}
        """;

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
