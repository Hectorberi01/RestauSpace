import {all, api} from './api';
import type {Menu, MenuSummary, ModifierGroup, Product, ProductSummary} from './types';

export interface SellableSection {
  key: string;
  name: string;
  products: ProductSummary[];
}

/**
 * Ce qui se vend dans l'établissement : les sections de ses cartes publiées, avec les produits en vente. Sans carte
 * publiée, tous les produits en vente, pour ne pas bloquer le service pendant la mise en place.
 */
export async function sellableCatalog(locationId: string, signal?: AbortSignal): Promise<{sections: SellableSection[]; published: boolean}> {
  const [menus, products] = await Promise.all([
    api<MenuSummary[]>('/catalog/menus', {query: {locationId}, signal}),
    all<ProductSummary>('/catalog/products', {status: 'Active'}),
  ]);
  // Seulement les produits vendus dans cet établissement et pas en rupture ici.
  const byId = new Map(
    products
      .filter(product => product.isSellable && (!product.locationIds?.length || product.locationIds.includes(locationId)) && !product.unavailableAt?.includes(locationId))
      .map(product => [product.id, product]),
  );
  const published = menus.filter(menu => menu.status === 'Published');
  if (published.length === 0) {
    return {published: false, sections: [{key: 'all', name: 'Tous les produits', products: [...byId.values()].sort((a, b) => a.name.localeCompare(b.name, 'fr'))}]};
  }
  const details = await Promise.all(published.map(menu => api<Menu>(`/catalog/menus/${menu.id}`, {signal})));
  const sections = details.flatMap(menu =>
    menu.sections.map(section => ({
      key: `${menu.id}-${section.id}`,
      name: details.length > 1 ? `${menu.name} · ${section.name}` : section.name,
      products: section.productIds.map(id => byId.get(id)).filter((product): product is ProductSummary => !!product),
    })),
  );
  return {published: true, sections: sections.filter(section => section.products.length > 0)};
}

const productCache = new Map<string, Promise<Product>>();

export function productDetail(id: string): Promise<Product> {
  let cached = productCache.get(id);
  if (!cached) {
    cached = api<Product>(`/catalog/products/${id}`);
    cached.catch(() => productCache.delete(id));
    productCache.set(id, cached);
    window.setTimeout(() => productCache.delete(id), 60_000);
  }
  return cached;
}

export function modifierGroups(): Promise<ModifierGroup[]> {
  return api<ModifierGroup[]>('/catalog/modifier-groups');
}
