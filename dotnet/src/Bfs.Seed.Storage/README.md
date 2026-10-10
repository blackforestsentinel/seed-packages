# Bfs.Seed.Storage

Datenhaltung für Azure Functions (dotnet-isolated) in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template): Clients für Table, Blob und Queue auf dem eigenen Storage Account des Projekts, ein schlankes Table-Repository mit ETag und Hilfen für die Trennung der Daten je Person oder Tenant.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.AddSeedCore();
builder.AddSeedStorage();
builder.Services.AddSeedTable<JobStatus>("jobs");
builder.Build().Run();
```

## Verbindung `SeedStorage`

Das Terraform-Modul `storage` legt den Storage Account an und setzt die App-Settings einer identitätsbasierten Verbindung namens `SeedStorage`. Der Name ist bewusst nicht `AzureWebJobsStorage`: Das ist der Host-Storage der Function aus dem Modul `core`.

| App-Setting | Wert |
| --- | --- |
| `SeedStorage__blobServiceUri`, `__queueServiceUri`, `__tableServiceUri` | Endpunkte des Storage Accounts |
| `SeedStorage__credential` | `managedidentity` |
| `SeedStorage__clientId` | Client-ID der User-Assigned Managed Identity der Function |

In Azure meldet sich das Paket per `ManagedIdentityCredential` mit dieser Client-ID an. Ohne `credential` nimmt es `DefaultAzureCredential`, etwa lokal gegen einen echten Account nach `az login` (die Person braucht dann selbst die Datenrollen). Lokal mit Azurite genügt `SeedStorage = UseDevelopmentStorage=true`. Fehlt die Verbindung, startet die App nicht.

Dieselbe Verbindung nutzen die Trigger der Functions:

```csharp
[Function("ProcessJob")]
public Task Process([QueueTrigger("jobs", Connection = SeedStorageOptions.ConnectionName)] JobMessage message) { … }
```

Tabellen, Queues und Container legt in Azure Terraform aus `project.yaml` an; ein Tippfehler im Namen fällt dort als Fehler auf. Lokal (Verbindungszeichenfolge) legt das Paket sie beim ersten Zugriff an.

## Clients

`AddSeedStorage()` registriert `SeedStorageClients` sowie `TableServiceClient`, `BlobServiceClient` und `QueueServiceClient`. `SeedStorageClients.GetTableAsync`, `GetContainerAsync` und `GetQueueAsync` liefern die Clients einzelner Tabellen, Container und Queues. Wer die Clients ohne Dependency Injection braucht, erzeugt sie mit `SeedStorageClientFactory`.

## Table-Repository mit ETag

`AddSeedTable<T>("jobs")` registriert `ISeedTableRepository<T>` für eine Entität mit `ITableEntity`:

| Methode | Verhalten |
| --- | --- |
| `GetAsync`, `QueryPartitionAsync`, `QueryAsync` | lesen samt ETag |
| `AddAsync` | legt an; gibt es den Datensatz schon: `SeedConcurrencyException` (409) |
| `UpdateAsync` | ersetzt nur, wenn der ETag noch gilt; sonst `SeedConcurrencyException` (412 geändert, 404 gelöscht) |
| `ModifyAsync` | lesen, ändern, bedingt schreiben; bei einem Konflikt bis zu fünfmal mit frischem Stand |
| `UpsertAsync` | legt an oder ersetzt ohne Prüfung, für Daten mit nur einem Schreiber |
| `DeleteAsync` | löscht nur mit gültigem ETag (`ETag.All`: bedingungslos) |

Nach jedem Schreiben steht der neue ETag in der Entität. So überschreibt niemand still die Änderung eines anderen; wer den Konflikt bekommt, lädt neu und entscheidet.

## Partition je Person oder Tenant

Konvention: PartitionKey = Object-ID der Person (`oid`) für persönliche Daten, Tenant-ID (`tid`) für Daten einer Organisation. Jede Abfrage bleibt so auf die Partition der angemeldeten Person begrenzt.

```csharp
var job = await jobs.GetAsync(SeedPartitionKeys.ForUser(request.HttpContext.User), id);
```

`SeedPartitionKeys` liest die öffentlichen Claims `oid` und `tid` aus dem `ClaimsPrincipal`, wie `Bfs.Seed.Auth` sie setzt, oder in der langen Form nach dem Claim-Mapping von ASP.NET Core. Eine Paketabhängigkeit zu `Bfs.Seed.Auth` gibt es deshalb nicht. Ohne Claim (nicht angemeldet) werfen `ForUser` und `ForTenant`, damit keine Daten ohne Eigentümerin entstehen; `TryForUser` und `TryForTenant` liefern dann `null`.

## Queue-Nachrichten

`ISeedQueueSender.SendAsync("jobs", message)` stellt eine Nachricht als JSON (camelCase) in die Queue, Base64-kodiert wie es der Queue-Trigger per Default erwartet. Ohne Base64 könnte der Trigger die Nachricht nicht lesen und schöbe sie nach mehreren Fehlversuchen in die Poison-Queue. Nachrichten sind auf 64 KiB nach Base64 begrenzt; größere Daten gehören in einen Blob, die Nachricht trägt nur den Verweis.

Verhalten des Triggers bei Fehlern (Einstellungen in `host.json` unter `extensions.queues`):

- Wirft die Function, wird die Nachricht nach `visibilityTimeout` erneut zugestellt, höchstens `maxDequeueCount`-mal (Default 5).
- Danach verschiebt der Host sie in die Queue `<name>-poison`, die er bei Bedarf selbst anlegt. Dort verarbeitet sie niemand automatisch; sie verfällt nach der Standard-Lebensdauer von 7 Tagen. Wer Fehler nicht verlieren will, hängt eine eigene Function an die Poison-Queue oder alarmiert darauf.
- Nachrichten kommen mindestens einmal an. Die Verarbeitung muss deshalb wiederholbar sein, etwa über einen Status in der Tabelle.
