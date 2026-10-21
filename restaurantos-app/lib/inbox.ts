import {api, post, put, type Page} from './api';

export type InboxState = 'Unread' | 'Read' | 'Archived';
export type Purpose = 'Transactional' | 'Marketing';
export type Channel = 'Email' | 'Sms' | 'WhatsApp' | 'Push' | 'InApp';

export interface InboxItem {
  id: string;
  type: string;
  priority: 'Critical' | 'High' | 'Normal' | 'Low';
  title: string | null;
  body: string;
  state: InboxState;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface PreferenceChoice {
  purpose: Purpose;
  channel: Channel;
  isAllowed: boolean;
  changedAtUtc: string | null;
  source: string | null;
}

export interface AlertTypeChoice {
  type: string;
  isEnabled: boolean;
}

/** Les alertes d'équipe que chacun peut couper pour lui-même. */
export const alertTypeLabel: Record<string, {title: string; hint: string}> = {
  'staff.new_reservation': {title: 'Nouvelle réservation', hint: 'Une table est réservée.'},
  'staff.order_cancelled': {title: 'Commande annulée', hint: 'Une commande est annulée après acceptation.'},
  'staff.payment_failed': {title: 'Paiement refusé', hint: 'Un paiement en ligne échoue.'},
  'staff.low_stock': {title: 'Stock bas', hint: 'Un article passe sous son seuil de réapprovisionnement.'},
  'staff.new_review': {title: 'Nouvel avis client', hint: 'Un client laisse une note ou un commentaire.'},
  'staff.report_ready': {title: 'Rapport prêt', hint: 'Un export ou un rapport planifié est disponible.'},
  'staff.webhook_disabled': {title: 'Webhook désactivé', hint: 'Une intégration ne répond plus et a été coupée.'},
  'staff.schedule_published': {title: 'Planning publié', hint: 'Vos créneaux de la semaine ont changé.'},
  'staff.time_off_requested': {title: 'Demande d’absence', hint: 'Un membre de l’équipe demande une absence.'},
  'staff.time_off_decided': {title: 'Réponse à votre absence', hint: 'Le gérant a répondu à votre demande.'},
  'staff.shift_swap': {title: 'Remplacement', hint: 'Un échange de créneau avance.'},
};

export const channelLabel: Record<Channel, string> = {InApp: 'Dans la console', Email: 'Email', Sms: 'SMS', WhatsApp: 'WhatsApp', Push: 'Notifications push'};
export const purposeLabel: Record<Purpose, {title: string; hint: string}> = {
  Transactional: {title: 'Alertes de service', hint: 'Nouvelles commandes, réservations, sécurité du compte, incidents.'},
  Marketing: {title: 'Nouveautés et conseils', hint: 'Nouvelles fonctionnalités, astuces, offres RestauSpace.'},
};

export const listInbox = (includeArchived: boolean, page: number, signal?: AbortSignal) =>
  api<Page<InboxItem>>('/me/notifications', {query: {includeArchived: includeArchived || undefined, page, pageSize: 20}, signal});

export const markRead = (id: string) => post(`/me/notifications/${id}/read`);

export const archive = (id: string) => post(`/me/notifications/${id}/archive`);

export const loadPreferences = (signal?: AbortSignal) =>
  api<{owner: string; choices: PreferenceChoice[]; alerts?: AlertTypeChoice[]}>('/me/notification-preferences', {signal});

export const setAlertType = (type: string, isEnabled: boolean) =>
  put(`/me/notification-preferences/alerts/${encodeURIComponent(type)}`, {isEnabled, source: 'console'});

export const setPreference = (purpose: Purpose, channel: Channel, isAllowed: boolean) =>
  put('/me/notification-preferences', {purpose, channel, isAllowed, source: 'console'});
