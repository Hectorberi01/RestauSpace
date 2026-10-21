import {api, post, put, type Page} from './api';
import type {Tone} from './format';
import type {OrderType} from './types';

export type PromotionStatus = 'Draft' | 'Active' | 'Paused' | 'Expired';
export type BenefitType = 'Percentage' | 'FixedAmount';

export interface Benefit {
  type: BenefitType;
  percentage: number | null;
  amount: number | null;
  maxDiscount: number | null;
}

export interface PromotionRules {
  validFromUtc: string;
  validUntilUtc: string;
  benefit: Benefit | null;
  currencyCode: string | null;
  minimumOrderAmount: number | null;
  productIds: string[];
  locationIds: string[];
  orderTypes: OrderType[];
  usageLimit: number | null;
  requiresCoupon: boolean;
}

export interface Promotion {
  id: string;
  name: string;
  status: PromotionStatus;
  rules: PromotionRules;
  usageCount: number;
  createdAtUtc: string;
  expiredAtUtc: string | null;
}

export interface Coupon {
  id: string;
  promotionId: string;
  code: string;
  maxUses: number;
  usageCount: number;
  remainingUses: number;
  status: 'Active' | 'Disabled' | 'Exhausted';
  createdAtUtc: string;
}

export interface DiscountPreview {
  promotionId: string;
  name: string;
  isEligible: boolean;
  reason: string | null;
  discount: number;
  currencyCode: string;
}

export const promotionStatusLabel: Record<PromotionStatus, string> = {Draft: 'Brouillon', Active: 'Active', Paused: 'En pause', Expired: 'Terminée'};
export const promotionStatusTone: Record<PromotionStatus, Tone> = {Draft: 'neutral', Active: 'green', Paused: 'amber', Expired: 'red'};

export const couponStatusLabel: Record<string, string> = {Active: 'Actif', Disabled: 'Désactivé', Exhausted: 'Épuisé'};

/** Pourquoi une promotion ne s'applique pas à une commande (codes stables du backend). */
export const ineligibleReason: Record<string, string> = {
  'promotions.discount.no_benefit': 'Aucun avantage défini.',
  'promotions.discount.coupon_required': 'Réservée aux détenteurs d’un code.',
  'promotions.discount.outside_validity_period': 'Hors de sa période de validité.',
  'promotions.discount.location_not_eligible': 'Pas valable dans cet établissement.',
  'promotions.discount.order_type_not_eligible': 'Pas valable pour ce type de commande.',
  'promotions.discount.currency_mismatch': 'Montants dans une autre devise.',
  'promotions.discount.minimum_order_amount_not_reached': 'Montant minimum non atteint.',
  'promotions.discount.no_eligible_amount': 'Aucun article concerné dans la commande.',
  'promotions.discount.zero_discount': 'La remise serait nulle.',
  'promotions.promotion.usage_limit_reached': 'Limite d’utilisation atteinte.',
  'promotions.promotion.already_applied_to_order': 'Déjà appliquée à cette commande.',
  'promotions.promotion.not_active': 'Promotion inactive.',
};

export interface RulesInput {
  validFromUtc: string;
  validUntilUtc: string;
  benefit: {type: BenefitType; percentage: number | null; amount: number | null; maxDiscount: number | null} | null;
  currencyCode: string | null;
  minimumOrderAmount: number | null;
  productIds: string[];
  locationIds: string[];
  orderTypes: OrderType[];
  usageLimit: number | null;
  requiresCoupon: boolean;
}

export const listPromotions = (status: PromotionStatus | '', page: number, signal?: AbortSignal) =>
  api<Page<Promotion>>('/promotions', {query: {status: status || undefined, page, pageSize: 50}, signal});

export const loadPromotion = (id: string, signal?: AbortSignal) => api<Promotion>(`/promotions/${id}`, {signal});

export const createPromotion = (name: string, rules: RulesInput) => post<{id: string}>('/promotions', {name, rules});

export const changeRules = (id: string, rules: RulesInput) => put(`/promotions/${id}/rules`, {rules});

export const promotionAction = (id: string, action: 'activate' | 'pause' | 'resume' | 'expire') => post(`/promotions/${id}/${action}`);

export const listCoupons = (id: string, signal?: AbortSignal) => api<Coupon[]>(`/promotions/${id}/coupons`, {signal});

export const createCoupon = (id: string, code: string | null, maxUses: number) => post<{id: string}>(`/promotions/${id}/coupons`, {code, maxUses});

export const disableCoupon = (couponId: string) => post(`/promotions/coupons/${couponId}/disable`);

export const previewDiscounts = (orderId: string, couponCode: string | null, signal?: AbortSignal) =>
  api<DiscountPreview[]>(`/promotions/orders/${orderId}/preview`, {query: {couponCode: couponCode || undefined}, signal});

export const applyPromotion = (orderId: string, target: {promotionId: string} | {couponCode: string}) =>
  post<{discountAmount: number; currencyCode: string}>(`/promotions/orders/${orderId}/apply`, target);

/** Résumé lisible de l'avantage : « −15 % (max 2 000 F CFA) », « −1 000 F CFA ». */
export function benefitLabel(rules: PromotionRules, format: (amount: number, currency: string) => string): string {
  const benefit = rules.benefit;
  if (!benefit) return 'Avantage à définir';
  const currency = rules.currencyCode ?? '';
  if (benefit.type === 'Percentage') {
    return `−${benefit.percentage} %${benefit.maxDiscount ? ` (max ${format(benefit.maxDiscount, currency)})` : ''}`;
  }
  return `−${format(benefit.amount ?? 0, currency)}`;
}

// ---------- Fidélité

export type AccountStatus = 'Active' | 'Suspended' | 'Closed';
export type TransactionType = 'Earn' | 'Redeem' | 'Adjust' | 'Expire' | 'Reversal';

export interface LoyaltyProgram {
  isEnabled: boolean;
  pointsPerCurrencyUnit: number;
  autoEnroll: boolean;
  inactivityExpiryDays: number | null;
}

export interface Reward {
  id: string;
  name: string;
  pointCost: number;
  discountAmount: number;
  currencyCode: string;
  status: 'Active' | 'Inactive';
}

export interface LoyaltyAccount {
  id: string;
  customerId: string;
  balance: number;
  status: AccountStatus;
  openedAtUtc: string;
  closedAtUtc: string | null;
}

export interface LoyaltyTransaction {
  id: string;
  type: TransactionType;
  direction: 'Credit' | 'Debit';
  points: number;
  signedPoints: number;
  reference: string | null;
  reason: string | null;
  reversedTransactionId: string | null;
  occurredAtUtc: string;
}

export const accountStatusLabel: Record<AccountStatus, string> = {Active: 'Actif', Suspended: 'Suspendu', Closed: 'Clos'};
export const accountStatusTone: Record<AccountStatus, Tone> = {Active: 'green', Suspended: 'amber', Closed: 'red'};

export const transactionLabel: Record<TransactionType, string> = {
  Earn: 'Points gagnés',
  Redeem: 'Récompense utilisée',
  Adjust: 'Ajustement',
  Expire: 'Points expirés',
  Reversal: 'Annulation',
};

/** Motifs écrits par les réactions automatiques. */
export const systemReason: Record<string, string> = {
  order_cancelled: 'Commande annulée',
  customer_anonymized: 'Client anonymisé',
  discount_not_applied: 'Remise non appliquée',
};

export const loadProgram = (signal?: AbortSignal) => api<LoyaltyProgram>('/loyalty/program', {signal});

export const saveProgram = (program: LoyaltyProgram) => put('/loyalty/program', program);

export const listRewards = (activeOnly: boolean, signal?: AbortSignal) =>
  api<Reward[]>('/loyalty/rewards', {query: {activeOnly: activeOnly || undefined}, signal});

export const createReward = (name: string, pointCost: number, discountAmount: number, currencyCode: string) =>
  post<{id: string}>('/loyalty/rewards', {name, pointCost, discountAmount, currencyCode});

export const changeRewardCost = (id: string, pointCost: number) => put(`/loyalty/rewards/${id}/cost`, {pointCost});

export const changeRewardValue = (id: string, discountAmount: number, currencyCode: string) =>
  put(`/loyalty/rewards/${id}/discount-value`, {discountAmount, currencyCode});

export const rewardAction = (id: string, action: 'activate' | 'deactivate') => post(`/loyalty/rewards/${id}/${action}`);

export const listAccounts = (status: AccountStatus | '', page: number, signal?: AbortSignal) =>
  api<Page<LoyaltyAccount>>('/loyalty/accounts', {query: {status: status || undefined, page, pageSize: 25}, signal});

export const openAccount = (customerId: string) => post<{id: string}>('/loyalty/accounts', {customerId});

export const accountOfCustomer = (customerId: string, signal?: AbortSignal) =>
  api<LoyaltyAccount>(`/loyalty/accounts/by-customer/${customerId}`, {signal});

export const listTransactions = (id: string, page: number, signal?: AbortSignal) =>
  api<Page<LoyaltyTransaction>>(`/loyalty/accounts/${id}/transactions`, {query: {page, pageSize: 10}, signal});

export const adjustPoints = (id: string, points: number, direction: 'Credit' | 'Debit', reason: string) =>
  post(`/loyalty/accounts/${id}/adjustments`, {points, direction, reason, reference: crypto.randomUUID()});

export const accountAction = (id: string, action: 'suspend' | 'reactivate') => post(`/loyalty/accounts/${id}/${action}`);

export const closeAccount = (id: string, reason: string) => post(`/loyalty/accounts/${id}/close`, {reason});

export const redeemReward = (accountId: string, rewardId: string, orderId: string) =>
  post<{points: number; discountAmount: number; balanceAfter: number}>(`/loyalty/accounts/${accountId}/rewards/${rewardId}/redeem`, {orderId});

/** Le compte du client, ou null s'il n'en a pas (404). */
export async function findAccountOfCustomer(customerId: string, signal?: AbortSignal): Promise<LoyaltyAccount | null> {
  try {
    return await accountOfCustomer(customerId, signal);
  } catch (error) {
    if ((error as {status?: number}).status === 404) return null;
    throw error;
  }
}

export type StoredValueKind = 'GiftCard' | 'StoreCredit';
export type StoredValueStatus = 'Active' | 'Cancelled';
export type StoredValueMovementType = 'Issue' | 'Redeem' | 'Credit' | 'Reversal' | 'Cancel';

export interface StoredValueMovement {
  id: string;
  type: StoredValueMovementType;
  amount: number;
  balanceAfter: number;
  reversedMovementId: string | null;
  occurredAtUtc: string;
}

/** Une carte cadeau ou un avoir : jamais son code, seulement ses 4 derniers caractères. */
export interface StoredValueCard {
  id: string;
  kind: StoredValueKind;
  codeHint: string;
  initialAmount: number;
  balance: number;
  currencyCode: string;
  status: StoredValueStatus;
  isExpired: boolean;
  isUsable: boolean;
  expiresAtUtc: string | null;
  customerId: string | null;
  note: string | null;
  issuedAtUtc: string;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  movements: StoredValueMovement[];
}

export const storedValueKindLabel: Record<StoredValueKind, string> = {GiftCard: 'Carte cadeau', StoreCredit: 'Avoir'};

export const movementLabel: Record<StoredValueMovementType, string> = {
  Issue: 'Émission',
  Redeem: 'Paiement',
  Credit: 'Remboursement',
  Reversal: 'Annulation',
  Cancel: 'Carte annulée',
};

export const listStoredValueCards = (kind: StoredValueKind | '', page: number, signal?: AbortSignal) =>
  api<Page<StoredValueCard>>('/loyalty/stored-value-cards', {query: {kind: kind || undefined, page, pageSize: 25}, signal});

export const loadStoredValueCard = (id: string, signal?: AbortSignal) => api<StoredValueCard>(`/loyalty/stored-value-cards/${id}`, {signal});

/** Le code passe dans le corps, jamais dans l'URL. */
export const lookupStoredValueCard = (code: string) => post<StoredValueCard>('/loyalty/stored-value-cards/lookup', {code});

/** Le code complet n'est renvoyé qu'ici, une seule fois. */
export const issueStoredValueCard = (input: {kind: StoredValueKind; amount: number; currencyCode: string; expiresAtUtc?: string | null; customerId?: string | null; note?: string | null}) =>
  post<{id: string; code: string; codeHint: string}>('/loyalty/stored-value-cards', input);

export const cancelStoredValueCard = (id: string, reason: string) => post(`/loyalty/stored-value-cards/${id}/cancel`, {reason});
