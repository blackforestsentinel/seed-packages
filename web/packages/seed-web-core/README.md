# @blackforestsentinel/seed-web-core

Grundausstattung für Frontends in Projekten aus [Sentinel Seed](https://github.com/blackforestsentinel/seed-template).

```ts
import { createHttpClient, loadRuntimeConfig } from '@blackforestsentinel/seed-web-core';

const config = await loadRuntimeConfig();          // liest /config.json
const api = createHttpClient({ baseUrl: config.apiBaseUrl });
const health = await api.get('/api/health');
```

- `loadRuntimeConfig()` lädt `/config.json`. Die Seed-Pipeline schreibt die Datei beim Deploy je Umgebung, deshalb reicht ein Build für alle Umgebungen.
- `createHttpClient()` spricht JSON mit der Function-API. Über `getAccessToken` hängt er ein Bearer-Token an, ab Phase 2 aus `@blackforestsentinel/seed-web-auth`.
- Pfade beginnen mit genau einem `/` und hängen an `baseUrl` an, auch an einen Pfad darin: `https://example.org/backend` und `/api/items` ergeben `https://example.org/backend/api/items`. Pfade ohne `/` am Anfang (`.evil.example/x`, `@evil.example/x`), mit `//` oder `/\` und volle URLs lehnt der Client mit einem Fehler ab, bevor er ein Token holt oder sendet. Das Token geht nur an Schema und Host von `baseUrl`. Eine relative `baseUrl` gilt nur im Browser, für die Origin der Seite.

Für bedingte Anfragen und eigene Header gibt es `request()`. Es liefert Status, Daten und Antwort-Header, und 304 gilt dort nicht als Fehler; `get`, `post`, `put` und `delete` bleiben, wie sie sind.

```ts
const res = await api.request<Items>('GET', '/api/items', {
  headers: etag ? { 'If-None-Match': etag } : undefined,
});
if (res.status === 304) return cached;
save(res.data, res.headers.get('ETag'));
```

- Außerhalb von 2xx und 304 wirft `request()` wie die anderen Methoden `HttpError`; das Token aus `getAccessToken` hängt es ebenso an.
- `data` ist bei 204, 304 und leerem Body `undefined`.
- Von einer fremden Origin aus liest das Frontend Header wie `ETag` nur, wenn die API sie per `Access-Control-Expose-Headers` freigibt.
