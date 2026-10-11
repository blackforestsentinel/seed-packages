// Setzt die Version aller Pakete und ihrer Abhängigkeiten untereinander auf die Release-Version,
// etwa die Peer-Abhängigkeit von seed-web-people auf seed-web-auth. Im Repo stehen 0.0.0-dev;
// npm version passt solche Abhängigkeiten nicht an und bricht beim anschließenden Install ab.
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';

const version = process.argv[2] ?? '';
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version)) {
  console.error('Aufruf: node scripts/set-version.mjs 1.2.3');
  process.exit(1);
}

const root = new URL('../packages/', import.meta.url);
const packages = readdirSync(root)
  .map((name) => new URL(`${name}/package.json`, root))
  .filter((url) => existsSync(url))
  .map((url) => ({ url, manifest: JSON.parse(readFileSync(url, 'utf8')) }));
const names = new Set(packages.map(({ manifest }) => manifest.name));

for (const { url, manifest } of packages) {
  manifest.version = version;
  for (const field of ['dependencies', 'peerDependencies', 'devDependencies', 'optionalDependencies']) {
    for (const name of Object.keys(manifest[field] ?? {})) {
      if (names.has(name)) {
        // Peers mit ^, damit ein Patch-Release eines Pakets allein passt.
        manifest[field][name] = field === 'peerDependencies' ? `^${version}` : version;
      }
    }
  }
  writeFileSync(url, `${JSON.stringify(manifest, null, 2)}\n`);
  console.log(`${manifest.name}@${version}`);
}
