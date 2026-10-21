import {api, del, fetchBlob, put, putBlob} from './api';

export interface Me {
  id: string;
  email: string;
  status: string;
  isEmailVerified: boolean;
  createdAtUtc: string;
  emailVerifiedAtUtc: string | null;
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

export interface Activity {
  id: string;
  tenantId: string | null;
  occurredAtUtc: string;
  action: string;
  targetType: string;
  targetId: string;
}

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
  {id: 'Africa/Casablanca', label: 'Africa/Casablanca (Maroc)'},
  {id: 'Europe/Brussels', label: 'Europe/Brussels (Belgique)'},
  {id: 'Europe/Zurich', label: 'Europe/Zurich (Suisse)'},
  {id: 'America/Montreal', label: 'America/Montreal (Canada)'},
  {id: 'UTC', label: 'UTC'},
];

export function fullName(me: Pick<Me, 'firstName' | 'lastName'>): string | null {
  const name = [me.firstName, me.lastName].filter(Boolean).join(' ');
  return name || null;
}

export function initialsOf(me: Pick<Me, 'firstName' | 'lastName' | 'email'>): string {
  const name = fullName(me) ?? me.email.split('@')[0].replace(/[._-]+/g, ' ');
  return name.split(' ').filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase() || '?';
}

/** Décalage UTC actuel d'un fuseau, pour l'affichage (« GMT+1 »). */
export function utcOffset(timeZone: string): string {
  try {
    const part = new Intl.DateTimeFormat('fr-FR', {timeZone, timeZoneName: 'shortOffset'}).formatToParts(new Date()).find(item => item.type === 'timeZoneName');
    return part?.value.replace('UTC', 'GMT') ?? '';
  } catch {
    return '';
  }
}

export const loadMe = (signal?: AbortSignal) => api<Me>('/me', {signal});

export const loadSessions = (signal?: AbortSignal) => api<MySession[]>('/me/sessions', {signal});

export const loadActivity = (signal?: AbortSignal) => api<Activity[]>('/audit/me', {query: {limit: 6}, signal}).catch(() => [] as Activity[]);

export const updateProfile = (body: {firstName: string | null; lastName: string | null; phone: string | null; city: string | null}) =>
  put('/me/profile', body);

export const updatePreferences = (body: {language: string; timeZone: string; productNewsEmails: boolean}) => put('/me/preferences', body);

export const changePassword = (currentPassword: string, newPassword: string) => put('/me/password', {currentPassword, newPassword});

export const changeEmail = (newEmail: string, currentPassword: string) => put('/me/email', {newEmail, currentPassword});

export const revokeSession = (id: string) => del(`/me/sessions/${id}`);

export const removeAvatar = () => del('/me/avatar');

/** La photo de l'utilisateur en URL locale (à libérer), ou null s'il n'en a pas. */
export async function loadAvatar(signal?: AbortSignal): Promise<string | null> {
  const blob = await fetchBlob('/me/avatar', signal);
  return blob ? URL.createObjectURL(blob) : null;
}

/** Recadre au centre en carré de 256 px et envoie en JPEG : quelques dizaines de Ko, loin de la limite de 512 Ko. */
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
  await putBlob('/me/avatar', blob);
}

const moduleLabel: Record<string, string> = {
  identity: 'Compte',
  staff: 'Personnel',
  catalog: 'Menu',
  orders: 'Commandes',
  kitchen: 'Cuisine',
  payments: 'Paiements',
  reservations: 'Réservations',
  floor: 'Salle',
  locations: 'Établissements',
  inventory: 'Stock',
  customers: 'Clients',
  promotions: 'Promotions',
  loyalty: 'Fidélité',
  billing: 'Abonnement',
  integrations: 'Intégrations',
  reviews: 'Avis',
  tenants: 'Restaurant',
  delivery: 'Livraison',
  files: 'Fichiers',
  reporting: 'Rapports',
};

const known: Record<string, string> = {
  'identity.session.started': 'Connexion à l’application',
  'identity.session.revoked': 'Session fermée',
  'identity.session.replay_detected': 'Session suspecte bloquée',
  'identity.user.password_changed': 'Mot de passe modifié',
  'identity.user.email_changed': 'Adresse email modifiée',
  'identity.user.email_verified': 'Adresse email vérifiée',
  'identity.user.profile_changed': 'Profil mis à jour',
  'identity.user.preferences_changed': 'Préférences modifiées',
  'identity.user.avatar_changed': 'Photo de profil modifiée',
  'identity.user.registered': 'Création du compte',
  'identity.user.invitation_accepted': 'Invitation acceptée',
};

const verbs: [RegExp, string][] = [
  [/created|added|opened|registered$/, 'création'],
  [/archived$/, 'archivage'],
  [/deleted|removed$/, 'suppression'],
  [/published$/, 'publication'],
  [/cancelled$/, 'annulation'],
  [/granted$/, 'droit accordé'],
  [/revoked$/, 'droit retiré'],
  [/invited$/, 'invitation'],
  [/suspended$/, 'suspension'],
  [/refund/, 'remboursement'],
  [/captured$/, 'encaissement'],
  [/adjusted$/, 'ajustement'],
];

export function activityLabel(action: string): string {
  if (known[action]) return known[action];
  const [module] = action.split('.');
  const verb = verbs.find(([pattern]) => pattern.test(action))?.[1] ?? 'modification';
  return `${moduleLabel[module] ?? module} : ${verb}`;
}

export const activityModule = (action: string) => action.split('.')[0];
