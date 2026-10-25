import {config} from './config';

// Contrôle anti-robot Cloudflare Turnstile, seulement si l'application l'exige (clé renvoyée par l'API) ou si
// NEXT_PUBLIC_TURNSTILE_SITE_KEY est défini. Widget discret ; chaque jeton ne sert qu'une fois.

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

let siteKey: string | null = config.turnstileSiteKey;
let widget: Promise<string> | null = null;
let waiting: ((token: string) => void)[] = [];

/** Clé renvoyée par l'API ; à défaut, celle du build. */
export function setHumanCheckKey(key: string | null | undefined) {
  siteKey = key ?? config.turnstileSiteKey;
}

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
    container.className = 'fixed bottom-4 left-1/2 z-[60] -translate-x-1/2';
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

/** Un jeton frais, ou null sans contrôle demandé. Lève si le contrôle n'aboutit pas. */
export async function humanCheckToken(): Promise<string | null> {
  if (!siteKey || typeof window === 'undefined') return null;
  const id = await render(siteKey);
  const api = window.turnstile!;
  const ready = api.getResponse(id);
  if (ready) return ready;
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      waiting = waiting.filter(item => item !== done);
      reject(new Error('turnstile'));
    }, WAIT_MS);
    const done = (token: string) => {
      window.clearTimeout(timer);
      resolve(token);
    };
    waiting.push(done);
  });
}

/** Le jeton est consommé : on en prépare un nouveau. */
export async function resetHumanCheck() {
  if (!widget || !window.turnstile) return;
  try {
    window.turnstile.reset(await widget);
  } catch {}
}
