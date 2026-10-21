import {api, post, put} from './api';
import type {Tone} from './format';

export type WaitlistStatus = 'Waiting' | 'Offered' | 'Booked' | 'Expired' | 'Cancelled';

/** Un client inscrit sur un créneau complet. */
export interface WaitlistEntry {
  id: string;
  locationId: string;
  status: WaitlistStatus;
  source: 'Online' | 'Staff';
  desiredStartUtc: string;
  flexibilityMinutes: number;
  partySize: number;
  guestName: string;
  guestPhone: string | null;
  guestEmail: string | null;
  notes: string | null;
  offeredStartUtc: string | null;
  offerExpiresAtUtc: string | null;
  missedOffers: number;
  reservationId: string | null;
  createdAtUtc: string;
}

export interface WaitlistSettings {
  acceptsOnline: boolean;
  offerWindowMinutes: number;
}

export const waitlistStatusLabel: Record<WaitlistStatus, string> = {
  Waiting: 'En attente',
  Offered: 'Place proposée',
  Booked: 'Réservé',
  Expired: 'Expiré',
  Cancelled: 'Retiré',
};

export const waitlistStatusTone: Record<WaitlistStatus, Tone> = {
  Waiting: 'amber',
  Offered: 'blue',
  Booked: 'green',
  Expired: 'neutral',
  Cancelled: 'neutral',
};

export const listWaitlist = (locationId: string, from: Date, to: Date, signal?: AbortSignal) =>
  api<WaitlistEntry[]>('/reservations/waitlist', {query: {locationId, from: from.toISOString(), to: to.toISOString()}, signal});

export const joinWaitlist = (input: {
  locationId: string; desiredStart: Date; partySize: number; flexibilityMinutes: number; guestName: string; guestPhone: string; notes: string | null;
}) => post<{id: string}>('/reservations/waitlist', {...input, desiredStart: input.desiredStart.toISOString()});

export const cancelWaitlistEntry = (id: string) => post(`/reservations/waitlist/${id}/cancel`);

export const bookWaitlistEntry = (id: string, startAt: Date) => post<{id: string}>(`/reservations/waitlist/${id}/book`, {startAt: startAt.toISOString()});

export const offerFreedTables = (locationId: string) => api<{offered: number}>('/reservations/waitlist/offers', {method: 'POST', query: {locationId}});

export const waitlistLink = (id: string, signal?: AbortSignal) => api<{url: string | null}>(`/online-sales/waitlist/${id}/link`, {signal});

export const getWaitlistSettings = (locationId: string, signal?: AbortSignal) => api<WaitlistSettings>(`/reservations/waitlist-settings/${locationId}`, {signal});

export const setWaitlistSettings = (locationId: string, settings: WaitlistSettings) => put(`/reservations/waitlist-settings/${locationId}`, settings);

// ---- File d'attente à la porte (module Salle).

export type WaitingPartyStatus = 'Waiting' | 'Notified' | 'Seated' | 'Left';

export interface WaitingParty {
  id: string;
  locationId: string;
  name: string;
  hasPhone: boolean;
  phone: string | null;
  partySize: number;
  note: string | null;
  quotedMinutes: number | null;
  status: WaitingPartyStatus;
  position: number | null;
  createdAtUtc: string;
  notifiedAtUtc: string | null;
  notificationCount: number;
  closedAtUtc: string | null;
  tableSessionId: string | null;
}

export const listWaitingParties = (locationId: string, signal?: AbortSignal) => api<WaitingParty[]>('/floor/waiting-parties', {query: {locationId}, signal});

export const addWaitingParty = (input: {locationId: string; name: string; partySize: number; phone: string | null; note: string | null; quotedMinutes: number | null}) =>
  post<{id: string}>('/floor/waiting-parties', input);

export const notifyWaitingParty = (id: string) => post(`/floor/waiting-parties/${id}/notify`);

export const seatWaitingParty = (id: string, tableId: string) => post<{id: string}>(`/floor/waiting-parties/${id}/seat`, {tableId});

export const removeWaitingParty = (id: string) => post(`/floor/waiting-parties/${id}/remove`);

/** Minutes écoulées depuis l'arrivée. */
export const waitedMinutes = (party: WaitingParty, now: number) => Math.max(0, Math.round((now - new Date(party.createdAtUtc).getTime()) / 60_000));
