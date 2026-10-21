import {api, post, put} from './api';
import type {MessageChannel} from './messages';

export type CampaignStatus = 'Draft' | 'Scheduled' | 'Sending' | 'Completed' | 'Cancelled';

export interface Campaign {
  id: string;
  name: string;
  type: string;
  channel: MessageChannel;
  tag: string | null;
  locationId: string | null;
  variables: Record<string, string>;
  status: CampaignStatus;
  createdAtUtc: string;
  scheduledForUtc: string | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  processed: number;
  created: number;
  skipped: number;
  skippedForFrequency: number;
  /** Seulement les clients de l'établissement locationId (au moins une commande). */
  onlyLocationCustomers?: boolean;
}

export interface CampaignInput {
  name: string;
  type: string;
  channel: MessageChannel;
  tag: string | null;
  locationId: string | null;
  variables: Record<string, string>;
  onlyLocationCustomers: boolean;
}

/** Variables ajoutées pour chaque client par l'envoi : une campagne ne les saisit jamais. */
export const perRecipientVariables = ['customer.firstName', 'unsubscribe.url', 'loyalty.points'];

export const campaignStatusLabel: Record<CampaignStatus, string> = {
  Draft: 'Brouillon',
  Scheduled: 'Programmée',
  Sending: 'En cours d’envoi',
  Completed: 'Terminée',
  Cancelled: 'Arrêtée',
};

export const campaignStatusTone: Record<CampaignStatus, 'neutral' | 'blue' | 'amber' | 'green' | 'red'> = {
  Draft: 'neutral',
  Scheduled: 'blue',
  Sending: 'amber',
  Completed: 'green',
  Cancelled: 'red',
};

export const listCampaigns = (signal?: AbortSignal) => api<Campaign[]>('/campaigns', {signal});

export const createCampaign = (input: CampaignInput) => post<{id: string}>('/campaigns', input);

export const updateCampaign = (id: string, input: CampaignInput) => put(`/campaigns/${id}`, input);

export const scheduleCampaign = (id: string, at: string | null) => post(`/campaigns/${id}/schedule`, {at});

export const unscheduleCampaign = (id: string) => post(`/campaigns/${id}/unschedule`);

export const cancelCampaign = (id: string) => post(`/campaigns/${id}/cancel`);

export const campaignAudience = (channel: MessageChannel, tag: string | null, customersOf: string | null, signal?: AbortSignal) =>
  api<{count: number}>('/campaigns/audience', {query: {channel, tag: tag || undefined, customersOf: customersOf || undefined}, signal});

export const marketingRules = (signal?: AbortSignal) => api<{minimumIntervalDays: number}>('/campaigns/rules', {signal});

export const setMarketingRules = (minimumIntervalDays: number) => put('/campaigns/rules', {minimumIntervalDays});
