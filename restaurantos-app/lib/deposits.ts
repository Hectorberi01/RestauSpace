import {api, post, put} from './api';
import type {Tone} from './format';
import type {DepositStatus, Reservation} from './reservations';

export type DepositMode = 'Off' | 'FromPartySize' | 'Always';
export type DepositBasis = 'PerGuest' | 'PerReservation';

/** Règle d'acompte d'un établissement. */
export interface DepositRule {
  mode: DepositMode;
  basis: DepositBasis;
  amount: number | null;
  currencyCode: string | null;
  minPartySize: number;
  paymentWindowMinutes: number;
}

export const depositStatusLabel: Record<DepositStatus, string> = {
  AwaitingPayment: 'Acompte à payer',
  Paid: 'Acompte payé',
  Applied: 'Acompte déduit',
  RefundRequested: 'Remboursement en cours',
  Refunded: 'Acompte remboursé',
  Forfeited: 'Acompte conservé',
  Waived: 'Acompte dispensé',
};

export const depositStatusTone: Record<DepositStatus, Tone> = {
  AwaitingPayment: 'amber',
  Paid: 'green',
  Applied: 'blue',
  RefundRequested: 'amber',
  Refunded: 'neutral',
  Forfeited: 'red',
  Waived: 'neutral',
};

export interface DepositPayment {
  id: string;
  provider: string;
  method: string;
  status: string;
  amount: number;
  capturedAmount: number;
  refundedAmount: number;
  refundableAmount: number;
  appliedToOrderId: string | null;
  createdAtUtc: string;
}

export interface DepositPayments {
  settled: number;
  hasPending: boolean;
  isApplied: boolean;
  payments: DepositPayment[];
}

export const getDepositRule = (locationId: string, signal?: AbortSignal) => api<DepositRule>(`/reservations/deposit-rules/${locationId}`, {signal});

export const setDepositRule = (locationId: string, rule: Omit<DepositRule, 'currencyCode'>) => put(`/reservations/deposit-rules/${locationId}`, rule);

/** L'acompte que la règle demanderait pour ce groupe ; null sans acompte. */
export function depositFor(rule: DepositRule | null | undefined, partySize: number): number | null {
  if (!rule || rule.mode === 'Off' || !rule.amount || partySize < 1) return null;
  if (rule.mode === 'FromPartySize' && partySize < rule.minPartySize) return null;
  return rule.basis === 'PerGuest' ? rule.amount * partySize : rule.amount;
}

export const waiveDeposit = (reservationId: string) => post(`/reservations/${reservationId}/deposit/waive`);

export const refundDeposit = (reservationId: string) => post(`/reservations/${reservationId}/deposit/refund`);

export const depositPayments = (reservationId: string, signal?: AbortSignal) => api<DepositPayments>(`/payments/deposits/${reservationId}`, {signal});

/** L'équipe a reçu l'acompte (espèces, mobile money…) : le montant est celui de la réservation. */
export const recordDeposit = (reservationId: string, provider: string, reference: string, paymentMethod: string | null) =>
  api(`/payments/deposits/${reservationId}/offline`, {
    method: 'POST',
    body: {provider, reference, paymentMethod},
    idempotencyKey: `deposit-${reservationId}-${reference}`,
  });

/** Déduit l'acompte payé de l'addition (même établissement). */
export const applyDeposit = (reservationId: string, orderId: string) => post<{applied: number}>(`/payments/deposits/${reservationId}/apply`, {orderId});

/** Lien de gestion de la réservation (paiement de l'acompte) sur le site du restaurant ; null sans site. */
export const reservationLink = (reservationId: string, signal?: AbortSignal) =>
  api<{url: string | null}>(`/online-sales/reservations/${reservationId}/link`, {signal});

/** Une réservation dont l'acompte payé peut encore être déduit d'une addition. */
export const hasDeductibleDeposit = (reservation: Reservation) => reservation.deposit?.status === 'Paid';
