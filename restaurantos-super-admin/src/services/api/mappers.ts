import type {RecordItem} from '../../types';
import type {
  AuditEntryDto,
  PlanDto,
  PlatformTenantRow,
  SubscriptionDto,
  SubscriptionPaymentDto,
  SubscriptionStatus,
  TenantStatus,
  UserDto,
  UserStatus,
} from './admin-api';

export const userStatus: Record<UserStatus, string> = {
  Pending: 'En attente',
  Active: 'Actif',
  Locked: 'Suspendu',
  Disabled: 'Désactivé',
};

export const tenantStatus: Record<TenantStatus, string> = {
  Trial: 'En essai',
  Active: 'Actif',
  Suspended: 'Suspendu',
  Closed: 'Fermé',
};

export const subscriptionStatus: Record<SubscriptionStatus, string> = {
  Trialing: 'En essai',
  Active: 'Actif',
  PastDue: 'En retard',
  Suspended: 'Suspendu',
  Cancelled: 'Résilié',
};

export const planStatus = {Active: 'Actif', Archived: 'Archivé'} as const;

export const actorType = {User: 'Utilisateur', ApiCredential: 'Clé d’API', System: 'Système'} as const;

export const monthly = (plan: Pick<PlanDto, 'price' | 'billingInterval'>) =>
  plan.billingInterval === 'Yearly' ? Math.round((plan.price / 12) * 100) / 100 : plan.price;

export const shortId = (id: string) => id.slice(0, 8);

export function userRecord(user: UserDto): RecordItem {
  return {
    id: user.id,
    name: user.email,
    email: user.email,
    status: userStatus[user.status] ?? user.status,
    apiStatus: user.status,
    verified: user.isEmailVerified ? 'Vérifié' : 'Non vérifié',
    password: user.hasPassword,
    canStartSession: user.canStartSession,
    date: user.createdAtUtc,
    verifiedAt: user.emailVerifiedAtUtc ?? '',
    passwordChangedAt: user.passwordChangedAtUtc ?? '',
  };
}

export function tenantRecord(row: PlatformTenantRow): RecordItem {
  return {
    id: row.tenantId,
    name: row.name,
    status: tenantStatus[row.status] ?? row.status,
    apiStatus: row.status,
    plan: row.planName ?? 'Sans abonnement',
    subscription: row.subscriptionStatus ? subscriptionStatus[row.subscriptionStatus] : 'Aucun',
    apiSubscription: row.subscriptionStatus ?? '',
    mrr: row.monthlyAmount ?? 0,
    currency: row.currencyCode ?? '',
    date: row.createdAtUtc,
  };
}

export function subscriptionRecord(
  subscription: SubscriptionDto,
  tenants: Map<string, PlatformTenantRow>,
  plans: Map<string, PlanDto>,
): RecordItem {
  const plan = plans.get(subscription.planId);
  return {
    id: subscription.id,
    name: tenants.get(subscription.tenantId)?.name ?? `Restaurant ${shortId(subscription.tenantId)}`,
    tenantId: subscription.tenantId,
    planId: subscription.planId,
    plan: plan?.name ?? shortId(subscription.planId),
    amount: plan && !subscription.isComplimentary ? monthly(plan) : 0,
    currency: plan?.currencyCode ?? '',
    status: subscription.isComplimentary ? 'Offert' : subscriptionStatus[subscription.status] ?? subscription.status,
    complimentary: !!subscription.isComplimentary,
    complimentaryUntil: subscription.complimentaryUntilUtc ?? '',
    apiStatus: subscription.status,
    date: subscription.startedAtUtc,
    periodStart: subscription.currentPeriodStartUtc,
    end: subscription.currentPeriodEndUtc,
    renewal: !subscription.cancelAtPeriodEnd,
    suspensionReason: subscription.suspensionReason ?? '',
    cancelledAt: subscription.cancelledAtUtc ?? '',
  };
}

export function planRecord(plan: PlanDto): RecordItem {
  return {
    id: plan.id,
    name: plan.name,
    status: planStatus[plan.status] ?? plan.status,
    apiStatus: plan.status,
    price: plan.price,
    currency: plan.currencyCode,
    interval: plan.billingInterval === 'Yearly' ? 'Annuel' : 'Mensuel',
    features: plan.entitlements.filter(e => e.enabled).map(e => e.featureCode),
  };
}

export function auditRecord(entry: AuditEntryDto, users: Map<string, UserDto>, tenants: Map<string, PlatformTenantRow>): RecordItem {
  const actor = entry.actorType === 'User' && entry.actorId ? users.get(entry.actorId)?.email : undefined;
  return {
    id: entry.id,
    name: entry.action,
    action: entry.action,
    module: entry.action.split('.')[0] ?? '',
    user: actor ?? (entry.actorType ? `${actorType[entry.actorType]}${entry.actorId ? ' ' + shortId(entry.actorId) : ''}` : 'Inconnu'),
    actorType: entry.actorType ? actorType[entry.actorType] : 'Inconnu',
    resource: `${entry.targetType} ${shortId(entry.targetId)}`,
    targetId: entry.targetId,
    tenant: entry.tenantId ? tenants.get(entry.tenantId)?.name ?? shortId(entry.tenantId) : 'Plateforme',
    correlationId: entry.correlationId,
    metadata: Object.entries(entry.metadata ?? {}).map(([key, value]) => `${key} : ${value}`),
    status: 'Enregistré',
    date: entry.occurredAtUtc,
  };
}

export function paymentRecord(payment: SubscriptionPaymentDto, tenants: Map<string, PlatformTenantRow>): RecordItem {
  return {
    id: payment.id,
    name: tenants.get(payment.tenantId)?.name ?? `Restaurant ${shortId(payment.tenantId)}`,
    tenantId: payment.tenantId,
    subscriptionId: payment.subscriptionId,
    plan: payment.planName,
    amount: payment.amount,
    currency: payment.currencyCode,
    periodStart: payment.paidFromUtc,
    end: payment.paidUntilUtc,
    status: 'Payé',
    date: payment.recordedAtUtc,
    lines: (payment.lines ?? []).map(line => `${line.label}${line.quantity > 1 ? ` × ${line.quantity}` : ''}|${line.amount}`),
  };
}
