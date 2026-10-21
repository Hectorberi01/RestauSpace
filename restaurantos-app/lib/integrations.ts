import {api, del, post, type Page} from './api';
import type {Tone} from './format';

export type DeliveryStatus = 'Pending' | 'Succeeded' | 'Failed' | 'DeadLetter';

export interface Catalog {
  schemaVersion: number;
  eventTypes: string[];
  apiScopes: string[];
}

export interface ApiCredential {
  id: string;
  name: string;
  scopes: string[];
  status: 'Active' | 'Revoked';
  issuedAtUtc: string;
  expiresAtUtc: string | null;
  rotatedAtUtc: string | null;
  revokedAtUtc: string | null;
  lastUsedAtUtc: string | null;
  isDormant: boolean;
}

export interface WebhookEndpoint {
  id: string;
  url: string;
  urlFingerprint: string;
  events: string[];
  status: 'Active' | 'Disabled';
  createdAtUtc: string;
  secretRotatedAtUtc: string | null;
  previousSecretValidUntilUtc: string | null;
  consecutiveDeadLetters: number;
  autoDisabledAtUtc: string | null;
}

export interface Delivery {
  id: string;
  endpointId: string;
  eventId: string;
  eventType: string;
  status: DeliveryStatus;
  attemptCount: number;
  maxAttempts: number;
  nextAttemptAtUtc: string | null;
  lastFailureReason: string | null;
  createdAtUtc: string;
  succeededAtUtc: string | null;
  deadLetteredAtUtc: string | null;
}

export const eventLabel: Record<string, string> = {
  'orders.order_placed': 'Commande passée',
  'orders.order_completed': 'Commande terminée',
  'orders.order_cancelled': 'Commande annulée',
  'payments.payment_captured': 'Paiement encaissé',
  'payments.refund_succeeded': 'Remboursement effectué',
  'reservations.reservation_created': 'Réservation créée',
  'reservations.reservation_confirmed': 'Réservation confirmée',
  'reservations.reservation_cancelled': 'Réservation annulée',
  'reservations.reservation_no_show': 'Client absent (réservation)',
  'kitchen.ticket_ready': 'Ticket cuisine prêt',
  'inventory.low_stock_reached': 'Stock bas atteint',
  'catalog.changed': 'Catalogue modifié',
  'integrations.ping': 'Test',
};

export const deliveryLabel: Record<DeliveryStatus, string> = {Pending: 'En attente', Succeeded: 'Livré', Failed: 'Nouvel essai prévu', DeadLetter: 'Abandonné'};
export const deliveryTone: Record<DeliveryStatus, Tone> = {Pending: 'neutral', Succeeded: 'green', Failed: 'amber', DeadLetter: 'red'};

export const loadCatalog = (signal?: AbortSignal) => api<Catalog>('/integrations/catalog', {signal});

export const listKeys = (signal?: AbortSignal) => api<ApiCredential[]>('/integrations/api-keys', {signal});

/** La clé en clair n'est renvoyée qu'une fois : elle n'est ni stockée ni journalisée côté console. */
export const issueKey = (name: string, scopes: string[], expiresAtUtc: string | null) =>
  post<{credentialId: string; key: string}>('/integrations/api-keys', {name, scopes, expiresAtUtc});

export const rotateKey = (id: string) => post<{credentialId: string; key: string}>(`/integrations/api-keys/${id}/rotate`);

export const revokeKey = (id: string) => post(`/integrations/api-keys/${id}/revoke`);

export const listEndpoints = (signal?: AbortSignal) => api<WebhookEndpoint[]>('/integrations/webhooks', {signal});

/** Le secret de signature n'est renvoyé qu'une fois. */
export const createEndpoint = (url: string, events: string[]) =>
  post<{endpointId: string; secret: string}>('/integrations/webhooks', {url, events});

export const subscribeEvent = (id: string, eventType: string) => post(`/integrations/webhooks/${id}/events`, {eventType});

export const unsubscribeEvent = (id: string, eventType: string) => del(`/integrations/webhooks/${id}/events/${encodeURIComponent(eventType)}`);

export const setEndpointEnabled = (id: string, enabled: boolean) => post(`/integrations/webhooks/${id}/${enabled ? 'enable' : 'disable'}`);

export const rotateSecret = (id: string, immediate: boolean) =>
  api<{endpointId: string; secret: string}>(`/integrations/webhooks/${id}/rotate-secret`, {method: 'POST', query: {immediate: immediate || undefined}});

export const testEndpoint = (id: string) => post<{eventId: string; succeeded: boolean; failureReason: string | null}>(`/integrations/webhooks/${id}/test`);

export const listDeliveries = (id: string, status: DeliveryStatus | '', page: number, signal?: AbortSignal) =>
  api<Page<Delivery>>(`/integrations/webhooks/${id}/deliveries`, {query: {status: status || undefined, page, pageSize: 15}, signal});

export const loadDelivery = (id: string, signal?: AbortSignal) =>
  api<{delivery: Delivery; schemaVersion: number; body: string}>(`/integrations/webhook-deliveries/${id}`, {signal});

export const retryDelivery = (id: string) => post<{id: string}>(`/integrations/webhook-deliveries/${id}/retry`);
