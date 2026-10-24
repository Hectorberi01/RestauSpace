import {moreMessages} from './error-messages';

export const API_URL = (process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/+$/, '');

export interface Session {
  userId: string;
  sessionId: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  /** Absent pour une session de caisse : elle ne se renouvelle pas. */
  refreshToken?: string;
  refreshTokenExpiresAtUtc?: string;
  tenantId?: string;
  /** Session ouverte par PIN sur une caisse partagée : qui tient la caisse. */
  operator?: {staffMemberId: string; name: string; tillId: string; locationId: string};
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

const STORAGE_KEY = 'restaurantos.app.session';
const listeners = new Set<() => void>();
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
  listeners.forEach(listener => listener());
}

export function onSessionChange(listener: () => void) {
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

/** Un 401 ne justifie un renouvellement que s'il vient du jeton (pas d'un mauvais mot de passe ou code 2FA). */
async function expiredToken(response: Response): Promise<boolean> {
  if (response.status !== 401) return false;
  try {
    const body = await response.clone().json();
    const code = typeof body?.code === 'string' ? body.code : undefined;
    return !code || code === 'identity.auth.invalid_access_token';
  } catch {
    return true;
  }
}

async function refresh(): Promise<Session | null> {
  const session = read();
  if (!session) return null;
  // Session de caisse expirée ou révoquée : pas de renouvellement, la caisse se reverrouille.
  if (!session.refreshToken) {
    setSession(null);
    return null;
  }
  refreshing ??= (async () => {
    try {
      const response = await fetch(`${API_URL}/v1/auth/refresh`, {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({refreshToken: session.refreshToken, tenantId: session.tenantId ?? null}),
      });
      if (!response.ok) {
        setSession(null);
        return null;
      }
      const next = {...((await response.json()) as Session), tenantId: session.tenantId, operator: session.operator};
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
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  query?: Query;
  body?: unknown;
  anonymous?: boolean;
  signal?: AbortSignal;
  /** Clé d'idempotence imposée (rejeu d'une vente enregistrée hors ligne) ; sinon une clé neuve par POST. */
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
  const idempotencyKey = options.method === 'POST' && !options.anonymous ? options.idempotencyKey ?? crypto.randomUUID() : undefined;
  const send = (session: Session | null) => {
    const headers: Record<string, string> = {Accept: 'application/json'};
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    if (session && !options.anonymous) headers.Authorization = `Bearer ${session.accessToken}`;
    if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;
    return fetch(url(path, options.query), {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  };

  let response = await send(options.anonymous ? null : await validSession());
  if (!options.anonymous && read() && (await expiredToken(response))) {
    const renewed = await refresh();
    if (renewed) response = await send(renewed);
  }

  if (!response.ok) throw await problem(response);
  if (response.status === 204 || response.headers.get('Content-Length') === '0') return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** Requête brute avec la session courante (fichiers, images) ; renouvelle le token une fois sur 401. */
async function authorized(path: string, init: {method?: 'GET' | 'PUT'; body?: Blob; signal?: AbortSignal} = {}): Promise<Response> {
  const send = (session: Session | null) =>
    fetch(url(path), {
      method: init.method ?? 'GET',
      headers: {
        ...(session ? {Authorization: `Bearer ${session.accessToken}`} : {}),
        ...(init.body ? {'Content-Type': init.body.type || 'application/octet-stream'} : {}),
      },
      body: init.body,
      signal: init.signal,
    });
  let response = await send(await validSession());
  if (read() && (await expiredToken(response))) {
    const renewed = await refresh();
    if (renewed) response = await send(renewed);
  }
  return response;
}

/** Fichier renvoyé par l'API (export CSV…), avec la session courante ; le nom vient de Content-Disposition. */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const response = await authorized(path);
  if (!response.ok) throw await problem(response);
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1];
  const link = document.createElement('a');
  link.href = URL.createObjectURL(await response.blob());
  link.download = name ? decodeURIComponent(name) : fallbackName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
}

/** Image ou fichier protégé ; null si l'API répond 404. */
export async function fetchBlob(path: string, signal?: AbortSignal): Promise<Blob | null> {
  const response = await authorized(path, {signal});
  if (response.status === 404) return null;
  if (!response.ok) throw await problem(response);
  return response.blob();
}

/** Envoie un fichier en corps brut (PUT). */
export async function putBlob(path: string, body: Blob): Promise<void> {
  const response = await authorized(path, {method: 'PUT', body});
  if (!response.ok) throw await problem(response);
}

export const post = <T = void>(path: string, body?: unknown) => api<T>(path, {method: 'POST', body});
export const put = <T = void>(path: string, body?: unknown) => api<T>(path, {method: 'PUT', body});
export const del = <T = void>(path: string) => api<T>(path, {method: 'DELETE'});

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number | null;
  hasMore: boolean;
}

export async function all<T>(path: string, query: Query = {}, limit = 1000): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; items.length < limit; page++) {
    const result = await api<Page<T>>(path, {query: {...query, page, pageSize: 100}});
    items.push(...result.items);
    if (!result.hasMore || result.items.length === 0) break;
  }
  return items.slice(0, limit);
}

/**
 * Première étape : le mot de passe. Renvoie le jeton de seconde étape si le compte a la validation en deux étapes
 * (rien n'est ouvert tant que le code n'est pas saisi), sinon null : la session est ouverte.
 */
export async function login(email: string, password: string): Promise<string | null> {
  const result = await api<Session | {twoFactorRequired: true; challenge: string}>('/auth/login', {method: 'POST', body: {email, password}, anonymous: true});
  if ('twoFactorRequired' in result) return result.challenge;
  setSession(result);
  return null;
}

/** Seconde étape : le code de l'application d'authentification, ou un code de secours. */
export async function loginWithCode(challenge: string, code: string): Promise<void> {
  const session = await api<Session>('/auth/login/two-factor', {method: 'POST', body: {challenge, code}, anonymous: true});
  setSession(session);
}

export async function selectTenant(tenantId: string): Promise<void> {
  const session = read();
  if (!session) throw new ApiError(401, undefined, 'Session expirée.');
  const token = await api<{tenantId: string; accessToken: string; accessTokenExpiresAtUtc: string}>('/me/tenant', {
    method: 'POST',
    body: {tenantId},
  });
  setSession({...session, tenantId: token.tenantId, accessToken: token.accessToken, accessTokenExpiresAtUtc: token.accessTokenExpiresAtUtc});
}

export async function leaveTenant(): Promise<void> {
  const session = read();
  if (!session?.tenantId) return;
  const next = {...session, tenantId: undefined, accessTokenExpiresAtUtc: new Date(0).toISOString()};
  setSession(next);
  await refresh();
}

export async function logout(): Promise<void> {
  const session = read();
  setSession(null);
  if (!session) return;
  try {
    if (session.refreshToken) {
      await api<void>('/auth/logout', {method: 'POST', body: {refreshToken: session.refreshToken}, anonymous: true});
    } else {
      // Session de caisse : elle se ferme avec son propre access token.
      await fetch(url(`/me/sessions/${session.sessionId}`), {method: 'DELETE', headers: {Authorization: `Bearer ${session.accessToken}`}});
    }
  } catch {}
}

const messages: Record<string, string> = {
  ...moreMessages,
  'identity.user.email_not_verified': 'Confirmez d’abord votre adresse : ouvrez le lien reçu par email.',
  'signup.terms_not_accepted': 'Acceptez les conditions d’utilisation pour créer votre compte.',
  'signup.country_unsupported': 'L’inscription en ligne n’est pas encore ouverte dans ce pays : contactez-nous.',
  'signup.plan_unavailable': 'Ce plan n’est plus proposé dans ce pays : choisissez-en un autre.',
  'staff.pin.not_set': 'Ce membre n’a pas encore choisi de PIN de caisse (Mon profil).',
  'staff.pin.locked': 'Trop d’essais manqués : ce PIN est bloqué pendant 15 minutes.',
  'staff.pin.invalid_format': 'Le PIN compte 4 à 6 chiffres.',
  'staff.pin.too_simple': 'PIN trop facile à deviner (chiffres répétés ou qui se suivent).',
  'staff.pin.wrong': 'PIN incorrect.',
  'staff.till.invalid_key': 'Cet appareil n’est plus une caisse enregistrée : demandez au gérant de l’enregistrer à nouveau.',
  'staff.till.member_not_here': 'Ce membre ne travaille pas dans l’établissement de cette caisse.',
  'staff.till.session_refused': 'Le compte de ce membre ne peut pas se connecter (mot de passe non défini, compte bloqué).',
  'staff.till.not_found': 'Cette caisse n’existe plus.',
  'staff.till.name_invalid': 'Le nom de la caisse compte 1 à 60 caractères.',
  'staff.till.auto_lock_invalid': 'Le verrouillage automatique est compris entre 1 et 60 minutes.',
  'staff.till.revoked': 'Cette caisse a été retirée.',
  'staff.time_entry.already_open': 'Ce membre a déjà pointé son arrivée.',
  'staff.time_entries.range_invalid': 'Période invalide (92 jours au plus).',
  'staff.schedule.week_start_not_monday': 'Une semaine de planning commence un lundi.',
  'staff.schedule.outside_week': 'Ce jour n’est pas dans la semaine affichée.',
  'staff.schedule.shift_too_long': 'Un créneau dure 16 heures au plus.',
  'staff.schedule.shift_too_short': 'Un créneau dure au moins 15 minutes.',
  'staff.schedule.break_invalid': 'La pause fait de 0 à 180 minutes et reste plus courte que le créneau.',
  'staff.schedule.position_too_long': 'Un poste compte 40 caractères au plus.',
  'staff.schedule.note_too_long': 'La note compte 200 caractères au plus.',
  'staff.schedule.overlap': 'Ce membre travaille déjà à ce moment-là.',
  'staff.schedule.shift_not_found': 'Ce créneau n’est plus dans le planning.',
  'staff.schedule.too_many_shifts': 'Une semaine compte 1000 créneaux au plus.',
  'staff.schedule.week_not_empty': 'La semaine contient déjà des créneaux : la copie se fait dans une semaine vide.',
  'staff.schedule.nothing_to_copy': 'La semaine précédente n’a aucun créneau à copier.',
  'staff.schedule.nothing_to_publish': 'Rien de nouveau à publier.',
  'staff.schedule.shift_not_published': 'Ce créneau n’est pas dans le planning publié.',
  'staff.schedule.same_member': 'Choisissez un autre collègue.',
  'staff.schedule.not_your_shift': 'Ce créneau est celui d’un autre membre.',
  'staff.schedule.not_for_you': 'Cette demande s’adresse à un autre collègue.',
  'staff.schedule.shift_started': 'Ce créneau a déjà commencé.',
  'staff.schedule.swap_closed': 'Ce remplacement est déjà réglé.',
  'staff.schedule.weekly_limit_invalid': 'Le plafond hebdomadaire est compris entre 1 et 80 heures.',
  'staff.schedule.too_many_positions': '20 postes au plus.',
  'staff.schedule.location_not_found': 'Cet établissement n’existe pas.',
  'staff.schedule.week_not_found': 'Rien n’est encore planifié cette semaine.',
  'staff.time_off.kind_invalid': 'Type d’absence inconnu.',
  'staff.time_off.period_invalid': 'Une absence dure de 1 à 92 jours, le dernier jour après le premier.',
  'staff.time_off.in_past': 'Une absence ne peut pas commencer dans le passé.',
  'staff.time_off.closed': 'Cette absence est déjà réglée.',
  'staff.time_off.not_found': 'Cette absence n’existe plus.',
  'staff.shift_swap.not_found': 'Ce remplacement n’existe plus.',
  'staff.shift_swap.already_open': 'Un remplacement est déjà en cours pour ce créneau.',
  'staff.shift_swap.colleague_not_here': 'Ce collègue ne travaille pas dans cet établissement.',
  'common.tenant.required': 'Choisissez d’abord un restaurant.',
  'locations.location_name.length_invalid': 'Le nom doit contenir entre 2 et 100 caractères.',
  'locations.capacity.negative': 'La capacité ne peut pas être négative.',
  'locations.address.street_empty': 'L’adresse est obligatoire.',
  'locations.address.city_empty': 'La ville est obligatoire.',
  'locations.address.field_too_long': 'Adresse, ville ou code postal trop long.',
  'locations.address.country_code_unknown': 'Pays inconnu.',
  'locations.business_hours.range_start_not_before_end': 'Une plage doit commencer avant de finir (ou finir le lendemain).',
  'locations.business_hours.overnight_range_too_long': 'Un service de nuit doit finir le lendemain avant son heure de début.',
  'locations.business_hours.ranges_overlap': 'Les plages d’ouverture se chevauchent.',
  'locations.exceptional_schedule.date_already_defined': 'Ce jour a déjà des horaires exceptionnels.',
  'locations.exceptional_schedule.opening_without_range': 'Indiquez au moins une plage d’ouverture.',
  'locations.exceptional_schedule.not_found': 'Ce jour exceptionnel n’existe plus.',
  'locations.location.reason_too_long': 'Motif trop long (200 caractères au plus).',
  'locations.location.archived': 'Établissement archivé : plus aucune modification n’est possible.',
  'locations.location.only_draft_can_be_activated': 'Seul un établissement en brouillon peut être ouvert.',
  'locations.location.only_active_can_be_temporarily_closed': 'Seul un établissement ouvert peut être fermé temporairement.',
  'locations.location.only_temporarily_closed_can_be_reopened': 'Seul un établissement fermé temporairement peut être rouvert.',
  'locations.tenant.not_writable': 'Restaurant suspendu : les établissements ne peuvent plus être modifiés.',
  'locations.time.invalid': 'Heure invalide (format HH:MM).',
  'common.email.invalid': 'Adresse email invalide.',
  'common.email.empty': 'L’email est obligatoire.',
  'customers.customer.name_required': 'Le nom du client est obligatoire.',
  'customers.customer.name_too_long': 'Nom trop long (150 caractères au plus).',
  'customers.customer.contact_required': 'Coordonnées insuffisantes pour la règle de ce restaurant (email et/ou téléphone).',
  'customers.customer.anonymized': 'Client anonymisé : la fiche ne peut plus être modifiée.',
  'customers.customer.tag_already_present': 'Ce client a déjà ce tag.',
  'customers.customer.tag_not_found': 'Ce tag n’est plus sur la fiche.',
  'customers.customer.too_many_tags': '20 tags au plus par client.',
  'customers.customer.not_found': 'Ce client n’existe pas.',
  'customers.tag.invalid': 'Un tag fait 1 à 30 caractères : lettres, chiffres, espaces, tirets.',
  'customers.preference.language_invalid': 'Langue invalide.',
  'customers.preference.language_not_supported': 'Langue non prise en charge.',
  'customers.consent.customer_anonymized': 'Client anonymisé : aucun accord ne peut être enregistré.',
  'customers.consent.already_granted': 'L’accord est déjà donné sur ce canal.',
  'customers.consent.already_withdrawn': 'L’accord est déjà retiré sur ce canal.',
  'customers.consent.not_found': 'Aucun accord n’a jamais été donné sur ce canal.',
  'customers.consent.not_unique': 'Un accord existe déjà pour ce canal : rechargez la fiche.',
  'customers.concurrent_update': 'La fiche a été modifiée entre-temps : rechargez et réessayez.',
  'customers.tenant.not_writable': 'Restaurant suspendu : les fiches clients ne peuvent plus être modifiées.',
  'customers.search.page_invalid': 'Pagination invalide.',
  'reservations.paging.invalid': 'Pagination invalide.',
  'identity.user.sole_owner': 'Vous êtes le seul propriétaire d’un restaurant : nommez un autre propriétaire dans Personnel avant de supprimer votre compte.',
  'audit.importance_invalid': 'Priorité inconnue.',
  'audit.summary_period_invalid': 'Choisissez une période de 366 jours au plus.',
  'audit.period_invalid': 'La période est invalide : le début doit précéder la fin.',
  'floor.table.capacity_not_positive': 'Une table a au moins une place.',
  'kitchen.ticket.closed': 'Ce ticket est déjà terminé ou annulé.',
  'kitchen.ticket.not_found': 'Ce ticket n’existe plus.',
  'kitchen.ticket.cancellation_reason_too_long': 'Motif trop long (500 caractères au plus).',
  'notifications.configuration.assignment_not_found': 'Cet établissement n’était pas rattaché à ce fournisseur.',
  'notifications.configuration.not_assigned_to_location': 'Ce canal n’est pas proposé dans cet établissement avec ce fournisseur.',
  'notifications.configuration.too_many_assignments': 'Trop d’établissements pour ce fournisseur.',
  'notifications.preference.purpose_unknown': 'Type de message inconnu.',
  'reporting.period.invalid': 'Période invalide : la fin doit suivre le début, 366 jours au plus.',
  'reporting.granularity.invalid': 'Découpage inconnu.',
  'notifications.template_key.invalid': 'Type de message inconnu.',
  'notifications.language.invalid': 'Langue invalide.',
  'notifications.template.channel_invalid': 'Canal non pris en charge.',
  'notifications.template.key_already_used': 'Un modèle existe déjà pour ce message, ce canal et cette langue : créez-en une nouvelle version.',
  'notifications.template.version_already_exists': 'Une nouvelle version existe déjà : modifiez ce brouillon.',
  'notifications.template.subject_required': 'Un e-mail a besoin d’un objet.',
  'notifications.template.subject_too_long': 'Objet : 200 caractères au plus.',
  'notifications.template.subject_not_supported': 'Les SMS et WhatsApp n’ont pas d’objet.',
  'notifications.template.body_empty': 'Le message est vide.',
  'notifications.template.body_too_long': 'Message : 20 000 caractères au plus.',
  'notifications.template.published_immutable': 'Une version publiée ne se modifie plus : créez une nouvelle version.',
  'notifications.template.archived': 'Cette version est archivée.',
  'notifications.template.type_unknown': 'Type de message inconnu.',
  'notifications.template.variable_syntax_invalid': 'Une variable s’écrit {{nom.variable}} : vérifiez les accolades.',
  'notifications.template.too_many_variables': '100 variables au plus dans un message.',
  'notifications.template.variable_not_allowed': 'Ce message utilise une information qui n’existe pas pour ce type.',
  'notifications.template.unsubscribe_link_required': 'Un e-mail marketing doit contenir le lien de désinscription {{unsubscribe.url}}.',
  'notifications.template.already_enabled': 'Ce modèle est déjà actif.',
  'notifications.template.already_disabled': 'Ce modèle est déjà désactivé.',
  'notifications.template.draft_cannot_be_versioned': 'Un brouillon se modifie directement.',
  'notifications.customer.not_found': 'Client introuvable ou sans coordonnée pour ce canal.',
  'notifications.notification.not_found': 'Ce message n’existe plus.',
  'notifications.notification.already_cancelled': 'Ce message est déjà annulé.',
  'notifications.notification.already_sent': 'Ce message est déjà parti : il ne peut plus être annulé.',
  'notifications.notification.already_delivered': 'Ce message a déjà été remis.',
  'notifications.notification.schedule_in_past': 'Choisissez une date future.',
  'notifications.notification.customer_required': 'Un message marketing s’adresse à un client enregistré.',
  'notifications.notification.consent_not_granted': 'Le client n’a pas accepté le marketing sur ce canal.',
  'notifications.send.duplicate': 'Ce message a déjà été envoyé.',
  'notifications.send.recipient_required': 'Choisissez un client.',
  'notifications.recipient.channel_invalid': 'Canal non pris en charge.',
  'reservations.policy.min_lead_time_negative': 'Le délai minimum ne peut pas être négatif.',
  'reservations.policy.max_advance_not_positive': 'La réservation à l’avance doit être d’au moins un jour.',
  'reservations.policy.max_advance_not_after_lead_time': 'La réservation à l’avance doit dépasser le délai minimum.',
  'reservations.policy.max_advance_too_long': 'Réservation à l’avance limitée à deux ans.',
  'reservations.policy.default_duration_not_positive': 'La durée par défaut doit être supérieure à zéro.',
  'reservations.policy.cancellation_cutoff_negative': 'La limite d’annulation ne peut pas être négative.',
  'reservations.policy.cancellation_cutoff_too_long': 'Limite d’annulation de deux ans au plus.',
  'reservations.policy.duration_bounds_invalid': 'Durées invalides : minimum supérieur à zéro, au plus égal au maximum, lui-même de 24 h au plus.',
  'reservations.policy.default_duration_out_of_bounds': 'La durée par défaut doit être comprise entre le minimum et le maximum.',
  'reservations.policy.max_party_size_invalid': 'Couverts maximum : entre 1 et 10 000.',
  'catalog.menu.position_out_of_range': 'Position impossible : rechargez la carte.',
  'catalog.modifier_group.modifier_name_duplicate': 'Une option de ce groupe porte déjà ce nom.',
  'catalog.modifier_group.modifier_price_negative': 'Un supplément ne peut pas être négatif.',
  'catalog.modifier_group.modifier_currency_mismatch': 'Le supplément doit être dans la devise du groupe.',
  'catalog.product.variant_price_negative': 'Le prix de la variante (prix de base + écart) ne peut pas être négatif.',
  'catalog.product.variant_currency_mismatch': 'L’écart de prix doit être dans la devise du produit.',
  'catalog.name.empty': 'Le nom est obligatoire.',
  'catalog.name.too_long': 'Le nom est trop long (120 caractères au plus).',
  'catalog.tax_category.name_empty': 'Le nom de la taxe est obligatoire.',
  'catalog.tax_category.name_too_long': 'Le nom de la taxe est trop long (100 caractères au plus).',
  'catalog.tax_category.rate_out_of_range': 'Le taux doit être compris entre 0 et 100 %.',
  'catalog.tax_category.rate_too_precise': 'Taux trop précis (4 décimales au plus en pourcentage).',
  'kitchen.station.in_use': 'Ce poste reçoit encore des plats : affectez-les d’abord à un autre poste.',
  'kitchen.station.name_empty': 'Donnez un nom au poste.',
  'kitchen.station.name_not_unique': 'Un autre poste de cet établissement porte déjà ce nom.',
  'kitchen.station.name_too_long': 'Le nom du poste est trop long (60 caractères au plus).',
  'kitchen.station.not_found': 'Ce poste n’existe plus.',
  'kitchen.station.already_active': 'Ce poste est déjà actif.',
  'kitchen.station.already_inactive': 'Ce poste est déjà désactivé.',
  'kitchen.station_routing.not_found': 'Choisissez d’abord un poste par défaut.',
  'kitchen.station_routing.location_already_has_routing': 'Cet établissement a déjà un poste par défaut.',
  'kitchen.station_routing.route_limit_reached': 'Trop de plats affectés (2000 au plus).',
  'kitchen.station_routing.route_not_found': 'Ce plat part déjà au poste par défaut.',
  'kitchen.station_routing.station_inactive': 'Ce poste est désactivé : réactivez-le avant de lui envoyer des plats.',
  'kitchen.station_routing.station_other_location': 'Ce poste appartient à un autre établissement.',
  'kitchen.product.not_found': 'Ce produit n’existe plus.',
  'kitchen.location.not_found': 'Établissement introuvable.',
  'kitchen.tenant.not_writable': 'Compte suspendu ou fermé : la cuisine est en lecture seule.',
  'orders.order.delivery_fee_requires_delivery': 'Les frais de livraison ne s’appliquent qu’à une commande en livraison.',
  'orders.order.delivery_fee_negative': 'Les frais de livraison ne peuvent pas être négatifs.',
  'orders.order.delivery_fee_reference_invalid': 'Référence de livraison invalide.',
  'reporting.report_definition.not_found': 'Ce rapport n’existe plus.',
  'reporting.report_definition.name_empty': 'Donnez un nom au rapport.',
  'reporting.report_definition.name_too_long': 'Le nom du rapport est trop long (100 caractères au plus).',
  'reporting.report_definition.metrics_empty': 'Choisissez au moins une mesure.',
  'reporting.report_definition.too_many_metrics': 'Trop de mesures (30 au plus).',
  'reporting.report_definition.metric_unknown': 'Mesure inconnue.',
  'reporting.report_definition.catalog_incompatible': 'Ces mesures ne peuvent pas être combinées dans un même rapport.',
  'reporting.report_definition.too_many_filters': 'Trop de filtres.',
  'reporting.report_definition.filter_duplicated': 'Ce filtre est déjà présent.',
  'reporting.report_definition.filter_field_not_allowed': 'Ce filtre n’est pas autorisé.',
  'reporting.report_definition.filter_operator_invalid': 'Filtre invalide.',
  'reporting.report_definition.filter_value_invalid': 'Établissement invalide.',
  'reporting.report_definition.not_scheduled': 'Ce rapport n’est pas programmé.',
  'reporting.report_definition.schedule_frequency_invalid': 'Fréquence invalide.',
  'reporting.report_definition.schedule_anchor_not_allowed': 'Le jour choisi ne correspond pas à la fréquence.',
  'reporting.report_definition.schedule_day_of_week_required': 'Choisissez le jour de la semaine.',
  'reporting.report_definition.schedule_day_of_week_invalid': 'Jour de la semaine invalide.',
  'reporting.report_definition.schedule_day_of_month_required': 'Choisissez le jour du mois.',
  'reporting.report_definition.schedule_day_of_month_invalid': 'Le jour du mois doit être entre 1 et 28.',
  'reporting.report_definition.schedule_recipients_empty': 'Ajoutez au moins un destinataire.',
  'reporting.report_definition.schedule_too_many_recipients': '20 destinataires au plus.',
  'reporting.schedule.time_of_day_invalid': 'Heure invalide (format HH:mm).',
  'integrations.concurrent_update': 'Modifié entre-temps : rechargez et réessayez.',
  'integrations.tenant.not_writable': 'Restaurant suspendu : les intégrations ne peuvent plus être modifiées.',
  'integrations.webhook_endpoint.not_found': 'Ce webhook n’existe pas.',
  'integrations.webhook_delivery.not_found': 'Cette livraison n’existe pas.',
  'integrations.api_credential.not_found': 'Cette clé n’existe pas.',
  'integrations.event_type.not_published': 'Cet événement n’est pas proposé.',
  'integrations.webhook_delivery.not_dead_lettered': 'Seule une livraison abandonnée peut être renvoyée.',
  'integrations.webhook_url.invalid': 'Adresse invalide.',
  'integrations.webhook_url.https_required': 'L’adresse doit commencer par https://.',
  'integrations.webhook_url.credentials_forbidden': 'L’adresse ne doit pas contenir d’identifiants.',
  'integrations.webhook_url.host_not_public': 'L’adresse doit viser un serveur public (pas localhost ni réseau privé).',
  'integrations.webhook_url.port_invalid': 'Port invalide.',
  'integrations.webhook_endpoint.events_required': 'Choisissez au moins un événement.',
  'integrations.webhook_endpoint.too_many_events': '100 événements au plus.',
  'integrations.webhook_endpoint.already_subscribed': 'Cet événement est déjà envoyé.',
  'integrations.webhook_endpoint.not_subscribed': 'Cet événement n’est plus envoyé.',
  'integrations.webhook_endpoint.already_disabled': 'Ce webhook est déjà désactivé.',
  'integrations.webhook_endpoint.already_active': 'Ce webhook est déjà actif.',
  'integrations.api_credential.name_empty': 'Le nom de la clé est obligatoire.',
  'integrations.api_credential.name_too_long': 'Nom trop long (100 caractères au plus).',
  'integrations.api_credential.scopes_required': 'Choisissez au moins un droit.',
  'integrations.api_credential.scope_unknown': 'Droit inconnu.',
  'integrations.api_credential.too_many_scopes': '50 droits au plus.',
  'integrations.api_credential.expiry_not_in_future': 'La date d’expiration doit être dans le futur.',
  'integrations.api_credential.revoked': 'Cette clé est révoquée.',
  'integrations.api_credential.expired': 'Cette clé a expiré.',
  'integrations.api_credential.option_required': 'Les clés d’API sont une option de votre abonnement : ajoutez l’option API depuis la page Abonnement.',
  'integrations.api_credential.scope_not_held': 'Vous ne pouvez donner à une clé que des droits que vous avez vous-même.',
  'audit.page_invalid': 'Pagination invalide.',
  'notifications.preference.source_invalid': 'Origine invalide.',
  'notifications.preference.marketing_for_customers_only': 'Les messages marketing ne concernent que les clients.',
  'inventory.concurrent_update': 'Modifié entre-temps : rechargez et réessayez.',
  'inventory.ingredient.not_found': 'Cet ingrédient n’existe pas.',
  'inventory.item.not_found': 'Cet article de stock n’existe pas.',
  'inventory.recipe.not_found': 'Cette recette n’existe pas.',
  'inventory.product.not_found': 'Ce produit n’existe pas.',
  'inventory.quantity.required': 'Indiquez une quantité.',
  'inventory.tenant.not_writable': 'Restaurant suspendu : le stock ne peut plus être modifié.',
  'inventory.unit.unknown': 'Unité inconnue.',
  'inventory.unit.incompatible': 'Unité incompatible avec celle de l’ingrédient.',
  'inventory.quantity.negative': 'Une quantité ne peut pas être négative.',
  'inventory.quantity.too_many_decimals': 'Six décimales au plus.',
  'inventory.quantity.too_large': 'Quantité trop grande.',
  'inventory.ingredient.name_required': 'Le nom de l’ingrédient est obligatoire.',
  'inventory.ingredient.name_too_long': 'Nom trop long.',
  'inventory.ingredient.name_not_unique': 'Un ingrédient porte déjà ce nom.',
  'inventory.ingredient.inactive': 'Cet ingrédient est désactivé.',
  'inventory.ingredient.already_inactive': 'Cet ingrédient est déjà désactivé.',
  'inventory.item.not_unique_for_location': 'Cet ingrédient est déjà suivi dans cet établissement.',
  'inventory.item.stock_is_negative': 'Le stock est négatif : faites un inventaire avant d’interdire le stock négatif.',
  'inventory.item.movement_quantity_not_positive': 'La quantité doit être supérieure à zéro.',
  'inventory.item.insufficient_stock': 'Stock insuffisant (stock négatif interdit pour cet article).',
  'inventory.item.movement_reason_required': 'Indiquez le motif.',
  'inventory.item.movement_reason_too_long': 'Motif trop long.',
  'inventory.item.movement_reference_too_long': 'Référence trop longue.',
  'inventory.transfer.same_item': 'Choisissez un autre établissement.',
  'inventory.transfer.ingredient_mismatch': 'Les deux articles doivent être le même ingrédient.',
  'inventory.recipe.not_unique_for_product': 'Ce produit a déjà une recette.',
  'inventory.recipe.ingredient_duplicate': 'Cet ingrédient est déjà dans la recette.',
  'inventory.recipe.ingredient_not_found': 'Cet ingrédient n’est plus dans la recette.',
  'inventory.recipe.quantity_not_positive': 'La quantité doit être supérieure à zéro.',
  'inventory.recipe.too_many_ingredients': 'Nombre maximal d’ingrédients atteint.',
  'reviews.concurrent_update': 'Modifié entre-temps : rechargez et réessayez.',
  'reviews.feedback.not_found': 'Cet avis n’existe pas.',
  'reviews.survey.not_found': 'Ce questionnaire n’existe pas.',
  'reviews.tenant.not_writable': 'Restaurant suspendu : les avis ne peuvent plus être modifiés.',
  'reviews.feedback.order_already_reviewed': 'Cette commande a déjà un avis.',
  'reviews.rating.out_of_range': 'La note doit être entre 1 et 5.',
  'reviews.feedback.comment_too_long': 'Commentaire trop long.',
  'reviews.feedback.customer_anonymized': 'Client anonymisé : impossible de lui attribuer un avis.',
  'reviews.feedback.not_submitted': 'Seul un avis à traiter peut être marqué comme traité.',
  'reviews.feedback.already_archived': 'Cet avis est archivé.',
  'reviews.survey.title_required': 'Le titre est obligatoire.',
  'reviews.survey.title_too_long': 'Titre trop long.',
  'reviews.survey.not_draft': 'Seul un brouillon peut être modifié ou publié.',
  'reviews.survey.not_published': 'Seul un questionnaire publié peut être clos.',
  'reviews.survey.closed': 'Questionnaire clos.',
  'reviews.survey.no_question': 'Ajoutez au moins une question avant de publier.',
  'reviews.survey.too_many_questions': '50 questions au plus.',
  'reviews.question.text_required': 'Le texte de la question est obligatoire.',
  'reviews.question.text_too_long': 'Question trop longue.',
  'reviews.question.choices_required': 'Au moins deux choix.',
  'reviews.question.too_many_choices': '20 choix au plus.',
  'reviews.question.choice_invalid': 'Un choix fait 1 à 200 caractères.',
  'reviews.question.choice_duplicate': 'Les choix doivent être différents.',
  'reviews.period.invalid': 'Période invalide.',
  'billing.subscription.already_subscribed': 'Vous avez déjà un abonnement en cours.',
  'billing.subscription.complimentary': 'Votre plan est offert par RestauSpace : contactez l’équipe pour le modifier.',
  'billing.add_on.complimentary_unlimited': 'Votre plan est offert sans limite de durée : contactez l’équipe RestauSpace pour ajouter une option.',
  'billing.online_payment.provider_not_ready': 'Ce prestataire ne peut pas encore encaisser dans cette devise.',
  'billing.subscription.plan_archived': 'Ce plan n’est plus proposé.',
  'billing.subscription.plan_change_not_allowed': 'Le plan ne peut changer que pendant l’essai, un abonnement actif ou après la fin de l’essai.',
  'billing.subscription.plan_change_scheduled': 'Un changement de plan est programmé : annulez-le avant de résilier.',
  'billing.subscription.plan_change_not_scheduled': 'Aucun changement de plan n’est programmé.',
  'billing.subscription.cancellation_scheduled': 'Une résiliation est programmée : annulez-la d’abord (par exemple avant de changer de plan).',
  'billing.subscription.free_plan_unavailable': 'Le plan gratuit n’est possible qu’une fois, pendant le premier mois : choisissez un plan payant.',
  'billing.subscription.cancelled': 'Abonnement résilié : souscrivez un nouveau plan.',
  'billing.subscription.cancellation_already_scheduled': 'La résiliation est déjà programmée.',
  'billing.subscription.cancellation_not_scheduled': 'Aucune résiliation n’est programmée.',
  'billing.subscription.not_found': 'Aucun abonnement en cours.',
  'billing.plan.not_found': 'Ce plan n’existe pas.',
  'billing.plan.currency_mismatch': 'Ce plan n’est pas vendu dans la devise du restaurant.',
  'billing.tenant.not_writable': 'Restaurant suspendu : contactez la plateforme.',
  'billing.concurrent_update': 'Modifié entre-temps : rechargez et réessayez.',
  'billing.add_on.subscription_required': 'Choisissez d’abord un plan : une option s’ajoute à un abonnement.',
  'billing.add_on.subscription_not_open': 'Les options se modifient pendant l’essai ou une période payée : régularisez d’abord votre abonnement.',
  'billing.add_on.not_found': 'Cette option n’existe pas ou a déjà été résiliée.',
  'billing.add_on.already_active': 'Cette option est déjà active.',
  'billing.add_on.not_an_add_on': 'Ce plan n’est pas une option.',
  'billing.add_on.archived': 'Cette option n’est plus proposée.',
  'billing.add_on.quantity_invalid': 'Quantité invalide (1 à 1 000).',
  'billing.add_on.cancelled': 'Cette option a été résiliée.',
  'promotions.coupon_code.required': 'Le code est obligatoire.',
  'promotions.coupon_code.length_invalid': 'Un code fait 3 à 32 caractères.',
  'promotions.coupon_code.characters_invalid': 'Un code n’accepte que des lettres, des chiffres et « - ».',
  'promotions.coupon.code_not_unique': 'Ce code existe déjà.',
  'promotions.coupon.max_uses_not_positive': 'Un code doit pouvoir servir au moins une fois.',
  'promotions.coupon.promotion_expired': 'Promotion terminée : plus de code possible.',
  'promotions.coupon.already_applied_to_order': 'Ce code est déjà utilisé sur cette commande.',
  'promotions.coupon.disabled': 'Ce code est désactivé.',
  'promotions.coupon.exhausted': 'Ce code n’a plus d’utilisation disponible.',
  'promotions.coupon.already_disabled': 'Ce code est déjà désactivé.',
  'promotions.coupon.not_found': 'Code promo inconnu.',
  'promotions.promotion.name_required': 'Le nom est obligatoire.',
  'promotions.promotion.name_too_long': 'Nom trop long (120 caractères au plus).',
  'promotions.promotion.rules_incomplete': 'Définissez la remise avant d’activer la promotion.',
  'promotions.promotion.period_ended': 'La période de validité est déjà passée.',
  'promotions.promotion.not_draft': 'Seul un brouillon peut être activé.',
  'promotions.promotion.not_active': 'La promotion n’est pas active.',
  'promotions.promotion.not_paused': 'Seule une promotion en pause peut reprendre.',
  'promotions.promotion.expired': 'Promotion terminée : elle ne peut plus changer.',
  'promotions.promotion.outside_validity_period': 'La promotion n’est pas valable aujourd’hui.',
  'promotions.promotion.usage_limit_reached': 'Limite d’utilisation atteinte.',
  'promotions.promotion.already_applied_to_order': 'Cette promotion est déjà appliquée à la commande.',
  'promotions.promotion.rules_locked_after_usage': 'Promotion déjà utilisée : ses règles ne peuvent plus changer.',
  'promotions.promotion.destructive_change_after_usage': 'Promotion déjà utilisée : seuls un report de la date de fin ou une limite plus haute sont possibles.',
  'promotions.promotion.usage_limit_below_usage': 'La limite ne peut pas être inférieure au nombre d’utilisations déjà faites.',
  'promotions.promotion.not_found': 'Cette promotion n’existe pas.',
  'promotions.usage_limit.not_positive': 'La limite d’utilisation doit être positive.',
  'promotions.benefit.percentage_out_of_range': 'Le pourcentage doit être entre 0 et 100.',
  'promotions.benefit.max_discount_not_positive': 'Le plafond doit être positif.',
  'promotions.benefit.amount_not_positive': 'Le montant de la remise doit être positif.',
  'promotions.benefit.invalid': 'Remise invalide : un pourcentage (plafond facultatif) ou un montant fixe.',
  'promotions.conditions.minimum_order_amount_not_positive': 'Le minimum de commande doit être positif.',
  'promotions.rules.currency_mismatch': 'Tous les montants doivent être dans la même devise.',
  'promotions.order.not_found': 'Cette commande n’existe pas.',
  'promotions.order.not_editable': 'La commande n’accepte plus de remise (validée).',
  'promotions.apply.target_required': 'Choisissez une promotion ou saisissez un code.',
  'promotions.concurrent_update': 'Modifiée entre-temps : rechargez et réessayez.',
  'promotions.tenant.not_writable': 'Restaurant suspendu : les promotions ne peuvent plus être modifiées.',
  'promotions.discount.coupon_required': 'Cette promotion n’est valable qu’avec un code.',
  'promotions.discount.outside_validity_period': 'Promotion hors de sa période de validité.',
  'promotions.discount.location_not_eligible': 'Promotion non valable dans cet établissement.',
  'promotions.discount.order_type_not_eligible': 'Promotion non valable pour ce type de commande.',
  'promotions.discount.minimum_order_amount_not_reached': 'Montant minimum de commande non atteint.',
  'promotions.discount.no_eligible_amount': 'Aucun article de la commande n’est concerné.',
  'promotions.discount.zero_discount': 'La remise serait nulle.',
  'promotions.discount.currency_mismatch': 'Promotion dans une autre devise que la commande.',
  'loyalty.concurrent_update': 'Modifié entre-temps : rechargez et réessayez.',
  'loyalty.tenant.not_writable': 'Restaurant suspendu : la fidélité ne peut plus être modifiée.',
  'loyalty.account.not_found': 'Ce compte fidélité n’existe pas.',
  'loyalty.reward.not_found': 'Cette récompense n’existe pas.',
  'loyalty.customer.not_found': 'Ce client n’existe pas.',
  'loyalty.order.not_found': 'Cette commande n’existe pas.',
  'loyalty.order.other_customer': 'La commande doit être rattachée au client de ce compte.',
  'loyalty.order.not_editable': 'La commande n’accepte plus de remise (validée).',
  'loyalty.reward.currency_mismatch': 'Récompense dans une autre devise que la commande.',
  'loyalty.account.already_exists': 'Ce client a déjà un compte fidélité.',
  'loyalty.account.customer_anonymized': 'Client anonymisé : pas de compte fidélité.',
  'loyalty.account.closed': 'Compte fidélité clos.',
  'loyalty.account.suspended': 'Compte fidélité suspendu.',
  'loyalty.account.already_suspended': 'Le compte est déjà suspendu.',
  'loyalty.account.not_suspended': 'Seul un compte suspendu peut être réactivé.',
  'loyalty.account.insufficient_balance': 'Solde de points insuffisant.',
  'loyalty.account.duplicate_transaction': 'Opération déjà enregistrée.',
  'loyalty.account.adjustment_reason_required': 'Indiquez le motif de l’ajustement.',
  'loyalty.account.adjustment_reason_too_long': 'Motif trop long (250 caractères au plus).',
  'loyalty.account.reason_required': 'Indiquez un motif.',
  'loyalty.account.reason_too_long': 'Motif trop long (250 caractères au plus).',
  'loyalty.account.reward_inactive': 'Cette récompense n’est plus disponible.',
  'loyalty.transaction.points_not_positive': 'Indiquez un nombre de points positif.',
  'loyalty.points.negative': 'Le nombre de points ne peut pas être négatif.',
  'loyalty.reward.name_required': 'Le nom de la récompense est obligatoire.',
  'loyalty.reward.name_too_long': 'Nom trop long.',
  'loyalty.reward.cost_not_positive': 'Une récompense coûte au moins 1 point.',
  'loyalty.reward.discount_value_not_positive': 'La valeur de la remise doit être positive.',
  'loyalty.reward.already_active': 'La récompense est déjà active.',
  'loyalty.reward.already_inactive': 'La récompense est déjà désactivée.',
  'loyalty.program.rate_out_of_range': 'Barème invalide : plus de 0 et au plus 1000 points par unité (4 décimales max).',
  'loyalty.program.expiry_out_of_range': 'Expiration entre 30 et 3650 jours d’inactivité, ou jamais.',
  'loyalty.page_invalid': 'Pagination invalide.',
  'orders.order.finalized': 'Commande terminée ou annulée : elle ne peut plus changer.',
  'orders.order.customer_anonymized': 'Client anonymisé : impossible de le rattacher.',
  'orders.order.platform_invalid': 'Plateforme de livraison invalide.',
  'orders.order.external_reference_invalid': 'Numéro de commande de la plateforme invalide (64 caractères au plus).',
  'orders.order.external_reference_duplicate': 'Cette commande de la plateforme a déjà été saisie.',
  'loyalty.stored_value.not_found': 'Aucune carte avec ce code.',
  'loyalty.stored_value.insufficient_balance': 'Solde de la carte insuffisant pour ce montant.',
  'loyalty.stored_value.expired': 'Cette carte a expiré.',
  'loyalty.stored_value.cancelled': 'Cette carte a été annulée.',
  'loyalty.stored_value.currency_mismatch': 'La carte n’est pas dans la devise de la commande.',
  'loyalty.stored_value.amount_not_positive': 'Le montant doit être positif.',
  'loyalty.stored_value.amount_too_large': 'Montant trop élevé.',
  'loyalty.stored_value.expiry_in_past': 'La date de fin de validité doit être dans le futur.',
  'loyalty.stored_value.reason_required': 'Indiquez un motif.',
  'loyalty.stored_value.already_cancelled': 'Carte déjà annulée.',
  'loyalty.stored_value.credit_above_initial': 'Le remboursement dépasserait le montant initial de la carte.',
  'loyalty.stored_value.too_many_movements': 'Cette carte a trop de mouvements : émettez-en une nouvelle.',
  'payments.gift_card.code_required': 'Saisissez le code de la carte cadeau ou de l’avoir.',
  'payments.gift_card.reference_invalid': 'Ce paiement ne désigne pas une carte cadeau.',
  'payments.gift_card.store_credit_not_allowed': 'Un paiement par carte cadeau se rembourse sur sa carte, pas en avoir.',
  'reporting.accounting_plan.invalid': 'Plan de comptes invalide : chaque code fait 1 à 20 lettres ou chiffres.',
  'orders.customer.not_found': 'Ce client n’existe pas.',
  'reservations.customer.not_found': 'Ce client n’existe pas.',
  'reservations.reservation.customer_anonymized': 'Client anonymisé : impossible de le rattacher.',
  'tenants.tenant_name.empty': 'Le nom du restaurant est obligatoire.',
  'tenants.tenant_name.length_invalid': 'Le nom doit contenir entre 2 et 100 caractères.',
  'tenants.branding.display_name_empty': 'Le nom affiché est obligatoire.',
  'tenants.branding.display_name_too_long': 'Le nom affiché ne peut pas dépasser 100 caractères.',
  'tenants.branding.logo_url_invalid': 'L’adresse du logo doit commencer par http:// ou https://.',
  'tenants.branding.color_invalid': 'Couleur invalide : utilisez un code comme #1A7F5A.',
  'tenants.profile.tagline_too_long': 'L’accroche ne peut pas dépasser 120 caractères.',
  'tenants.profile.description_too_long': 'La présentation ne peut pas dépasser 2 000 caractères.',
  'tenants.profile.invalid_characters': 'Le texte contient des caractères non autorisés.',
  'tenants.profile.image_url_invalid': 'L’adresse de la photo doit commencer par http:// ou https://.',
  'tenants.profile.link_invalid': 'Chaque lien doit être une adresse complète commençant par https://.',
  'tenants.profile.link_duplicated': 'Un seul lien par réseau.',
  'tenants.profile.too_many_links': 'Trop de liens.',
  'tenants.tenant.currency_locked_by_priced_catalog': 'La devise ne peut plus changer : des produits ont déjà un prix au catalogue.',
  'tenants.tenant.closed': 'Ce restaurant est fermé : plus aucune modification n’est possible.',
  'common.currency.unknown': 'Devise inconnue.',
  'notifications.configuration.test_not_supported': 'Seuls les canaux email, SMS et WhatsApp peuvent recevoir un message de test.',
  'notifications.configuration.credentials_missing': 'Saisissez les identifiants de votre compte avant d’activer ce prestataire.',
  'notifications.configuration.credentials_incomplete': 'Tous les champs d’identifiants sont obligatoires.',
  'notifications.configuration.mode_not_allowed': 'La plateforme n’autorise pas ce type de compte pour ce prestataire.',
  'notifications.configuration.provider_unavailable': 'Ce prestataire n’est plus proposé par la plateforme.',
  'notifications.configuration.already_exists': 'Ce prestataire est déjà configuré.',
  'notifications.configuration.already_enabled': 'Ce prestataire est déjà actif.',
  'notifications.configuration.already_disabled': 'Ce prestataire est déjà désactivé.',
  'notifications.provider.integration_missing': 'Ce prestataire n’est pas encore branché.',
  'orders.order.not_settled': 'Commande non soldée : encaissez-la avant de la terminer.',
  'payments.order.not_payable': 'Cette commande ne peut pas être encaissée (brouillon, terminée ou annulée).',
  'payments.order.nothing_due': 'Il ne reste rien à payer sur cette commande (paiements en cours compris).',
  'payments.method.not_accepted': 'Ce moyen de paiement n’est pas accepté pour cette commande.',
  'payments.method.payment_method_required': 'Précisez le moyen de paiement utilisé.',
  'payments.payment.amount_not_positive': 'Le montant doit être supérieur à zéro.',
  'payments.payment.amount_exceeds_amount_due': 'Le montant dépasse le reste à payer.',
  'payments.payment.refund_exceeds_refundable': 'Le remboursement dépasse ce qui reste remboursable.',
  'payments.payment.refund_not_allowed': 'Ce paiement ne peut pas être remboursé.',
  'payments.payment.cancel_not_allowed': 'Un paiement encaissé ne s’annule pas : remboursez-le.',
  'payments.payment.retry_not_allowed': 'Seul un paiement en ligne échoué peut être relancé.',
  'payments.payment.too_many_attempts': 'Trop de tentatives : annulez ce paiement et recommencez.',
  'payments.provider.call_failed': 'Le prestataire de paiement est injoignable ou a refusé la demande : réessayez.',
  'payments.provider_reference.invalid': 'Référence invalide (sans espace, 255 caractères au plus).',
  'payments.refund.reason_invalid': 'Indiquez le motif du remboursement.',
  'payments.tenant.not_writable': 'Restaurant suspendu : les paiements peuvent être consultés, pas encaissés.',
  'payments.payer_phone.invalid': 'Numéro du client invalide : indiquez-le avec l’indicatif du pays (8 à 15 chiffres).',
  'payments.configuration.already_enabled': 'Ce moyen de paiement est déjà accepté.',
  'payments.configuration.already_disabled': 'Ce moyen de paiement n’est déjà plus accepté.',
  'payments.configuration.already_exists': 'Ce moyen de paiement est déjà configuré.',
  'payments.configuration.payments_in_progress': 'Des paiements en ligne ou des remboursements de ce prestataire sont encore en cours : attendez qu’ils se terminent (ou annulez-les) avant de changer de compte d’encaissement.',
  'payments.configuration.test_not_applicable': 'Seul un compte de paiement en ligne peut être testé.',
  'payments.configuration.credentials_missing': 'Saisissez les identifiants du compte marchand avant d’activer ce moyen.',
  'payments.configuration.credential_fields_invalid': 'Tous les champs d’identifiants sont obligatoires.',
  'payments.configuration.credentials_unreadable': 'Les identifiants enregistrés ne sont plus lisibles : saisissez-les à nouveau.',
  'payments.configuration.provider_unavailable': 'Ce moyen de paiement n’est plus proposé par la plateforme.',
  'payments.configuration.last_method': 'Gardez au moins un moyen : désactivez plutôt le prestataire.',
  'payments.configuration.not_assigned_to_location': 'Ce moyen de paiement n’est pas proposé dans cet établissement.',
  'payments.configuration.amount_out_of_bounds': 'Montant hors des limites fixées pour ce moyen de paiement.',
  'storefront.option_required': 'Ce canal est une option de votre abonnement : ajoutez-la depuis la page Abonnement.',
  'storefront.tenant.not_writable': 'Votre compte est suspendu : la vente en ligne est en lecture seule.',
  'storefront.app.not_found': 'Cette application n’existe pas.',
  'storefront.location.not_found': 'Cet établissement n’existe pas.',
  'storefront.app.name_required': 'Donnez un nom à l’application.',
  'storefront.app.name_too_long': 'Nom trop long (80 caractères au plus).',
  'storefront.human_check.unavailable': 'Le contrôle anti-robot n’est pas encore configuré par la plateforme.',
  'storefront.app.human_check_not_for_kiosk': 'Une borne n’utilise pas le contrôle anti-robot.',
  'storefront.app.origin_invalid': 'Adresse invalide : https://votre-site.com sans chemin (capacitor://localhost ou ionic://localhost pour une application mobile).',
  'storefront.app.too_many_origins': '10 adresses de site au plus.',
  'storefront.app.too_many_locations': 'Trop d’établissements.',
  'storefront.app.return_url_invalid': 'Page de retour invalide : une adresse https complète.',
  'storefront.app.too_many_return_urls': '5 pages de retour au plus.',
  'storefront.app.already_disabled': 'Cette application est déjà désactivée.',
  'storefront.app.already_active': 'Cette application est déjà active.',
  'storefront.settings.service_mode_required': 'Proposez au moins un mode : sur place, à emporter ou livraison.',
  'storefront.settings.preparation_invalid': 'Le délai de préparation est compris entre 5 et 240 minutes.',
  'storefront.settings.amount_invalid': 'Montant invalide.',
  'storefront.settings.acceptance_delay_invalid': 'Le délai d’acceptation est compris entre 2 et 60 minutes.',
  'storefront.settings.expiry_invalid': 'Le délai de paiement est compris entre 5 et 120 minutes.',
  'storefront.settings.payment_required': 'Acceptez au moins un moyen de paiement par canal.',
  'storefront.settings.counter_limit_without_counter': 'Un plafond au comptoir exige le paiement au comptoir.',
  'storefront.kiosk.already_revoked': 'Cette borne est déjà révoquée.',
  'storefront.kiosk.device_required': 'Cette borne n’est pas appairée.',
  'storefront.kiosk.limit_reached': 'Toutes les bornes de votre abonnement sont appairées : révoquez-en une ou ajoutez des bornes à votre option.',
  'storefront.kiosk.location_not_served': 'L’application borne ne sert pas cet établissement.',
  'storefront.kiosk.name_required': 'Donnez un nom à la borne.',
  'storefront.kiosk.name_too_long': 'Nom trop long (60 caractères au plus).',
  'storefront.kiosk.not_a_kiosk_app': 'Seule une application de type borne appaire des bornes.',
  'storefront.kiosk.not_found': 'Cette borne n’existe pas.',
  'storefront.kiosk.pairing_invalid': 'Code inconnu ou expiré : affichez un nouveau code sur la borne.',
  'printing.concurrent_update': 'Quelqu’un a modifié cette imprimante en même temps : rechargez et réessayez.',
  'printing.job.document_invalid': 'Ce ticket ne peut pas être imprimé.',
  'printing.job.not_found': 'Ce ticket n’existe pas.',
  'printing.job.printer_disabled': 'L’imprimante est coupée : réactivez-la d’abord.',
  'printing.location.not_found': 'Cet établissement n’existe pas.',
  'printing.printer.already_active': 'Cette imprimante est déjà active.',
  'printing.printer.already_disabled': 'Cette imprimante est déjà coupée.',
  'printing.printer.name_required': 'Donnez un nom à l’imprimante.',
  'printing.printer.name_too_long': 'Nom trop long (80 caractères au plus).',
  'printing.printer.not_found': 'Cette imprimante n’existe pas.',
  'printing.printer.protocol_invalid': 'Type d’imprimante inconnu.',
  'printing.tenant.not_writable': 'Votre compte est suspendu : les imprimantes sont en lecture seule.',
  'storefront.settings.slot_capacity_invalid': 'Le nombre de commandes par créneau est compris entre 1 et 500.',
  'storefront.concurrent_update': 'Quelqu’un a modifié ces réglages en même temps : rechargez et réessayez.',
  'payments.provider.integration_missing': 'Ce prestataire en ligne n’est pas encore branché.',
  'identity.auth.invalid_credentials': 'Mot de passe actuel incorrect.',
  'identity.auth.too_many_attempts': 'Trop de tentatives : réessayez dans quelques minutes.',
  'fiscal.profile.ifu_invalid': 'L’IFU compte 13 chiffres.',
  'fiscal.profile.rccm_invalid': 'RCCM : 40 caractères au plus.',
  'fiscal.profile.token_missing': 'Enregistrez d’abord le jeton e-MECeF de la DGI.',
  'fiscal.profile.token_invalid': 'Ce jeton n’est pas valide : copiez-le de nouveau depuis le portail e-MECeF.',
  'fiscal.profile.token_unreadable': 'Le jeton enregistré ne peut plus être lu : saisissez-le de nouveau.',
  'fiscal.profile.currency_not_supported': 'La facture normalisée e-MECeF concerne les restaurants en francs CFA.',
  'fiscal.profile.not_found': 'Saisissez d’abord l’identité fiscale du restaurant.',
  'fiscal.authority.rejected': 'La DGI a refusé le jeton : vérifiez-le sur le portail e-MECeF.',
  'fiscal.authority.unreachable': 'Le service e-MECeF de la DGI ne répond pas : réessayez plus tard.',
  'fiscal.invoice.not_failed': 'Seule une facture en échec peut être relancée.',
  'payments.cash_session.already_open': 'Une caisse est déjà ouverte pour cet établissement.',
  'payments.cash_session.not_open': 'Cette session de caisse est clôturée.',
  'payments.cash_session.count_missing': 'Comptez les espèces et chaque moyen encaissé pendant la session.',
  'payments.cash_session.denominations_mismatch': 'Le total saisi ne correspond pas au détail des billets et pièces.',
  'payments.cash_session.movement_reason_required': 'Indiquez le motif du mouvement.',
  'payments.journal.period_invalid': 'Choisissez une période de 93 jours au plus.',
  'printing.receipt.no_printer': 'Aucune imprimante de tickets active dans cet établissement.',
  'printing.receipt.printer_elsewhere': 'Cette imprimante est coupée ou appartient à un autre établissement.',
  'printing.printer.station_invalid': 'Un poste ne se choisit que pour une imprimante de bons de cuisine.',
  'identity.token.invalid': 'Ce lien n’est plus valable : il a expiré ou a déjà servi. Demandez-en un nouveau.',
  'identity.password.too_short': 'Le mot de passe doit contenir au moins 8 caractères.',
  'identity.password.too_long': 'Le mot de passe ne peut pas dépasser 128 caractères.',
  'identity.password.compromised': 'Ce mot de passe figure dans des fuites de données connues : choisissez-en un autre.',
  'identity.password.empty': 'Saisissez un mot de passe.',
  'identity.user.email_already_used': 'Cette adresse est déjà utilisée par un autre compte.',
  'identity.user.name_too_long': 'Prénom et nom : 60 caractères au plus.',
  'identity.user.city_too_long': 'Ville : 80 caractères au plus.',
  'identity.user.language_unsupported': 'Langue non disponible.',
  'identity.user.avatar_invalid': 'La photo doit être une image JPEG, PNG ou WebP de 512 Ko au plus.',
  'identity.session.not_found': 'Cette session est déjà fermée.',
  'identity.session.already_revoked': 'Cette session est déjà fermée.',
  'common.time_zone.unknown': 'Fuseau horaire inconnu.',
  'common.phone.invalid': 'Numéro de téléphone invalide : utilisez le format international (+229…).',
  'common.money.invalid_precision': 'Montant invalide pour cette devise (trop de décimales).',
  'delivery.settings.lead_time_out_of_range': 'Le délai doit être compris entre 0 et 240 minutes.',
  'delivery.settings.delivery_needs_zone': 'Ajoutez d’abord une zone de livraison active.',
  'delivery.settings.last_active_zone': 'C’est la dernière zone active : désactivez d’abord la livraison.',
  'delivery.settings.too_many_zones': '50 zones au plus par établissement.',
  'delivery.zone.not_found': 'Cette zone n’existe plus.',
  'delivery.zone.name_required': 'Donnez un nom à la zone.',
  'delivery.zone.name_too_long': 'Nom de zone trop long (80 caractères au plus).',
  'delivery.zone.name_already_used': 'Une zone porte déjà ce nom.',
  'delivery.zone.amount_negative': 'Les frais et le minimum ne peuvent pas être négatifs.',
  'delivery.zone.minutes_out_of_range': 'La durée de livraison doit être comprise entre 5 et 240 minutes.',
  'delivery.fulfillment.order_not_open': 'Cette commande est terminée ou annulée.',
  'delivery.fulfillment.order_kind_mismatch': 'Le retrait est pour les commandes à emporter, la livraison pour les commandes en livraison.',
  'delivery.fulfillment.order_location_mismatch': 'Cette commande n’a pas été prise dans cet établissement.',
  'delivery.fulfillment.pickup_disabled': 'Cet établissement ne propose pas le retrait.',
  'delivery.fulfillment.delivery_disabled': 'Cet établissement ne propose pas la livraison.',
  'delivery.fulfillment.zone_unavailable': 'Cette zone n’existe pas ou n’est pas active.',
  'delivery.fulfillment.below_minimum_order': 'La commande n’atteint pas le minimum de cette zone.',
  'delivery.fulfillment.customer_name_required': 'Le nom du client est obligatoire.',
  'delivery.fulfillment.customer_name_too_long': 'Nom du client trop long (100 caractères au plus).',
  'delivery.fulfillment.customer_phone_required': 'Le téléphone du client est obligatoire pour une livraison.',
  'delivery.fulfillment.address_required': 'L’adresse de livraison est obligatoire.',
  'delivery.fulfillment.address_too_long': 'Adresse, repère ou instructions trop longs.',
  'delivery.fulfillment.pickup_time_invalid': 'L’heure de retrait doit être dans les 7 prochains jours.',
  'delivery.fulfillment.courier_name_required': 'Le nom du livreur est obligatoire.',
  'delivery.fulfillment.courier_name_too_long': 'Nom du livreur trop long (80 caractères au plus).',
  'delivery.fulfillment.reason_required': 'Indiquez le motif.',
  'delivery.fulfillment.reason_too_long': 'Motif trop long (300 caractères au plus).',
  'delivery.fulfillment.wrong_mode': 'Cette étape ne s’applique pas à ce type de fiche.',
  'delivery.fulfillment.invalid_transition': 'Cette étape n’est pas possible depuis le statut actuel.',
  'delivery.fulfillment.already_closed': 'Cette fiche est terminée ou annulée.',
  'delivery.fulfillment.order_already_has_one': 'Cette commande a déjà une fiche de retrait ou de livraison.',
  'delivery.fulfillment.not_found': 'Fiche introuvable.',
  'delivery.order.not_found': 'Commande introuvable.',
  'orders.search.period_invalid': 'La date de début doit précéder la date de fin.',
  'orders.search.amount_invalid': 'Montants invalides : le minimum doit être inférieur ou égal au maximum.',
  'orders.search.filter_invalid': 'Filtre inconnu.',
  'storefront.receipt.not_found': 'Reçu introuvable (commande en brouillon ou inexistante).',
  'storefront.receipt.recipient_required': 'Saisissez le numéro du client, ou rattachez à la commande un client qui a un numéro.',
  'storefront.receipt.channel_invalid': 'Le reçu part par WhatsApp ou par SMS.',
  'delivery.fulfillment.courier_not_found': 'Ce livreur n’existe pas.',
  'delivery.fulfillment.amount_to_collect_invalid': 'Montant à encaisser invalide.',
  'delivery.fulfillment.collection_required': 'Indiquez combien le livreur a encaissé et comment le client a payé.',
  'delivery.fulfillment.collected_amount_invalid': 'Le montant encaissé doit être supérieur à zéro et au plus égal au montant dû.',
  'delivery.fulfillment.nothing_to_collect': 'Cette livraison n’a rien à encaisser.',
  'delivery.fulfillment.collection_method_invalid': 'Le client paie le livreur en espèces ou par mobile money.',
  'delivery.collection.not_accepted': 'La commande n’est pas réglée et l’établissement n’accepte pas l’encaissement par le livreur : activez « Espèces encaissées par le livreur » (ou le mobile money) dans les moyens de paiement.',
  'delivery.courier.not_found': 'Ce livreur n’existe plus.',
  'delivery.courier.name_required': 'Le nom du livreur est obligatoire.',
  'delivery.courier.name_too_long': 'Nom du livreur trop long (80 caractères au plus).',
  'delivery.courier.kind_invalid': 'Choisissez salarié ou indépendant.',
  'delivery.courier.fee_invalid': 'Le tarif par course doit être positif ou nul, dans la devise du restaurant.',
  'delivery.courier.inactive': 'Ce livreur est désactivé.',
  'delivery.remittance.no_delivery': 'Choisissez au moins une course.',
  'delivery.remittance.too_many_deliveries': '200 courses au plus par remise.',
  'delivery.remittance.not_awaiting': 'Une de ces courses n’est pas terminée ou a déjà été soldée : rechargez la page.',
  'delivery.remittance.mixed_couriers': 'Une remise couvre les courses d’un seul livreur.',
  'delivery.remittance.mixed_locations': 'Une remise couvre les courses d’un seul établissement.',
  'delivery.remittance.note_too_long': 'Note trop longue (300 caractères au plus).',
  'delivery.remittance.amount_invalid': 'Montant remis invalide pour cette devise.',
  'delivery.remittance.not_found': 'Remise introuvable.',
  'staff.staff_member.last_owner': 'C’est le dernier propriétaire actif : nommez d’abord un autre propriétaire.',
  'staff.staff_member.cannot_change_own_role': 'Vous ne pouvez pas modifier votre propre rôle, vos affectations ni votre statut : demandez à un autre responsable.',
  'staff.role.not_held': 'Vous ne pouvez donner qu’un rôle dont vous avez vous-même toutes les permissions.',
  'staff.permission.not_held': 'Vous ne pouvez accorder qu’une permission que vous avez vous-même.',
  'staff.role.owner_only': 'Seul un propriétaire peut donner, retirer ou gérer le rôle de propriétaire.',
  'staff.staff_member.user_already_in_tenant': 'Cette personne fait déjà partie de l’équipe.',
  'staff.staff_member.already_assigned_to_location': 'Ce membre est déjà affecté à cet établissement.',
  'staff.staff_member.location_not_operational': 'Cet établissement n’est pas ouvert : impossible d’y affecter du personnel.',
  'staff.staff_member.suspended_cannot_be_assigned': 'Un membre suspendu ne peut pas être affecté : réactivez-le d’abord.',
  'staff.staff_member.has_left': 'Ce membre a quitté le restaurant.',
  'staff.staff_member.not_active': 'Ce membre n’est pas actif.',
  'staff.role.name_already_used': 'Un rôle porte déjà ce nom.',
  'staff.role.in_use': 'Ce rôle est encore attribué : retirez-le d’abord aux membres qui l’ont.',
  'staff.staff_member.invitation_already_accepted': 'Ce membre a déjà accepté l’invitation.',
  'staff.role.system_role_protected': 'Les rôles Propriétaire et Gérant ne sont pas modifiables.',
  'staff.role.permission_unknown': 'Permission inconnue.',
  'catalog.menu.empty_section': 'Une catégorie de la carte est vide : ajoutez-y un produit ou supprimez-la avant de publier.',
  'catalog.menu.no_section': 'La carte n’a aucune catégorie.',
  'catalog.menu.product_archived': 'La carte contient un produit archivé : retirez-le avant de publier.',
  'catalog.menu.published_is_read_only': 'Carte publiée : retirez-la de la publication pour la modifier.',
  'catalog.menu.product_already_in_section': 'Ce produit est déjà dans cette catégorie.',
  'catalog.menu.section_name_duplicate': 'Une catégorie porte déjà ce nom.',
  'catalog.product.archived': 'Ce produit est archivé.',
  'catalog.product.not_sold_here': 'Ce produit n’est pas vendu dans cet établissement.',
  'catalog.product.unavailable_here': 'Ce produit est en rupture dans cet établissement.',
  'catalog.product.too_many_locations': 'Un produit compte 200 établissements au plus.',
  'notifications.campaign.location_required': 'Choisissez l’établissement dont les clients reçoivent la campagne.',
  'catalog.product.allergen_unknown': 'Cet allergène ne fait pas partie de la liste réglementaire.',
  'catalog.product.channel_invalid': 'Ce canal de vente n’existe pas.',
  'catalog.product.already_active': 'Ce produit est déjà en vente.',
  'catalog.product.not_active': 'Ce produit n’est pas en vente.',
  'catalog.product.negative_price': 'Le prix ne peut pas être négatif.',
  'catalog.product.tax_category_archived': 'Cette taxe est archivée.',
  'catalog.product.variant_name_duplicate': 'Une variante porte déjà ce nom.',
  'catalog.product.modifier_group_currency_mismatch': 'Ce groupe d’options n’est pas dans la devise du produit.',
  'files.media_file.content_type_not_allowed': 'Formats acceptés : JPEG, PNG ou WebP.',
  'files.media_file.size_exceeds_limit': 'La photo est trop lourde (5 Mo au plus).',
  'files.media_file.not_uploaded': 'La photo n’a pas été reçue par le stockage : réessayez.',
  'files.file_name.invalid': 'Nom de fichier invalide : renommez la photo.',
  'files.file_name.too_long': 'Nom de fichier trop long : renommez la photo.',
  'orders.plan.monthly_limit_reached': 'Plafond mensuel de commandes de votre plan atteint. Passez au plan supérieur pour continuer.',
  'locations.plan.limit_reached': 'Nombre maximal d’établissements de votre plan atteint.',
  'staff.plan.limit_reached': 'Nombre maximal de membres de l’équipe de votre plan atteint.',
};

export function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code && messages[error.code]) return messages[error.code];
    if (error.status === 401) return 'Session expirée : reconnectez-vous.';
    if (error.status === 403) return 'Vous n’avez pas la permission de faire cette action.';
    if (error.code?.endsWith('.concurrent_update')) return 'Quelqu’un a modifié cet élément en même temps : rechargez et réessayez.';
    if (error.status === 409) return 'Action impossible dans l’état actuel : rechargez la page et réessayez.';
    if (error.status === 429) return 'Trop de requêtes : patientez quelques secondes.';
    if (error.status >= 500) return 'Erreur du serveur : réessayez dans un instant.';
    if (error.status === 400 && !error.code) return 'Requête invalide : vérifiez les champs saisis.';
    return error.message;
  }
  if (error instanceof TypeError) return `API injoignable (${API_URL}). Vérifiez votre connexion.`;
  return error instanceof Error ? error.message : String(error);
}
