using System.Runtime.CompilerServices;
using Azure;
using Azure.Data.Tables;

namespace Bfs.Seed.Storage;

/// <summary>
/// Schlankes Repository für eine Tabelle mit optimistischer Nebenläufigkeit: Ändern und
/// Löschen nur mit dem ETag des gelesenen Stands; wer dazwischen geschrieben hat, gewinnt,
/// und der zweite Schreiber bekommt <see cref="SeedConcurrencyException"/>.
/// </summary>
/// <typeparam name="T">Entität mit PartitionKey, RowKey und ETag.</typeparam>
public interface ISeedTableRepository<T> where T : class, ITableEntity, new()
{
    /// <summary>Name der Tabelle.</summary>
    string TableName { get; }

    /// <summary>Liest einen Datensatz samt ETag; <c>null</c>, wenn es ihn nicht gibt.</summary>
    Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    /// <summary>Alle Datensätze einer Partition, z. B. einer Person (<see cref="SeedPartitionKeys.ForUser"/>).</summary>
    IAsyncEnumerable<T> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abfrage mit OData-Filter; Werte darin per <see cref="TableClient.CreateQueryFilter(FormattableString)"/>
    /// maskieren. Ohne Filter die ganze Tabelle.
    /// </summary>
    IAsyncEnumerable<T> QueryAsync(string? filter, CancellationToken cancellationToken = default);

    /// <summary>Legt einen Datensatz an und setzt den neuen ETag. Gibt es ihn schon: 409.</summary>
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ersetzt einen Datensatz, aber nur, wenn sein ETag noch gilt, und setzt den neuen ETag.
    /// Geändert: 412, gelöscht: 404. <see cref="ETag.All"/> schreibt bedingungslos.
    /// </summary>
    Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder ersetzt ohne ETag-Prüfung, für Daten mit nur einem Schreiber.</summary>
    Task<T> UpsertAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lesen, ändern, bedingt schreiben; bei einem Konflikt mit frisch gelesenem Stand erneut,
    /// höchstens fünfmal. <paramref name="change"/> liefert <c>false</c>, wenn nichts zu schreiben
    /// ist. Ergebnis: der geschriebene Stand, <c>null</c>, wenn es den Datensatz nicht (mehr) gibt.
    /// </summary>
    Task<T?> ModifyAsync(string partitionKey, string rowKey, Func<T, bool> change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Löscht einen Datensatz, wenn sein ETag noch gilt (<see cref="ETag.All"/>: bedingungslos).
    /// <c>false</c>, wenn es ihn schon nicht mehr gibt; geändert: 412.
    /// </summary>
    Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken = default);
}

/// <summary>Implementierung von <see cref="ISeedTableRepository{T}"/> auf Azure Table Storage.</summary>
public sealed class SeedTableRepository<T> : ISeedTableRepository<T> where T : class, ITableEntity, new()
{
    internal const int MaxModifyAttempts = 5;

    private readonly SeedStorageClients clients;

    /// <summary>Repository für die Tabelle <paramref name="tableName"/>.</summary>
    public SeedTableRepository(SeedStorageClients clients, string tableName)
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (!SeedTableKeys.IsValidTableName(tableName))
        {
            throw new ArgumentException($"Ungültiger Tabellenname '{tableName}': Buchstaben und Ziffern, 3-63 Zeichen, beginnt mit einem Buchstaben.", nameof(tableName));
        }

        this.clients = clients;
        TableName = tableName;
    }

    /// <inheritdoc />
    public string TableName { get; }

    /// <inheritdoc />
    public async Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        SeedTableKeys.Validate(partitionKey, nameof(partitionKey));
        SeedTableKeys.Validate(rowKey, nameof(rowKey));

        var table = await clients.GetTableAsync(TableName, cancellationToken);
        var response = await table.GetEntityIfExistsAsync<T>(partitionKey, rowKey, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<T> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default)
    {
        SeedTableKeys.Validate(partitionKey, nameof(partitionKey));
        return QueryAsync(TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}"), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync(string? filter, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var table = await clients.GetTableAsync(TableName, cancellationToken);
        await foreach (var entity in table.QueryAsync<T>(filter, cancellationToken: cancellationToken))
        {
            yield return entity;
        }
    }

    /// <inheritdoc />
    public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ValidateEntity(entity);

        var table = await clients.GetTableAsync(TableName, cancellationToken);
        try
        {
            var response = await table.AddEntityAsync(entity, cancellationToken);
            entity.ETag = response.Headers.ETag ?? default;
            return entity;
        }
        catch (RequestFailedException e) when (e.Status == 409)
        {
            throw new SeedConcurrencyException(TableName, entity.PartitionKey, entity.RowKey, e.Status, e);
        }
    }

    /// <inheritdoc />
    public async Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ValidateEntity(entity);
        if (entity.ETag == default)
        {
            throw new ArgumentException("UpdateAsync braucht den ETag des gelesenen Stands: erst GetAsync, oder UpsertAsync ohne Prüfung.", nameof(entity));
        }

        var table = await clients.GetTableAsync(TableName, cancellationToken);
        try
        {
            // Replace statt Merge: Der Datensatz entspricht danach genau dem Objekt, auch wenn
            // Eigenschaften entfallen sind.
            var response = await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
            entity.ETag = response.Headers.ETag ?? default;
            return entity;
        }
        catch (RequestFailedException e) when (e.Status is 404 or 412)
        {
            throw new SeedConcurrencyException(TableName, entity.PartitionKey, entity.RowKey, e.Status, e);
        }
    }

    /// <inheritdoc />
    public async Task<T> UpsertAsync(T entity, CancellationToken cancellationToken = default)
    {
        ValidateEntity(entity);

        var table = await clients.GetTableAsync(TableName, cancellationToken);
        var response = await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        entity.ETag = response.Headers.ETag ?? default;
        return entity;
    }

    /// <inheritdoc />
    public async Task<T?> ModifyAsync(string partitionKey, string rowKey, Func<T, bool> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        for (var attempt = 1; ; attempt++)
        {
            var entity = await GetAsync(partitionKey, rowKey, cancellationToken);
            if (entity is null)
            {
                return null;
            }

            if (!change(entity))
            {
                return entity;
            }

            try
            {
                return await UpdateAsync(entity, cancellationToken);
            }
            catch (SeedConcurrencyException e) when (e.Status == 404)
            {
                return null;
            }
            catch (SeedConcurrencyException) when (attempt < MaxModifyAttempts)
            {
                // Jemand war schneller: mit dem neuen Stand noch einmal.
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        ValidateEntity(entity);
        if (entity.ETag == default)
        {
            throw new ArgumentException("DeleteAsync braucht den ETag des gelesenen Stands, oder ETag.All für bedingungsloses Löschen.", nameof(entity));
        }

        var table = await clients.GetTableAsync(TableName, cancellationToken);
        try
        {
            // Azure.Data.Tables wertet 404 beim Löschen nicht als Fehler.
            var response = await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag, cancellationToken);
            return response.Status != 404;
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return false;
        }
        catch (RequestFailedException e) when (e.Status == 412)
        {
            throw new SeedConcurrencyException(TableName, entity.PartitionKey, entity.RowKey, e.Status, e);
        }
    }

    private static void ValidateEntity(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        SeedTableKeys.Validate(entity.PartitionKey, "entity.PartitionKey");
        SeedTableKeys.Validate(entity.RowKey, "entity.RowKey");
    }
}
