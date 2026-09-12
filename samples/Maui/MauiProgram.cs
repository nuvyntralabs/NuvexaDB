using Microsoft.Extensions.Logging;
using Nuventra.NuvexaDB;

namespace NuvexaDB.Samples.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddNuvexaDB(o =>
        {
            o.Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NuvexaDB.Sample", "cache.nvx");
        });
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
