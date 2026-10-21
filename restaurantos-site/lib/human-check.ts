import {tr} from '@/lib/i18n';
// Contrôle anti-robot (Cloudflare Turnstile) quand l'application de vente l'exige : un widget discret, rendu une fois,
// qui ne s'affiche que si Cloudflare demande une interaction. Chaque jeton ne sert qu'une fois.

interface TurnstileApi {
  render: (container: HTMLElement, options: Record<string, unknown>) => string;
  getResponse: (widget: string) => string | undefined;
  reset: (widget: string) => void;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

const SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
const WAIT_MS = 30_000;

let siteKey: string | null = null;
let widget: Promise<string> | null = null;
let waiting: ((token: string) => void)[] = [];

/** Enregistre la clé de site renvoyée par l'API (null : pas de contrôle demandé). */
export function setHumanCheckKey(key: string | null | undefined) {
  siteKey = key ?? null;
}

export const humanCheckRequired = () => siteKey !== null;

function loadScript(): Promise<TurnstileApi> {
  if (window.turnstile) return Promise.resolve(window.turnstile);
  return new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = SCRIPT;
    script.async = true;
    script.onload = () => (window.turnstile ? resolve(window.turnstile) : reject(new Error('turnstile')));
    script.onerror = () => reject(new Error('turnstile'));
    document.head.appendChild(script);
  });
}

function render(key: string): Promise<string> {
  widget ??= loadScript().then(api => {
    const container = document.createElement('div');
    container.className = 'fixed bottom-4 left-1/2 z-50 -translate-x-1/2';
    document.body.appendChild(container);
    return api.render(container, {
      sitekey: key,
      appearance: 'interaction-only',
      callback: (token: string) => {
        const resolvers = waiting;
        waiting = [];
        resolvers.forEach(resolve => resolve(token));
      },
    });
  });
  widget.catch(() => (widget = null));
  return widget;
}

/** Un jeton frais pour une requête protégée ; null si aucun contrôle n'est demandé. Lève si le contrôle n'aboutit pas. */
export async function humanCheckToken(): Promise<string | null> {
  if (!siteKey || typeof window === 'undefined') return null;
  let id: string;
  try {
    id = await render(siteKey);
  } catch {
    throw new Error(tr('Le contrôle anti-robot n’a pas pu se charger. Vérifiez votre connexion et réessayez.'));
  }

  const api = window.turnstile!;
  const ready = api.getResponse(id);
  if (ready) return ready;
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      waiting = waiting.filter(item => item !== done);
      reject(new Error(tr('Le contrôle anti-robot n’a pas abouti. Réessayez.')));
    }, WAIT_MS);
    const done = (token: string) => {
      window.clearTimeout(timer);
      resolve(token);
    };
    waiting.push(done);
  });
}

/** Après usage, le jeton est consommé : un nouveau se prépare pour la requête suivante. */
export async function resetHumanCheck() {
  if (!widget || !window.turnstile) return;
  try {
    window.turnstile.reset(await widget);
  } catch {}
}
