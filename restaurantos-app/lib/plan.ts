import {api} from './api';
import type {SubscriptionStatus} from './billing';

/** Codes des droits du plan (contrat `GET /v1/billing/features`). */
export const Feature = {
  CustomersAdvanced: 'customers.advanced',
  Loyalty: 'loyalty',
  MarketingCampaigns: 'marketing.campaigns',
  MarketingGiftCards: 'marketing.gift_cards',
  Automations: 'automations',
  ReportingAdvanced: 'reporting.advanced',
  ReportingConsolidated: 'reporting.consolidated',
  IntegrationsApi: 'storefront.api',
  IntegrationsWebhooks: 'integrations.webhooks',
  NotificationsOwnAccount: 'notifications.byok',
  SupportPriority: 'support.priority',
  StorefrontMobileApp: 'storefront.mobile_app',
  StorefrontKiosk: 'storefront.kiosk',
  LocationsExtra: 'locations.extra',
} as const;

export interface PlanFeature {
  code: string;
  enabled: boolean;
  limit: number | null;
}

export interface PlanFeatures {
  planName: string | null;
  status: SubscriptionStatus | null;
  trialEndsAtUtc: string | null;
  features: PlanFeature[];
  usage: {locations: number | null; staff: number | null; ordersThisMonth: number | null} | null;
}

export const loadPlanFeatures = (signal?: AbortSignal) => api<PlanFeatures>('/billing/features', {signal});

/** Plan minimal qui inclut la fonction (badge). */
export function requiredPlan(code: string): string {
  if (code === Feature.ReportingConsolidated) return 'Multi-sites';
  if (code === Feature.Loyalty || code === Feature.NotificationsOwnAccount) return 'Essentiel';
  if (code === Feature.StorefrontMobileApp || code === Feature.StorefrontKiosk) return 'Option';
  return 'Pro';
}

/** Option qui donne aussi le droit, quand il y en a une. */
export function optionFor(code: string): string | null {
  switch (code) {
    case Feature.StorefrontMobileApp: return 'Application mobile';
    case Feature.StorefrontKiosk: return 'Borne';
    case Feature.IntegrationsApi:
    case Feature.IntegrationsWebhooks: return 'API et webhooks';
    default: return null;
  }
}
