import type {PublicProfile, SocialNetwork} from './types';
import {api, del, post, put} from './api';

export type Channel = 'Email' | 'Sms' | 'WhatsApp' | 'Push' | 'InApp';
export type NotificationMode = 'PlatformManaged' | 'TenantManaged';
export type ConfigurationType = 'Internal' | 'PlatformOnly' | 'TenantOnly' | 'PlatformOrTenant';

export interface NotificationProvider {
  id: string;
  code: string;
  name: string;
  channel: Channel;
  configurationType: ConfigurationType;
  isEnabled: boolean;
  hasIntegration: boolean;
  credentialFields: string[];
}

export interface NotificationConfiguration {
  id: string;
  providerId: string;
  provider: string;
  channel: Channel;
  mode: NotificationMode;
  status: 'AwaitingCredentials' | 'Configured';
  isEnabled: boolean;
  hasCredentials: boolean;
  isGlobal: boolean;
  assignments: {locationId: string; priority: number; isEnabled: boolean}[];
}

export const channelLabel: Record<Channel, string> = {Email: 'Email', Sms: 'SMS', WhatsApp: 'WhatsApp', Push: 'Notifications push', InApp: 'Dans l’application'};

export const channelHint: Record<Channel, string> = {
  Email: 'Confirmations de réservation, reçus et messages aux clients par email.',
  Sms: 'Rappels et confirmations par SMS.',
  WhatsApp: 'Messages WhatsApp Business aux clients.',
  Push: 'Notifications sur l’application mobile.',
  InApp: 'Alertes de l’équipe dans la console.',
};

export const currencies: {code: string; label: string}[] = [
  {code: 'XOF', label: 'Franc CFA BCEAO (XOF)'},
  {code: 'XAF', label: 'Franc CFA BEAC (XAF)'},
  {code: 'EUR', label: 'Euro (EUR)'},
  {code: 'USD', label: 'Dollar américain (USD)'},
  {code: 'GHS', label: 'Cedi ghanéen (GHS)'},
  {code: 'NGN', label: 'Naira nigérian (NGN)'},
  {code: 'GNF', label: 'Franc guinéen (GNF)'},
  {code: 'MAD', label: 'Dirham marocain (MAD)'},
  {code: 'CHF', label: 'Franc suisse (CHF)'},
  {code: 'CAD', label: 'Dollar canadien (CAD)'},
  {code: 'GBP', label: 'Livre sterling (GBP)'},
];

export const renameRestaurant = (name: string) => put('/tenant/name', {name});

export const updatePublicProfile = (body: PublicProfile) => put('/tenant/public-profile', body);

export const socialNetworks: {network: SocialNetwork; label: string; placeholder: string}[] = [
  {network: 'Website', label: 'Site web', placeholder: 'https://www.chez-awa.bj'},
  {network: 'Facebook', label: 'Facebook', placeholder: 'https://facebook.com/…'},
  {network: 'Instagram', label: 'Instagram', placeholder: 'https://instagram.com/…'},
  {network: 'TikTok', label: 'TikTok', placeholder: 'https://tiktok.com/@…'},
  {network: 'X', label: 'X (Twitter)', placeholder: 'https://x.com/…'},
  {network: 'YouTube', label: 'YouTube', placeholder: 'https://youtube.com/@…'},
  {network: 'GoogleMaps', label: 'Fiche Google Maps', placeholder: 'https://maps.app.goo.gl/…'},
];

export const updateBranding = (body: {displayName: string; logoUrl: string | null; primaryColor: string | null}) => put('/tenant/branding', body);

export const changeTimeZone = (timeZoneId: string) => put('/tenant/time-zone', {timeZoneId});

export const changeCurrency = (currencyCode: string) => put('/tenant/default-currency', {currencyCode});

export const changePriceTaxMode = (pricesIncludeTax: boolean) => put('/tenant/price-tax-mode', {pricesIncludeTax});

export const loadNotificationProviders = (signal?: AbortSignal) => api<NotificationProvider[]>('/notification-configurations/providers', {signal});

export const loadNotificationConfigurations = (signal?: AbortSignal) => api<NotificationConfiguration[]>('/notification-configurations', {signal});

export const configureNotificationProvider = (providerId: string, mode: NotificationMode) =>
  post<{id: string}>(`/notification-configurations/${providerId}/configure`, {mode});

export const setNotificationCredentials = (id: string, credentials: Record<string, string>) =>
  put(`/notification-configurations/${id}/credentials`, {credentials});

export const setNotificationMode = (id: string, mode: NotificationMode) => put(`/notification-configurations/${id}/mode`, {mode});

export const enableNotification = (id: string) => post(`/notification-configurations/${id}/enable`);

export const disableNotification = (id: string) => post(`/notification-configurations/${id}/disable`);

/** Réserve le fournisseur à un établissement (rang : le plus petit est essayé d'abord). */
export const assignNotificationLocation = (id: string, locationId: string, priority: number, isEnabled = true) =>
  put(`/notification-configurations/${id}/locations`, {locationId, priority, isEnabled});

export const removeNotificationLocation = (id: string, locationId: string) => del(`/notification-configurations/${id}/locations/${locationId}`);

/** Modes permis par la plateforme pour un prestataire. */
export function modesOf(type: ConfigurationType): NotificationMode[] {
  if (type === 'PlatformOnly' || type === 'Internal') return ['PlatformManaged'];
  if (type === 'TenantOnly') return ['TenantManaged'];
  return ['PlatformManaged', 'TenantManaged'];
}

export const fieldLabel = (field: string) => field.replace(/_/g, ' ').replace(/^\w/, letter => letter.toUpperCase());

/** Un champ d'identifiants d'un prestataire de notification : libellé, secret ou non, type, choix possibles. */
export interface NotificationField {
  label: string;
  secret: boolean;
  hint?: string;
  type?: 'text' | 'email' | 'number';
  options?: [string, string][];
}

const notificationFields: Record<string, Record<string, NotificationField>> = {
  SMTP: {
    host: {label: 'Serveur SMTP', secret: false, hint: 'Mailjet : in-v3.mailjet.com · Brevo : smtp-relay.brevo.com'},
    port: {label: 'Port', secret: false, type: 'number', hint: '587 avec STARTTLS, 465 en SSL.'},
    security: {label: 'Sécurité de la connexion', secret: false, options: [['starttls', 'STARTTLS (recommandé)'], ['ssl', 'SSL / TLS'], ['none', 'Aucune']]},
    username: {label: 'Identifiant', secret: false, hint: 'Mailjet : votre clé API.'},
    password: {label: 'Mot de passe', secret: true, hint: 'Mailjet : votre clé secrète.'},
    from_email: {label: 'Adresse d’expédition', secret: false, type: 'email', hint: 'Doit être validée chez votre fournisseur.'},
    from_name: {label: 'Nom d’expéditeur', secret: false, hint: 'Ce que vos clients voient, par exemple le nom du restaurant.'},
  },
  MAILJET: {
    api_key: {label: 'Clé API', secret: false, hint: 'Mailjet › Paramètres du compte › Clés API (REST API).'},
    secret_key: {label: 'Clé secrète', secret: true},
    from_email: {label: 'Adresse d’expédition', secret: false, type: 'email', hint: 'Adresse ou domaine validé dans Mailjet (Expéditeurs et domaines).'},
    from_name: {label: 'Nom d’expéditeur', secret: false, hint: 'Ce que vos clients voient, par exemple le nom du restaurant.'},
    webhook_password: {label: 'Mot de passe du webhook', secret: true, hint: 'Choisissez-le librement : il figure dans l’adresse à déclarer chez Mailjet, ci-dessous.'},
  },
  WHATSAPP_META: {
    access_token: {label: 'Jeton d’accès permanent', secret: true},
    phone_number_id: {label: 'Identifiant du numéro (Phone number ID)', secret: false},
    app_secret: {label: 'Clé secrète de l’application', secret: true},
    verify_token: {label: 'Jeton de vérification du webhook', secret: true, hint: 'Choisissez-le librement et saisissez le même chez Meta.'},
    template_name: {label: 'Nom du modèle de message approuvé', secret: false},
    template_language: {label: 'Langue du modèle', secret: false, hint: 'Par exemple fr ou en_US.'},
  },
};

export function notificationField(provider: string, field: string): NotificationField {
  return notificationFields[provider]?.[field] ?? {label: fieldLabel(field), secret: true};
}

/** Préréglages proposés pour un compte SMTP. */
export const smtpPresets: {label: string; values: Record<string, string>}[] = [
  {label: 'Mailjet', values: {host: 'in-v3.mailjet.com', port: '587', security: 'starttls'}},
  {label: 'Brevo', values: {host: 'smtp-relay.brevo.com', port: '587', security: 'starttls'}},
];

/** L'adresse de webhook d'une configuration, avec un mot de passe en authentification Basic si fourni. */
export function notificationWebhookUrl(apiUrl: string, provider: string, configurationId: string, password?: string): string {
  const url = new URL(`${apiUrl}/v1/notifications/webhooks/${provider}/${configurationId}`);
  if (password !== undefined) {
    url.username = 'mailjet';
    url.password = password || 'MOT_DE_PASSE';
  }
  return url.toString();
}

export type NotificationTestOutcome = 'sent' | 'delivered' | 'failed' | 'credentials_missing' | 'credentials_invalid';

/** Résultat d'un message de test : aucun identifiant ni message brut du prestataire. */
export interface NotificationTest {
  outcome: NotificationTestOutcome;
  succeeded: boolean;
  failureCode: string | null;
  isPermanent: boolean;
  testedAtUtc: string;
}

/** Canaux qui peuvent recevoir un message de test. */
export const testableChannels = new Set<Channel>(['Email', 'Sms', 'WhatsApp']);

export const testNotification = (id: string, address: string) =>
  post<NotificationTest>(`/notification-configurations/${id}/test`, {address});

const failureMessages: Record<string, string> = {
  PROVIDER_AUTH_FAILED: 'Le prestataire a refusé les identifiants : vérifiez la clé, le mot de passe ou le jeton.',
  SENDER_REJECTED: 'L’adresse d’expédition est refusée : validez-la chez votre fournisseur (expéditeurs et domaines).',
  TEMPLATE_INVALID: 'Le modèle WhatsApp est introuvable ou pas encore approuvé par Meta : vérifiez son nom et sa langue.',
  INVALID_EMAIL: 'Le destinataire est refusé : vérifiez l’adresse.',
  INVALID_PHONE_NUMBER: 'Le numéro est refusé : vérifiez-le (format international).',
  INVALID_RECIPIENT: 'Le destinataire est refusé par le prestataire.',
  PROVIDER_UNAVAILABLE: 'Prestataire injoignable : vérifiez le serveur et le port, puis réessayez.',
  TIMEOUT: 'Le prestataire n’a pas répondu à temps : réessayez.',
  RATE_LIMITED: 'Le prestataire limite les envois : réessayez dans quelques minutes.',
  REJECTED: 'Le prestataire a refusé le message.',
};

/** Ce qu'on dit au restaurateur après un message de test. */
export function notificationTestSummary(result: NotificationTest, channel: Channel, address: string): {tone: 'green' | 'amber' | 'red'; title: string; detail?: string} {
  switch (result.outcome) {
    case 'delivered':
      return {tone: 'green', title: `Message remis à ${address}.`};
    case 'sent':
      return {
        tone: 'green',
        title: `Message accepté par le prestataire pour ${address}.`,
        detail: channel === 'Email' ? 'Il arrive en général en moins d’une minute ; regardez aussi dans les indésirables.' : 'Il arrive en général en quelques secondes.',
      };
    case 'credentials_missing':
      return {tone: 'amber', title: 'Aucun identifiant enregistré.', detail: 'Saisissez d’abord les identifiants du compte.'};
    case 'credentials_invalid':
      return {tone: 'red', title: 'Identifiants enregistrés incomplets ou invalides.', detail: 'Saisissez-les à nouveau.'};
    default: {
      const code = result.failureCode ?? '';
      const detail = failureMessages[code]
        ?? (code.startsWith('MJ_') ? `Mailjet a refusé le message (code ${code.slice(3).toLowerCase().replace(/_/g, '-')}).` : code ? `Code d’erreur : ${code}.` : undefined);
      return {tone: 'red', title: 'L’envoi a échoué.', detail};
    }
  }
}
