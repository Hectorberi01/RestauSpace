'use client';
import {useCallback, useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {ArrowLeft, CloudOff, Minus, Plus, RefreshCw, ShoppingBasket, Trash2, Wifi} from 'lucide-react';
import {money, parseAmount} from '@/lib/format';
import {
  deleteSale, listSales, loadCatalog, localNumber, refreshCatalog, saveSale, syncAll,
  type OfflineCatalog, type OfflineProduct, type OfflineSale, type OfflineSaleLine,
} from '@/lib/offline';
import {notifyOfflineSales, useOnline} from '@/components/connection';
import {useConsole, WithLocation} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Input, Modal, Spinner, cx, useToast} from '@/components/ui';

/**
 * Vente rapide au comptoir, à emporter et payée en espèces. Fonctionne sans réseau : la carte est celle gardée sur
 * l'appareil, chaque vente est notée localement avec un numéro provisoire puis envoyée dès que le réseau revient.
 */
export default function QuickSalePage() {
  const {can} = useConsole();
  if (!can('orders.take') || !can('orders.process') || !can('payments.collect')) {
    return <Alert tone="amber">La vente rapide demande de pouvoir prendre, traiter et encaisser les commandes.</Alert>;
  }
  return <WithLocation>{location => <QuickSale locationId={location.id} locationName={location.name} />}</WithLocation>;
}

function QuickSale({locationId, locationName}: {locationId: string; locationName: string}) {
  const {currency} = useConsole();
  const toast = useToast();
  const online = useOnline();
  const [catalog, setCatalog] = useState<OfflineCatalog | null | undefined>();
  const [section, setSection] = useState<string>('');
  const [cart, setCart] = useState<OfflineSaleLine[]>([]);
  const [cash, setCash] = useState('');
  const [choosing, setChoosing] = useState<OfflineProduct | null>(null);
  const [sales, setSales] = useState<OfflineSale[]>([]);
  const [busy, setBusy] = useState(false);
  const [syncing, setSyncing] = useState(false);

  const reloadSales = useCallback(async () => {
    try {
      setSales((await listSales()).filter(sale => sale.locationId === locationId));
    } catch {
      setSales([]);
    }
  }, [locationId]);

  useEffect(() => {
    let active = true;
    void loadCatalog(locationId).then(saved => active && setCatalog(current => current ?? saved)).catch(() => active && setCatalog(null));
    if (navigator.onLine) {
      refreshCatalog(locationId, currency)
        .then(fresh => active && setCatalog(fresh))
        .catch(() => undefined);
    }
    void reloadSales();
    return () => {
      active = false;
    };
  }, [locationId, currency, reloadSales]);

  useEffect(() => {
    if (catalog && !catalog.sections.some(item => item.key === section)) setSection(catalog.sections[0]?.key ?? '');
  }, [catalog, section]);

  const sync = useCallback(async () => {
    if (!navigator.onLine) return;
    setSyncing(true);
    try {
      const results = await syncAll();
      const done = results.filter(result => result.status === 'synced');
      if (done.length > 0) toast('success', done.length === 1 ? `Vente ${done[0].localNumber} envoyée : commande ${done[0].orderNumber ?? ''}.` : `${done.length} ventes envoyées.`);
      const failed = results.find(result => result.status === 'failed');
      if (failed) toast('error', `Vente ${failed.localNumber} refusée : ${failed.error}`);
    } finally {
      setSyncing(false);
      notifyOfflineSales();
      await reloadSales();
    }
  }, [reloadSales, toast]);

  useEffect(() => {
    if (online) void sync();
  }, [online, sync]);

  const total = cart.reduce((sum, line) => sum + line.unitPrice * line.quantity, 0);
  const given = cash.trim() === '' ? total : parseAmount(cash);
  const change = given !== null ? given - total : null;
  const fmt = (value: number) => money(value, catalog?.currencyCode ?? currency);
  const products = useMemo(() => catalog?.sections.find(item => item.key === section)?.products ?? [], [catalog, section]);

  const add = (product: OfflineProduct, variant?: {id: string; name: string; price: number}) => {
    setCart(current => {
      const variantId = variant?.id ?? null;
      const existing = current.findIndex(line => line.productId === product.id && line.variantId === variantId);
      if (existing >= 0) return current.map((line, index) => (index === existing ? {...line, quantity: line.quantity + 1} : line));
      return [...current, {productId: product.id, variantId, name: variant ? `${product.name} (${variant.name})` : product.name, unitPrice: variant?.price ?? product.price, quantity: 1}];
    });
  };

  const change1 = (index: number, delta: number) =>
    setCart(current => current.flatMap((line, at) => (at !== index ? [line] : line.quantity + delta <= 0 ? [] : [{...line, quantity: line.quantity + delta}])));

  const checkout = async () => {
    if (cart.length === 0 || given === null || given < total) return;
    setBusy(true);
    try {
      const id = crypto.randomUUID();
      const sale: OfflineSale = {
        id, localNumber: localNumber(id), locationId, currencyCode: catalog?.currencyCode ?? currency, lines: cart, total, cashGiven: given,
        createdAtUtc: new Date().toISOString(), status: 'pending', done: [], orderId: null, orderNumber: null, error: null,
      };
      await saveSale(sale);
      setCart([]);
      setCash('');
      toast('success', `Vente ${sale.localNumber} enregistrée${change && change > 0 ? ` · rendu ${fmt(change)}` : ''}.`);
      notifyOfflineSales();
      await reloadSales();
      if (navigator.onLine) await sync();
    } catch {
      toast('error', 'Impossible d’enregistrer la vente sur cet appareil.');
    } finally {
      setBusy(false);
    }
  };

  const waiting = sales.filter(sale => sale.status !== 'synced');

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-3">
        <Link href="/caisse" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Caisse</Link>
        <h1 className="text-2xl leading-tight font-bold tracking-tight text-ink-900">Vente rapide</h1>
        <span className="text-sm text-ink-500">{locationName} · à emporter · espèces</span>
        <span className="ml-auto flex items-center gap-2">
          {online ? <Badge tone="green"><Wifi className="mr-1 inline size-3.5" aria-hidden="true" />En ligne</Badge> : <Badge tone="red"><CloudOff className="mr-1 inline size-3.5" aria-hidden="true" />Hors ligne</Badge>}
          {waiting.length > 0 && (
            <Button size="sm" variant="secondary" busy={syncing} disabled={!online} onClick={() => void sync()}>
              {!syncing && <RefreshCw className="size-4" aria-hidden="true" />}Envoyer ({waiting.filter(sale => sale.status === 'pending').length})
            </Button>
          )}
        </span>
      </div>

      {!online && <Alert tone="amber">Pas de réseau : les ventes sont gardées sur cet appareil avec un numéro provisoire et partiront automatiquement au retour du réseau. Ne videz pas les données du navigateur.</Alert>}

      {catalog === undefined ? <Spinner /> : catalog === null ? (
        <Empty icon={<CloudOff className="size-8" />} title="Carte non disponible hors ligne">Ouvrez cette page une fois avec le réseau pour garder la carte sur l’appareil.</Empty>
      ) : (
        <div className="grid gap-5 lg:grid-cols-[1fr_22rem]">
          <div className="min-w-0 space-y-4">
            <div role="tablist" aria-label="Sections" className="flex gap-2 overflow-x-auto pb-1">
              {catalog.sections.map(item => (
                <button key={item.key} type="button" role="tab" aria-selected={item.key === section} onClick={() => setSection(item.key)}
                  className={cx('shrink-0 rounded-xl px-4 py-2 text-sm font-semibold', item.key === section ? 'bg-brand-600 text-white' : 'bg-white text-ink-700 ring-1 ring-slate-200')}>
                  {item.name}
                </button>
              ))}
            </div>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
              {products.map(product => (
                <button key={product.id} type="button" disabled={product.needsOptions}
                  onClick={() => (product.variants.length > 0 ? setChoosing(product) : add(product))}
                  className="flex min-h-24 flex-col justify-between rounded-2xl border border-slate-200 bg-white p-3 text-left shadow-sm transition active:scale-[0.98] enabled:hover:border-brand-400 disabled:opacity-50">
                  <span className="line-clamp-2 font-semibold">{product.name}</span>
                  <span className="text-sm text-ink-600">{product.needsOptions ? 'Options : via Commandes' : product.variants.length > 0 ? `${product.variants.length} choix` : fmt(product.price)}</span>
                </button>
              ))}
            </div>
            <p className="text-xs text-ink-500">Carte gardée le {new Date(catalog.savedAtUtc).toLocaleString('fr-FR')}.</p>
          </div>

          <Card className="flex h-fit flex-col p-4 lg:sticky lg:top-24">
            <h2 className="mb-3 flex items-center gap-2 font-semibold"><ShoppingBasket className="size-5" aria-hidden="true" />Panier</h2>
            {cart.length === 0 ? <p className="py-6 text-center text-sm text-ink-500">Touchez un produit pour l’ajouter.</p> : (
              <ul className="divide-y divide-slate-100">
                {cart.map((line, index) => (
                  <li key={`${line.productId}-${line.variantId}`} className="flex items-center gap-2 py-2 text-sm">
                    <span className="min-w-0 flex-1"><span className="block truncate font-medium">{line.name}</span><span className="text-xs text-ink-500">{fmt(line.unitPrice)}</span></span>
                    <button type="button" className="grid size-8 place-items-center rounded-lg ring-1 ring-slate-200" aria-label={`Retirer un ${line.name}`} onClick={() => change1(index, -1)}>
                      {line.quantity === 1 ? <Trash2 className="size-4" /> : <Minus className="size-4" />}
                    </button>
                    <span className="w-6 text-center font-semibold tabular-nums">{line.quantity}</span>
                    <button type="button" className="grid size-8 place-items-center rounded-lg ring-1 ring-slate-200" aria-label={`Ajouter un ${line.name}`} onClick={() => change1(index, 1)}><Plus className="size-4" /></button>
                  </li>
                ))}
              </ul>
            )}
            <div className="mt-3 space-y-3 border-t border-slate-200 pt-3">
              <p className="flex justify-between text-lg font-bold"><span>Total</span><span className="tabular-nums">{fmt(total)}</span></p>
              <label className="block text-sm">
                <span className="mb-1 block font-medium">Espèces reçues</span>
                <Input inputMode="decimal" placeholder={String(total)} value={cash} onChange={event => setCash(event.target.value)} />
              </label>
              {given !== null && given >= total && total > 0 && <p className="flex justify-between text-sm"><span>À rendre</span><strong className="tabular-nums">{fmt(given - total)}</strong></p>}
              {given !== null && given < total && <p className="text-sm text-red-700">Montant reçu insuffisant.</p>}
              <Button size="lg" className="w-full" busy={busy} disabled={cart.length === 0 || given === null || given < total} onClick={() => void checkout()}>Encaisser {total > 0 ? fmt(total) : ''}</Button>
            </div>
          </Card>
        </div>
      )}

      {waiting.length > 0 && (
        <Card className="p-4">
          <h2 className="mb-2 font-semibold">Ventes de cet appareil à envoyer</h2>
          <ul className="divide-y divide-slate-100 text-sm">
            {waiting.map(sale => (
              <li key={sale.id} className="flex flex-wrap items-center gap-2 py-2">
                <span className="font-mono font-semibold">{sale.localNumber}</span>
                <span className="text-ink-500">{new Date(sale.createdAtUtc).toLocaleTimeString('fr-FR', {hour: '2-digit', minute: '2-digit'})} · {fmt(sale.total)}</span>
                {sale.status === 'pending' ? <Badge tone="amber">En attente</Badge> : <Badge tone="red">Refusée</Badge>}
                {sale.error && <span className="w-full text-xs text-red-700 sm:w-auto">{sale.error}</span>}
                {sale.status === 'failed' && (
                  <span className="ml-auto flex gap-2">
                    {sale.orderId && <Link href={`/commandes/${sale.orderId}`} className="text-sm font-semibold text-brand-700">Ouvrir la commande</Link>}
                    <Button size="sm" variant="ghost" onClick={() => void (async () => {
                      await saveSale({...sale, status: 'pending', error: null});
                      notifyOfflineSales();
                      await reloadSales();
                      await sync();
                    })()}>Réessayer</Button>
                    <Button size="sm" variant="ghost" className="text-red-600" onClick={() => void (async () => {
                      await deleteSale(sale.id);
                      notifyOfflineSales();
                      await reloadSales();
                    })()}>Oublier</Button>
                  </span>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}

      {choosing && (
        <Modal open onClose={() => setChoosing(null)} title={choosing.name}>
          <div className="grid gap-2 sm:grid-cols-2">
            {choosing.variants.map(variant => (
              <button key={variant.id} type="button" onClick={() => {
                add(choosing, variant);
                setChoosing(null);
              }} className="flex items-center justify-between rounded-xl border border-slate-200 px-4 py-3 text-left hover:border-brand-400">
                <span className="font-medium">{variant.name}</span><span className="text-sm text-ink-600">{fmt(variant.price)}</span>
              </button>
            ))}
          </div>
        </Modal>
      )}
    </div>
  );
}
