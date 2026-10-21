import {api, post, put} from './api';

export type TaxRegime = 'Vat' | 'Tps';
export type AibRate = 'None' | 'A' | 'B';
export type FiscalMode = 'Disabled' | 'Sandbox' | 'Production';
export type FiscalInvoiceStatus = 'Pending' | 'Submitted' | 'Certified' | 'Failed';

/** Le jeton e-MECeF n'est jamais renvoyé : seulement le fait qu'il existe. */
export interface FiscalProfile {
  ifu: string;
  rccm: string | null;
  regime: TaxRegime;
  aib: AibRate;
  mode: FiscalMode;
  hasToken: boolean;
  tokenUpdatedAtUtc: string | null;
  updatedAtUtc: string;
}

export interface FiscalInvoice {
  id: string;
  orderId: string;
  locationId: string;
  orderNumber: string;
  kind: 'Sale' | 'CreditNote';
  mode: FiscalMode;
  status: FiscalInvoiceStatus;
  total: number;
  attempts: number;
  nextAttemptAtUtc: string | null;
  codeMecef: string | null;
  qrCode: string | null;
  counters: string | null;
  nim: string | null;
  dgiDateTime: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  createdAtUtc: string;
  certifiedAtUtc: string | null;
}

export interface OrderFiscal {
  ifu: string;
  rccm: string | null;
  sale: FiscalInvoice | null;
}

export interface FiscalConnection {
  testedMode: FiscalMode;
  ifuMatches: boolean;
  nim: string | null;
  tokenValidUntil: string | null;
}

export const loadProfile = (signal?: AbortSignal) => api<FiscalProfile | undefined>('/fiscal/profile', {signal}).then(profile => profile ?? null);
export const saveProfile = (input: {ifu: string; rccm: string | null; regime: TaxRegime; aib: AibRate}) => put('/fiscal/profile', input);
export const saveToken = (token: string) => put('/fiscal/profile/token', {token});
export const setMode = (mode: FiscalMode) => put('/fiscal/profile/mode', {mode});
export const testConnection = () => post<FiscalConnection>('/fiscal/profile/test');
export const listInvoices = (status: FiscalInvoiceStatus | null, signal?: AbortSignal) =>
  api<FiscalInvoice[]>('/fiscal/invoices', {query: {...(status ? {status} : {}), take: 100}, signal});
export const retryInvoice = (id: string) => post(`/fiscal/invoices/${id}/retry`);
/** Mentions fiscales du reçu d'une commande ; null si le restaurant n'a pas de profil fiscal. */
export const orderFiscal = (orderId: string, signal?: AbortSignal) =>
  api<OrderFiscal | undefined>(`/fiscal/orders/${orderId}`, {signal}).then(value => value ?? null).catch(() => null);

export const modeLabel: Record<FiscalMode, string> = {Disabled: 'Désactivée', Sandbox: 'Bac à sable (test DGI)', Production: 'Production'};
export const regimeLabel: Record<TaxRegime, string> = {Vat: 'TVA (groupe B, 18 %)', Tps: 'TPS (groupe E)'};
export const aibLabel: Record<AibRate, string> = {None: 'Aucun', A: 'AIB 1 %', B: 'AIB 5 %'};
export const invoiceStatusLabel: Record<FiscalInvoiceStatus, string> = {Pending: 'En file', Submitted: 'Envoyée', Certified: 'Certifiée', Failed: 'Échec'};

/** Lignes des mentions fiscales (reçu à l'écran et imprimé). */
export function fiscalFooter(info: OrderFiscal | null): {lines: string[]; qr: string | null} | null {
  if (!info) return null;
  const lines = [`IFU : ${info.ifu}`];
  if (info.rccm) lines.push(`RCCM : ${info.rccm}`);
  const sale = info.sale;
  if (sale?.status === 'Certified') {
    lines.push('FACTURE NORMALISÉE', `Code MECeF/DGI : ${sale.codeMecef}`, `MECeF NIM : ${sale.nim}`, `MECeF Compteurs : ${sale.counters}`, `MECeF Heure : ${sale.dgiDateTime}`);
    return {lines, qr: sale.qrCode};
  }
  if (sale) lines.push('Facture normalisée en cours de certification');
  return {lines, qr: null};
}
