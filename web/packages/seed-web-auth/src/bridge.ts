import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge';

/**
 * Für die Bridge-Seite des Frontends (im Template /redirect.html). MSAL 5 lädt bei der stillen
 * Anmeldung die Redirect-URI in einem unsichtbaren iframe und erwartet von dieser Seite die Antwort
 * von Entra ID per BroadcastChannel. Die App selbst ruft dort nichts auf; ohne Bridge-Seite läuft
 * die stille Anmeldung in redirect_bridge_timeout.
 *
 * Ruft jemand die Seite direkt auf, gibt es keine Antwort weiterzureichen; dann geht es zur Startseite.
 */
export async function handleSeedAuthRedirect(): Promise<void> {
  try {
    await broadcastResponseToMainFrame();
  } catch (error) {
    if (window.parent === window && !window.opener) {
      window.location.replace('/');
      return;
    }
    throw error;
  }
}
