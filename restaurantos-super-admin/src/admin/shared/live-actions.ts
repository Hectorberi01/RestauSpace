import {subscriptions,tenants,users,type SubscriptionSuspensionReason,type TenantSuspensionReason} from '../../services/api/admin-api';
import type {RecordItem} from '../../types';

export const tenantReasons: Record<TenantSuspensionReason, string> = {
  PaymentOverdue: 'Impayé',
  TrialExpired: 'Fin de la période d’essai',
  PolicyViolation: 'Non-respect des conditions',
  RequestedByTenant: 'Demande du restaurant',
  Other: 'Autre motif',
};

export const subscriptionReasons: Record<SubscriptionSuspensionReason, string> = {
  PaymentOverdue: 'Impayé',
  TrialExpired: 'Fin de la période d’essai',
  PolicyViolation: 'Non-respect des conditions',
  Other: 'Autre motif',
};

export const reasonFromLabel = <T extends string>(labels: Record<T, string>, label: unknown): T =>
  (Object.entries(labels) as [T, string][]).find(([, value]) => value === label)?.[0] ?? ('Other' as T);

export const can = {
  lockUser: (r: RecordItem) => r.apiStatus === 'Active' || r.apiStatus === 'Pending',
  unlockUser: (r: RecordItem) => r.apiStatus === 'Locked',
  disableUser: (r: RecordItem) => r.apiStatus !== 'Disabled',
  activateTenant: (r: RecordItem) => r.apiStatus === 'Trial',
  suspendTenant: (r: RecordItem) => r.apiStatus === 'Trial' || r.apiStatus === 'Active',
  reactivateTenant: (r: RecordItem) => r.apiStatus === 'Suspended',
  closeTenant: (r: RecordItem) => r.apiStatus !== 'Closed',
  recordPayment: (r: RecordItem) => !r.complimentary && ['Trialing', 'Active', 'PastDue', 'Suspended'].includes(String(r.apiStatus)),
  markPastDue: (r: RecordItem) => !r.complimentary && r.apiStatus === 'Active',
  grantComplimentary: (r: RecordItem) => r.apiStatus !== 'Cancelled',
  endComplimentary: (r: RecordItem) => !!r.complimentary,
  suspendSubscription: (r: RecordItem) => ['Trialing', 'Active', 'PastDue'].includes(String(r.apiStatus)),
  cancelSubscription: (r: RecordItem) => r.apiStatus !== 'Cancelled',
};

export function statusAction(domain: string, r: RecordItem, target: 'Actif' | 'Suspendu'): (() => Promise<unknown>) | null {
  if (domain === 'users') {
    if (target === 'Suspendu' && can.lockUser(r)) return () => users.lock(r.id);
    if (target === 'Actif' && can.unlockUser(r)) return () => users.unlock(r.id);
  }
  if (domain === 'restaurants') {
    if (target === 'Suspendu' && can.suspendTenant(r)) return () => tenants.suspend(r.id, 'Other');
    if (target === 'Actif' && can.reactivateTenant(r)) return () => tenants.reactivate(r.id);
    if (target === 'Actif' && can.activateTenant(r)) return () => tenants.activate(r.id);
  }
  return null;
}

export async function runAll(actions: (() => Promise<unknown>)[]): Promise<{done: number; failed: unknown[]}> {
  const failed: unknown[] = [];
  let done = 0;
  for (const action of actions) {
    try {
      await action();
      done++;
    } catch (error) {
      failed.push(error);
    }
  }
  return {done, failed};
}

export {subscriptions, tenants, users};
