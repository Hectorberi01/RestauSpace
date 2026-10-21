import {API_URL, api, post, put} from './api';

export type PrinterProtocol = 'StarCloudPrnt' | 'EscPosRelay';
export type PrintJobKind = 'CustomerTicket' | 'TestPage' | 'KitchenTicket' | 'OrderReceipt';
export type PrintJobStatus = 'Pending' | 'Sent' | 'Printed' | 'Failed' | 'Expired';

export interface Printer {
  id: string;
  locationId: string;
  name: string;
  protocol: PrinterProtocol;
  printsCustomerTickets: boolean;
  printsKitchenTickets: boolean;
  kitchenStationId: string | null;
  status: 'Active' | 'Disabled';
  tokenHint: string;
  lastSeenAtUtc: string | null;
  isOnline: boolean;
  createdAtUtc: string;
}

export interface PrintJob {
  id: string;
  printerId: string;
  locationId: string;
  kind: PrintJobKind;
  reference: string;
  status: PrintJobStatus;
  attempts: number;
  createdAtUtc: string;
  sentAtUtc: string | null;
  completedAtUtc: string | null;
  failureCode: string | null;
}

export const protocolLabel: Record<PrinterProtocol, string> = {StarCloudPrnt: 'Star CloudPRNT', EscPosRelay: 'ESC/POS par relais'};

export const protocolHint: Record<PrinterProtocol, string> = {
  StarCloudPrnt: 'Star mC-Print3, TSP143IV, TSP654II CloudPRNT… : l’imprimante interroge directement RestauSpace par Internet.',
  EscPosRelay: 'Epson et compatibles sur le réseau local : un petit relais (un ordinateur ou un Raspberry Pi) transmet les tickets.',
};

export const jobKindLabel: Record<PrintJobKind, string> = {
  CustomerTicket: 'Ticket borne',
  TestPage: 'Page de test',
  KitchenTicket: 'Bon de cuisine',
  OrderReceipt: 'Reçu',
};

export const jobStatusLabel: Record<PrintJobStatus, string> = {
  Pending: 'En file',
  Sent: 'Envoyé',
  Printed: 'Imprimé',
  Failed: 'Échec',
  Expired: 'Expiré',
};

/** L'adresse à saisir dans l'imprimante (CloudPRNT) ou dans le relais : elle contient le jeton, à ne pas diffuser. */
export const deviceUrl = (protocol: PrinterProtocol, token: string) =>
  protocol === 'StarCloudPrnt' ? `${API_URL}/v1/printing/cloudprnt/${token}` : `${API_URL}/v1/printing/relay/${token}`;

export const listPrinters = (signal?: AbortSignal) => api<Printer[]>('/printing/printers', {signal});
export const listPrintJobs = (signal?: AbortSignal) => api<PrintJob[]>('/printing/jobs?take=30', {signal});
export const createPrinter = (input: {locationId: string; name: string; protocol: PrinterProtocol; printsCustomerTickets: boolean}) =>
  post<{id: string; token: string}>('/printing/printers', input);
export const updatePrinter = (id: string, input: {name: string; printsCustomerTickets: boolean}) => put(`/printing/printers/${id}`, input);
export const rotatePrinterToken = (id: string) => post<{id: string; token: string}>(`/printing/printers/${id}/token`);
export const disablePrinter = (id: string) => post(`/printing/printers/${id}/disable`);
export const enablePrinter = (id: string) => post(`/printing/printers/${id}/enable`);
export const printTestPage = (id: string) => post<{jobId: string}>(`/printing/printers/${id}/test`);
export const reprintJob = (id: string) => post<{jobId: string}>(`/printing/jobs/${id}/reprint`);
export const setKitchenRole = (id: string, printsKitchenTickets: boolean, kitchenStationId: string | null) =>
  put(`/printing/printers/${id}/kitchen`, {printsKitchenTickets, kitchenStationId: printsKitchenTickets ? kitchenStationId : null});
/** Reçu d'une commande sur l'imprimante de tickets de son établissement (ou celle donnée). */
export const printReceipt = (orderId: string, printerId?: string) => post<{jobId: string}>(`/printing/receipts/${orderId}`, {printerId: printerId ?? null});
