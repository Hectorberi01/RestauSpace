// Politique de sécurité du contenu (CSP), partagée par next.config.ts et proxy.ts.
// Pages sensibles (jetons client, borne, suivi, réservation…) : CSP stricte à nonce, rendu dynamique.
// Pages vitrine : statiques, CSP de next.config.ts ('unsafe-inline' requis par les scripts inline de Next).

/** Premiers segments d'URL des pages sensibles. Chacun a un layout qui appelle `connection()`. */
export const sensitiveSegments = [
  'borne',
  'commande',
  'commander',
  'compte',
  'desinscription',
  'menu',
  'questionnaire',
  'recu',
  'reservation',
  'reserver',
  'table',
] as const;

export const isSensitivePath = (pathname: string) => {
  const first = pathname.split('/')[1] ?? '';
  return (sensitiveSegments as readonly string[]).includes(first);
};

/** Motif `source` de next.config.ts : tous les chemins sauf les pages sensibles. */
export const nonSensitiveSource = `/((?!(?:${sensitiveSegments.join('|')})(?:/|$)).*)`;

const apiOrigin = (() => {
  try {
    return new URL(process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5080').origin;
  } catch {
    return '';
  }
})();

const turnstile = 'https://challenges.cloudflare.com';

// Démos des modèles de site, intégrées en iframe sur /modeles/<slug>.
const demoOrigin = (() => {
  try {
    return new URL(process.env.NEXT_PUBLIC_DEMO_BASE_URL || 'https://demo.restauspace.com').origin;
  } catch {
    return '';
  }
})();

function policy(scriptSrc: string, isDev: boolean) {
  return [
    "default-src 'self'",
    `script-src ${scriptSrc}`,
    `frame-src ${turnstile} ${demoOrigin}${isDev ? ' http://localhost:3005' : ''}`.trim(),
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: https:",
    "font-src 'self'",
    `connect-src 'self' ${apiOrigin}${isDev ? ' ws: wss:' : ''}`.trim(),
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join('; ');
}

/** Pages vitrine (statiques). */
export const staticPolicy = () => policy(`'self' 'unsafe-inline' ${turnstile}`, false);

/** Pages sensibles. Turnstile est chargé par un script de confiance : 'strict-dynamic' l'autorise. */
export const noncePolicy = (nonce: string, isDev: boolean) =>
  policy(`'self' 'nonce-${nonce}' 'strict-dynamic' ${turnstile}${isDev ? " 'unsafe-eval'" : ''}`, isDev);
