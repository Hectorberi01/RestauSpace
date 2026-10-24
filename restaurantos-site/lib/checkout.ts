/** Adresse de paiement externe acceptée seulement en https:, sinon null. */
export function safeCheckoutUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    return new URL(url).protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}
