import {config} from './config';

/** Adresse de paiement acceptée seulement en https:, sinon null. */
export function safeCheckoutUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    return new URL(url).protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}

/**
 * Redirige vers la page du prestataire. En démo, la page de paiement simulé (même origine) relit d'abord
 * sessionStorage['restauspace-demo-payment'] pour savoir où revenir et quelle API appeler.
 */
export function goToCheckout(checkoutUrl: string, returnUrl: string) {
  if (config.demo) {
    try {
      window.sessionStorage.setItem('restauspace-demo-payment', JSON.stringify({returnUrl, apiUrl: config.apiUrl, publishableKey: config.publishableKey}));
    } catch {}
  }
  window.location.assign(checkoutUrl);
}
