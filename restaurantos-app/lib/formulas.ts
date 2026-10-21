import type {OrderItem, OrderItemComponent} from './types';

/** Un produit choisi dans une formule, tel que l'API l'attend à l'ajout d'une ligne. */
export interface ComponentChoice {
  stepId: string;
  productId: string;
  variantId: string | null;
  modifierIds: string[];
}

/** « Plat : Poulet braisé (Grande) + Sauce arachide ». */
export function componentLabel(component: OrderItemComponent, withStep = true): string {
  const name = `${component.productName}${component.variantName ? ` (${component.variantName})` : ''}`;
  const options = component.modifiers.length > 0 ? ` + ${component.modifiers.map(modifier => modifier.name).join(', ')}` : '';
  return `${withStep ? `${component.stepName} : ` : ''}${name}${options}`;
}

/** Supplément et options des composants d'une ligne, par unité. */
export const componentsDelta = (item: Pick<OrderItem, 'components'>) =>
  (item.components ?? []).reduce((sum, component) => sum + component.supplement + component.modifiers.reduce((total, modifier) => total + modifier.priceDelta, 0), 0);

/** Prix unitaire d'une ligne : prix du produit, ses options et, pour une formule, ses composants. */
export const itemUnitPrice = (item: OrderItem) =>
  item.unitPrice + item.modifiers.reduce((sum, modifier) => sum + modifier.priceDelta, 0) + componentsDelta(item);

/** Même composition (ordre indifférent), pour regrouper deux lignes identiques d'un panier. */
export function sameComponents(a: ComponentChoice[] | undefined, b: ComponentChoice[] | undefined): boolean {
  const key = (list: ComponentChoice[] | undefined) =>
    (list ?? []).map(item => `${item.stepId}/${item.productId}/${item.variantId ?? ''}/${[...item.modifierIds].sort().join('+')}`).sort().join('|');
  return key(a) === key(b);
}
