# Sicherheit

## Schwachstelle melden

Sicherheitslücken bitte nicht als öffentliches Issue, Pull Request oder Diskussion melden, sondern vertraulich über GitHub: im Reiter **Security** dieses Repos auf **Report a vulnerability** (Private Vulnerability Reporting). Die Meldung sehen nur die Maintainer des Repos.

Hilfreich sind:

- betroffenes Paket und Version, etwa `Bfs.Seed.Auth` 0.8.0 oder `@blackforestsentinel/seed-web-core` 0.8.0,
- was sich ausnutzen lässt und mit welcher Wirkung,
- Schritte oder Code zum Nachvollziehen, ohne echte Tokens, Zugangsdaten oder personenbezogene Daten.

Die Maintainer bestätigen den Eingang, prüfen die Meldung und stimmen Fix und Veröffentlichung eines Security Advisorys über die Meldung ab.

## Unterstützte Versionen

Alle Pakete erscheinen gemeinsam in einer Version. Fixes gibt es für die jeweils neueste Minor-Version, als Patch-Release (etwa 0.8.1, solange 0.8 die neueste ist). Ältere Versionen bekommen keine Fixes; vor einer Meldung bitte prüfen, ob die Lücke in der neuesten Version noch besteht.
