// Vente au comptoir sans réseau : la carte est gardée sur l'appareil, chaque vente est enregistrée localement
// (IndexedDB) puis envoyée à l'API au retour du réseau, étape par étape, avec des clés d'idempotence dérivées de son
// identifiant : rejouer une étape déjà faite ne crée jamais de doublon. Aucune donnée du client n'est enregistrée.

import {api, describe} from './api';
import {sellableCatalog, productDetail, modifierGroups} from './catalog';

export interface OfflineProduct {
  id: string;
  name: string;
  price: number;
  /** Variantes vendables ; vide si le produit n'en a pas. */
  variants: {id: string; name: string; price: number}[];
  /** Le produit exige des options : il se vend par l'écran Commandes, pas en vente rapide. */
  needsOptions: boolean;
}

export interface OfflineCatalog {
  locationId: string;
  currencyCode: string;
  savedAtUtc: string;
  sections: {key: string; name: string; products: OfflineProduct[]}[];
}

export interface OfflineSaleLine {
  productId: string;
  variantId: string | null;
  name: string;
  unitPrice: number;
  quantity: number;
}

export type OfflineSaleStatus = 'pending' | 'synced' | 'failed';

export interface OfflineSale {
  id: string;
  /** Numéro provisoire affiché tant que la commande n'existe pas sur le serveur. */
  localNumber: string;
  locationId: string;
  currencyCode: string;
  lines: OfflineSaleLine[];
  total: number;
  cashGiven: number;
  createdAtUtc: string;
  status: OfflineSaleStatus;
  /** Étapes déjà faites sur le serveur (création, lignes, validation, acceptation, encaissement). */
  done: string[];
  orderId: string | null;
  orderNumber: string | null;
  error: string | null;
}

const DB = 'restaurantos-offline';
const SALES = 'sales';
const CATALOGS = 'catalogs';

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB, 1);
    request.onupgradeneeded = () => {
      request.result.createObjectStore(SALES, {keyPath: 'id'});
      request.result.createObjectStore(CATALOGS, {keyPath: 'locationId'});
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function run<T>(store: string, mode: IDBTransactionMode, action: (objects: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await open();
  try {
    return await new Promise<T>((resolve, reject) => {
      const transaction = db.transaction(store, mode);
      const request = action(transaction.objectStore(store));
      transaction.oncomplete = () => resolve(request.result);
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  } finally {
    db.close();
  }
}

export const saveCatalog = (catalog: OfflineCatalog) => run(CATALOGS, 'readwrite', objects => objects.put(catalog)).then(() => undefined);
export const loadCatalog = (locationId: string) => run<OfflineCatalog | undefined>(CATALOGS, 'readonly', objects => objects.get(locationId)).then(value => value ?? null);
export const saveSale = (sale: OfflineSale) => run(SALES, 'readwrite', objects => objects.put(sale)).then(() => undefined);
export const deleteSale = (id: string) => run(SALES, 'readwrite', objects => objects.delete(id)).then(() => undefined);
export const listSales = () =>
  run<OfflineSale[]>(SALES, 'readonly', objects => objects.getAll()).then(sales => sales.sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc)));

/** La carte vendable de l'établissement, avec variantes et options obligatoires, gardée pour le mode hors ligne. */
export async function refreshCatalog(locationId: string, currencyCode: string): Promise<OfflineCatalog> {
  const [{sections}, groups] = await Promise.all([sellableCatalog(locationId), modifierGroups()]);
  const required = new Set(groups.filter(group => group.minSelections > 0).map(group => group.id));
  const ids = [...new Set(sections.flatMap(section => section.products.map(product => product.id)))];
  const details = new Map((await Promise.all(ids.map(id => productDetail(id)))).map(product => [product.id, product]));
  const catalog: OfflineCatalog = {
    locationId,
    currencyCode,
    savedAtUtc: new Date().toISOString(),
    sections: sections.map(section => ({
      key: section.key,
      name: section.name,
      products: section.products.map(summary => {
        const detail = details.get(summary.id);
        return {
          id: summary.id,
          name: summary.name,
          price: summary.basePrice,
          variants: (detail?.variants ?? []).filter(variant => variant.isAvailable).map(variant => ({id: variant.id, name: variant.name, price: variant.price})),
          needsOptions: (detail?.modifierGroupIds ?? []).some(id => required.has(id)) || (detail?.formulaSteps?.length ?? 0) > 0,
        };
      }),
    })),
  };
  await saveCatalog(catalog);
  return catalog;
}

/** Numéro provisoire lisible : H + heure + 3 caractères de l'identifiant (unique sur l'appareil). */
export const localNumber = (id: string, at = new Date()) =>
  `H${String(at.getHours()).padStart(2, '0')}${String(at.getMinutes()).padStart(2, '0')}-${id.replace(/-/g, '').slice(0, 3).toUpperCase()}`;

/** Erreur de réseau (fetch rejeté) : la vente reste en attente et sera renvoyée. */
export const isNetworkError = (error: unknown) => error instanceof TypeError;

/**
 * Envoie une vente au serveur : commande à emporter, lignes, validation, acceptation (le bon part en cuisine), puis
 * l'encaissement en espèces. Chaque étape faite est notée avant la suivante ; une étape refusée par l'API arrête la vente
 * en échec (à reprendre dans Commandes), une coupure réseau la laisse en attente.
 */
export async function syncSale(sale: OfflineSale): Promise<OfflineSale> {
  const current: OfflineSale = {...sale, done: [...sale.done]};
  const step = async (name: string, action: () => Promise<void>) => {
    if (current.done.includes(name)) return;
    await action();
    current.done.push(name);
    await saveSale(current);
  };
  const key = (name: string) => `offline-${sale.id}-${name}`;
  try {
    await step('create', async () => {
      const created = await api<{id: string}>('/orders', {method: 'POST', body: {locationId: sale.locationId, type: 'Takeaway', tableSessionId: null}, idempotencyKey: key('create')});
      current.orderId = created.id;
    });
    const orderId = current.orderId!;
    for (const [index, line] of sale.lines.entries()) {
      await step(`item-${index}`, () =>
        api('/orders/' + orderId + '/items', {
          method: 'POST',
          body: {productId: line.productId, variantId: line.variantId, quantity: line.quantity, modifierIds: [], note: null},
          idempotencyKey: key(`item-${index}`),
        }));
    }
    await step('place', () => api(`/orders/${orderId}/place`, {method: 'POST', idempotencyKey: key('place')}));
    await step('accept', () => api(`/orders/${orderId}/accept`, {method: 'POST', idempotencyKey: key('accept')}));
    await step('pay', async () => {
      const order = await api<{total: number; number: string}>(`/orders/${orderId}`);
      current.orderNumber = order.number;
      await api('/payments/offline', {
        method: 'POST',
        body: {orderId, provider: 'cash', reference: sale.localNumber, amount: order.total, paymentMethod: 'Cash'},
        idempotencyKey: key('pay'),
      });
    });
    current.status = 'synced';
    current.error = null;
  } catch (error) {
    if (isNetworkError(error)) {
      current.status = 'pending';
    } else {
      current.status = 'failed';
      current.error = describe(error);
    }
  }
  await saveSale(current);
  return current;
}

let syncing: Promise<OfflineSale[]> | null = null;

/** Envoie toutes les ventes en attente, une à la fois, dans l'ordre où elles ont été faites. */
export function syncAll(): Promise<OfflineSale[]> {
  syncing ??= (async () => {
    try {
      const results: OfflineSale[] = [];
      for (const sale of (await listSales()).filter(item => item.status === 'pending')) {
        const result = await syncSale(sale);
        results.push(result);
        if (result.status === 'pending') break;
      }
      return results;
    } finally {
      syncing = null;
    }
  })();
  return syncing;
}
