import {API_URL, api, post, put} from './api';

export type SalesAppKind = 'Web' | 'MobileApp' | 'Kiosk';
export type ServiceMode = 'DineIn' | 'Takeaway' | 'Delivery';
export type OrderAcceptance = 'Automatic' | 'Manual';

export interface SalesApp {
  id: string;
  name: string;
  kind: SalesAppKind;
  status: 'Active' | 'Disabled';
  keyPrefix: string;
  keyHint: string;
  keyIssuedAtUtc: string;
  allowedOrigins: string[];
  locationIds: string[];
  returnUrls: string[];
  createdAtUtc: string;
  requiresHumanCheck?: boolean;
}

export interface ChannelPolicy {
  channel: SalesAppKind;
  payOnline: boolean;
  payAtCounter: boolean;
  counterLimit: number | null;
  acceptance: OrderAcceptance;
}

export interface OnlineOrderingSettings {
  locationId: string;
  locationName: string;
  isOperational: boolean;
  isSaved: boolean;
  isPaused: boolean;
  serviceModes: ServiceMode[];
  preparationMinutes: number;
  minimumOrderAmount: number | null;
  manualAcceptanceMinutes: number;
  unpaidExpiryMinutes: number;
  policies: ChannelPolicy[];
  slotCapacity: number | null;
}

export interface StorefrontOverview {
  access: Record<'web' | 'mobileApp' | 'kiosk', boolean>;
  apps: SalesApp[];
  locations: OnlineOrderingSettings[];
}

export interface AppInput {
  name: string;
  allowedOrigins: string[];
  locationIds: string[];
  returnUrls: string[];
  requireHumanCheck: boolean;
}

export const kindLabel: Record<SalesAppKind, string> = {Web: 'Site web', MobileApp: 'Application mobile', Kiosk: 'Borne'};

export const kindHint: Record<SalesAppKind, string> = {
  Web: 'Votre site ou celui de votre agence : la clé n’est acceptée que depuis les adresses déclarées.',
  MobileApp: 'Votre application à votre nom, publiée sur l’App Store et Google Play.',
  Kiosk: 'Les bornes de commande de vos établissements, avec ticket imprimé.',
};

export const accessOf = (overview: StorefrontOverview, kind: SalesAppKind) =>
  kind === 'Web' ? overview.access.web : kind === 'MobileApp' ? overview.access.mobileApp : overview.access.kiosk;

export const modeLabel: Record<ServiceMode, string> = {DineIn: 'Sur place', Takeaway: 'À emporter', Delivery: 'Livraison'};

export const loadOnlineSales = (signal?: AbortSignal) => api<StorefrontOverview>('/online-sales', {signal});

export const createSalesApp = (kind: SalesAppKind, input: AppInput) =>
  post<{id: string; key: string}>('/online-sales/apps', {kind, ...input});

export const updateSalesApp = (id: string, input: AppInput) => put(`/online-sales/apps/${id}`, input);

export const rotateSalesAppKey = (id: string) => post<{id: string; key: string}>(`/online-sales/apps/${id}/key`);

export const disableSalesApp = (id: string) => post(`/online-sales/apps/${id}/disable`);

export const enableSalesApp = (id: string) => post(`/online-sales/apps/${id}/enable`);

export const saveOnlineOrdering = (settings: OnlineOrderingSettings) =>
  put(`/online-sales/locations/${settings.locationId}`, {
    isPaused: settings.isPaused,
    serviceModes: settings.serviceModes,
    preparationMinutes: settings.preparationMinutes,
    minimumOrderAmount: settings.minimumOrderAmount,
    manualAcceptanceMinutes: settings.manualAcceptanceMinutes,
    unpaidExpiryMinutes: settings.unpaidExpiryMinutes,
    policies: settings.policies,
    slotCapacity: settings.slotCapacity,
  });

export const pauseOnlineOrdering = (locationId: string, isPaused: boolean) => put(`/online-sales/locations/${locationId}/pause`, {isPaused});

/** Le document OpenAPI publié pour les agences. */
export const storefrontDocsUrl = `${API_URL}/openapi/storefront.json`;

/** La documentation développeurs, sur le site public (vente en ligne, intégrations, webhooks). */
export const developerDocsUrl = `${(process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3002').replace(/\/+$/, '')}/developpeurs`;

/** Une ligne par valeur, sans doublon ni ligne vide. */
export const lines = (text: string) => [...new Set(text.split(/\s*\n\s*/).map(line => line.trim()).filter(Boolean))];

export interface Kiosk {
  id: string;
  appId: string;
  appName: string;
  locationId: string;
  name: string;
  status: 'Active' | 'Revoked';
  tokenHint: string;
  pairedAtUtc: string;
  revokedAtUtc: string | null;
}

export interface Kiosks {
  limit: number | null;
  active: number;
  kiosks: Kiosk[];
}

export const listKiosks = (signal?: AbortSignal) => api<Kiosks>('/online-sales/kiosks', {signal});
export const pairKiosk = (code: string, locationId: string, name: string) => post('/online-sales/kiosks/pair', {code, locationId, name});
export const revokeKiosk = (id: string) => post(`/online-sales/kiosks/${id}/revoke`);
