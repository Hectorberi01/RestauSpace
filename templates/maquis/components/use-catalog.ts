'use client';
import {useEffect, useState} from 'react';
import {errorMessage, loadCatalog, type Catalog} from '@/lib/storefront';

/** La carte d'un établissement (null pendant le chargement). */
export function useCatalog(locationId: string | null) {
  const [state, setState] = useState<{id: string; catalog: Catalog | null; error: string | null} | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!locationId) return;
    const controller = new AbortController();
    loadCatalog(locationId, controller.signal)
      .then(catalog => setState({id: locationId, catalog, error: null}))
      .catch(reason => !controller.signal.aborted && setState({id: locationId, catalog: null, error: errorMessage(reason)}));
    return () => controller.abort();
  }, [locationId, attempt]);

  const current = state?.id === locationId ? state : null;
  return {catalog: current?.catalog ?? null, error: current?.error ?? null, retry: () => setAttempt(value => value + 1)};
}

/** Les sections de toutes les cartes, dans l'ordre, avec leurs produits. */
export function sectionsOf(catalog: Catalog) {
  const products = new Map(catalog.products.map(product => [product.id, product]));
  return catalog.menus.flatMap(menu => menu.sections.map(section => ({
    key: `${menu.id}-${section.id}`,
    name: section.name,
    products: section.productIds.map(id => products.get(id)).filter((product): product is NonNullable<typeof product> => !!product),
  }))).filter(section => section.products.length > 0);
}

/** Position stable d'un produit dans la carte (choix de l'illustration et du fond). */
export const productIndex = (catalog: Catalog, productId: string) => Math.max(0, catalog.products.findIndex(product => product.id === productId));
