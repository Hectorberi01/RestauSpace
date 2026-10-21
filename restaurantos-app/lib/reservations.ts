import {api, post, put} from './api';
import type {DiningArea, TableSession} from './types';
import type {Tone} from './format';

export type ReservationStatus = 'Pending' | 'Confirmed' | 'Seated' | 'Completed' | 'Cancelled' | 'NoShow';

export interface Reservation {
  id: string;
  locationId: string;
  status: ReservationStatus;
  startAtUtc: string;
  durationMinutes: number;
  partySize: number;
  guestName: string | null;
  guestPhone?: string | null;
  guestEmail?: string | null;
  notes: string | null;
  customerId: string | null;
  tableSessionId?: string | null;
  cancelledBy?: 'Guest' | 'Restaurant' | null;
  cancellationReason?: string | null;
  createdAtUtc: string;
  deposit?: ReservationDeposit | null;
}

export type DepositStatus = 'AwaitingPayment' | 'Paid' | 'Applied' | 'RefundRequested' | 'Refunded' | 'Forfeited' | 'Waived';

/** Acompte demandé à la réservation (règle de l'établissement) et ce que ses paiements détiennent. */
export interface ReservationDeposit {
  amount: number;
  currencyCode: string;
  status: DepositStatus;
  dueBeforeUtc: string | null;
  settled: number;
}

export interface Availability {
  startAtUtc: string;
  durationMinutes: number;
  partySize: number;
  openDuringSlot: boolean;
  isAvailable: boolean;
  capacity: number;
  peakBookedSeats: number;
  remainingSeats: number;
}

export interface ReservationPolicy {
  minLeadTimeMinutes: number;
  maxAdvanceDays: number;
  defaultDurationMinutes: number;
  cancellationCutoffMinutes: number;
  minDurationMinutes: number;
  maxDurationMinutes: number;
  maxPartySize: number;
  acceptsOnlineBookings?: boolean;
}

export const reservationStatusLabel: Record<ReservationStatus, string> = {
  Pending: 'À confirmer',
  Confirmed: 'Confirmée',
  Seated: 'Installée',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
  NoShow: 'Absent',
};

export const reservationStatusTone: Record<ReservationStatus, Tone> = {
  Pending: 'amber',
  Confirmed: 'green',
  Seated: 'blue',
  Completed: 'neutral',
  Cancelled: 'red',
  NoShow: 'red',
};

/** Statuts qui occupent des places (même règle que le domaine). */
export function holdsSeats(status: ReservationStatus): boolean {
  return status === 'Pending' || status === 'Confirmed' || status === 'Seated';
}

export type ReservationAction = 'confirm' | 'seat' | 'complete' | 'no-show' | 'cancel';

/** Actions permises par le domaine selon le statut, dans l'ordre d'affichage. */
export function actionsFor(status: ReservationStatus): ReservationAction[] {
  switch (status) {
    case 'Pending':
      return ['confirm', 'cancel'];
    case 'Confirmed':
      return ['seat', 'no-show', 'cancel'];
    case 'Seated':
      return ['complete'];
    default:
      return [];
  }
}

export const actionLabel: Record<ReservationAction, {label: string; success: string}> = {
  confirm: {label: 'Confirmer', success: 'Réservation confirmée.'},
  seat: {label: 'Installer', success: 'Clients installés.'},
  complete: {label: 'Terminer', success: 'Réservation terminée.'},
  'no-show': {label: 'Absent', success: 'Réservation marquée absente.'},
  cancel: {label: 'Annuler', success: 'Réservation annulée.'},
};

export function runAction(id: string, action: ReservationAction, reason?: string, keepDeposit = false) {
  return post(`/reservations/${id}/${action}`, action === 'cancel' ? {reason: reason?.trim() || null, keepDeposit} : undefined);
}

export function listReservations(locationId: string, from: Date, to: Date, signal?: AbortSignal) {
  return api<Reservation[]>('/reservations', {query: {locationId, from: from.toISOString(), to: to.toISOString()}, signal}).then(items =>
    [...items].sort((a, b) => a.startAtUtc.localeCompare(b.startAtUtc)),
  );
}

export function checkAvailability(locationId: string, startAt: Date, partySize: number, durationMinutes?: number, signal?: AbortSignal) {
  return api<Availability>('/reservations/availability', {
    query: {locationId, startAt: startAt.toISOString(), partySize, durationMinutes},
    signal,
  });
}

export function getPolicy(locationId: string, signal?: AbortSignal) {
  return api<ReservationPolicy>(`/reservations/policies/${locationId}`, {signal});
}

export interface NewReservation {
  locationId: string;
  startAt: Date;
  partySize: number;
  guestName: string;
  guestPhone: string | null;
  guestEmail: string | null;
  notes: string | null;
  durationMinutes: number | null;
  customerId?: string | null;
  withoutDeposit?: boolean;
}

export async function createReservation(input: NewReservation): Promise<string> {
  const created = await post<{id: string}>('/reservations', {...input, startAt: input.startAt.toISOString()});
  return created.id;
}

export function reschedule(id: string, startAt: Date, durationMinutes?: number) {
  return put(`/reservations/${id}/slot`, {startAt: startAt.toISOString(), durationMinutes});
}

export function changePartySize(id: string, partySize: number) {
  return put(`/reservations/${id}/party-size`, {partySize});
}

/** Combine une date « AAAA-MM-JJ » et une heure « HH:mm » saisies, en heure locale du navigateur. */
export function localDateTime(date: string, time: string): Date | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !/^\d{2}:\d{2}$/.test(time)) return null;
  const value = new Date(`${date}T${time}:00`);
  return Number.isNaN(value.getTime()) ? null : value;
}

/** Téléphone saisi → E.164 (« +22997000000 ») ; null si vide, undefined si invalide. */
export function normalizePhone(value: string): string | null | undefined {
  const compact = value.replace(/[\s().-]/g, '');
  if (!compact) return null;
  const normalized = compact.startsWith('00') ? `+${compact.slice(2)}` : compact;
  return /^\+[1-9]\d{6,14}$/.test(normalized) ? normalized : undefined;
}

export function hhmm(date: Date): string {
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

export interface FloorTable {
  tableId: string;
  label: string;
  capacity: number;
  areaId: string;
  areaName: string;
}

export interface FloorState {
  areas: DiningArea[];
  sessions: TableSession[];
  bySession: Map<string, FloorTable>;
  byReservation: Map<string, FloorTable>;
  seats: number;
}

/** Salles, tables et sessions ouvertes de l'établissement, indexées par session et par réservation. */
export async function loadFloor(locationId: string, signal?: AbortSignal): Promise<FloorState> {
  const [areas, sessions] = await Promise.all([
    api<DiningArea[]>('/floor/dining-areas', {query: {locationId}, signal}),
    api<TableSession[]>('/floor/table-sessions', {query: {locationId}, signal}),
  ]);
  const tables = new Map<string, FloorTable>(
    areas.flatMap(area =>
      area.tables.map(table => [table.id, {tableId: table.id, label: table.label, capacity: table.capacity, areaId: area.id, areaName: area.name}] as const),
    ),
  );
  const bySession = new Map<string, FloorTable>();
  const byReservation = new Map<string, FloorTable>();
  sessions.forEach(session => {
    const table = tables.get(session.tableId);
    if (!table) return;
    bySession.set(session.id, table);
    if (session.reservationId) byReservation.set(session.reservationId, table);
  });
  return {areas, sessions, bySession, byReservation, seats: areas.reduce((sum, area) => sum + area.seatsInService, 0)};
}

export function tableOf(reservation: Reservation, floor: FloorState | null | undefined): FloorTable | undefined {
  if (!floor) return undefined;
  return (reservation.tableSessionId && floor.bySession.get(reservation.tableSessionId)) || floor.byReservation.get(reservation.id);
}

/**
 * Installe la réservation, à une table si `tableId` est fourni : la session de table est ouverte d'abord (liée à la
 * réservation), puis la réservation est installée sur cette session ; si l'installation échoue, la session est refermée.
 */
export async function seat(reservation: Reservation, tableId: string | null): Promise<void> {
  if (!tableId) {
    await post(`/reservations/${reservation.id}/seat`, {});
    return;
  }
  const session = await post<{id: string}>('/floor/table-sessions', {tableId, guestCount: reservation.partySize, reservationId: reservation.id});
  try {
    await post(`/reservations/${reservation.id}/seat`, {tableSessionId: session.id});
  } catch (error) {
    await post(`/floor/table-sessions/${session.id}/close`).catch(() => undefined);
    throw error;
  }
}

export function reservationNumber(reservation: Reservation): string {
  return `#R-${reservation.id.replace(/-/g, '').slice(0, 6).toUpperCase()}`;
}

const avatarColors = ['bg-emerald-100 text-emerald-700', 'bg-sky-100 text-sky-700', 'bg-violet-100 text-violet-700', 'bg-amber-100 text-amber-800', 'bg-rose-100 text-rose-700', 'bg-teal-100 text-teal-700'];

export function avatarOf(name: string | null): {initials: string; color: string} {
  const value = (name || 'Client').trim();
  const initials = value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase();
  let hash = 0;
  for (const char of value) hash = (hash * 31 + char.charCodeAt(0)) | 0;
  return {initials, color: avatarColors[Math.abs(hash) % avatarColors.length]};
}

export function endOf(reservation: Reservation): Date {
  return new Date(new Date(reservation.startAtUtc).getTime() + reservation.durationMinutes * 60_000);
}

export function durationLabel(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours}h${rest ? String(rest).padStart(2, '0') : ''}` : `${rest} min`;
}

/** Couverts présents en même temps au pire moment de la journée (réservations non annulées). */
export function peakCovers(items: Reservation[]): number {
  const events: [number, number][] = [];
  items
    .filter(item => item.status !== 'Cancelled' && item.status !== 'NoShow')
    .forEach(item => {
      events.push([new Date(item.startAtUtc).getTime(), item.partySize]);
      events.push([endOf(item).getTime(), -item.partySize]);
    });
  events.sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  let current = 0;
  let peak = 0;
  events.forEach(([, delta]) => {
    current += delta;
    peak = Math.max(peak, current);
  });
  return peak;
}
