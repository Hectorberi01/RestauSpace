import {api, del, post, put, type Page} from './api';
import type {Reservation} from './reservations';
import type {OrderSummary} from './types';

export type Channel = 'Email' | 'Sms' | 'WhatsApp' | 'Push';
export type ContactRequirement = 'None' | 'EmailOrPhone' | 'Email' | 'Phone';
export type WithdrawalReason = 'CustomerRequest' | 'UnsubscribeLink' | 'InvalidAddress' | 'CustomerAnonymized' | 'Other';

export interface Customer {
  id: string;
  name: string;
  email: string | null;
  phone: string | null;
  status: 'Active' | 'Anonymized' | 'Merged';
  isAnonymized: boolean;
  contactRequirement: ContactRequirement;
  tags: string[];
  preferences: {language: string; marketingChannels: Channel[]} | null;
  createdAtUtc: string;
  anonymizedAtUtc: string | null;
  /** La fiche qui a absorbé ce doublon. */
  mergedIntoId: string | null;
}

export interface ConsentChange {
  status: 'Granted' | 'Withdrawn';
  occurredAtUtc: string;
  source: string;
  reason: WithdrawalReason | null;
}

export interface Consent {
  id: string;
  channel: Channel;
  status: 'Granted' | 'Withdrawn';
  isGranted: boolean;
  grantedAtUtc: string;
  source: string;
  withdrawnAtUtc: string | null;
  withdrawalSource: string | null;
  withdrawalReason: WithdrawalReason | null;
  history: ConsentChange[];
}

export const channels: {value: Channel; label: string}[] = [
  {value: 'Email', label: 'Email'},
  {value: 'Sms', label: 'SMS'},
  {value: 'WhatsApp', label: 'WhatsApp'},
  {value: 'Push', label: 'Notification push'},
];

export const channelLabel = (channel: Channel) => channels.find(item => item.value === channel)?.label ?? channel;

export const requirementLabel: Record<ContactRequirement, string> = {
  None: 'Aucune coordonnée exigée',
  EmailOrPhone: 'Un email ou un téléphone',
  Email: 'Un email',
  Phone: 'Un téléphone',
};

export const languages: {value: string; label: string}[] = [
  {value: 'fr', label: 'Français'},
  {value: 'en', label: 'Anglais'},
  {value: 'es', label: 'Espagnol'},
  {value: 'pt', label: 'Portugais'},
  {value: 'de', label: 'Allemand'},
  {value: 'it', label: 'Italien'},
  {value: 'nl', label: 'Néerlandais'},
  {value: 'ar', label: 'Arabe'},
];

export const languageLabel = (code: string) => languages.find(item => item.value === code.slice(0, 2))?.label ?? code;

/** Codes courts de la preuve de consentement (minuscules, sans donnée personnelle). */
export const consentSources: {value: string; label: string}[] = [
  {value: 'back-office', label: 'Saisi dans la console'},
  {value: 'pos', label: 'Au comptoir / en salle'},
  {value: 'phone', label: 'Par téléphone'},
  {value: 'paper-form', label: 'Formulaire papier'},
];

export const sourceLabel = (source: string) =>
  consentSources.find(item => item.value === source)?.label
  ?? ({'system.customer-anonymization': 'Anonymisation du client', 'unsubscribe-link': 'Lien de désinscription'} as Record<string, string>)[source]
  ?? source;

export const withdrawalReasons: {value: WithdrawalReason; label: string}[] = [
  {value: 'CustomerRequest', label: 'Demande du client'},
  {value: 'UnsubscribeLink', label: 'Lien de désinscription'},
  {value: 'InvalidAddress', label: 'Coordonnée invalide'},
  {value: 'Other', label: 'Autre'},
];

export const withdrawalLabel = (reason: WithdrawalReason | null) =>
  reason === 'CustomerAnonymized' ? 'Client anonymisé' : withdrawalReasons.find(item => item.value === reason)?.label ?? '';

/** Une saisie libre : « +… » = téléphone (format international, recherche exacte), « @ » = email exact, sinon nom. */
export function searchQuery(text: string, tag: string, includeAnonymized: boolean, page: number) {
  const value = text.trim();
  const phone = value.startsWith('+');
  return {
    name: value && !phone && !value.includes('@') ? value : undefined,
    email: value.includes('@') ? value : undefined,
    phone: phone ? value.replace(/[\s.\-()]/g, '') : undefined,
    tag: tag || undefined,
    includeAnonymized: includeAnonymized || undefined,
    page,
    pageSize: 25,
  };
}

export const searchCustomers = (query: ReturnType<typeof searchQuery>, signal?: AbortSignal) =>
  api<Page<Customer>>('/customers', {query, signal});

export const loadCustomer = (id: string, signal?: AbortSignal) => api<Customer>(`/customers/${id}`, {signal});

export const loadConsents = (id: string, signal?: AbortSignal) => api<Consent[]>(`/customers/${id}/consents`, {signal});

export const createCustomer = (body: {name: string; email: string | null; phone: string | null; tags: string[]; preferences: {language: string; marketingChannels: Channel[]} | null}) =>
  post<{id: string}>('/customers', body);

export const renameCustomer = (id: string, name: string) => put(`/customers/${id}/name`, {name});

export const changeContact = (id: string, email: string | null, phone: string | null) => put(`/customers/${id}/contact`, {email, phone});

export const applyContactPolicy = (id: string) => post(`/customers/${id}/contact-policy/apply`);

export const addTag = (id: string, tag: string) => post(`/customers/${id}/tags`, {tag});

export const removeTag = (id: string, tag: string) => del(`/customers/${id}/tags?tag=${encodeURIComponent(tag)}`);

export const setPreferences = (id: string, language: string, marketingChannels: Channel[]) =>
  put(`/customers/${id}/preferences`, {language, marketingChannels});

export const anonymize = (id: string) => post(`/customers/${id}/anonymize`);

export const grantConsent = (id: string, channel: Channel, source: string) => post(`/customers/${id}/consents/grant`, {channel, source});

export const withdrawConsent = (id: string, channel: Channel, reason: WithdrawalReason, source: string) =>
  post(`/customers/${id}/consents/withdraw`, {channel, reason, source});

export const loadContactPolicy = (signal?: AbortSignal) => api<{requirement: ContactRequirement}>('/customers/settings/contact-policy', {signal});

export const setContactPolicy = (requirement: ContactRequirement) => put('/customers/settings/contact-policy', {requirement});

export const customerOrders = (id: string, page: number, signal?: AbortSignal) =>
  api<Page<OrderSummary>>(`/orders/by-customer/${id}`, {query: {page, pageSize: 10}, signal});

export const customerReservations = (id: string, page: number, signal?: AbortSignal) =>
  api<Page<Reservation>>(`/reservations/by-customer/${id}`, {query: {page, pageSize: 10}, signal});

export function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase() || '?';
}

export const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));

export const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

export const setOrderCustomer = (orderId: string, customerId: string | null) => put(`/orders/${orderId}/customer`, {customerId});

export const setReservationCustomer = (reservationId: string, customerId: string | null) =>
  put(`/reservations/${reservationId}/customer`, {customerId});

export interface CustomerNote {
  id: string;
  text: string;
  authorId: string | null;
  createdAtUtc: string;
}

export const loadNotes = (id: string, signal?: AbortSignal) => api<CustomerNote[]>(`/customers/${id}/notes`, {signal});

export const addNote = (id: string, text: string) => post<{id: string}>(`/customers/${id}/notes`, {text});

export const deleteNote = (id: string, noteId: string) => del(`/customers/${id}/notes/${noteId}`);

export const mergeCustomer = (id: string, duplicateId: string) => post(`/customers/${id}/merge`, {duplicateId});

export interface CustomerExport {
  exportedAtUtc: string;
  customer: Customer;
  consents: Consent[];
  notes: CustomerNote[];
}

export const exportCustomer = (id: string) => api<CustomerExport>(`/customers/${id}/export`);

export interface ImportRow {
  name: string | null;
  email: string | null;
  phone: string | null;
  tags: string[];
  marketingChannels: Channel[];
}

export type ImportOutcome = 'Created' | 'Updated' | 'Skipped' | 'Invalid';

export interface ImportResult {
  created: number;
  updated: number;
  skipped: number;
  invalid: number;
  lines: {row: number; outcome: ImportOutcome; customerId: string | null; error: string | null}[];
}

export const importCustomers = (rows: ImportRow[], consentCertified: boolean) =>
  post<ImportResult>('/customers/import', {rows, consentCertified, consentSource: consentCertified ? 'import' : null});
