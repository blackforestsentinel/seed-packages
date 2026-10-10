using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bfs.Seed.Storage;

/// <summary>Registrierung der Datenhaltung.</summary>
public static class SeedStorageExtensions
{
    /// <summary>
    /// Registriert <see cref="SeedStorageClients"/>, die Clients für Table, Blob und Queue und
    /// <see cref="ISeedQueueSender"/> für die Verbindung <c>SeedStorage</c>. Fehlt sie, startet
    /// die App nicht.
    /// </summary>
    public static FunctionsApplicationBuilder AddSeedStorage(this FunctionsApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Configuration.GetSection(SeedStorageOptions.ConnectionName);

        builder.Services
            .AddOptions<SeedStorageOptions>()
            .Bind(section)
            // Lokal steht die Verbindungszeichenfolge direkt im Wert von SeedStorage, wie es
            // auch die Trigger der Functions verstehen.
            .PostConfigure(o => o.ConnectionString ??= section.Value)
            .Validate(o => o.IsComplete,
                "AddSeedStorage(): Verbindung SeedStorage fehlt oder ist unvollständig: lokal SeedStorage = UseDevelopmentStorage=true, " +
                "in Azure SeedStorage__blobServiceUri, __queueServiceUri, __tableServiceUri (credential nur managedidentity). " +
                "Ist das Feature storage in project.yaml aktiv?")
            .ValidateOnStart();

        builder.Services.TryAddSingleton<SeedStorageClients>();
        builder.Services.TryAddSingleton(sp => sp.GetRequiredService<SeedStorageClients>().Tables);
        builder.Services.TryAddSingleton(sp => sp.GetRequiredService<SeedStorageClients>().Blobs);
        builder.Services.TryAddSingleton(sp => sp.GetRequiredService<SeedStorageClients>().Queues);
        builder.Services.TryAddSingleton<ISeedQueueSender, SeedQueueSender>();

        return builder;
    }

    /// <summary>
    /// Registriert <see cref="ISeedTableRepository{T}"/> für die Tabelle <paramref name="tableName"/>.
    /// Setzt <see cref="AddSeedStorage"/> voraus.
    /// </summary>
    public static IServiceCollection AddSeedTable<T>(this IServiceCollection services, string tableName)
        where T : class, ITableEntity, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!SeedTableKeys.IsValidTableName(tableName))
        {
            throw new ArgumentException($"Ungültiger Tabellenname '{tableName}': Buchstaben und Ziffern, 3-63 Zeichen, beginnt mit einem Buchstaben.", nameof(tableName));
        }

        services.TryAddSingleton<ISeedTableRepository<T>>(sp =>
            new SeedTableRepository<T>(sp.GetRequiredService<SeedStorageClients>(), tableName));
        return services;
    }
}
