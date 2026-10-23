import {api, apiBlob, apiPutBlob, type Page} from './client';

export interface MyProfile {
  id: string;
  email: string;
  status: string;
  isEmailVerified: boolean;
  createdAtUtc: string;
  passwordChangedAtUtc: string | null;
  firstName: string | null;
  lastName: string | null;
  phone: string | null;
  city: string | null;
  language: string;
  timeZone: string;
  productNewsEmails: boolean;
  avatarUpdatedAtUtc: string | null;
  twoFactorEnabled?: boolean;
  recoveryCodesLeft?: number;
}

export interface MySession {
  id: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  isCurrent: boolean;
  deviceLabel: string | null;
}

export interface MyActivity {
  id: string;
  occurredAtUtc: string;
  action: string;
  targetType: string;
}

/** Signalé après une modification du profil, pour que l'en-tête se mette à jour. */
export const PROFILE_CHANGED = 'restaurantos:profile-changed';
export const profileChanged = () => window.dispatchEvent(new Event(PROFILE_CHANGED));

export const languageLabel: Record<string, string> = {fr: 'Français', en: 'English'};

export const timeZones: {id: string; label: string}[] = [
  {id: 'Europe/Paris', label: 'Europe/Paris (France)'},
  {id: 'Africa/Porto-Novo', label: 'Africa/Porto-Novo (Bénin)'},
  {id: 'Africa/Lome', label: 'Africa/Lome (Togo)'},
  {id: 'Africa/Abidjan', label: 'Africa/Abidjan (Côte d’Ivoire)'},
  {id: 'Africa/Dakar', label: 'Africa/Dakar (Sénégal)'},
  {id: 'Africa/Ouagadougou', label: 'Africa/Ouagadougou (Burkina Faso)'},
  {id: 'Africa/Bamako', label: 'Africa/Bamako (Mali)'},
  {id: 'Africa/Niamey', label: 'Africa/Niamey (Niger)'},
  {id: 'Africa/Lagos', label: 'Africa/Lagos (Nigeria)'},
  {id: 'Africa/Douala', label: 'Africa/Douala (Cameroun)'},
  {id: 'Europe/Brussels', label: 'Europe/Brussels (Belgique)'},
  {id: 'Europe/Zurich', label: 'Europe/Zurich (Suisse)'},
  {id: 'America/Montreal', label: 'America/Montreal (Canada)'},
  {id: 'UTC', label: 'UTC'},
];

export function fullName(me: Pick<MyProfile, 'firstName' | 'lastName'>): string | null {
  return [me.firstName, me.lastName].filter(Boolean).join(' ') || null;
}

export const loadProfile = () => api<MyProfile>('/me');
export const loadSessions = () => api<MySession[]>('/me/sessions');
/** Ses propres actions, par le journal de la plateforme : /audit/me exige un restaurant sélectionné. */
export const loadActivity = (userId: string) =>
  api<Page<MyActivity>>('/admin/audit', {query: {actorType: 'User', actorId: userId, pageSize: 8}}).then(result => result.items).catch(() => [] as MyActivity[]);

export const updateProfile = (body: {firstName: string | null; lastName: string | null; phone: string | null; city: string | null}) =>
  api<void>('/me/profile', {method: 'PUT', body});
export const updatePreferences = (body: {language: string; timeZone: string; productNewsEmails: boolean}) =>
  api<void>('/me/preferences', {method: 'PUT', body});
export const changePassword = (currentPassword: string, newPassword: string) =>
  api<void>('/me/password', {method: 'PUT', body: {currentPassword, newPassword}});
export const changeEmail = (newEmail: string, currentPassword: string) =>
  api<void>('/me/email', {method: 'PUT', body: {newEmail, currentPassword}});
export const revokeSession = (id: string) => api<void>(`/me/sessions/${id}`, {method: 'DELETE'});
export const revokeAllSessions = () => api<void>('/me/sessions', {method: 'DELETE'});
export const startTwoFactor = (password: string) => api<{secret: string; otpAuthUri: string}>('/me/two-factor/setup', {method: 'POST', body: {password}});
export const confirmTwoFactor = (code: string) => api<{codes: string[]}>('/me/two-factor/confirm', {method: 'POST', body: {code}});
export const disableTwoFactor = (password: string, code: string) => api<void>('/me/two-factor/disable', {method: 'POST', body: {password, code}});
export const removeAvatar = () => api<void>('/me/avatar', {method: 'DELETE'});

/** La photo en URL locale (à libérer), ou null. */
export async function loadAvatar(signal?: AbortSignal): Promise<string | null> {
  const blob = await apiBlob('/me/avatar', signal);
  return blob ? URL.createObjectURL(blob) : null;
}

/** Recadrée au centre en carré de 256 px, envoyée en JPEG. */
export async function uploadAvatar(file: File): Promise<void> {
  if (!/^image\/(jpeg|png|webp)$/.test(file.type)) throw new Error('Formats acceptés : JPEG, PNG ou WebP.');
  const bitmap = await createImageBitmap(file);
  const side = Math.min(bitmap.width, bitmap.height);
  const canvas = document.createElement('canvas');
  canvas.width = 256;
  canvas.height = 256;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('Impossible de préparer la photo.');
  context.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, 256, 256);
  bitmap.close();
  const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.88));
  if (!blob) throw new Error('Impossible de préparer la photo.');
  await apiPutBlob('/me/avatar', blob);
}

/** Numéro saisi au format international (+33…), espaces retirés ; null si vide, undefined si invalide. */
export function normalizePhone(value: string): string | null | undefined {
  const compact = value.replace(/[\s.()-]/g, '');
  if (!compact) return null;
  const international = compact.startsWith('00') ? `+${compact.slice(2)}` : compact;
  return /^\+[1-9]\d{6,14}$/.test(international) ? international : undefined;
}

const actions: Record<string, string> = {
  'identity.session.started': 'Connexion',
  'identity.session.revoked': 'Session fermée',
  'identity.session.replay_detected': 'Session suspecte bloquée',
  'identity.user.password_changed': 'Mot de passe modifié',
  'identity.user.email_changed': 'Adresse email modifiée',
  'identity.user.email_verified': 'Adresse email vérifiée',
  'identity.user.profile_changed': 'Profil mis à jour',
  'identity.user.preferences_changed': 'Préférences modifiées',
  'identity.user.avatar_changed': 'Photo de profil modifiée',
};

const modules: Record<string, string> = {
  identity: 'Compte', tenants: 'Restaurant', billing: 'Abonnement', payments: 'Paiements', notifications: 'Notifications', staff: 'Personnel',
  catalog: 'Menu', orders: 'Commandes', storefront: 'Vente en ligne', audit: 'Audit', integrations: 'Intégrations',
};

const verbs: [RegExp, string][] = [
  [/created|added|opened|registered|started$/, 'création'],
  [/archived$/, 'archivage'],
  [/deleted|removed$/, 'suppression'],
  [/published$/, 'publication'],
  [/cancelled$/, 'annulation'],
  [/suspended$/, 'suspension'],
  [/activated|reactivated$/, 'activation'],
  [/granted$/, 'droit accordé'],
  [/revoked$/, 'droit retiré'],
  [/invited$/, 'invitation'],
  [/refund/, 'remboursement'],
];

export function activityLabel(action: string): string {
  if (actions[action]) return actions[action];
  const [module] = action.split('.');
  const verb = verbs.find(([pattern]) => pattern.test(action))?.[1] ?? 'modification';
  return `${modules[module] ?? module} : ${verb}`;
}

/** Libellés des types de cible du journal d'audit (AuditBridges.cs). */
const targets: Record<string, string> = {
  tenant: 'Restaurant', user: 'Compte', session: 'Session', subscription: 'Abonnement', subscription_checkout: 'Paiement en ligne',
  plan: 'Plan', staff_member: 'Membre du personnel', role: 'Rôle', payment: 'Paiement', refund: 'Remboursement',
  payment_configuration: 'Configuration de paiement', payment_provider: 'Prestataire de paiement',
  notification: 'Notification', notification_configuration: 'Configuration de notification', notification_provider: 'Prestataire de notification',
  customer: 'Client', consent: 'Consentement', feedback: 'Avis', survey: 'Enquête', stored_value_card: 'Carte cadeau',
  api_credential: 'Clé d’API', webhook_endpoint: 'Webhook', inventory_item: 'Article de stock', export_job: 'Export',
};

export function targetLabel(targetType: string): string {
  return targets[targetType] ?? targetType;
}

/** +22997001122 -> +229 97 00 11 22 ; +33612345678 -> +33 6 12 34 56 78. */
export function formatPhone(phone: string): string {
  const match = /^\+(33|32|41|1|\d{3})(\d+)$/.exec(phone);
  if (!match) return phone;
  const [, country, rest] = match;
  const head = rest.length % 2 === 1 ? `${rest[0]} ` : '';
  const pairs = (rest.length % 2 === 1 ? rest.slice(1) : rest).match(/\d{2}/g)?.join(' ') ?? rest;
  return `+${country} ${head}${pairs}`;
}
