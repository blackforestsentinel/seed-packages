// Fehler der Anmeldung, ohne Abhängigkeit von MSAL, damit auch der Einstieg /extension sie nutzen
// kann, ohne MSAL mitzuladen.

/** Wird geworfen, wenn für ein Token eine Anmeldung nötig ist und die Seite dafür umleitet. */
export class SeedAuthRedirectError extends Error {
  constructor() {
    super('Anmeldung nötig, die Seite wird umgeleitet.');
    this.name = 'SeedAuthRedirectError';
  }
}

/**
 * Wird geworfen, wenn es für zusätzliche Berechtigungen (etwa Graph) kein Token gibt und die
 * Anmeldung dafür schon versucht wurde: im Web je Tab still und interaktiv per Umleitung, in einer
 * Browser-Erweiterung je Browser-Sitzung still. Meist fehlt die Einwilligung, die die Person nicht
 * selbst erteilen darf; ein weiterer Versuch änderte nichts.
 */
export class SeedAuthScopeError extends Error {
  readonly scopes: string[];

  constructor(scopes: string[], options?: { cause?: unknown }) {
    super(`Kein Token für ${scopes.join(' ')}: Entra ID verlangt eine Anmeldung oder Einwilligung, die schon versucht wurde.`, options);
    this.name = 'SeedAuthScopeError';
    this.scopes = scopes;
  }
}

/**
 * Wird in Browser-Erweiterungen (createSeedExtensionAuth) geworfen, wenn es ein Token nur noch mit
 * einem Anmeldefenster gäbe. getAccessToken öffnet keins; die App zeigt dann etwa „Anmeldung
 * erneuern“ und ruft nach einem Klick login() auf.
 */
export class SeedAuthInteractionRequiredError extends Error {
  constructor(options?: { cause?: unknown }) {
    super('Anmeldung nötig: Entra ID verlangt eine Interaktion.', options);
    this.name = 'SeedAuthInteractionRequiredError';
  }
}
