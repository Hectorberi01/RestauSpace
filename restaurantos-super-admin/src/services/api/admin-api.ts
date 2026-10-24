import {all, api, type Page, type Query} from './client';

export type UserStatus = 'Pending' | 'Active' | 'Locked' | 'Disabled';
export type TenantStatus = 'Trial' | 'Active' | 'Suspended' | 'Closed';
export type TenantSuspensionReason = 'PaymentOverdue' | 'TrialExpired' | 'PolicyViolation' | 'RequestedByTenant' | 'Other';
export type SubscriptionStatus = 'Trialing' | 'Active' | 'PastDue' | 'Suspended' | 'Cancelled';
export type SubscriptionSuspensionReason = 'TrialExpired' | 'PaymentOverdue' | 'PolicyViolation' | 'Other';
export type BillingInterval = 'Monthly' | 'Yearly';
export type PlanStatus = 'Active' | 'Archived';
export type Granularity = 'Day' | 'Week' | 'Month';

export interface UserDto {
  id: string;
  email: string;
  status: UserStatus;
  isEmailVerified: boolean;
  hasPassword: boolean;
  canStartSession: boolean;
  createdAtUtc: string;
  emailVerifiedAtUtc: string | null;
  passwordChangedAtUtc: string | null;
  erasedAtUtc: string | null;
}

export interface TenantDto {
  id: string;
  name: string;
  status: TenantStatus;
  canAcceptBusinessWrites: boolean;
  defaultCurrencyCode: string;
  timeZoneId: string;
  pricesIncludeTax: boolean;
  branding: {displayName: string | null; logoUrl: string | null; primaryColor: string | null};
  createdAtUtc: string;
  suspensionReason: TenantSuspensionReason | null;
  suspendedAtUtc: string | null;
  closedAtUtc: string | null;
}

export interface PlatformTenantRow {
  tenantId: string;
  name: string;
  status: TenantStatus;
  createdAtUtc: string;
  subscriptionStatus: SubscriptionStatus | null;
  planName: string | null;
  monthlyAmount: number | null;
  currencyCode: string | null;
}

export interface Entitlement {
  featureCode: string;
  enabled: boolean;
  limit: number | null;
}

export interface PlanDto {
  id: string;
  name: string;
  price: number;
  currencyCode: string;
  billingInterval: BillingInterval;
  status: PlanStatus;
  entitlements: Entitlement[];
  kind?: PlanKind;
}

/** Plan de base (un par abonnement) ou option ajoutée à l'abonnement (vente en ligne, bornes…). */
export type PlanKind = 'Base' | 'AddOn';

export interface TenantAddOnDto {
  id: string;
  tenantId: string;
  addOn: PlanDto;
  quantity: number;
  status: 'Active' | 'Cancelled';
  activatedAtUtc: string;
  cancelledAtUtc: string | null;
}

export interface ChargeLine {
  planId: string;
  kind: PlanKind;
  label: string;
  quantity: number;
  unitAmount: number;
  amount: number;
}

export interface SubscriptionCharge {
  subscriptionId: string;
  tenantId: string;
  currencyCode: string;
  billingInterval: BillingInterval;
  lines: ChargeLine[];
  total: number;
  monthlyTotal: number;
}

export interface SubscriptionDto {
  id: string;
  tenantId: string;
  planId: string;
  status: SubscriptionStatus;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
  cancelAtPeriodEnd: boolean;
  suspensionReason: SubscriptionSuspensionReason | null;
  startedAtUtc: string;
  cancelledAtUtc: string | null;
  /** Plan offert par la plateforme : rien à payer avant la fin de l'offre. */
  isComplimentary?: boolean;
  /** Fin de l'offre ; null quand elle est sans limite. */
  complimentaryUntilUtc?: string | null;
  /** Plan programmé par le restaurant pour la fin de la période payée. */
  scheduledPlanId?: string | null;
  scheduledPlanEffectiveAtUtc?: string | null;
}

export interface AuditEntryDto {
  id: string;
  tenantId: string | null;
  occurredAtUtc: string;
  recordedAtUtc: string;
  action: string;
  targetType: string;
  targetId: string;
  actorType: 'User' | 'ApiCredential' | 'System' | null;
  actorId: string | null;
  correlationId: string;
  metadata: Record<string, string>;
}

export interface StatusCount<T> {
  status: T;
  count: number;
}

export interface AmountByCurrency {
  currencyCode: string;
  amount: number;
}

export interface ReportRow {
  periodStart: string;
  periodEnd: string;
  locationId: string | null;
  metric: string;
  dimension: string;
  currency: string;
  value: number;
}

export interface PlatformDashboard {
  from: string;
  to: string;
  tenantsByStatus: StatusCount<TenantStatus>[];
  newTenants: number;
  subscriptionsByStatus: StatusCount<SubscriptionStatus>[];
  monthlyRecurringRevenue: AmountByCurrency[];
  activity: ReportRow[];
}

export interface ReportResult {
  from: string;
  to: string;
  granularity: Granularity;
  rows: ReportRow[];
}

export interface Created {
  id: string;
}

const post = <T = void>(path: string, body?: unknown) => api<T>(path, {method: 'POST', body});
const put = (path: string, body: unknown) => api<void>(path, {method: 'PUT', body});

export const users = {
  list: (query: Query = {}) => all<UserDto>('/admin/users', query),
  get: (id: string) => api<UserDto>(`/admin/users/${id}`),
  invite: (email: string) => post('/admin/users/invitations', {email}),
  resendInvitation: (id: string) => post(`/admin/users/${id}/invitation`),
  lock: (id: string) => post(`/admin/users/${id}/lock`),
  unlock: (id: string) => post(`/admin/users/${id}/unlock`),
  disable: (id: string) => post(`/admin/users/${id}/disable`),
  revokeSessions: (id: string) => api<void>(`/admin/users/${id}/sessions`, {method: 'DELETE'}),
  changeEmail: (id: string, newEmail: string) => put(`/admin/users/${id}/email`, {newEmail}),
  erase: (id: string) => api<void>(`/admin/users/${id}`, {method: 'DELETE'}),
};

export const tenants = {
  list: (query: Query = {}) => all<PlatformTenantRow>('/admin/reporting/tenants', query),
  get: (id: string) => api<TenantDto>(`/admin/tenants/${id}`),
  create: (input: {name: string; defaultCurrencyCode: string; timeZoneId: string; pricesIncludeTax: boolean}) =>
    post<Created>('/admin/tenants', input),
  activate: (id: string) => post(`/admin/tenants/${id}/activate`),
  suspend: (id: string, reason: TenantSuspensionReason) => post(`/admin/tenants/${id}/suspend`, {reason}),
  reactivate: (id: string) => post(`/admin/tenants/${id}/reactivate`),
  close: (id: string) => post(`/admin/tenants/${id}/close`),
  rename: (id: string, name: string) => put(`/admin/tenants/${id}/name`, {name}),
  /** Rattache un propriétaire au restaurant (compte invité s'il n'existe pas). */
  addOwner: (id: string, email: string) => post<Created>(`/admin/tenants/${id}/owners`, {email}),
};

export const plans = {
  list: (activeOnly = false) => api<PlanDto[]>('/admin/billing/plans', {query: {activeOnly}}),
  create: (input: {name: string; price: number; currencyCode: string; billingInterval: BillingInterval; entitlements: Entitlement[]; kind: PlanKind}) =>
    post<Created>('/admin/billing/plans', input),
  changePrice: (id: string, price: number) => put(`/admin/billing/plans/${id}/price`, {price}),
  setEntitlement: (id: string, entitlement: Entitlement) => put(`/admin/billing/plans/${id}/entitlements`, entitlement),
  archive: (id: string) => post(`/admin/billing/plans/${id}/archive`),
};

export const addOns = {
  /** Plateforme : toutes les options actives, tous restaurants confondus. */
  active: () => all<TenantAddOnDto>('/admin/billing/add-ons'),
  list: (tenantId: string) => api<TenantAddOnDto[]>(`/admin/billing/tenants/${tenantId}/add-ons`),
  activate: (tenantId: string, planId: string, quantity: number) => post<Created>(`/admin/billing/tenants/${tenantId}/add-ons`, {planId, quantity}),
  changeQuantity: (tenantId: string, id: string, quantity: number) => put(`/admin/billing/tenants/${tenantId}/add-ons/${id}/quantity`, {quantity}),
  cancel: (tenantId: string, id: string) => post(`/admin/billing/tenants/${tenantId}/add-ons/${id}/cancel`),
};

export const subscriptions = {
  list: (query: Query = {}) => all<SubscriptionDto>('/admin/billing/subscriptions', query),
  start: (tenantId: string, planId: string, trialDays?: number) =>
    post(`/admin/billing/tenants/${tenantId}/subscription`, {planId, trialDays}),
  recordPayment: (id: string) =>
    api<unknown>(`/admin/billing/subscriptions/${id}/payments`, {method: 'POST', idempotencyKey: crypto.randomUUID()}),
  markPastDue: (id: string) => post(`/admin/billing/subscriptions/${id}/past-due`),
  suspend: (id: string, reason: SubscriptionSuspensionReason) => post(`/admin/billing/subscriptions/${id}/suspend`, {reason}),
  cancel: (id: string) => post(`/admin/billing/subscriptions/${id}/cancel`),
  charge: (id: string) => api<SubscriptionCharge>(`/admin/billing/subscriptions/${id}/charge`),
  /** Offre le plan au restaurant jusqu'à une date (ISO) ou sans fin (null). */
  grantComplimentary: (tenantId: string, planId: string, untilUtc: string | null) =>
    post<Created>(`/admin/billing/tenants/${tenantId}/complimentary`, {planId, untilUtc}),
  endComplimentary: (id: string) => post(`/admin/billing/subscriptions/${id}/complimentary/end`),
};

export type AttentionReason = 'TrialEnding' | 'ComplimentaryEnding' | 'CancellationEnding' | 'PastDue';

export interface AttentionDto {
  subscriptionId: string;
  tenantId: string;
  planName: string;
  status: SubscriptionStatus;
  reason: AttentionReason;
  atUtc: string;
  monthlyAmount: number | null;
  currencyCode: string;
}

export interface BillingOverview {
  atUtc: string;
  monthlyRecurringRevenue: AmountByCurrency[];
  averageRevenuePerSubscription: AmountByCurrency[];
  paying: number;
  trialing: number;
  pastDue: number;
  suspended: number;
  complimentary: number;
  complimentaryUnlimited: number;
  cancellationsScheduled: number;
  cancelledThisMonth: number;
  trialsStarted: number;
  trialsConverted: number;
  collectedThisMonth: AmountByCurrency[];
  collectedLastMonth: AmountByCurrency[];
  collections: {month: string; currencyCode: string; amount: number; count: number}[];
  attention: AttentionDto[];
  failedCheckouts: number;
  recentFailedCheckouts: {tenantId: string; amount: number; currencyCode: string; provider: string; status: 'Failed' | 'Expired'; atUtc: string}[];
}

/** Ce qui manque à un prestataire : inactif, devise non autorisée, absent du code, compte plateforme absent ou illisible. */
export type CollectionMissing = 'disabled' | 'currency' | 'integration' | 'credentials' | 'credentials_unreadable';

export interface CollectorCandidate {
  provider: string;
  name: string;
  ready: boolean;
  missing: CollectionMissing[];
  methods?: PaymentMethodType[] | null;
}

export interface OnlinePaymentRoute {
  currencyCode: string;
  provider: string | null;
  chosen: boolean;
  ready: boolean;
  updatedAtUtc: string | null;
  candidates?: CollectorCandidate[] | null;
  providers?: string[] | null;
}

export const billingAdmin = {
  overview: () => api<BillingOverview>('/admin/billing/overview'),
  onlinePayment: () => api<OnlinePaymentRoute[]>('/admin/billing/online-payment'),
  setOnlinePayment: (currency: string, providers: string[]) => put(`/admin/billing/online-payment/${currency}`, {providers}),
};

export interface TenantActivityRow {
  tenantId: string;
  name: string;
  status: TenantStatus;
  createdAtUtc: string;
  orders: number;
  revenue: number;
  currency: string | null;
  lastOrderDay: string | null;
}

export interface TenantActivity {
  from: string;
  to: string;
  mostActive: TenantActivityRow[];
  inactive: TenantActivityRow[];
}

export const audit = {
  list: (query: Query = {}, limit = 500) => all<AuditEntryDto>('/admin/audit', query, limit),
};

export const reporting = {
  dashboard: (from: string, to: string) => api<PlatformDashboard>('/admin/reporting/dashboard', {query: {from, to}}),
  tenantActivity: (from: string, to: string) => api<TenantActivity>('/admin/reporting/tenant-activity', {query: {from, to}}),
  activity: (from: string, to: string, granularity: Granularity, metrics: string[]) =>
    api<ReportResult>('/admin/reporting/activity', {query: {from, to, granularity, metrics: metrics.join(',')}}),
};

export interface SubscriptionPaymentDto {
  id: string;
  tenantId: string;
  subscriptionId: string;
  planId: string;
  planName: string;
  amount: number;
  currencyCode: string;
  paidFromUtc: string;
  paidUntilUtc: string;
  recordedAtUtc: string;
  lines?: ChargeLine[] | null;
}

export const payments = {
  list: (query: Query = {}, limit = 1000) => all<SubscriptionPaymentDto>('/admin/billing/payments', query, limit),
};

export type PaymentProviderType = 'OnSite' | 'Online';
export type PaymentMethodType =
  | 'Card' | 'ApplePay' | 'GooglePay' | 'PayPal' | 'MobileMoney' | 'BankTransfer'
  | 'Cash' | 'CardOnSite' | 'MobileMoneyOnSite' | 'MealVoucher';

export interface PaymentCapability {
  methods: PaymentMethodType[];
  countries: string[];
  currencies: string[];
  supportsAuthorization: boolean;
  supportsRefund: boolean;
  supportsPartialRefund: boolean;
  supportsWebhooks: boolean;
  supportsRecurring: boolean;
  paymentExpirationMinutes: number | null;
  amountLimits: {currency: string; minimum: number | null; maximum: number | null}[];
}

export interface PaymentProviderDto {
  id: string;
  code: string;
  type: PaymentProviderType;
  displayName: string;
  isEnabled: boolean;
  currencies: string[];
  capability: PaymentCapability;
  credentialFields: string[];
  hasIntegration: boolean;
}

export interface PaymentIntegrationDto {
  code: string;
  name: string;
  credentialFields: string[];
  isRegistered: boolean;
}

export interface PaymentCapabilityInput {
  methods: PaymentMethodType[];
  countries?: string[];
  currencies?: string[];
  supportsAuthorization?: boolean;
  supportsRefund?: boolean;
  supportsPartialRefund?: boolean;
  supportsWebhooks?: boolean;
  supportsRecurring?: boolean;
}

export const paymentProviders = {
  list: () => api<PaymentProviderDto[]>('/admin/payment-providers'),
  integrations: () => api<PaymentIntegrationDto[]>('/admin/payment-providers/integrations'),
  register: (input: {code: string; type: PaymentProviderType; displayName: string; capability: PaymentCapabilityInput}) =>
    post<Created>('/admin/payment-providers', input),
  update: (id: string, displayName: string, capability: PaymentCapabilityInput) =>
    put(`/admin/payment-providers/${id}`, {displayName, capability}),
  setCredentials: (id: string, credentials: Record<string, string>) => put(`/admin/payment-providers/${id}/credentials`, {credentials}),
  enable: (id: string) => post(`/admin/payment-providers/${id}/enable`),
  disable: (id: string) => post(`/admin/payment-providers/${id}/disable`),
};

export type NotificationChannel = 'Email' | 'Sms' | 'WhatsApp' | 'Push' | 'InApp';
export type NotificationFeature = 'Text' | 'Html' | 'Attachments' | 'ProviderTemplates';
export type NotificationConfigurationType = 'Internal' | 'PlatformOnly' | 'TenantOnly' | 'PlatformOrTenant';

export interface NotificationProviderDto {
  id: string;
  code: string;
  name: string;
  channel: NotificationChannel;
  configurationType: NotificationConfigurationType;
  capability: {channel: NotificationChannel; features: NotificationFeature[]; countries: string[]; maxContentLength: number | null};
  isEnabled: boolean;
  hasIntegration: boolean;
  /** Compte de la plateforme : identifiants rangés au coffre (null : ce prestataire n'utilise pas de compte plateforme). */
  hasPlatformCredentials?: boolean | null;
}

export interface NotificationIntegrationDto {
  provider: string;
  channels: NotificationChannel[];
  credentialFields: string[];
}

export interface DeadLetterDto {
  id: string;
  tenantId: string | null;
  type: string;
  channel: NotificationChannel;
  provider: string;
  attemptCount: number;
  lastFailureCode: string | null;
  lastFailureKind: 'Temporary' | 'Permanent' | null;
  createdAtUtc: string;
}

export const notificationProviders = {
  list: () => api<NotificationProviderDto[]>('/admin/notification-providers'),
  integrations: () => api<NotificationIntegrationDto[]>('/admin/notification-providers/integrations'),
  create: (input: {
    code: string;
    name: string;
    channel: NotificationChannel;
    configurationType: NotificationConfigurationType;
    capability: {features: NotificationFeature[]; countries?: string[]; maxContentLength?: number | null};
  }) => post<Created>('/admin/notification-providers', input),
  setCredentials: (id: string, credentials: Record<string, string>) => put(`/admin/notification-providers/${id}/credentials`, {credentials}),
  enable: (id: string) => post(`/admin/notification-providers/${id}/enable`),
  disable: (id: string) => post(`/admin/notification-providers/${id}/disable`),
};

export type NotificationStatus = 'Pending' | 'Scheduled' | 'Processing' | 'Sent' | 'PartiallyDelivered' | 'Delivered' | 'Failed' | 'Cancelled';

/** Une ligne du journal d'envoi : jamais le destinataire ni le contenu. */
export interface NotificationLogDto {
  id: string;
  tenantId: string | null;
  type: string;
  channel: NotificationChannel;
  provider: string;
  status: NotificationStatus;
  attemptCount: number;
  lastFailureCode: string | null;
  createdAtUtc: string;
  scheduledAtUtc: string | null;
  sentAtUtc: string | null;
  deliveredAtUtc: string | null;
}

export interface NotificationsOverview {
  sinceUtc: string;
  created: number;
  handedOver: number;
  delivered: number;
  failed: number;
  inProgress: number;
  cancelled: number;
  averageSendSeconds: number | null;
  channels: {channel: NotificationChannel; created: number; handedOver: number; failed: number}[];
}

export type TemplateStatus = 'Draft' | 'Published' | 'Archived';

export interface NotificationTemplateDto {
  id: string;
  isPlatform: boolean;
  key: string;
  channel: NotificationChannel;
  language: string;
  subject: string | null;
  body: string;
  version: number;
  status: TemplateStatus;
  isEnabled: boolean;
  variables: string[];
  updatedAtUtc: string;
  publishedAtUtc: string | null;
}

export interface NotificationTypeDto {
  key: string;
  purpose: string;
  priority: string;
  isEssential: boolean;
  audience: string;
  variables: string[];
}

export const notificationAdmin = {
  overview: () => api<NotificationsOverview>('/admin/notifications/overview'),
  log: (query: {channel?: NotificationChannel; status?: NotificationStatus; page?: number; pageSize?: number}) =>
    api<Page<NotificationLogDto>>('/admin/notifications', {query: query as Query}),
};

/** Les modèles de la PLATEFORME (repli de tous les restaurants). */
export const platformTemplates = {
  list: () => api<NotificationTemplateDto[]>('/admin/notification-templates'),
  types: () => api<NotificationTypeDto[]>('/admin/notification-templates/types'),
  create: (input: {key: string; channel: NotificationChannel; language: string; subject: string | null; body: string}) =>
    post<Created>('/admin/notification-templates', input),
  update: (id: string, subject: string | null, body: string) => put(`/admin/notification-templates/${id}`, {subject, body}),
  newVersion: (id: string) => post<Created>(`/admin/notification-templates/${id}/versions`),
  publish: (id: string) => post(`/admin/notification-templates/${id}/publish`),
  enable: (id: string) => post(`/admin/notification-templates/${id}/enable`),
  disable: (id: string) => post(`/admin/notification-templates/${id}/disable`),
  archive: (id: string) => api<void>(`/admin/notification-templates/${id}`, {method: 'DELETE'}),
};

export const deadLetters = {
  list: () => all<DeadLetterDto>('/admin/notification-dead-letters', {}, 500),
  retry: (id: string) => post(`/admin/notification-dead-letters/${id}/retry`),
  abandon: (id: string) => post(`/admin/notification-dead-letters/${id}/abandon`),
};

export type DemoRequestStatus = 'New' | 'Contacted' | 'Converted' | 'Declined';

export interface DemoRequestDto {
  id: string;
  contactName: string;
  restaurantName: string;
  email: string | null;
  phone: string | null;
  country: string;
  planInterest: string | null;
  message: string | null;
  status: DemoRequestStatus;
  note: string | null;
  submittedAtUtc: string;
  updatedAtUtc: string;
}

export const demoRequests = {
  list: (status?: DemoRequestStatus) => all<DemoRequestDto>('/admin/demo-requests', {status}, 1000),
  update: (id: string, status: DemoRequestStatus, note: string | null) => put(`/admin/demo-requests/${id}`, {status, note}),
  remove: (id: string) => api<void>(`/admin/demo-requests/${id}`, {method: 'DELETE'}),
};

export type SalesAppKind = 'Web' | 'MobileApp' | 'Kiosk';
export type OnlineServiceMode = 'Takeaway' | 'DineIn' | 'Delivery';

export interface OnlineChannelStats {
  channel: SalesAppKind;
  placed: number;
  handled: number;
  waiting: number;
  expired: number;
  cancelledByCustomer: number;
  paidOnline: number;
}

export interface OnlineSalesOverview {
  from: string;
  to: string;
  tenantsWithApps: number;
  tenantsWithOrders: number;
  apps: {kind: SalesAppKind; active: number; disabled: number}[];
  activeKiosks: number;
  revokedKiosks: number;
  configuredLocations: number;
  pausedLocations: number;
  channels: OnlineChannelStats[];
  activeCustomerSessions: number;
  customerSignIns: number;
}

export interface OnlineSalesTenantRow {
  tenantId: string;
  webApps: number;
  mobileApps: number;
  kioskApps: number;
  disabledApps: number;
  activeKiosks: number;
  configuredLocations: number;
  pausedLocations: number;
  ordersPlaced: number;
  ordersExpired: number;
  lastOrderAtUtc: string | null;
  activeCustomerSessions: number;
}

export interface OnlineSalesTenantDetail {
  tenantId: string;
  from: string;
  to: string;
  apps: {id: string; name: string; kind: SalesAppKind; status: 'Active' | 'Disabled'; createdAtUtc: string}[];
  kiosks: {id: string; appId: string; locationId: string; name: string; status: 'Active' | 'Revoked'; pairedAtUtc: string; revokedAtUtc: string | null}[];
  locations: {locationId: string; isPaused: boolean; serviceModes: OnlineServiceMode[]; slotCapacity: number | null; updatedAtUtc: string}[];
  channels: OnlineChannelStats[];
  lastOrderAtUtc: string | null;
  activeCustomerSessions: number;
  customerSignIns: number;
}

export const onlineSales = {
  overview: (from: string, to: string) => api<OnlineSalesOverview>('/admin/online-sales/overview', {query: {from, to}}),
  tenants: (from: string, to: string) => all<OnlineSalesTenantRow>('/admin/online-sales/tenants', {from, to}),
  tenant: (tenantId: string, from: string, to: string) => api<OnlineSalesTenantDetail>(`/admin/online-sales/tenants/${tenantId}`, {query: {from, to}}),
};

export type BackupTrigger = 'Manual' | 'Scheduled';
export type BackupStatus = 'Pending' | 'Running' | 'Uploading' | 'Completed' | 'Failed' | 'Deleting' | 'Deleted';
export type BackupFailureCode = 'dump_failed' | 'verify_failed' | 'encrypt_failed' | 'upload_failed' | 'timeout' | 'agent_restarted';
export type RestoreStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type RestoreFailureCode =
  | 'disabled' | 'download_failed' | 'checksum_mismatch' | 'invalid_key' | 'key_missing' | 'decrypt_failed' | 'restore_failed' | 'swap_failed' | 'timeout';

export interface BackupDto {
  id: string;
  databaseName: string;
  trigger: BackupTrigger;
  status: BackupStatus;
  sizeBytes: number | null;
  checksumSha256: string | null;
  requestedBy: string | null;
  failureCode: BackupFailureCode | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  expiresAtUtc: string | null;
}

export interface RestoreDto {
  id: string;
  backupId: string;
  databaseName: string;
  status: RestoreStatus;
  requestedBy: string;
  failureCode: RestoreFailureCode | null;
  createdAtUtc: string;
  notBeforeUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  previousDatabaseName: string | null;
}

/** Agent du serveur de base ; champs null s'il ne s'est jamais signalé. */
export interface BackupAgentDto {
  host: string | null;
  version: string | null;
  lastSeenAtUtc: string | null;
  restoreEnabled: boolean | null;
  online: boolean;
}

export interface BackupSettingsDto {
  databaseName: string;
  retentionDays: number;
  keepMin: number;
  dailyAtUtc: string;
  restoreDelayMinutes: number;
  storageConfigured: boolean;
}

export const backups = {
  list: (page: number, pageSize: number) => api<Page<BackupDto>>('/admin/backups', {query: {page, pageSize}}),
  get: (id: string) => api<BackupDto>(`/admin/backups/${id}`),
  create: () => post<BackupDto>('/admin/backups'),
  remove: (id: string) => api<void>(`/admin/backups/${id}`, {method: 'DELETE'}),
  /** URL pré-signée de courte durée : à ouvrir tout de suite, jamais à garder. */
  downloadLink: (id: string, code: string) => post<{url: string; expiresAtUtc: string}>(`/admin/backups/${id}/download-link`, {code}),
  restore: (id: string, code: string, confirmDatabaseName: string) => post<RestoreDto>(`/admin/backups/${id}/restore`, {code, confirmDatabaseName}),
  restores: (page: number, pageSize: number) => api<Page<RestoreDto>>('/admin/backups/restores', {query: {page, pageSize}}),
  cancelRestore: (id: string) => post(`/admin/backups/restores/${id}/cancel`),
  agent: () => api<BackupAgentDto>('/admin/backups/agent'),
  settings: () => api<BackupSettingsDto>('/admin/backups/settings'),
};
