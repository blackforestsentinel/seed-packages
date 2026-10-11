// tsc kopiert keine CSS-Dateien; das Stylesheet geht unverändert nach dist.
import { copyFileSync } from 'node:fs';

copyFileSync(new URL('../src/styles.css', import.meta.url), new URL('../dist/styles.css', import.meta.url));
