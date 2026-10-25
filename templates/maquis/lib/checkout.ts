/** Adresse de paiement acceptée seulement en https:, sinon null. */
export function safeCheckoutUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    return new URL(url).protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}

/** Redirige vers la page du prestataire ; il renvoie le client vers la page de retour déclarée au paiement. */
export function goToCheckout(checkoutUrl: string, _returnUrl: string) {
  window.location.assign(checkoutUrl);
}
