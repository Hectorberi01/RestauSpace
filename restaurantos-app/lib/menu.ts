import {all, api, del, post, put} from './api';
import type {AllergenCode, Menu, MenuSummary, OnlineChannel, Product, ProductSummary} from './types';

/** Une catégorie = une section de carte ; même nom dans plusieurs cartes = même catégorie. */
export interface Category {
  key: string;
  name: string;
  productIds: Set<string>;
}

export interface Catalog {
  products: ProductSummary[];
  menus: Menu[];
  /** Carte où les catégories se modifient : la publiée, sinon le premier brouillon. */
  primary: Menu | null;
  categories: Category[];
  categoryOf: Map<string, string[]>;
}

const keyOf = (name: string) => name.trim().toLocaleLowerCase('fr');

export async function loadCatalog(locationId: string, signal?: AbortSignal): Promise<Catalog> {
  const [products, summaries] = await Promise.all([
    all<ProductSummary>('/catalog/products', {}),
    api<MenuSummary[]>('/catalog/menus', {query: {locationId}, signal}),
  ]);
  const live = summaries.filter(menu => menu.status !== 'Archived');
  const menus = await Promise.all(live.map(menu => api<Menu>(`/catalog/menus/${menu.id}`, {signal})));
  const primary = menus.find(menu => menu.status === 'Published') ?? menus[0] ?? null;
  const ordered = primary ? [primary, ...menus.filter(menu => menu.id !== primary.id)] : menus;

  const categories = new Map<string, Category>();
  const categoryOf = new Map<string, string[]>();
  ordered.forEach(menu =>
    menu.sections.forEach(section => {
      const key = keyOf(section.name);
      const category = categories.get(key) ?? {key, name: section.name, productIds: new Set<string>()};
      section.productIds.forEach(id => {
        category.productIds.add(id);
        const names = categoryOf.get(id) ?? [];
        if (!names.includes(category.name)) categoryOf.set(id, [...names, category.name]);
      });
      categories.set(key, category);
    }),
  );
  return {products, menus, primary, categories: [...categories.values()], categoryOf};
}

/**
 * Range le produit dans une section de la carte principale (null : hors carte). Une carte publiée est en lecture seule :
 * elle est retirée, modifiée puis republiée. Refusé si une section se retrouvait vide (la carte ne serait plus publiable).
 */
export async function setCategory(menu: Menu, productId: string, sectionId: string | null): Promise<void> {
  const current = menu.sections.filter(section => section.productIds.includes(productId));
  if (current.length === (sectionId ? 1 : 0) && (!sectionId || current[0]?.id === sectionId)) return;
  const emptied = current.find(section => section.id !== sectionId && section.productIds.length === 1);
  if (emptied && menu.status === 'Published') {
    throw new Error(`La catégorie « ${emptied.name} » serait vide : la carte ne pourrait plus être publiée. Ajoutez-y d’abord un autre produit.`);
  }
  const published = menu.status === 'Published';
  if (published) await post(`/catalog/menus/${menu.id}/unpublish`);
  try {
    for (const section of current) {
      if (section.id !== sectionId) await del(`/catalog/menus/${menu.id}/sections/${section.id}/products/${productId}`);
    }
    if (sectionId && !current.some(section => section.id === sectionId)) {
      await post(`/catalog/menus/${menu.id}/sections/${sectionId}/products`, {productId});
    }
  } finally {
    if (published) await post(`/catalog/menus/${menu.id}/publish`);
  }
}

/** Copie d'un produit (brouillon) : nom, prix, taxe, description, variantes et groupes d'options. */
export const allergenLabel: Record<AllergenCode, string> = {
  gluten: 'Gluten',
  crustaceans: 'Crustacés',
  eggs: 'Œufs',
  fish: 'Poissons',
  peanuts: 'Arachides',
  soybeans: 'Soja',
  milk: 'Lait',
  nuts: 'Fruits à coque',
  celery: 'Céleri',
  mustard: 'Moutarde',
  sesame: 'Sésame',
  sulphites: 'Sulfites',
  lupin: 'Lupin',
  molluscs: 'Mollusques',
};

export const allergenCodes = Object.keys(allergenLabel) as AllergenCode[];

let allergenList: Promise<AllergenCode[]> | null = null;

/** La liste du catalogue (`GET /catalog/allergens`), lue une fois ; la liste connue de la console si l'API ne répond pas. */
export function loadAllergens(): Promise<AllergenCode[]> {
  allergenList ??= api<AllergenCode[]>('/catalog/allergens').catch(() => {
    allergenList = null;
    return allergenCodes;
  });
  return allergenList;
}

/** Libellé d'un allergène ; un code ajouté au catalogue sans traduction garde son code. */
export const allergenName = (code: string) => allergenLabel[code as AllergenCode] ?? code;

export const onlineChannelLabel: Record<OnlineChannel, string> = {Web: 'Site web', MobileApp: 'Application mobile', Kiosk: 'Borne'};

export async function duplicateProduct(product: Product): Promise<string> {
  const created = await post<{id: string}>('/catalog/products', {
    name: `${product.name} (copie)`.slice(0, 120),
    basePrice: product.basePrice,
    taxCategoryId: product.taxCategoryId,
    description: product.description,
  });
  for (const variant of product.variants) {
    await post(`/catalog/products/${created.id}/variants`, {name: variant.name, priceDelta: variant.priceDelta, sku: null});
  }
  for (const groupId of product.modifierGroupIds) {
    await post(`/catalog/products/${created.id}/modifier-groups`, {modifierGroupId: groupId});
  }
  if (product.allergens.length) await put(`/catalog/products/${created.id}/allergens`, {allergens: product.allergens});
  if (product.hiddenChannels.length) await put(`/catalog/products/${created.id}/hidden-channels`, {channels: product.hiddenChannels});
  return created.id;
}

// --- Établissements : un produit est vendu partout (aucun établissement) ou dans certains, avec une rupture possible par établissement.

type Located = {locationIds?: string[]; unavailableAt?: string[]};

export const soldAt = (product: Located, locationId: string) => !product.locationIds?.length || product.locationIds.includes(locationId);

export const outHere = (product: Located, locationId: string) => !!product.unavailableAt?.includes(locationId);

/** Aucun établissement : vendu dans tous. */
export const setProductLocations = (productId: string, locationIds: string[]) => put(`/catalog/products/${productId}/locations`, {locationIds});

export const setAvailableHere = (productId: string, locationId: string, isAvailable: boolean) =>
  put(`/catalog/products/${productId}/locations/${locationId}/availability`, {isAvailable});
