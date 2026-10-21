export const API_URL = (process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/+$/, '');

export interface Session {
  userId: string;
  sessionId: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

const STORAGE_KEY = 'restaurantos.session';
const listeners = new Set<(session: Session | null) => void>();
let current: Session | null = null;
let loaded = false;
let refreshing: Promise<Session | null> | null = null;

function read(): Session | null {
  if (loaded || typeof window === 'undefined') return current;
  loaded = true;
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    current = raw ? (JSON.parse(raw) as Session) : null;
  } catch {
    current = null;
  }
  return current;
}

export function getSession(): Session | null {
  return read();
}

export function setSession(session: Session | null) {
  current = session;
  loaded = true;
  try {
    if (session) window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    else window.sessionStorage.removeItem(STORAGE_KEY);
  } catch {}
  listeners.forEach(listener => listener(session));
}

export function onSessionChange(listener: (session: Session | null) => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

async function problem(response: Response): Promise<ApiError> {
  let code: string | undefined;
  let message = `${response.status} ${response.statusText}`;
  try {
    const body = await response.json();
    code = typeof body.code === 'string' ? body.code : undefined;
    message = body.detail || body.title || message;
  } catch {}
  return new ApiError(response.status, code, message);
}

async function refresh(): Promise<Session | null> {
  const session = read();
  if (!session) return null;
  refreshing ??= (async () => {
    try {
      const response = await fetch(`${API_URL}/v1/auth/refresh`, {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({refreshToken: session.refreshToken}),
      });
      if (!response.ok) {
        setSession(null);
        return null;
      }
      const next = (await response.json()) as Session;
      setSession(next);
      return next;
    } catch {
      return null;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

async function validSession(): Promise<Session | null> {
  const session = read();
  if (!session) return null;
  const expiresIn = new Date(session.accessTokenExpiresAtUtc).getTime() - Date.now();
  return expiresIn > 30_000 ? session : refresh();
}

export type Query = Record<string, string | number | boolean | undefined | null>;

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  query?: Query;
  body?: unknown;
  anonymous?: boolean;
  idempotencyKey?: string;
}

function url(path: string, query?: Query) {
  const target = new URL(`${API_URL}/v1${path}`);
  Object.entries(query ?? {}).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') target.searchParams.set(key, String(value));
  });
  return target.toString();
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const send = async (session: Session | null) => {
    const headers: Record<string, string> = {Accept: 'application/json'};
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    if (session && !options.anonymous) headers.Authorization = `Bearer ${session.accessToken}`;
    if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
    return fetch(url(path, options.query), {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
  };

  let response = await send(options.anonymous ? null : await validSession());
  if (response.status === 401 && !options.anonymous && read()) {
    const renewed = await refresh();
    if (renewed) response = await send(renewed);
  }

  if (!response.ok) throw await problem(response);
  if (response.status === 204 || response.headers.get('Content-Length') === '0') return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** Un contenu binaire de l'API (image…) ; null s'il n'existe pas (404). */
export async function apiBlob(path: string, signal?: AbortSignal): Promise<Blob | null> {
  const send = async (session: Session | null) =>
    fetch(url(path), {headers: session ? {Authorization: `Bearer ${session.accessToken}`} : {}, signal});
  let response = await send(await validSession());
  if (response.status === 401 && read()) {
    const renewed = await refresh();
    if (renewed) response = await send(renewed);
  }
  if (response.status === 404) return null;
  if (!response.ok) throw await problem(response);
  return response.blob();
}

/** Envoie un corps binaire brut (PUT). */
export async function apiPutBlob(path: string, blob: Blob): Promise<void> {
  const send = async (session: Session | null) =>
    fetch(url(path), {method: 'PUT', headers: {'Content-Type': blob.type || 'application/octet-stream', ...(session ? {Authorization: `Bearer ${session.accessToken}`} : {})}, body: blob});
  let response = await send(await validSession());
  if (response.status === 401 && read()) {
    const renewed = await refresh();
    if (renewed) response = await send(renewed);
  }
  if (!response.ok) throw await problem(response);
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number | null;
  hasMore: boolean;
}

/** Une liste chargée page par page ; `truncated` : la limite est atteinte et l'API a encore des éléments. */
export type Loaded<T> = T[] & {truncated: boolean};

export async function all<T>(path: string, query: Query = {}, limit = 1000): Promise<Loaded<T>> {
  const items: T[] = [];
  let more = false;
  for (let page = 1; items.length < limit; page++) {
    const result = await api<Page<T>>(path, {query: {...query, page, pageSize: 100}});
    items.push(...result.items);
    more = result.hasMore && result.items.length > 0;
    if (!more) break;
  }
  return Object.assign(items.slice(0, limit), {truncated: more || items.length > limit});
}

const messages: Record<string, string> = {
  'identity.two_factor.unavailable': 'La validation en deux étapes n’est pas configurée sur le serveur (clé Identity:TwoFactor:SecretKey manquante).',
  'identity.two_factor.code_invalid': 'Code incorrect : vérifiez l’heure de votre téléphone et saisissez le code affiché.',
  'identity.two_factor.code_reused': 'Ce code a déjà servi : attendez le suivant.',
  'identity.two_factor.already_enabled': 'La validation en deux étapes est déjà activée.',
  'identity.two_factor.setup_missing': 'Recommencez l’activation depuis le début.',
  'identity.two_factor.recovery_code_invalid': 'Code de secours incorrect.',
  'identity.auth.invalid_credentials': 'Mot de passe incorrect.',
  'billing.plan.currency_mismatch': 'Ce plan n’est pas vendu dans la devise du restaurant.',
  'billing.online_payment.provider_not_ready': 'Ce prestataire ne peut pas encore encaisser dans cette devise : activez-le, autorisez la devise et enregistrez son compte plateforme.',
  'payments.configuration.platform_mode_not_allowed': 'Seul un prestataire en ligne peut recevoir un compte plateforme.',
  'payments.configuration.credential_fields_invalid': 'Tous les champs du compte sont obligatoires, et seulement ceux-là.',
  'payments.provider.integration_missing': 'Ce prestataire n’a pas d’intégration dans le code de la plateforme.',
  'payments.configuration.credentials_unreadable': 'Les identifiants enregistrés ne sont plus lisibles : ressaisissez-les.',
  'billing.subscription.complimentary_end_invalid': 'La fin de l’offre doit être dans le futur.',
  'billing.subscription.not_complimentary': 'Cet abonnement n’est pas offert.',
  'billing.subscription.nothing_to_pay': 'La période en cours est déjà payée : enregistrez le paiement une fois qu’elle est terminée.',
  'billing.subscription.already_subscribed': 'Ce restaurant a déjà un abonnement en cours.',
  'orders.plan.monthly_limit_reached': 'Plafond mensuel de commandes du plan atteint.',
  'locations.plan.limit_reached': 'Nombre maximal d’établissements du plan atteint.',
  'staff.plan.limit_reached': 'Nombre maximal de membres du personnel du plan atteint.',
  'identity.user.not_invitable': 'Ce compte a déjà un mot de passe : il n’a pas besoin d’invitation.',
  'notifications.provider.integration_missing': 'Aucune intégration n’existe dans le code pour ce prestataire : choisissez-en une dans la liste.',
  'notifications.provider.code_not_unique': 'Ce prestataire est déjà au catalogue.',
  'notifications.configuration.credentials_incomplete': 'Tous les champs du compte sont obligatoires.',
  'notifications.provider.already_enabled': 'Ce prestataire est déjà activé.',
  'notifications.provider.already_disabled': 'Ce prestataire est déjà désactivé.',
};

export function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && messages[error.code]) return messages[error.code];
    if (error.status === 401) return 'Session expirée : reconnectez-vous.';
    if (error.status === 403) return 'Accès refusé : ce compte n’est pas administrateur de la plateforme.';
    return error.message;
  }
  if (error instanceof TypeError) return `API injoignable (${API_URL}). Vérifiez qu’elle est démarrée.`;
  return error instanceof Error ? error.message : String(error);
}
