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
