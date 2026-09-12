using Microsoft.Extensions.DependencyInjection;

namespace Nuventra.NuvexaDB;

public sealed class NuvexaDbOptions
{
    public string Path { get; set; } = "app.nvx";
    public string? EncryptionKey { get; set; }
    public bool CreateIfMissing { get; set; } = true;
    public int CacheSizeMb { get; set; } = 16;
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNuvexaDB(this IServiceCollection services, Action<NuvexaDbOptions>? configure = null)
    {
        var options = new NuvexaDbOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<NuvexaDbOptions>();
            if (File.Exists(o.Path))
            {
                return NuvexaDatabase.Open(o.Path, new NuvexaOpenOptions
                {
                    EncryptionKey = o.EncryptionKey,
                    CacheSizeMb = o.CacheSizeMb
                });
            }

            if (!o.CreateIfMissing)
            {
                throw new NuvexaException($"Database '{o.Path}' was not found.");
            }

            return NuvexaDatabase.Create(o.Path, new NuvexaCreateOptions
            {
                EncryptionKey = o.EncryptionKey,
                CacheSizeMb = o.CacheSizeMb
            });
        });
        return services;
    }
}
