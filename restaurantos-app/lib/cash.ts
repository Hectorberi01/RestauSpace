import {api, post} from './api';
import type {PaymentMethod} from './payments';

export type CashSessionStatus = 'Open' | 'Closed';
export type CashMovementKind = 'PayIn' | 'PayOut';

export interface CashMovement {
  kind: CashMovementKind;
  amount: number;
  reason: string;
  atUtc: string;
  by: string | null;
}

/** Encaissements d'un moyen pendant la session ; `toCount` : moyen compté à la clôture. */
export interface CashTakings {
  method: PaymentMethod;
  toCount: boolean;
  collected: number;
  refunded: number;
  net: number;
}

export interface CashCountLine {
  method: PaymentMethod;
  expected: number;
  counted: number;
  difference: number;
}

export interface CashSession {
  id: string;
  locationId: string;
  number: number;
  status: CashSessionStatus;
  currencyCode: string;
  openingFloat: number;
  openedAtUtc: string;
  openedBy: string | null;
  closedAtUtc: string | null;
  closedBy: string | null;
  closingNote: string | null;
  paidIn: number;
  paidOut: number;
  expectedCash: number;
  totalDifference: number;
  movements: CashMovement[];
  takings: CashTakings[];
  lines: CashCountLine[];
  denominations: {value: number; count: number}[];
}

export interface CashSessionSummary {
  id: string;
  locationId: string;
  number: number;
  status: CashSessionStatus;
  currencyCode: string;
  openingFloat: number;
  openedAtUtc: string;
  closedAtUtc: string | null;
  totalDifference: number;
  countedCash: number | null;
}

export interface JournalEntry {
  paymentId: string;
  orderId: string;
  locationId: string;
  kind: 'Capture' | 'Refund';
  provider: string;
  method: PaymentMethod;
  toCount: boolean;
  currencyCode: string;
  amount: number;
  atUtc: string;
  reference: string | null;
}

export interface PaymentJournal {
  fromUtc: string;
  toUtc: string;
  entries: JournalEntry[];
  totals: CashTakings[];
  truncated: boolean;
}

/** Billets et pièces du franc CFA (UEMOA) ; pour une autre devise, seul le montant total se saisit. */
export const denominations: Record<string, {bills: number[]; coins: number[]}> = {
  XOF: {bills: [10000, 5000, 2000, 1000, 500], coins: [500, 250, 200, 100, 50, 25, 10, 5]},
};

/** Valeur unique par coupure : la pièce et le billet de 500 F se comptent ensemble. */
export const denominationValues = (currency: string) => [...new Set([...(denominations[currency]?.bills ?? []), ...(denominations[currency]?.coins ?? [])])];

export const openSession = (locationId: string, signal?: AbortSignal) =>
  api<CashSession | undefined>('/payments/cash-sessions/open', {query: {locationId}, signal}).then(session => session ?? null);

export const getSession = (id: string, signal?: AbortSignal) => api<CashSession>(`/payments/cash-sessions/${id}`, {signal});

export const listSessions = (locationId: string, signal?: AbortSignal) =>
  api<CashSessionSummary[]>('/payments/cash-sessions', {query: {locationId, limit: 60}, signal});

export const sessionsIn = (locationId: string | null, from: Date, to: Date, signal?: AbortSignal) =>
  api<CashSessionSummary[]>('/payments/cash-sessions', {query: {...(locationId ? {locationId} : {}), from: from.toISOString(), to: to.toISOString(), limit: 1000}, signal});

export const openTill = (locationId: string, openingFloat: number) =>
  post<{id: string}>('/payments/cash-sessions', {locationId, openingFloat});

export const recordMovement = (id: string, kind: CashMovementKind, amount: number, reason: string) =>
  post(`/payments/cash-sessions/${id}/movements`, {kind, amount, reason});

export const closeTill = (
  id: string,
  counted: {method: PaymentMethod; amount: number}[],
  coins: {value: number; count: number}[] | null,
  note: string,
) => post(`/payments/cash-sessions/${id}/close`, {counted, denominations: coins, note: note.trim() || null});

export const journal = (locationId: string | null, from: Date, to: Date, signal?: AbortSignal) =>
  api<PaymentJournal>('/payments/journal', {
    query: {...(locationId ? {locationId} : {}), from: from.toISOString(), to: to.toISOString(), limit: 1000},
    signal,
  });

export const movementLabel: Record<CashMovementKind, string> = {PayIn: 'Entrée', PayOut: 'Sortie'};
