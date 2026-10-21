import {api, del, post, put, type Page} from './api';

export type Unit = 'Gram' | 'Kilogram' | 'Milliliter' | 'Liter' | 'Piece';
export type MovementType = 'Receipt' | 'Consumption' | 'Adjustment' | 'Waste' | 'Transfer' | 'CountConfirmed';
export type NegativePolicy = 'Forbid' | 'Allow';

export interface Ingredient {
  id: string;
  name: string;
  baseUnit: Unit;
  isActive: boolean;
}

export interface StockItem {
  id: string;
  locationId: string;
  ingredientId: string;
  ingredientName: string | null;
  unit: Unit;
  currentQuantity: number;
  reorderThreshold: number;
  negativeStockPolicy: NegativePolicy;
  isLowStock: boolean;
  /** Coût moyen pondéré d'une unité de l'article (devise du restaurant) ; null tant qu'aucune réception n'est chiffrée. */
  averageUnitCost: number | null;
}

export interface Movement {
  id: string;
  type: MovementType;
  quantityDelta: number;
  unit: Unit;
  occurredAtUtc: string;
  reference: string | null;
  reason: string | null;
  unitCost: number | null;
}

export interface Recipe {
  id: string;
  productId: string;
  variantId: string | null;
  ingredients: {ingredientId: string; ingredientName: string | null; quantity: number; unit: Unit}[];
}

export const units: {value: Unit; label: string; short: string}[] = [
  {value: 'Gram', label: 'Gramme', short: 'g'},
  {value: 'Kilogram', label: 'Kilogramme', short: 'kg'},
  {value: 'Milliliter', label: 'Millilitre', short: 'ml'},
  {value: 'Liter', label: 'Litre', short: 'L'},
  {value: 'Piece', label: 'Pièce', short: 'pce'},
];

/** Unités dans lesquelles une quantité peut être saisie (même dimension que l'unité de l'article). */
export function compatibleUnits(unit: Unit): Unit[] {
  if (unit === 'Gram' || unit === 'Kilogram') return ['Kilogram', 'Gram'];
  if (unit === 'Milliliter' || unit === 'Liter') return ['Liter', 'Milliliter'];
  return ['Piece'];
}

export const unitShort = (unit: Unit) => units.find(item => item.value === unit)?.short ?? unit;

export function quantity(amount: number, unit: Unit): string {
  return `${new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 3}).format(amount)} ${unitShort(unit)}`;
}

export const movementLabel: Record<MovementType, string> = {
  Receipt: 'Réception',
  Consumption: 'Consommation',
  Adjustment: 'Ajustement d’inventaire',
  Waste: 'Perte',
  Transfer: 'Transfert',
  CountConfirmed: 'Inventaire confirmé',
};

export const wasteReasons = ['Périmé', 'Abîmé', 'Erreur de préparation', 'Casse', 'Autre'];

export interface QuantityInput {
  amount: number;
  unit: Unit;
}

export const listIngredients = (includeInactive: boolean, signal?: AbortSignal) =>
  api<Ingredient[]>('/inventory/ingredients', {query: {includeInactive: includeInactive || undefined}, signal});

export const createIngredient = (name: string, baseUnit: Unit) => post<{id: string}>('/inventory/ingredients', {name, baseUnit});

export const renameIngredient = (id: string, name: string) => put(`/inventory/ingredients/${id}/name`, {name});

export const deactivateIngredient = (id: string) => post(`/inventory/ingredients/${id}/deactivate`);

export const listItems = (locationId: string, lowStockOnly: boolean, signal?: AbortSignal) =>
  api<StockItem[]>('/inventory/items', {query: {locationId, lowStockOnly: lowStockOnly || undefined}, signal});

export const listAllItems = (signal?: AbortSignal) => api<StockItem[]>('/inventory/items', {signal});

export const createItem = (locationId: string, ingredientId: string, threshold: QuantityInput | null, negativeStockPolicy: NegativePolicy) =>
  post<{id: string}>('/inventory/items', {locationId, ingredientId, reorderThreshold: threshold, negativeStockPolicy});

export const listMovements = (id: string, page: number, signal?: AbortSignal) =>
  api<Page<Movement>>(`/inventory/items/${id}/movements`, {query: {page, pageSize: 15}, signal});

export const receive = (id: string, input: QuantityInput, reference: string | null, totalCost: number | null = null) =>
  post(`/inventory/items/${id}/receipts`, {quantity: input, reference, totalCost});

export const consume = (id: string, input: QuantityInput, reference: string | null) =>
  post(`/inventory/items/${id}/consumptions`, {quantity: input, reference});

export const setUnitCost = (id: string, unitCost: number | null) => put(`/inventory/items/${id}/unit-cost`, {unitCost});

export const waste = (id: string, input: QuantityInput, reason: string) => post(`/inventory/items/${id}/waste`, {quantity: input, reason});

export const count = (id: string, input: QuantityInput, reason: string) =>
  post(`/inventory/items/${id}/counts`, {countedQuantity: input, reason});

export const setThreshold = (id: string, input: QuantityInput) => put(`/inventory/items/${id}/reorder-threshold`, {threshold: input});

export const setPolicy = (id: string, policy: NegativePolicy) => put(`/inventory/items/${id}/negative-stock-policy`, {policy});

export const transfer = (sourceItemId: string, destinationItemId: string, input: QuantityInput) =>
  post('/inventory/transfers', {sourceItemId, destinationItemId, quantity: input});

export const listRecipes = (signal?: AbortSignal) => api<Recipe[]>('/inventory/recipes', {signal});

export const createRecipe = (productId: string, variantId: string | null) => post<{id: string}>('/inventory/recipes', {productId, variantId});

export const addRecipeIngredient = (id: string, ingredientId: string, input: QuantityInput) =>
  post(`/inventory/recipes/${id}/ingredients`, {ingredientId, quantity: input});

export const changeRecipeQuantity = (id: string, ingredientId: string, input: QuantityInput) =>
  put(`/inventory/recipes/${id}/ingredients/${ingredientId}`, {quantity: input});

export const removeRecipeIngredient = (id: string, ingredientId: string) => del(`/inventory/recipes/${id}/ingredients/${ingredientId}`);

export interface Supplier {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
  isActive: boolean;
}

export interface SupplierInput {
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
}

export const listSuppliers = (signal?: AbortSignal) => api<Supplier[]>('/inventory/suppliers', {signal});

export const createSupplier = (input: SupplierInput) => post<{id: string}>('/inventory/suppliers', input);

export const updateSupplier = (id: string, input: SupplierInput) => put(`/inventory/suppliers/${id}`, input);

export const setSupplierActive = (id: string, isActive: boolean) => put(`/inventory/suppliers/${id}/active`, {isActive});

export type PurchaseStatus = 'Draft' | 'Sent' | 'PartiallyReceived' | 'Received' | 'Cancelled';

export const purchaseStatusLabel: Record<PurchaseStatus, string> = {
  Draft: 'Brouillon',
  Sent: 'Envoyé',
  PartiallyReceived: 'Reçu en partie',
  Received: 'Reçu',
  Cancelled: 'Annulé',
};

export interface PurchaseLine {
  itemId: string;
  ingredientId: string;
  ingredientName: string | null;
  unit: Unit;
  ordered: number;
  unitPrice: number | null;
  received: number;
}

export interface PurchaseOrder {
  id: string;
  number: string;
  locationId: string;
  supplierId: string;
  supplierName: string | null;
  status: PurchaseStatus;
  notes: string | null;
  estimatedTotal: number | null;
  lines: PurchaseLine[];
  createdAtUtc: string;
  sentAtUtc: string | null;
  receivedAtUtc: string | null;
}

/** Une ligne à commander : quantité dans l'unité de l'article, prix d'une unité facultatif. */
export interface PurchaseLineInput {
  itemId: string;
  quantity: QuantityInput;
  unitPrice: number | null;
}

export const listPurchaseOrders = (locationId: string, signal?: AbortSignal) =>
  api<PurchaseOrder[]>('/inventory/purchase-orders', {query: {locationId}, signal});

export const createPurchaseOrder = (locationId: string, supplierId: string, lines: PurchaseLineInput[], notes: string | null) =>
  post<{id: string}>('/inventory/purchase-orders', {locationId, supplierId, lines, notes});

export const composePurchaseOrder = (id: string, lines: PurchaseLineInput[], notes: string | null) =>
  put(`/inventory/purchase-orders/${id}`, {lines, notes});

export const sendPurchaseOrder = (id: string) => post(`/inventory/purchase-orders/${id}/send`);

export const cancelPurchaseOrder = (id: string) => post(`/inventory/purchase-orders/${id}/cancel`);

export const receivePurchaseOrder = (id: string, lines: {itemId: string; quantity: number; unitPrice: number | null}[], close: boolean) =>
  post(`/inventory/purchase-orders/${id}/receipts`, {lines, close});

export interface ReorderSuggestion {
  itemId: string;
  ingredientId: string;
  ingredientName: string | null;
  unit: Unit;
  currentQuantity: number;
  reorderThreshold: number;
  suggestedQuantity: number;
  averageUnitCost: number | null;
}

export const reorderSuggestions = (locationId: string, signal?: AbortSignal) =>
  api<ReorderSuggestion[]>('/inventory/reorder-suggestions', {query: {locationId}, signal});

export interface RecipeCost {
  recipeId: string;
  productId: string;
  variantId: string | null;
  cost: number;
  isComplete: boolean;
  lines: {ingredientId: string; ingredientName: string | null; quantity: number; unit: Unit; cost: number | null}[];
}

export const recipeCosts = (locationId: string, signal?: AbortSignal) =>
  api<RecipeCost[]>('/inventory/recipe-costs', {query: {locationId}, signal});

/** Coût d'une unité d'article : plus de décimales pour les petites unités (gramme, millilitre). */
export function unitCost(cost: number, unit: Unit, currency: string): string {
  const digits = cost >= 100 ? 0 : cost >= 1 ? 2 : 4;
  const formatted = currency
    ? new Intl.NumberFormat('fr-FR', {style: 'currency', currency, minimumFractionDigits: 0, maximumFractionDigits: digits}).format(cost)
    : new Intl.NumberFormat('fr-FR', {maximumFractionDigits: digits}).format(cost);
  return `${formatted} / ${unitShort(unit)}`;
}

/** Prix saisi (virgule acceptée) ; null si vide, NaN si invalide. */
export function parseMoney(value: string): number | null {
  if (!value.trim()) return null;
  const parsed = Number(value.replace(',', '.').replace(/\s/g, ''));
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : Number.NaN;
}

export function parseQuantity(value: string): number | null {
  const parsed = Number(value.replace(',', '.').replace(/\s/g, ''));
  return value.trim() && Number.isFinite(parsed) ? parsed : null;
}
