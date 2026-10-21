import {api, del, post, put} from './api';

export type MessageChannel = 'Email' | 'Sms' | 'WhatsApp' | 'Push' | 'InApp';
export type TemplateStatus = 'Draft' | 'Published' | 'Archived';
export type MessageStatus = 'Pending' | 'Scheduled' | 'Processing' | 'Sent' | 'PartiallyDelivered' | 'Delivered' | 'Failed' | 'Cancelled';
export type SkipReason = 'OptedOut' | 'NotACustomer' | 'ConsentRequired' | 'ConsentNotGranted' | 'NoProvider' | 'NoTemplate' | 'TemplateIncomplete' | 'QuotaExceeded' | 'Duplicate' | 'NoContact' | 'FrequencyLimited';

export interface MessageTemplate {
  id: string;
  isPlatform: boolean;
  key: string;
  channel: MessageChannel;
  language: string;
  subject: string | null;
  body: string;
  version: number;
  status: TemplateStatus;
  isEnabled: boolean;
  variables: string[];
  updatedAtUtc: string;
  publishedAtUtc: string | null;
}

export interface MessageDelivery {
  id: string;
  attemptNumber: number;
  provider: string;
  status: 'InProgress' | 'Sent' | 'PartiallyDelivered' | 'Delivered' | 'Failed';
  startedAtUtc: string;
  completedAtUtc: string | null;
  deliveredAtUtc: string | null;
  failureCode: string | null;
  failureKind: 'Temporary' | 'Permanent' | null;
  nextRetryAtUtc: string | null;
}

export interface SentMessage {
  id: string;
  type: string;
  purpose: 'Transactional' | 'Marketing';
  priority: string;
  channel: MessageChannel;
  provider: string;
  status: MessageStatus;
  recipient: string;
  customerId: string | null;
  locationId: string | null;
  language: string;
  createdAtUtc: string;
  scheduledAtUtc: string | null;
  sentAtUtc: string | null;
  deliveredAtUtc: string | null;
  cancelledAtUtc: string | null;
  isRedacted: boolean;
  isDeliveryAbandoned: boolean;
  lastFailureCode: string | null;
  deliveries: MessageDelivery[];
}

export type MessageGroup = 'Commandes' | 'Réservations' | 'Paiements' | 'Fidélité' | 'Marketing' | 'Autres';

export interface MessageType {
  key: string;
  label: string;
  group: MessageGroup;
  marketing: boolean;
  variables: string[];
}

/** Un type du catalogue du backend (`GET /notification-templates/types`) : pour un restaurant, ceux adressés aux clients. */
interface CatalogType {
  key: string;
  purpose: 'Transactional' | 'Marketing';
  variables: string[];
}

/** Libellés et rubriques des types connus, dans l'ordre d'affichage. Le catalogue (variables, nature) vient du backend. */
const typeNames: [key: string, label: string, group: MessageGroup][] = [
  ['order.confirmed', 'Commande confirmée', 'Commandes'],
  ['order.ready', 'Commande prête', 'Commandes'],
  ['order.cancelled', 'Commande annulée', 'Commandes'],
  ['order.receipt', 'Reçu de la commande', 'Commandes'],
  ['reservation.confirmed', 'Réservation confirmée', 'Réservations'],
  ['reservation.reminder', 'Rappel de réservation', 'Réservations'],
  ['reservation.cancelled', 'Réservation annulée', 'Réservations'],
  ['reservation.deposit_request', 'Acompte à payer', 'Réservations'],
  ['waitlist.offer', 'Table libérée (liste d’attente)', 'Réservations'],
  ['waitlist.table_ready', 'Table prête (file à la porte)', 'Réservations'],
  ['payment.succeeded', 'Paiement reçu', 'Paiements'],
  ['payment.failed', 'Paiement échoué', 'Paiements'],
  ['refund.succeeded', 'Remboursement effectué', 'Paiements'],
  ['loyalty.reward_earned', 'Récompense de fidélité', 'Fidélité'],
  ['marketing.promotional_campaign', 'Campagne promotionnelle', 'Marketing'],
  ['marketing.new_menu', 'Nouvelle carte', 'Marketing'],
  ['marketing.loyalty_campaign', 'Campagne de fidélité', 'Marketing'],
  ['marketing.customer_reactivation', 'Relance d’un client', 'Marketing'],
  ['marketing.special_event', 'Événement spécial', 'Marketing'],
];

export const messageGroups: MessageGroup[] = ['Commandes', 'Réservations', 'Paiements', 'Fidélité', 'Marketing', 'Autres'];

const position = new Map(typeNames.map(([key], index) => [key, index]));
const names = new Map(typeNames.map(([key, label, group]) => [key, {label, group}]));

export const typeLabel = (key: string) => names.get(key)?.label ?? key;

let catalog: Promise<MessageType[]> | null = null;

/** Le catalogue des types de messages clients, chargé une fois par session. */
export function loadMessageTypes(signal?: AbortSignal): Promise<MessageType[]> {
  catalog ??= api<CatalogType[]>('/notification-templates/types')
    .then(list =>
      list
        .map(type => ({
          key: type.key,
          label: typeLabel(type.key),
          group: names.get(type.key)?.group ?? (type.purpose === 'Marketing' ? 'Marketing' : 'Autres'),
          marketing: type.purpose === 'Marketing',
          variables: type.variables,
        }))
        .sort((a, b) => (position.get(a.key) ?? typeNames.length) - (position.get(b.key) ?? typeNames.length) || a.key.localeCompare(b.key)),
    )
    .catch(error => {
      catalog = null;
      throw error;
    });
  return signal ? Promise.race([catalog, new Promise<never>((_, reject) => signal.addEventListener('abort', () => reject(signal.reason), {once: true}))]) : catalog;
}

export const variableLabel: Record<string, string> = {
  'restaurant.name': 'Nom du restaurant',
  'location.name': 'Établissement',
  'customer.firstName': 'Prénom du client',
  'order.number': 'N° de commande',
  'order.total': 'Total de la commande',
  'receipt.url': 'Lien du reçu',
  'reservation.date': 'Date de réservation',
  'reservation.time': 'Heure de réservation',
  'reservation.guestCount': 'Nombre de couverts',
  'reservation.url': 'Lien de la réservation',
  'deposit.amount': 'Montant de l’acompte',
  'deposit.dueTime': 'Heure limite de paiement',
  'offer.url': 'Lien pour accepter la table',
  'offer.expiresTime': 'Heure limite de réponse',
  'payment.amount': 'Montant payé',
  'payment.method': 'Moyen de paiement',
  'refund.amount': 'Montant remboursé',
  'loyalty.points': 'Points de fidélité',
  'loyalty.reward': 'Récompense',
  'campaign.title': 'Titre de la campagne',
  'campaign.url': 'Lien de la campagne',
  'unsubscribe.url': 'Lien de désinscription',
  'event.date': 'Date de l’événement',
};

/** Valeurs d'exemple pour l'aperçu d'un modèle. */
export const sampleValues: Record<string, string> = {
  'restaurant.name': 'Chez Awa',
  'location.name': 'Cotonou Centre',
  'customer.firstName': 'Fatou',
  'order.number': '20261011-042',
  'order.total': '8 200 F CFA',
  'receipt.url': 'https://exemple.com/recu?jeton=…',
  'reservation.date': 'samedi 18 octobre',
  'reservation.time': '20:00',
  'reservation.guestCount': '4',
  'reservation.url': 'https://exemple.com/reservation?jeton=…',
  'deposit.amount': '20 000 F CFA',
  'deposit.dueTime': '18:30',
  'offer.url': 'https://exemple.com/reservation/attente?jeton=…',
  'offer.expiresTime': '19:15',
  'payment.amount': '8 200 F CFA',
  'payment.method': 'Mobile Money',
  'refund.amount': '2 500 F CFA',
  'loyalty.points': '120',
  'loyalty.reward': 'Un dessert offert',
  'campaign.title': 'Le menu d’automne est arrivé',
  'campaign.url': 'https://exemple.com/menu',
  'unsubscribe.url': 'https://exemple.com/desinscription',
  'event.date': 'vendredi 24 octobre',
};

export const channelLabel: Record<MessageChannel, string> = {Email: 'E-mail', Sms: 'SMS', WhatsApp: 'WhatsApp', Push: 'Notification mobile', InApp: 'Dans l’application'};
/** Canaux joignables pour un client du restaurant. */
export const customerChannels: MessageChannel[] = ['Email', 'Sms', 'WhatsApp'];
export const languages: [string, string][] = [['fr', 'Français'], ['en', 'Anglais']];

export const statusLabel: Record<MessageStatus, string> = {
  Pending: 'En attente', Scheduled: 'Programmé', Processing: 'En cours', Sent: 'Envoyé', PartiallyDelivered: 'Remis en partie',
  Delivered: 'Remis', Failed: 'Échec', Cancelled: 'Annulé',
};
export const statusTone: Record<MessageStatus, 'neutral' | 'blue' | 'green' | 'amber' | 'red'> = {
  Pending: 'neutral', Scheduled: 'blue', Processing: 'blue', Sent: 'green', PartiallyDelivered: 'amber', Delivered: 'green', Failed: 'red', Cancelled: 'neutral',
};

export const skipLabel: Record<SkipReason, string> = {
  OptedOut: 'Le client a refusé ce type de message sur ce canal.',
  NotACustomer: 'Ce destinataire n’est pas un client du restaurant.',
  ConsentRequired: 'Un message marketing demande le consentement du client.',
  ConsentNotGranted: 'Le client n’a pas donné son accord pour recevoir du marketing sur ce canal.',
  NoProvider: 'Aucun fournisseur actif pour ce canal : configurez-le dans Paramètres › Notifications.',
  NoTemplate: 'Aucun modèle publié et actif pour ce type, ce canal et cette langue.',
  TemplateIncomplete: 'Une variable du modèle n’a pas de valeur.',
  QuotaExceeded: 'Quota d’envoi atteint.',
  Duplicate: 'Ce message a déjà été envoyé.',
  NoContact: 'Le client n’a pas de coordonnée pour ce canal.',
  FrequencyLimited: 'Le client a déjà reçu un message marketing récemment (règle de fréquence).',
};

const PLACEHOLDER = /\{\{\s*([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*)\s*\}\}/g;

/** Variables écrites dans un contenu ({{nom.chemin}}), avec la même règle que le backend : toute autre accolade double est refusée. */
export function variablesIn(text: string): {used: string[]; invalid: boolean} {
  const used = [...new Set([...text.matchAll(PLACEHOLDER)].map(match => match[1]))];
  const rest = text.replace(PLACEHOLDER, '');
  return {used, invalid: rest.includes('{{') || rest.includes('}}') || used.some(name => name.length > 64)};
}

export const render = (text: string, values: Record<string, string>) => text.replace(PLACEHOLDER, (all, name: string) => values[name] ?? all);

/** SMS : 160 caractères par message (70 avec accents ou emoji), 153 / 67 au-delà d'un message. */
export function smsSegments(text: string): {length: number; segments: number; unicode: boolean} {
  const unicode = /[^\n\r\x20-\x7E£¥èéùìòÇØøÅåÄÖÑÜäöñüà€]/.test(text);
  const single = unicode ? 70 : 160;
  const multi = unicode ? 67 : 153;
  const length = [...text].length;
  return {length, unicode, segments: length === 0 ? 0 : length <= single ? 1 : Math.ceil(length / multi)};
}

export const listTemplates = (signal?: AbortSignal) => api<MessageTemplate[]>('/notification-templates', {signal});

export const createTemplate = (input: {key: string; channel: MessageChannel; language: string; subject: string | null; body: string}) =>
  post<{id: string}>('/notification-templates', input).then(created => created.id);

export const updateTemplate = (id: string, subject: string | null, body: string) => put(`/notification-templates/${id}`, {subject, body});
export const newVersion = (id: string) => post<{id: string}>(`/notification-templates/${id}/versions`).then(created => created.id);
export const templateAction = (id: string, action: 'publish' | 'enable' | 'disable') => post(`/notification-templates/${id}/${action}`);
export const archiveTemplate = (id: string) => del(`/notification-templates/${id}`);

export interface HistoryQuery {
  status?: MessageStatus | '';
  channel?: MessageChannel | '';
  type?: string;
  customerId?: string;
  from?: string;
  to?: string;
}

export const PAGE = 30;

export const listMessages = (query: HistoryQuery, page: number, signal?: AbortSignal) =>
  api<SentMessage[]>('/notifications', {
    query: {
      status: query.status || undefined,
      channel: query.channel || undefined,
      type: query.type || undefined,
      customerId: query.customerId || undefined,
      from: query.from || undefined,
      to: query.to || undefined,
      skip: (page - 1) * PAGE,
      take: PAGE + 1,
    },
    signal,
  });

export const loadMessage = (id: string, signal?: AbortSignal) => api<SentMessage>(`/notifications/${id}`, {signal});
export const cancelMessage = (id: string) => post(`/notifications/${id}/cancel`);

export interface SendInput {
  type: string;
  channel: MessageChannel;
  customerId: string;
  locationId: string | null;
  language: string;
  variables: Record<string, string>;
  scheduledAtUtc: string | null;
  idempotencyKey: string;
}

export const sendMessage = (input: SendInput) => post<{notificationIds: string[]; skipped: SkipReason | null}>('/notifications', input);

/**
 * Le modèle réellement utilisé pour (type, canal, langue), selon la règle du backend : la plus haute version publiée
 * du restaurant si elle est active ; désactivée, le restaurant renonce au sien et la plateforme prend le relais.
 */
export function effectiveTemplate(templates: MessageTemplate[], key: string, channel: MessageChannel, language: string): MessageTemplate | null {
  const pick = (platform: boolean) =>
    templates
      .filter(item => item.isPlatform === platform && item.key === key && item.channel === channel && item.language === language && item.status === 'Published')
      .sort((a, b) => b.version - a.version)[0];
  const own = pick(false);
  if (own?.isEnabled) return own;
  const platform = pick(true);
  return platform?.isEnabled ? platform : null;
}
