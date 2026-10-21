'use client';
import {useCallback, useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {
  ArrowLeft, Banknote, Bike, CheckCircle2, ChefHat, CloudOff, CreditCard, Gift, Lock, Minus, Plus, Receipt, ReceiptText, ShoppingBag, Smartphone, Trash2,
  Utensils, Wallet, X,
} from 'lucide-react';
import {api, describe, type Page} from '@/lib/api';
import {modifierGroups, productDetail, sellableCatalog, type SellableSection} from '@/lib/catalog';
import {money, parseAmount} from '@/lib/format';
import {sameComponents} from '@/lib/formulas';
import {tablesBySession, type TableInfo} from '@/lib/orders';
import {counterReference, loadConfigurations, loadOrderPayments, methodLabel, recordOffline, tendersOf, type OrderPayments, type Tender} from '@/lib/payments';
import type {ModifierGroup, Order, OrderSummary, Product, ProductSummary} from '@/lib/types';
import {CollectDialog} from '@/app/(console)/commandes/[id]/payments';
import {useOnline} from './connection';
import {useConsole, useSession} from './console';
import {ItemOptions, type ItemChoice, type ItemSummary} from './item-options';
import {Alert, Badge, Button, Empty, Modal, Spinner, cx, useToast} from './ui';

interface CartLine extends ItemChoice {
  key: string;
  name: string;
  details: string[];
  unitPrice: number;
}

type Service = {type: 'Takeaway'} | {type: 'DineIn'; tableSessionId: string | null};

interface Sale {
  id: string;
  orderId: string | null;
  number: string | null;
  done: string[];
}

interface Done {
  orderId: string;
  number: string;
  change: number | null;
  paid: boolean;
}

const newSale = (): Sale => ({id: crypto.randomUUID(), orderId: null, number: null, done: []});

const sameChoice = (line: CartLine, choice: ItemChoice) =>
  line.productId === choice.productId && line.variantId === choice.variantId && line.note === choice.note
  && line.modifierIds.length === choice.modifierIds.length && line.modifierIds.every(id => choice.modifierIds.includes(id))
  && sameComponents(line.components, choice.components);

/** Montants proposés pour les espèces : le compte juste puis les billets ronds au-dessus (francs CFA). */
function cashSuggestions(total: number): number[] {
  const steps = [500, 1000, 2000, 5000, 10000];
  const values = new Set<number>([total]);
  steps.forEach(step => values.add(Math.ceil(total / step) * step));
  return [...values].filter(value => value >= total).sort((a, b) => a - b).slice(0, 5);
}

const tenderIcon = (tender: Tender) => (tender.method === 'Cash' ? Banknote : tender.method === 'CardOnSite' ? CreditCard : tender.method === 'MobileMoneyOnSite' ? Smartphone : tender.method === 'GiftCard' ? Gift : tender.method === 'DeliveryPlatform' ? Bike : Wallet);

/**
 * Caisse tactile : tuiles des produits par section, panier, puis « Encaisser » et le moyen de paiement (espèces au
 * compte juste : trois touches). « Envoyer » part en cuisine sans encaisser (service à table) ; « Additions » reprend
 * les commandes à régler de l'établissement.
 */
export function TouchRegister({onLock}: {onLock?: () => void}) {
  const {can, location, setLocationId} = useConsole();
  const session = useSession();
  const operator = session?.operator;

  useEffect(() => {
    if (operator && location?.id !== operator.locationId) setLocationId(operator.locationId);
  }, [operator, location?.id, setLocationId]);

  if (!can('orders.take')) {
    return <div className="p-6"><Alert tone="amber">La caisse demande de pouvoir prendre les commandes.</Alert>{onLock && <Button className="mt-4" onClick={onLock}>Verrouiller</Button>}</div>;
  }
  if (!location || (operator && location.id !== operator.locationId)) return <Spinner label="Ouverture de la caisse…" />;
  return <Register key={location.id} locationId={location.id} locationName={location.name} operatorName={operator?.name ?? null} onLock={onLock} />;
}

function Register({locationId, locationName, operatorName, onLock}: {locationId: string; locationName: string; operatorName: string | null; onLock?: () => void}) {
  const {can, currency, tenant} = useConsole();
  const toast = useToast();
  const online = useOnline();
  const canPay = can('payments.collect');
  const canProcess = can('orders.process');

  const [sections, setSections] = useState<SellableSection[] | null>(null);
  const [groups, setGroups] = useState<ModifierGroup[]>([]);
  const [tenders, setTenders] = useState<Tender[]>([]);
  const [tables, setTables] = useState<Map<string, TableInfo>>(new Map());
  const [error, setError] = useState<string | null>(null);
  const [section, setSection] = useState('');
  const [cart, setCart] = useState<CartLine[]>([]);
  const [service, setService] = useState<Service>({type: 'Takeaway'});
  const [sale, setSale] = useState<Sale>(newSale);
  const [choosing, setChoosing] = useState<Product | null>(null);
  const [opening, setOpening] = useState<string | null>(null);
  const [paying, setPaying] = useState(false);
  const [pickingTable, setPickingTable] = useState(false);
  const [bills, setBills] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [done, setDone] = useState<Done | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([
      sellableCatalog(locationId, controller.signal),
      modifierGroups().catch(() => [] as ModifierGroup[]),
      canPay ? loadConfigurations(controller.signal).catch(() => []) : Promise.resolve([]),
      tablesBySession(locationId, controller.signal).catch(() => new Map<string, TableInfo>()),
    ])
      .then(([catalog, loadedGroups, configurations, loadedTables]) => {
        setSections(catalog.sections);
        setSection(catalog.sections[0]?.key ?? '');
        setGroups(loadedGroups);
        const onSite = tendersOf(configurations, currency).filter(tender => tender.type === 'OnSite');
        setTenders(onSite.length > 0 ? onSite : [{key: 'cash:Cash', provider: 'cash', method: 'Cash', type: 'OnSite', label: 'Espèces'}]);
        setTables(loadedTables);
      })
      .catch(err => !controller.signal.aborted && setError(describe(err)));
    return () => controller.abort();
  }, [locationId, currency, canPay]);

  const products = useMemo(() => sections?.find(item => item.key === section)?.products ?? [], [sections, section]);

  // Les détails (variantes, options) des produits affichés sont chargés d'avance : la touche répond tout de suite.
  useEffect(() => {
    products.slice(0, 40).forEach(product => void productDetail(product.id).catch(() => undefined));
  }, [products]);

  const total = cart.reduce((sum, line) => sum + line.unitPrice * line.quantity, 0);
  const fmt = (value: number) => money(value, currency);
  const tableLabel = service.type === 'DineIn' && service.tableSessionId ? tables.get(service.tableSessionId) : undefined;
  const locked = sale.orderId !== null;

  const addLine = (choice: ItemChoice, summary: ItemSummary, name: string) =>
    setCart(current => {
      const existing = current.findIndex(line => sameChoice(line, choice));
      if (existing >= 0) return current.map((line, index) => (index === existing ? {...line, quantity: line.quantity + choice.quantity} : line));
      return [...current, {...choice, key: crypto.randomUUID(), name, details: summary.details, unitPrice: summary.unitPrice}];
    });

  const tap = async (summary: ProductSummary) => {
    if (locked) return;
    setOpening(summary.id);
    try {
      const product = await productDetail(summary.id);
      const needsChoice = product.variants.some(variant => variant.isAvailable) || product.modifierGroupIds.length > 0 || (product.formulaSteps?.length ?? 0) > 0;
      if (needsChoice) setChoosing(product);
      else addLine({productId: product.id, quantity: 1, variantId: null, modifierIds: [], note: null}, {unitPrice: product.basePrice, details: []}, product.name);
    } catch (err) {
      toast('error', describe(err));
    } finally {
      setOpening(null);
    }
  };

  const changeQuantity = (key: string, delta: number) =>
    setCart(current => current.flatMap(line => (line.key !== key ? [line] : line.quantity + delta <= 0 ? [] : [{...line, quantity: line.quantity + delta}])));

  const reset = () => {
    setCart([]);
    setSale(newSale());
    setService({type: 'Takeaway'});
  };

  /**
   * Crée la commande du panier, étape par étape, avec des clés d'idempotence propres à la vente : une étape rejouée
   * après une coupure ne crée jamais de doublon.
   */
  const ensureOrder = async (): Promise<{id: string; number: string; total: number}> => {
    const current: Sale = {...sale, done: [...sale.done]};
    const key = (step: string) => `pos-${current.id}-${step}`;
    const step = async (name: string, action: () => Promise<void>) => {
      if (current.done.includes(name)) return;
      await action();
      current.done.push(name);
      setSale({...current, done: [...current.done]});
    };
    await step('create', async () => {
      const created = await api<{id: string}>('/orders', {
        method: 'POST',
        body: {locationId, type: service.type, tableSessionId: service.type === 'DineIn' ? service.tableSessionId : null},
        idempotencyKey: key('create'),
      });
      current.orderId = created.id;
    });
    const orderId = current.orderId!;
    for (const [index, line] of cart.entries()) {
      await step(`item-${index}`, async () => {
        await api(`/orders/${orderId}/items`, {
          method: 'POST',
          body: {productId: line.productId, variantId: line.variantId, quantity: line.quantity, modifierIds: line.modifierIds, note: line.note, components: line.components ?? null},
          idempotencyKey: key(`item-${index}`),
        });
      });
    }
    await step('place', () => api(`/orders/${orderId}/place`, {method: 'POST', idempotencyKey: key('place')}));
    if (canProcess) await step('accept', () => api(`/orders/${orderId}/accept`, {method: 'POST', idempotencyKey: key('accept')}));
    const order = await api<Order>(`/orders/${orderId}`);
    current.number = order.number;
    setSale({...current, done: [...current.done]});
    return {id: orderId, number: order.number, total: order.total};
  };

  const send = async () => {
    setBusy('send');
    try {
      const order = await ensureOrder();
      toast('success', `Commande ${order.number} envoyée en cuisine.`);
      setDone({orderId: order.id, number: order.number, change: null, paid: false});
      reset();
    } catch (err) {
      toast('error', describe(err));
    } finally {
      setBusy(null);
    }
  };

  const pay = async (tender: Tender, given: number | null, code?: string) => {
    setBusy(tender.key);
    try {
      const order = await ensureOrder();
      await api('/payments/offline', {
        method: 'POST',
        body: {orderId: order.id, provider: tender.provider, amount: order.total, paymentMethod: tender.method, reference: code ?? counterReference(order.number, tender.method)},
        idempotencyKey: `pos-${sale.id}-pay`,
      });
      setPaying(false);
      setDone({orderId: order.id, number: order.number, change: given !== null ? given - order.total : null, paid: true});
      reset();
    } catch (err) {
      toast('error', describe(err));
    } finally {
      setBusy(null);
    }
  };

  if (error) return <div className="p-6"><Alert>{error}</Alert></div>;

  return (
    <div className="flex h-dvh flex-col bg-slate-100">
      <header className="flex flex-wrap items-center gap-2 bg-ink-900 px-4 py-2.5 text-white">
        {!onLock && <Link href="/caisse" className="mr-1 grid size-9 place-items-center rounded-lg hover:bg-white/10" aria-label="Retour à la console"><ArrowLeft className="size-5" /></Link>}
        <span className="font-semibold">{tenant.name}</span>
        <span className="text-white/60">· {locationName}</span>
        {operatorName && <Badge tone="brand" className="ml-1">{operatorName}</Badge>}
        {!online && <Badge tone="red" className="ml-1"><CloudOff className="mr-1 inline size-3.5" aria-hidden="true" />Hors ligne</Badge>}
        <span className="ml-auto flex items-center gap-2">
          {canPay && <Button size="sm" variant="ghost" className="text-white hover:bg-white/10 hover:text-white" onClick={() => setBills(true)}><ReceiptText className="size-4" aria-hidden="true" />Additions</Button>}
          {!online && <Link href="/caisse/vente" className="rounded-lg bg-amber-500 px-3 py-1.5 text-sm font-semibold text-ink-900">Vente hors ligne</Link>}
          {onLock && <Button size="sm" variant="ghost" className="text-white hover:bg-white/10 hover:text-white" onClick={onLock}><Lock className="size-4" aria-hidden="true" />Verrouiller</Button>}
        </span>
      </header>

      {!sections ? <Spinner label="Chargement de la carte…" /> : (
        <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[1fr_24rem]">
          <section className="flex min-h-0 flex-col">
            <div role="tablist" aria-label="Sections" className="flex gap-2 overflow-x-auto px-4 pt-3 pb-2">
              {sections.map(item => (
                <button key={item.key} type="button" role="tab" aria-selected={item.key === section} onClick={() => setSection(item.key)}
                  className={cx('shrink-0 rounded-xl px-4 py-2.5 text-sm font-semibold', item.key === section ? 'bg-brand-600 text-white' : 'bg-white text-ink-700 ring-1 ring-slate-200')}>
                  {item.name}
                </button>
              ))}
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto px-4 pb-4">
              {products.length === 0 ? <Empty title="Aucun produit en vente">Ajoutez des produits à la carte de l’établissement.</Empty> : (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4 2xl:grid-cols-5">
                  {products.map(product => (
                    <button key={product.id} type="button" disabled={locked || opening === product.id} onClick={() => void tap(product)}
                      className="flex min-h-28 flex-col justify-between rounded-2xl bg-white p-3 text-left shadow-sm ring-1 ring-slate-200 transition active:scale-[0.97] enabled:hover:ring-brand-400 disabled:opacity-60">
                      <span className="line-clamp-3 font-semibold leading-snug">{product.name}</span>
                      <span className="text-sm text-ink-600">{product.isFormula ? `Formule · dès ${fmt(product.basePrice)}` : product.variantCount > 0 ? `dès ${fmt(product.basePrice)}` : fmt(product.basePrice)}</span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          </section>

          <aside className="flex min-h-0 flex-col border-t border-slate-200 bg-white lg:border-t-0 lg:border-l">
            <div className="grid grid-cols-2 gap-2 p-3">
              <button type="button" disabled={locked} onClick={() => setService({type: 'Takeaway'})}
                className={cx('flex items-center justify-center gap-2 rounded-xl py-2.5 text-sm font-semibold', service.type === 'Takeaway' ? 'bg-brand-600 text-white' : 'bg-slate-100 text-ink-700')}>
                <ShoppingBag className="size-4" aria-hidden="true" />À emporter
              </button>
              <button type="button" disabled={locked} onClick={() => setPickingTable(true)}
                className={cx('flex items-center justify-center gap-2 rounded-xl py-2.5 text-sm font-semibold', service.type === 'DineIn' ? 'bg-brand-600 text-white' : 'bg-slate-100 text-ink-700')}>
                <Utensils className="size-4" aria-hidden="true" />{tableLabel ? `${tableLabel.area} · ${tableLabel.label}` : 'Sur place'}
              </button>
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto px-3">
              {cart.length === 0 ? <p className="py-10 text-center text-sm text-ink-500">Touchez un produit pour l’ajouter.</p> : (
                <ul className="divide-y divide-slate-100">
                  {cart.map(line => (
                    <li key={line.key} className="flex items-center gap-2 py-2.5">
                      <span className="min-w-0 flex-1">
                        <span className="block font-medium">{line.name}</span>
                        {line.details.length > 0 && <span className={cx('block text-xs text-ink-500', !line.components?.length && 'truncate')}>{line.details.join(line.components?.length ? ' · ' : ', ')}</span>}
                        {line.note && <span className="block truncate text-xs text-amber-700">{line.note}</span>}
                        <span className="text-xs text-ink-500">{fmt(line.unitPrice)}</span>
                      </span>
                      <button type="button" disabled={locked} aria-label={`Retirer un ${line.name}`} onClick={() => changeQuantity(line.key, -1)} className="grid size-10 place-items-center rounded-xl ring-1 ring-slate-200">
                        {line.quantity === 1 ? <Trash2 className="size-4" /> : <Minus className="size-4" />}
                      </button>
                      <span className="w-7 text-center font-semibold tabular-nums">{line.quantity}</span>
                      <button type="button" disabled={locked} aria-label={`Ajouter un ${line.name}`} onClick={() => changeQuantity(line.key, 1)} className="grid size-10 place-items-center rounded-xl ring-1 ring-slate-200"><Plus className="size-4" /></button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            <div className="space-y-2 border-t border-slate-200 p-3">
              <p className="flex items-baseline justify-between"><span className="text-sm text-ink-500">Total</span><span className="text-2xl font-bold tabular-nums">{fmt(total)}</span></p>
              {locked && <Alert tone="amber">Commande {sale.number ?? ''} déjà créée : terminez l’encaissement ou envoyez-la.</Alert>}
              <div className="grid grid-cols-[auto_1fr_1fr] gap-2">
                <Button size="lg" variant="secondary" aria-label="Vider le panier" disabled={cart.length === 0 || locked} onClick={() => setCart([])}><X className="size-5" /></Button>
                <Button size="lg" variant="secondary" busy={busy === 'send'} disabled={cart.length === 0 || !!busy} onClick={() => void send()}><ChefHat className="size-5" aria-hidden="true" />Envoyer</Button>
                <Button size="lg" variant="success" disabled={cart.length === 0 || !canPay || !!busy} onClick={() => setPaying(true)}><Receipt className="size-5" aria-hidden="true" />Encaisser</Button>
              </div>
            </div>
          </aside>
        </div>
      )}

      {choosing && (
        <ItemOptions product={choosing} groups={groups} currency={currency} onClose={() => setChoosing(null)} onAdd={async (choice, summary) => {
          addLine(choice, summary, choosing.name);
          return true;
        }} />
      )}

      {paying && <PaySheet total={total} currency={currency} tenders={tenders} busy={busy} onClose={() => setPaying(false)} onPay={pay} />}

      {pickingTable && (
        <Modal open onClose={() => setPickingTable(false)} title="Sur place">
          <div className="grid gap-2 sm:grid-cols-2">
            <button type="button" onClick={() => { setService({type: 'DineIn', tableSessionId: null}); setPickingTable(false); }}
              className="rounded-xl border border-slate-200 px-4 py-3 text-left font-medium hover:border-brand-400">Sans table</button>
            {[...tables.entries()].sort((a, b) => a[1].label.localeCompare(b[1].label, 'fr', {numeric: true})).map(([sessionId, table]) => (
              <button key={sessionId} type="button" onClick={() => { setService({type: 'DineIn', tableSessionId: sessionId}); setPickingTable(false); }}
                className="rounded-xl border border-slate-200 px-4 py-3 text-left hover:border-brand-400">
                <span className="font-semibold">{table.label}</span> <span className="text-sm text-ink-500">{table.area}</span>
              </button>
            ))}
          </div>
          {tables.size === 0 && <p className="mt-3 text-sm text-ink-500">Aucune table ouverte : ouvrez une table dans Salle pour y rattacher la commande.</p>}
        </Modal>
      )}

      {bills && <OpenBills locationId={locationId} tenders={tenders} tables={tables} onClose={() => setBills(false)} />}

      {done && (
        <Modal open onClose={() => setDone(null)} title={done.paid ? 'Vente encaissée' : 'Commande envoyée'} footer={
          <>
            <Link href={`/commandes/${done.orderId}/recu`} target="_blank" className="inline-flex h-10 items-center gap-2 rounded-lg border border-slate-300 bg-white px-4 text-sm font-semibold"><Receipt className="size-4" aria-hidden="true" />Reçu</Link>
            <Button onClick={() => setDone(null)}>Nouvelle vente</Button>
          </>
        }>
          <div className="py-4 text-center">
            <CheckCircle2 className="mx-auto size-12 text-emerald-600" aria-hidden="true" />
            <p className="mt-3 text-lg font-semibold">Commande {done.number}</p>
            {done.change !== null && done.change > 0 && <p className="mt-2 text-3xl font-bold">Rendu {fmt(done.change)}</p>}
            {!done.paid && <p className="mt-2 text-sm text-ink-500">À encaisser plus tard dans Additions.</p>}
          </div>
        </Modal>
      )}
    </div>
  );
}

/** Moyen de paiement puis, pour les espèces, le montant reçu (le compte juste d'une touche). */
function PaySheet({total, currency, tenders, busy, onClose, onPay}: {
  total: number;
  currency: string;
  tenders: Tender[];
  busy: string | null;
  onClose: () => void;
  onPay: (tender: Tender, given: number | null, code?: string) => Promise<void>;
}) {
  const [cash, setCash] = useState<Tender | null>(null);
  const [gift, setGift] = useState<Tender | null>(null);
  const [code, setCode] = useState('');
  const [typed, setTyped] = useState('');
  const given = typed.trim() === '' ? null : parseAmount(typed);

  return (
    <Modal open onClose={onClose} title={`Encaisser ${money(total, currency)}`}>
      {gift ? (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          if (code.trim()) void onPay(gift, null, code.trim());
        }}>
          <p className="text-sm font-semibold">Code de la carte cadeau ou de l’avoir</p>
          <input autoFocus autoComplete="off" spellCheck={false} placeholder="XXXX-XXXX-XXXX-XXXX" value={code} onChange={event => setCode(event.target.value)}
            className="h-12 w-full rounded-xl border border-slate-300 px-3 font-mono text-lg uppercase" />
          <p className="text-xs text-ink-500">La carte est débitée de {money(total, currency)}. Si son solde ne suffit pas, encaissez depuis la commande pour régler le reste autrement.</p>
          <div className="flex gap-2">
            <Button type="submit" size="lg" variant="success" disabled={!code.trim() || !!busy} busy={busy === gift.key}>Débiter la carte</Button>
            <Button type="button" variant="ghost" onClick={() => setGift(null)}>Autre moyen de paiement</Button>
          </div>
        </form>
      ) : !cash ? (
        <div className="grid grid-cols-2 gap-3">
          {tenders.map(tender => {
            const Icon = tenderIcon(tender);
            return (
              <button key={tender.key} type="button" disabled={!!busy} onClick={() => (tender.method === 'Cash' ? setCash(tender) : tender.method === 'GiftCard' ? setGift(tender) : void onPay(tender, null))}
                className="flex min-h-24 flex-col items-center justify-center gap-2 rounded-2xl border border-slate-200 p-4 text-center font-semibold hover:border-brand-400 disabled:opacity-50">
                <Icon className="size-7" aria-hidden="true" />{tender.label || methodLabel[tender.method]}
                {busy === tender.key && <Spinner label="" />}
              </button>
            );
          })}
        </div>
      ) : (
        <div className="space-y-4">
          <p className="text-sm font-semibold">Espèces reçues</p>
          <div className="grid grid-cols-3 gap-2">
            {cashSuggestions(total).map(value => (
              <Button key={value} size="lg" variant={value === total ? 'success' : 'secondary'} busy={busy === cash.key && given === null && value === total} disabled={!!busy}
                onClick={() => void onPay(cash, value)}>
                {value === total ? 'Compte juste' : money(value, currency)}
              </Button>
            ))}
          </div>
          <div className="flex gap-2">
            <input inputMode="numeric" placeholder="Autre montant" value={typed} onChange={event => setTyped(event.target.value)}
              className="h-12 w-full rounded-xl border border-slate-300 px-3 text-lg" />
            <Button size="lg" variant="success" disabled={given === null || given < total || !!busy} busy={busy === cash.key && given !== null} onClick={() => given !== null && void onPay(cash, given)}>Valider</Button>
          </div>
          {given !== null && given >= total && <p className="text-center text-xl font-bold">Rendu {money(given - total, currency)}</p>}
          <Button variant="ghost" onClick={() => setCash(null)}>Autre moyen de paiement</Button>
        </div>
      )}
    </Modal>
  );
}

/** Commandes de l'établissement qui restent à régler (service à table, paiement à la fin du repas). */
function OpenBills({locationId, tenders, tables, onClose}: {locationId: string; tenders: Tender[]; tables: Map<string, TableInfo>; onClose: () => void}) {
  const {currency} = useConsole();
  const [bills, setBills] = useState<{order: OrderSummary; payments: OrderPayments}[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [collecting, setCollecting] = useState<{order: Order; payments: OrderPayments} | null>(null);

  const load = useCallback(async () => {
    try {
      const pages = await Promise.all((['Placed', 'Accepted', 'Preparing', 'Ready'] as const).map(status =>
        api<Page<OrderSummary>>('/orders', {query: {locationId, status, pageSize: 100}})));
      const orders = pages.flatMap(page => page.items);
      const withPayments = await Promise.all(orders.map(async order => ({order, payments: await loadOrderPayments(order.id)})));
      setBills(withPayments.filter(bill => bill.payments.amountDue > 0).sort((a, b) => a.order.createdAtUtc.localeCompare(b.order.createdAtUtc)));
      setError(null);
    } catch (err) {
      setError(describe(err));
    }
  }, [locationId]);

  useEffect(() => {
    void load();
  }, [load]);

  const open = async (bill: {order: OrderSummary; payments: OrderPayments}) => {
    try {
      setCollecting({order: await api<Order>(`/orders/${bill.order.id}`), payments: bill.payments});
    } catch (err) {
      setError(describe(err));
    }
  };

  return (
    <Modal open onClose={onClose} title="Additions à régler" wide>
      {error && <Alert>{error}</Alert>}
      {!bills ? <Spinner /> : bills.length === 0 ? <Empty title="Tout est réglé">Aucune commande en cours ne reste à payer.</Empty> : (
        <ul className="divide-y divide-slate-100">
          {bills.map(bill => {
            const table = bill.order.tableSessionId ? tables.get(bill.order.tableSessionId) : undefined;
            return (
              <li key={bill.order.id}>
                <button type="button" onClick={() => void open(bill)} className="flex w-full items-center gap-3 py-3 text-left hover:bg-slate-50">
                  <span className="font-mono font-semibold">{bill.order.number}</span>
                  <span className="text-sm text-ink-500">{table ? `${table.area} · ${table.label}` : bill.order.type === 'Takeaway' ? 'À emporter' : 'Sur place'}</span>
                  <span className="ml-auto text-right">
                    <span className="block font-semibold tabular-nums">{money(bill.payments.amountDue, currency)}</span>
                    {bill.payments.settledAmount > 0 && <span className="text-xs text-ink-500">sur {money(bill.payments.total, currency)}</span>}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
      {collecting && (
        <CollectDialog order={collecting.order} payments={collecting.payments} tenders={tenders} open onClose={() => setCollecting(null)} onDone={load} />
      )}
    </Modal>
  );
}
