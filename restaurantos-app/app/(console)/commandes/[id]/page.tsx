'use client';
import {use, useEffect, useMemo, useState, type FormEvent} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {ArrowLeft, Boxes, ChefHat, Gift, MessageCircle, Minus, Percent, Receipt, Tag, Plus, Search, SlidersHorizontal, StickyNote, Trash2, Truck, XCircle} from 'lucide-react';
import {api, del, post, put} from '@/lib/api';
import {modifierGroups, productDetail, sellableCatalog} from '@/lib/catalog';
import {dateTime, money, orderStatusLabel, orderStatusTone, orderTypeLabel, parseAmount, time} from '@/lib/format';
import {fulfillmentOfOrder, modeLabel, statusLabel as fulfillmentStatusLabel, statusTone as fulfillmentStatusTone} from '@/lib/delivery';
import {useResource} from '@/lib/hooks';
import {availableTenders, loadConfigurations, loadOrderPayments, tendersOf} from '@/lib/payments';
import {advance, cancel, cancellable, nextStep, platformName, tablesBySession} from '@/lib/orders';
import type {ModifierGroup, Order, OrderItem, Product, ProductSummary} from '@/lib/types';
import {setOrderCustomer} from '@/lib/customers';
import {DevicePrintButton} from '@/components/device-print';
import {SendReceiptDialog} from '@/components/send-receipt';
import {kitchenLines} from '@/lib/tickets';
import {componentLabel, type ComponentChoice} from '@/lib/formulas';
import {useConsole} from '@/components/console';
import {LinkedCustomer} from '@/components/customer-picker';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Spinner, Textarea, cx, useAction, useToast} from '@/components/ui';
import {PaymentPanel} from './payments';
import {DepositsForOrder} from './deposit';
import {ItemOptions} from '@/components/item-options';
import {PromotionDialog, RewardDialog} from './marketing';

function CatalogPanel({order, onChanged}: {order: Order; onChanged: () => Promise<void>}) {
  const [query, setQuery] = useState('');
  const [options, setOptions] = useState<Product>();
  const {run, busy} = useAction();
  const {data, error} = useResource(async signal => {
    const [catalog, groups] = await Promise.all([sellableCatalog(order.locationId, signal), modifierGroups()]);
    return {...catalog, groups};
  }, [order.locationId]);

  const add = (body: object) => run('add', async () => {
    await post(`/orders/${order.id}/items`, body);
    await onChanged();
  });

  const choose = (summary: ProductSummary) =>
    run(`product-${summary.id}`, async () => {
      const product = await productDetail(summary.id);
      const needsChoice = product.variants.some(variant => variant.isAvailable) || product.modifierGroupIds.length > 0 || (product.formulaSteps?.length ?? 0) > 0;
      if (needsChoice) setOptions(product);
      else {
        await post(`/orders/${order.id}/items`, {productId: product.id, quantity: 1, variantId: null, modifierIds: [], note: null});
        await onChanged();
      }
    });

  const sections = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase('fr');
    if (!data) return [];
    return data.sections
      .map(section => ({...section, products: needle ? section.products.filter(product => product.name.toLocaleLowerCase('fr').includes(needle)) : section.products}))
      .filter(section => section.products.length > 0);
  }, [data, query]);

  return (
    <Card className="p-4 lg:sticky lg:top-20">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
        <Input value={query} onChange={event => setQuery(event.target.value)} placeholder="Rechercher un produit" className="pl-9" aria-label="Rechercher un produit" />
      </div>
      {error && <div className="mt-4"><Alert>{error}</Alert></div>}
      {!data && !error && <Spinner />}
      {data && !data.published && <p className="mt-3 text-xs text-ink-500">Aucune carte publiée pour cet établissement : tous les produits en vente sont proposés.</p>}
      {data && sections.length === 0 && <p className="py-8 text-center text-sm text-ink-500">Aucun produit en vente{query ? ' pour cette recherche' : ''}.</p>}
      <div className="mt-4 max-h-[65dvh] space-y-5 overflow-y-auto pr-1">
        {sections.map(section => (
          <section key={section.key}>
            <h3 className="mb-2 text-xs font-semibold tracking-wide text-ink-500 uppercase">{section.name}</h3>
            <div className="grid grid-cols-2 gap-2">
              {section.products.map(product => (
                <button
                  key={product.id}
                  type="button"
                  disabled={!!busy}
                  onClick={() => choose(product)}
                  className="flex min-h-20 flex-col justify-between rounded-xl border border-slate-200 p-3 text-left text-sm transition hover:border-brand-400 hover:bg-brand-50/50 disabled:opacity-60">
                  <span className="line-clamp-2 font-semibold">{product.name}</span>
                  <span className="mt-1 text-ink-600">{busy === `product-${product.id}` ? 'Ajout…' : money(product.basePrice, product.currencyCode)}{product.variantCount > 0 && <span className="text-ink-500"> · {product.variantCount} var.</span>}{product.isFormula && <span className="text-ink-500"> · formule</span>}</span>
                </button>
              ))}
            </div>
          </section>
        ))}
      </div>
      {options && data && <ItemOptions product={options} groups={data.groups} currency={order.currencyCode} onClose={() => setOptions(undefined)} onAdd={add} />}
    </Card>
  );
}

/** Composants d'une ligne de formule, repris dans l'étape du même nom (les noms d'étapes sont uniques dans une formule). */
function recomposed(product: Product, item: OrderItem): ComponentChoice[] {
  const steps = product.formulaSteps ?? [];
  return (item.components ?? []).flatMap(component => {
    const step = steps.find(candidate => candidate.name.toLocaleLowerCase('fr') === component.stepName.toLocaleLowerCase('fr'));
    return step ? [{stepId: step.id, productId: component.productId, variantId: component.variantId, modifierIds: component.modifiers.map(modifier => modifier.modifierId)}] : [];
  });
}

function Line({order, item, editable, onChanged}: {order: Order; item: OrderItem; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [editingNote, setEditingNote] = useState(false);
  const [note, setNote] = useState(item.note ?? '');
  const [recompose, setRecompose] = useState<{product: Product; groups: ModifierGroup[]} | null>(null);
  const setQuantity = (quantity: number) => run('qty', async () => {
    if (quantity <= 0) await del(`/orders/${order.id}/items/${item.id}`);
    else await put(`/orders/${order.id}/items/${item.id}/quantity`, {quantity});
    await onChanged();
  });
  return (
    <li className="py-3">
      <div className="flex items-start gap-3">
        <span className="w-8 shrink-0 pt-0.5 font-bold">{item.quantity}×</span>
        <div className="min-w-0 flex-1">
          <p className="font-semibold">{item.productName}{item.variantName && <span className="font-normal text-ink-600"> · {item.variantName}</span>}</p>
          {item.components && item.components.length > 0 && (
            <ul className="mt-0.5 space-y-0.5 text-sm text-ink-700">
              {item.components.map(component => <li key={component.id}>{componentLabel(component)}</li>)}
            </ul>
          )}
          {item.modifiers.length > 0 && <p className="text-sm text-ink-600">{item.modifiers.map(modifier => modifier.name).join(', ')}</p>}
          {item.note && !editingNote && <p className="mt-0.5 text-sm text-amber-800">Note : {item.note}</p>}
          {editingNote && (
            <form className="mt-2 flex gap-2" onSubmit={event => {
              event.preventDefault();
              void run('note', async () => {
                await put(`/orders/${order.id}/items/${item.id}/note`, {note: note.trim() || null});
                await onChanged();
                setEditingNote(false);
              });
            }}>
              <Input value={note} maxLength={200} onChange={event => setNote(event.target.value)} autoFocus aria-label="Note" />
              <Button type="submit" size="sm" busy={busy === 'note'} className="h-10">OK</Button>
            </form>
          )}
        </div>
        <span className="shrink-0 font-semibold whitespace-nowrap">{money(item.totalWithTax, order.currencyCode)}</span>
      </div>
      {editable && (
        <div className="mt-2 flex flex-wrap items-center gap-1 pl-11">
          <Button variant="secondary" size="sm" aria-label="Retirer un" disabled={!!busy} onClick={() => setQuantity(item.quantity - 1)}><Minus className="size-4" /></Button>
          <Button variant="secondary" size="sm" aria-label="Ajouter un" disabled={!!busy} onClick={() => setQuantity(item.quantity + 1)}><Plus className="size-4" /></Button>
          <Button variant="ghost" size="sm" onClick={() => setEditingNote(v => !v)}><StickyNote className="size-4" aria-hidden="true" />Note</Button>
          <Button variant="ghost" size="sm" busy={busy === 'options'} disabled={!!busy} onClick={() => void run('options', async () => {
            const [product, groups] = await Promise.all([productDetail(item.productId), modifierGroups()]);
            setRecompose({product, groups});
          })}><SlidersHorizontal className="size-4" aria-hidden="true" />{item.components?.length ? 'Composition' : 'Options'}</Button>
          <Button variant="ghost" size="sm" className="text-red-600" disabled={!!busy} onClick={() => setQuantity(0)}><Trash2 className="size-4" aria-hidden="true" />Retirer</Button>
        </div>
      )}
      {recompose && (
        <ItemOptions product={recompose.product} groups={recompose.groups} currency={order.currencyCode} onClose={() => setRecompose(null)}
          initial={{variantId: item.variantId, modifierIds: item.modifiers.map(modifier => modifier.modifierId), quantity: item.quantity, note: item.note, components: recomposed(recompose.product, item)}}
          onAdd={choice => run('replace', async () => {
            await put(`/orders/${order.id}/items/${item.id}`, {quantity: choice.quantity, variantId: choice.variantId, modifierIds: choice.modifierIds, note: choice.note, components: choice.components ?? null});
            await onChanged();
          }, 'Ligne modifiée.')} />
      )}
    </li>
  );
}

function DiscountDialog({order, onClose, onChanged}: {order: Order; onClose: () => void; onChanged: () => Promise<void>}) {
  const [amount, setAmount] = useState('');
  const [label, setLabel] = useState('');
  const {busy, run} = useAction();
  const value = parseAmount(amount);
  return (
    <Modal open onClose={onClose} title="Remise manuelle" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'add'} disabled={!value || value <= 0} onClick={() => run('add', async () => {
          await post(`/orders/${order.id}/discounts`, {amount: value, reference: crypto.randomUUID().slice(0, 8), label: label.trim() || null});
          await onChanged();
          onClose();
        }, 'Remise appliquée.')}>Appliquer</Button>
      </>
    }>
      <div className="space-y-4">
        <Field label={`Montant (${order.currencyCode})`}><Input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} autoFocus /></Field>
        <Field label="Motif" hint="Visible sur la commande et dans le journal."><Input value={label} maxLength={80} onChange={event => setLabel(event.target.value)} placeholder="Geste commercial" /></Field>
      </div>
    </Modal>
  );
}

function OrderFulfillment({order}: {order: Order}) {
  const {data: fulfillment, loading} = useResource(signal => fulfillmentOfOrder(order.id, signal), [order.id, order.status]);
  const open = order.status !== 'Completed' && order.status !== 'Cancelled';
  if (loading && fulfillment === undefined) return null;
  return (
    <Card className="flex flex-wrap items-center gap-3 p-4">
      <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-sky-50 text-sky-600" aria-hidden="true"><Truck className="size-5" /></span>
      <div className="min-w-0 flex-1 text-sm">
        {fulfillment ? (
          <>
            <p className="font-semibold">
              {modeLabel[fulfillment.mode]} pour {fulfillment.customerName}{' '}
              <Badge tone={fulfillmentStatusTone[fulfillment.status]}>{fulfillmentStatusLabel[fulfillment.status]}</Badge>
            </p>
            <p className="text-ink-500">
              Prévue à {time(fulfillment.promisedAtUtc)}
              {fulfillment.mode === 'Delivery' && fulfillment.addressLine ? ` · ${fulfillment.addressLine}` : ''}
              {fulfillment.courierName ? ` · livreur ${fulfillment.courierName}` : ''}
            </p>
          </>
        ) : (
          <p className="text-ink-600">{order.type === 'Delivery' ? 'Aucune fiche de livraison.' : 'Aucune fiche de retrait.'}{order.status === 'Draft' && ' Validez la commande pour en créer une.'}</p>
        )}
      </div>
      {fulfillment ? (
        <Link href="/livraison" className="text-sm font-semibold text-brand-700 hover:underline">Voir le suivi</Link>
      ) : (
        open && order.status !== 'Draft' && <Link href={`/livraison?commande=${order.id}`} className="inline-flex h-9 items-center rounded-xl bg-brand-600 px-3 text-sm font-semibold text-white hover:bg-brand-700">Créer la fiche</Link>
      )}
    </Card>
  );
}

/**
 * Reprise manuelle après l'échec d'un pont automatique : le ticket cuisine (commande acceptée) et le déstockage
 * (préparation commencée) se refont sans risque, les deux opérations étant idempotentes.
 */
function Recovery({order}: {order: Order}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const toast = useToast();
  const started = order.status === 'Preparing' || order.status === 'Ready' || order.status === 'Completed';
  return (
    <details className="rounded-xl border border-slate-200 bg-white p-4 text-sm">
      <summary className="cursor-pointer font-semibold text-ink-700">Reprise manuelle</summary>
      <p className="mt-2 text-ink-500">À utiliser si le bon n’est pas arrivé en cuisine ou si le stock n’a pas été décompté. Sans effet si c’est déjà fait.</p>
      <div className="mt-3 flex flex-wrap gap-2">
        {can('kitchen.operate') && order.status !== 'Completed' && (
          <Button size="sm" variant="secondary" busy={busy === 'ticket'} onClick={() => run('ticket', async () => {
            await post('/kitchen/tickets', {orderId: order.id});
          }, 'Ticket cuisine en place.')}><ChefHat className="size-4" aria-hidden="true" />Renvoyer en cuisine</Button>
        )}
        {can('inventory.operate') && started && (
          <Button size="sm" variant="secondary" busy={busy === 'stock'} onClick={() => run('stock', async () => {
            const result = await post<{alreadyConsumed: boolean; movementCount: number; productsWithoutRecipe: string[]}>(`/inventory/orders/${order.id}/consumption`);
            if (result.alreadyConsumed) toast('success', 'Le stock de cette commande était déjà décompté.');
            else toast('success', `Stock décompté : ${result.movementCount} mouvement${result.movementCount > 1 ? 's' : ''}${result.productsWithoutRecipe.length ? `, ${result.productsWithoutRecipe.length} produit(s) sans recette` : ''}.`);
          })}><Boxes className="size-4" aria-hidden="true" />Décompter le stock</Button>
        )}
      </div>
    </details>
  );
}

export default function OrderPage({params}: {params: Promise<{id: string}>}) {
  const {id} = use(params);
  const {can, locations} = useConsole();
  const {busy, run} = useAction();
  const [cancelling, setCancelling] = useState(false);
  const [sendingReceipt, setSendingReceipt] = useState(false);
  const [reason, setReason] = useState('');
  const [discount, setDiscount] = useState(false);
  const [promoting, setPromoting] = useState(false);
  const [rewarding, setRewarding] = useState(false);
  const {data: order, error, reload} = useResource(signal => api<Order>(`/orders/${id}`, {signal}), [id], 10_000);
  const {data: tables} = useResource(signal => (order ? tablesBySession(order.locationId, signal) : Promise.resolve(undefined)), [order?.locationId]);
  const router = useRouter();
  const search = useSearchParams();
  const [collecting, setCollecting] = useState(false);
  const settlement = useResource(
    signal => (order && order.status !== 'Draft' ? loadOrderPayments(order.id, signal) : Promise.resolve(null)),
    [order?.id, order?.status],
  );
  const {data: configurations} = useResource(signal => loadConfigurations(signal).catch(() => []), []);
  const due = settlement.data?.amountDue ?? order?.total ?? 0;
  const available = useResource(
    signal => (order && due > 0 ? availableTenders(order.locationId, due, order.currencyCode, signal).catch(() => null) : Promise.resolve(null)),
    [order?.locationId, order?.currencyCode, due],
  );
  const tenders = useMemo(
    () => available.data ?? (order && configurations ? tendersOf(configurations, order.currencyCode) : []),
    [available.data, order, configurations],
  );
  const reloadAll = useMemo(() => async () => {
    await Promise.all([reload(), settlement.reload()]);
  }, [reload, settlement.reload]);
  useEffect(() => {
    if (search.get('encaisser') === '1' && settlement.data && settlement.data.amountDue > 0) {
      setCollecting(true);
      router.replace(`/commandes/${id}`);
    }
  }, [search, settlement.data, router, id]);

  if (error) return <div className="space-y-4"><Link href="/commandes" className="text-sm font-semibold text-brand-700">Retour aux commandes</Link><Alert>{error}</Alert></div>;
  if (!order) return <Spinner />;

  const draft = order.status === 'Draft';
  const editable = draft && can('orders.take');
  const step = nextStep[order.status];
  const table = order.tableSessionId ? tables?.get(order.tableSessionId) : undefined;
  const location = locations.find(item => item.id === order.locationId);

  return (
    <div className={cx('grid gap-6', (editable || !draft) && 'lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]')}>
      <div className="min-w-0 space-y-4">
        <Link href="/commandes" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Commandes</Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-bold tracking-tight">Commande #{order.number}</h1>
          <Badge tone={orderStatusTone[order.status]}>{orderStatusLabel[order.status]}</Badge>
          {order.channel === 'DeliveryPlatform' && <Badge tone="blue">{platformName(order.platform)}{order.externalReference ? ` · n° ${order.externalReference}` : ''}</Badge>}
        </div>
        <p className="text-sm text-ink-500">
          {orderTypeLabel[order.type]}{table ? ` · ${table.area} · table ${table.label}` : ''}{location ? ` · ${location.name}` : ''} · créée {dateTime(order.createdAtUtc)}
        </p>
        {order.cancellationReason && <Alert>Annulée : {order.cancellationReason}</Alert>}
        {order.type !== 'DineIn' && can('delivery.operate') && <OrderFulfillment order={order} />}
        {(order.customerId || (can('orders.take') && can('customers.view') && order.status !== 'Completed' && order.status !== 'Cancelled')) && (
          <Card className="p-4">
            <LinkedCustomer customerId={order.customerId} editable={can('orders.take') && order.status !== 'Completed' && order.status !== 'Cancelled'}
              onChange={async customerId => {
                await setOrderCustomer(order.id, customerId);
                await reload();
              }} />
          </Card>
        )}

        <Card className="p-5">
          {order.items.length === 0 ? (
            <Empty title="Commande vide">{editable ? 'Ajoutez des produits depuis la carte.' : 'Aucun article.'}</Empty>
          ) : (
            <ul className="divide-y divide-slate-100">
              {order.items.map(item => <Line key={item.id} order={order} item={item} editable={editable} onChanged={reload} />)}
            </ul>
          )}
          <dl className="mt-4 space-y-1.5 border-t border-slate-100 pt-4 text-sm">
            <div className="flex justify-between"><dt className="text-ink-600">Sous-total</dt><dd>{money(order.subTotal, order.currencyCode)}</dd></div>
            {order.discounts.map(item => (
              <div key={`${item.source}-${item.reference}`} className="flex items-center justify-between gap-2">
                <dt className="text-ink-600">Remise{item.label ? ` · ${item.label}` : ''}</dt>
                <dd className="flex items-center gap-2">
                  −{money(item.amount, order.currencyCode)}
                  {draft && item.source === 'Manual' && can('orders.discount') && (
                    <button type="button" className="text-red-600" aria-label="Retirer la remise" onClick={() => run('discount', async () => {
                      await del(`/orders/${order.id}/discounts/${item.source}/${encodeURIComponent(item.reference)}`);
                      await reload();
                    })}><XCircle className="size-4" /></button>
                  )}
                </dd>
              </div>
            ))}
            <div className="flex justify-between"><dt className="text-ink-600">Taxes {order.priceTaxMode === 'TaxInclusive' ? '(incluses)' : ''}</dt><dd>{money(order.taxTotal, order.currencyCode)}</dd></div>
            {order.deliveryFee ? <div className="flex justify-between"><dt className="text-ink-600">Frais de livraison</dt><dd>{money(order.deliveryFee, order.currencyCode)}</dd></div> : null}
            <div className="flex justify-between pt-1 text-lg font-bold"><dt>Total</dt><dd>{money(order.total, order.currencyCode)}</dd></div>
          </dl>
        </Card>

        <div className="flex flex-wrap gap-2">
          {step && can(step.permission) && (order.status === 'Ready' && settlement.data && settlement.data.amountDue > 0 ? (
            <Button size="lg" variant="success" disabled={!can('payments.collect')} onClick={() => setCollecting(true)}>Encaisser avant de terminer</Button>
          ) : (
            <Button size="lg" variant={order.status === 'Ready' ? 'success' : 'primary'} busy={busy === 'next'} disabled={draft && order.items.length === 0}
              onClick={() => run('next', async () => {
                await advance(order.id, order.status);
                await reloadAll();
              }, step.success)}>
              {step.label}
            </Button>
          ))}
          {draft && can('orders.discount') && order.items.length > 0 && (
            <Button size="lg" variant="secondary" onClick={() => setDiscount(true)}><Percent className="size-4" aria-hidden="true" />Remise</Button>
          )}
          {draft && can('promotions.apply') && can('promotions.view') && order.items.length > 0 && (
            <Button size="lg" variant="secondary" onClick={() => setPromoting(true)}><Tag className="size-4" aria-hidden="true" />Promotion</Button>
          )}
          {draft && order.customerId && can('loyalty.operate') && can('loyalty.view') && order.items.length > 0 && (
            <Button size="lg" variant="secondary" onClick={() => setRewarding(true)}><Gift className="size-4" aria-hidden="true" />Fidélité</Button>
          )}
          {!draft && (
            <Link href={`/commandes/${order.id}/recu`} className="inline-flex h-12 items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 font-semibold text-ink-800 hover:bg-slate-50">
              <Receipt className="size-4" aria-hidden="true" />Reçu
            </Link>
          )}
          {!draft && can('storefront.receipts') && (
            <Button size="lg" variant="secondary" onClick={() => setSendingReceipt(true)}><MessageCircle className="size-4" aria-hidden="true" />Envoyer le reçu</Button>
          )}
          {!draft && order.status !== 'Cancelled' && <DevicePrintButton label="Bon de cuisine" lines={() => kitchenLines(order)} />}
          {cancellable.includes(order.status) && can(draft ? 'orders.take' : 'orders.process') && (
            <Button size="lg" variant="ghost" className="text-red-600" onClick={() => setCancelling(true)}>Annuler la commande</Button>
          )}
        </div>
        {!draft && order.status !== 'Placed' && order.status !== 'Cancelled' && (can('kitchen.operate') || can('inventory.operate')) && <Recovery order={order} />}
        {!draft && <SendReceiptDialog orderId={order.id} customerId={order.customerId} open={sendingReceipt} onClose={() => setSendingReceipt(false)} />}
      </div>

      {editable && <CatalogPanel order={order} onChanged={reload} />}
      {!draft && (
        <div className="min-w-0 lg:pt-12">
          {settlement.error && <Alert>{settlement.error}</Alert>}
          {settlement.data && can('payments.collect') && order.type === 'DineIn' && order.status !== 'Cancelled' && order.status !== 'Completed' && (
            <DepositsForOrder order={order} amountDue={settlement.data.amountDue} onApplied={reloadAll} />
          )}
          {settlement.data && (
            <PaymentPanel order={order} payments={settlement.data} tenders={tenders} collectOpen={collecting}
              onCollect={() => setCollecting(true)} onCloseCollect={() => setCollecting(false)} onChanged={reloadAll} />
          )}
        </div>
      )}

      {discount && <DiscountDialog order={order} onClose={() => setDiscount(false)} onChanged={reload} />}
      <PromotionDialog order={order} open={promoting} onClose={() => setPromoting(false)} onChanged={reload} />
      <RewardDialog order={order} open={rewarding} onClose={() => setRewarding(false)} onChanged={reload} />
      <Modal open={cancelling} onClose={() => setCancelling(false)} title={`Annuler la commande #${order.number}`} footer={
        <>
          <Button variant="secondary" onClick={() => setCancelling(false)}>Retour</Button>
          <Button variant="danger" busy={busy === 'cancel'} disabled={!reason.trim()} onClick={() => run('cancel', async () => {
            await cancel(order.id, reason.trim());
            setCancelling(false);
            await reload();
          }, 'Commande annulée.')}>Annuler la commande</Button>
        </>
      }>
        <Field label="Motif" hint="Le ticket de cuisine est retiré et le motif est conservé.">
          <Textarea value={reason} maxLength={200} onChange={event => setReason(event.target.value)} autoFocus />
        </Field>
      </Modal>
    </div>
  );
}
