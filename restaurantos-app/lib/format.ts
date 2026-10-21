import type {LocationStatus, MenuStatus, OrderStatus, OrderType, ProductStatus, TicketPriority, TicketStatus} from './types';

/** Décimales d'une devise : aucune pour le franc CFA, deux sinon. */
export function currencyDigits(currency: string): number {
  return currency === 'XOF' || currency === 'XAF' ? 0 : 2;
}

export function money(amount: number, currency: string): string {
  if (!currency) return new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 2}).format(amount);
  const digits = currencyDigits(currency);
  return new Intl.NumberFormat('fr-FR', {style: 'currency', currency, minimumFractionDigits: digits, maximumFractionDigits: digits}).format(amount);
}

export function number(value: number, digits = 0): string {
  return new Intl.NumberFormat('fr-FR', {maximumFractionDigits: digits}).format(value);
}

export function percent(fraction: number): string {
  return new Intl.NumberFormat('fr-FR', {style: 'percent', maximumFractionDigits: 1}).format(fraction);
}

export function time(iso: string): string {
  return new Intl.DateTimeFormat('fr-FR', {hour: '2-digit', minute: '2-digit'}).format(new Date(iso));
}

export function dateTime(iso: string): string {
  return new Intl.DateTimeFormat('fr-FR', {day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));
}

export function minutesSince(iso: string, now = Date.now()): number {
  return Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60_000));
}

export function elapsed(iso: string, now = Date.now()): string {
  const minutes = minutesSince(iso, now);
  if (minutes < 1) return 'à l’instant';
  if (minutes < 60) return `${minutes} min`;
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')}`;
}

/** « à l’instant », « il y a 4 min », « il y a 2 h », « il y a 3 j ». */
export function ago(iso: string, now = Date.now()): string {
  const minutes = minutesSince(iso, now);
  if (minutes < 1) return 'à l’instant';
  if (minutes < 60) return `il y a ${minutes} min`;
  if (minutes < 24 * 60) return `il y a ${Math.floor(minutes / 60)} h`;
  return `il y a ${Math.floor(minutes / 1440)} j`;
}

/** Montant abrégé pour les axes : « 60 k », « 1,2 M ». */
export function compact(value: number): string {
  return new Intl.NumberFormat('fr-FR', {notation: 'compact', maximumFractionDigits: 1}).format(value);
}

/** Symbole de la devise tel qu'affiché en français (« F CFA », « € »). */
export function currencySymbol(currency: string): string {
  if (!currency) return '';
  return new Intl.NumberFormat('fr-FR', {style: 'currency', currency}).formatToParts(0).find(part => part.type === 'currency')?.value ?? currency;
}

export function isoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function parseAmount(value: string): number | null {
  const normalized = value.replace(/\s/g, '').replace(',', '.');
  if (normalized === '') return null;
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

export const orderStatusLabel: Record<OrderStatus, string> = {
  Draft: 'Brouillon',
  Placed: 'Nouvelle',
  Accepted: 'Acceptée',
  Preparing: 'En préparation',
  Ready: 'Prête',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
};

export const orderTypeLabel: Record<OrderType, string> = {
  DineIn: 'Sur place',
  Takeaway: 'À emporter',
  Delivery: 'Livraison',
};

export const ticketStatusLabel: Record<TicketStatus, string> = {
  Queued: 'En attente',
  InProgress: 'En cours',
  Ready: 'Prêt',
  Completed: 'Servi',
  Cancelled: 'Annulé',
};

export const priorityLabel: Record<TicketPriority, string> = {Normal: 'Normale', High: 'Haute', Rush: 'Urgente'};

export const productStatusLabel: Record<ProductStatus, string> = {
  Draft: 'Brouillon',
  Active: 'En vente',
  Unavailable: 'Indisponible',
  Archived: 'Archivé',
};

export const menuStatusLabel: Record<MenuStatus, string> = {Draft: 'Brouillon', Published: 'Publiée', Archived: 'Archivée'};

export const locationStatusLabel: Record<LocationStatus, string> = {
  Draft: 'Brouillon',
  Active: 'Ouvert',
  TemporarilyClosed: 'Fermé temporairement',
  Archived: 'Archivé',
};

export type Tone = 'neutral' | 'brand' | 'blue' | 'amber' | 'green' | 'red';

export const orderTypeTone: Record<OrderType, Tone> = {DineIn: 'green', Delivery: 'blue', Takeaway: 'amber'};

export const orderTypeColor: Record<OrderType, string> = {DineIn: '#6b3ee6', Delivery: '#3b82f6', Takeaway: '#f59e0b'};

export const orderStatusTone: Record<OrderStatus, Tone> = {
  Draft: 'neutral',
  Placed: 'blue',
  Accepted: 'brand',
  Preparing: 'amber',
  Ready: 'green',
  Completed: 'neutral',
  Cancelled: 'red',
};
