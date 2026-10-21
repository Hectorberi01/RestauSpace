'use client';
import {useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {
  AlarmClock, Banknote, Bike, CheckCircle2, ClipboardList, History, MapPin, MessageCircle, MoreHorizontal, Phone, Plus, Settings2, ShoppingBag, Timer,
  Truck, User, Users,
} from 'lucide-react';
import {
  collectionLabel, courierWhatsAppLink, fulfillmentsSince, isLate, listCouriers, loadSettings, modeLabel, nextAction, openFulfillments, openOffsiteOrders,
  statusLabel, statusTone, step, type Courier, type Fulfillment, type FulfillmentMode, type FulfillmentStatus,
} from '@/lib/delivery';
import {ordersSince, startOfDay} from '@/lib/dashboard';
import {money, orderTypeLabel, orderTypeTone, time} from '@/lib/format';
import {useNow, useResource} from '@/lib/hooks';
import type {LocationSummary, OrderSummary} from '@/lib/types';
import {useConsole, WithLocation} from '@/components/console';
import {accentOf, Alert, Badge, Button, Card, Empty, Spinner, StatCard, Tabs, cx, type Accent, useAction} from '@/components/ui';
import {CouriersPanel} from './couriers';
import {CourierDialog, DeliveredDialog, EditDialog, NewFulfillmentDialog, ReasonDialog} from './dialogs';
import {SettingsPanel} from './settings';

type Tab = 'board' | 'history' | 'couriers' | 'settings';
type ModeFilter = 'all' | FulfillmentMode;

const columns: {key: string; label: string; statuses: FulfillmentStatus[]; dot: string; ring: string}[] = [
  {key: 'todo', label: 'À préparer', statuses: ['Pending'], dot: 'bg-amber-500', ring: 'border-t-amber-500'},
  {key: 'ready', label: 'Prêtes / livreur assigné', statuses: ['ReadyForPickup', 'Assigned'], dot: 'bg-emerald-500', ring: 'border-t-emerald-500'},
  {key: 'road', label: 'En route', statuses: ['OutForDelivery'], dot: 'bg-sky-500', ring: 'border-t-sky-500'},
  {key: 'failed', label: 'Échecs', statuses: ['Failed'], dot: 'bg-red-500', ring: 'border-t-red-500'},
];

function Kpi({icon, tone, label, value, hint, accent}: {icon: ReactNode; tone?: string; label: string; value: number; hint?: string; accent?: Accent}) {
  return <StatCard icon={icon} label={label} value={value} hint={hint} accent={accent ?? accentOf(tone)} />;
}

function minutesLeft(fulfillment: Fulfillment, now: number) {
  return Math.round((new Date(fulfillment.promisedAtUtc).getTime() - now) / 60_000);
}

function PromisedAt({fulfillment, now}: {fulfillment: Fulfillment; now: number}) {
  const left = minutesLeft(fulfillment, now);
  const late = isLate(fulfillment, now);
  const soon = !late && left <= 10 && fulfillment.status !== 'ReadyForPickup';
  return (
    <span className={cx('flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold whitespace-nowrap', late ? 'bg-red-50 text-red-700' : soon ? 'bg-amber-50 text-amber-700' : 'bg-slate-100 text-ink-600')}>
      <Timer className="size-3" aria-hidden="true" />
      {time(fulfillment.promisedAtUtc)}
      {late ? ` · ${-left} min de retard` : left > 0 && left <= 90 ? ` · dans ${left} min` : ''}
    </span>
  );
}

function CardMenu({fulfillment, onEdit, onCourier, onCancel}: {fulfillment: Fulfillment; onEdit: () => void; onCourier: () => void; onCancel: () => void}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const item = 'block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50';
  const choose = (action: () => void) => () => {
    setOpen(false);
    action();
  };
  return (
    <div ref={ref} className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label={`Actions pour ${fulfillment.customerName}`} onClick={() => setOpen(value => !value)} className="grid size-8 place-items-center rounded-lg text-ink-500 hover:bg-slate-100">
        <MoreHorizontal className="size-4" />
      </button>
      {open && (
        <div role="menu" className="absolute top-9 right-0 z-20 w-52 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          <button type="button" role="menuitem" className={item} onClick={choose(onEdit)}>Client et adresse</button>
          {fulfillment.status === 'Assigned' && <button type="button" role="menuitem" className={item} onClick={choose(onCourier)}>Changer de livreur</button>}
          <button type="button" role="menuitem" className={cx(item, 'text-red-600')} onClick={choose(onCancel)}>Annuler la fiche</button>
        </div>
      )}
    </div>
  );
}

function FulfillmentCard({fulfillment, order, now, restaurant, onChanged, onEdit, onCourier, onDelivered, onFail, onCancel}: {
  fulfillment: Fulfillment;
  order: OrderSummary | undefined;
  now: number;
  restaurant: string;
  onChanged: () => Promise<void>;
  onEdit: () => void;
  onCourier: () => void;
  onDelivered: () => void;
  onFail: () => void;
  onCancel: () => void;
}) {
  const {busy, run} = useAction();
  const next = nextAction(fulfillment);
  const delivery = fulfillment.mode === 'Delivery';
  const late = isLate(fulfillment, now);
  const currency = fulfillment.feeCurrencyCode ?? order?.currencyCode ?? 'XOF';
  const collects = fulfillment.status === 'OutForDelivery' && fulfillment.amountToCollect !== null;
  const whatsapp = delivery && (fulfillment.status === 'Assigned' || fulfillment.status === 'OutForDelivery')
    ? courierWhatsAppLink(fulfillment, {
      orderNumber: order?.number,
      restaurant,
      amountLabel: fulfillment.status === 'OutForDelivery' ? (fulfillment.amountToCollect !== null ? money(fulfillment.amountToCollect, currency) : null) : undefined,
    })
    : null;
  return (
    <Card className={cx('p-4 shadow-sm', late && 'border-red-300 ring-1 ring-red-200')}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          {order ? (
            <Link href={`/commandes/${order.id}`} className="text-lg font-bold hover:underline">#{order.number}</Link>
          ) : (
            <span className="text-lg font-bold">Commande</span>
          )}
          <div className="mt-1 flex flex-wrap items-center gap-1.5">
            <Badge tone={delivery ? 'blue' : 'amber'}>{modeLabel[fulfillment.mode]}</Badge>
            {fulfillment.status !== 'Pending' && <Badge tone={statusTone[fulfillment.status]}>{statusLabel[fulfillment.status]}</Badge>}
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          <PromisedAt fulfillment={fulfillment} now={now} />
          <CardMenu fulfillment={fulfillment} onEdit={onEdit} onCourier={onCourier} onCancel={onCancel} />
        </div>
      </div>

      <dl className="mt-3 space-y-1.5 text-sm">
        <div className="flex items-center gap-2">
          <dt className="sr-only">Client</dt>
          <User className="size-4 shrink-0 text-ink-500" aria-hidden="true" />
          <dd className="min-w-0 truncate font-medium">{fulfillment.customerName}</dd>
        </div>
        {fulfillment.customerPhone && (
          <div className="flex items-center gap-2">
            <dt className="sr-only">Téléphone</dt>
            <Phone className="size-4 shrink-0 text-ink-500" aria-hidden="true" />
            <dd><a href={`tel:${fulfillment.customerPhone}`} className="text-brand-700 hover:underline">{fulfillment.customerPhone}</a></dd>
          </div>
        )}
        {delivery && (
          <div className="flex items-start gap-2">
            <dt className="sr-only">Adresse</dt>
            <MapPin className="mt-0.5 size-4 shrink-0 text-ink-500" aria-hidden="true" />
            <dd className="min-w-0">
              <span className="block">{fulfillment.addressLine}</span>
              {fulfillment.landmark && <span className="block text-xs text-ink-500">{fulfillment.landmark}</span>}
              {fulfillment.instructions && <span className="block text-xs text-ink-500 italic">{fulfillment.instructions}</span>}
              <span className="block text-xs text-ink-500">
                {fulfillment.zoneName} · frais {fulfillment.deliveryFee !== null ? money(fulfillment.deliveryFee, fulfillment.feeCurrencyCode ?? order?.currencyCode ?? 'XOF') : '—'}
              </span>
            </dd>
          </div>
        )}
        {fulfillment.courierName && (
          <div className="flex items-start gap-2">
            <dt className="sr-only">Livreur</dt>
            <Bike className="mt-0.5 size-4 shrink-0 text-ink-500" aria-hidden="true" />
            <dd className="min-w-0">
              <span className="block truncate">
                {fulfillment.courierName}
                {fulfillment.courierPhone && <a href={`tel:${fulfillment.courierPhone}`} className="ml-2 text-brand-700 hover:underline">{fulfillment.courierPhone}</a>}
              </span>
              {fulfillment.courierFee ? <span className="block text-xs text-ink-500">Tarif {money(fulfillment.courierFee, currency)} la course</span> : null}
            </dd>
          </div>
        )}
        {collects && (
          <p className="flex items-center gap-2 rounded-lg bg-amber-50 px-2 py-1.5 text-sm font-semibold text-amber-900">
            <Banknote className="size-4 shrink-0" aria-hidden="true" />
            À encaisser : {money(fulfillment.amountToCollect!, currency)}
          </p>
        )}
        {fulfillment.status === 'OutForDelivery' && fulfillment.amountToCollect === null && (
          <p className="text-xs font-medium text-emerald-700">Commande réglée : rien à encaisser.</p>
        )}
        {fulfillment.status === 'Failed' && fulfillment.failureReason && (
          <p className="rounded-lg bg-red-50 px-2 py-1 text-xs text-red-700">Échec : {fulfillment.failureReason}</p>
        )}
      </dl>
      {order && <p className="mt-2 text-xs text-ink-500">Commande {money(order.total, order.currencyCode)} · {order.itemCount} article{order.itemCount > 1 ? 's' : ''}</p>}

      <div className="mt-3 flex flex-wrap gap-2">
        {delivery && fulfillment.status === 'Pending' && (
          <Button size="sm" className="flex-1" onClick={onCourier}><Bike className="size-4" aria-hidden="true" />Assigner un livreur</Button>
        )}
        {next && next.action === 'delivered' && collects ? (
          <Button size="sm" className="flex-1" variant="success" onClick={onDelivered}>Livrée et encaissée</Button>
        ) : next && (
          <Button size="sm" className="flex-1" variant={next.action === 'picked-up' || next.action === 'delivered' ? 'success' : 'primary'} busy={busy === 'next'}
            onClick={() => run('next', async () => {
              await step(fulfillment.id, next.action);
              await onChanged();
            }, next.success)}>
            {next.label}
          </Button>
        )}
        {whatsapp && (
          <a href={whatsapp} target="_blank" rel="noopener noreferrer" aria-label={`Envoyer la course à ${fulfillment.courierName} sur WhatsApp`}
            className="inline-flex h-8 items-center gap-1.5 rounded-lg border border-emerald-200 bg-emerald-50 px-2.5 text-sm font-semibold text-emerald-700 hover:bg-emerald-100">
            <MessageCircle className="size-4" aria-hidden="true" />WhatsApp
          </a>
        )}
        {fulfillment.status === 'OutForDelivery' && <Button size="sm" variant="secondary" onClick={onFail}>Échec</Button>}
      </div>
    </Card>
  );
}

function Board({fulfillments, orders, waiting, couriers, restaurant, currency, mode, now, onChanged, onCreate}: {
  fulfillments: Fulfillment[];
  orders: Map<string, OrderSummary>;
  waiting: OrderSummary[];
  couriers: Courier[];
  restaurant: string;
  currency: string;
  mode: ModeFilter;
  now: number;
  onChanged: () => Promise<void>;
  onCreate: (orderId: string | null) => void;
}) {
  const [editing, setEditing] = useState<Fulfillment | null>(null);
  const [courier, setCourier] = useState<Fulfillment | null>(null);
  const [delivering, setDelivering] = useState<Fulfillment | null>(null);
  const [failing, setFailing] = useState<Fulfillment | null>(null);
  const [cancelling, setCancelling] = useState<Fulfillment | null>(null);
  const list = fulfillments.filter(fulfillment => mode === 'all' || fulfillment.mode === mode);
  const pending = waiting.filter(order => mode === 'all' || (mode === 'Delivery' ? order.type === 'Delivery' : order.type === 'Takeaway'));
  const visible = columns.filter(column => column.key !== 'failed' || list.some(fulfillment => fulfillment.status === 'Failed'));

  return (
    <>
      {pending.length > 0 && (
        <Card className="mb-5 border-amber-200 bg-amber-50/60 p-4">
          <p className="mb-3 flex items-center gap-2 text-sm font-semibold text-amber-900">
            <ClipboardList className="size-4" aria-hidden="true" />
            {pending.length} commande{pending.length > 1 ? 's' : ''} sans fiche de retrait ou de livraison
          </p>
          <div className="flex flex-wrap gap-2">
            {pending.map(order => (
              <button key={order.id} type="button" onClick={() => onCreate(order.id)}
                className="flex w-full items-center gap-2 rounded-xl border border-amber-200 bg-white px-3 py-2 text-left text-sm hover:border-amber-400 sm:w-auto">
                <span className="font-bold">#{order.number}</span>
                <Badge tone={orderTypeTone[order.type]}>{orderTypeLabel[order.type]}</Badge>
                <span className="whitespace-nowrap text-ink-500">{money(order.total, order.currencyCode)}</span>
                <span className="ml-auto pl-2 font-semibold whitespace-nowrap text-brand-700">Créer la fiche</span>
              </button>
            ))}
          </div>
        </Card>
      )}

      {list.length === 0 ? (
        <Empty icon={<Truck className="size-8" />} title="Aucun retrait ni livraison en cours">
          Les fiches apparaissent ici dès leur création, de la plus urgente à la moins urgente.
        </Empty>
      ) : (
        <div className={cx('grid grid-cols-1 gap-4 md:grid-cols-2', visible.length === 4 ? '2xl:grid-cols-4' : 'xl:grid-cols-3')}>
          {visible.map(column => {
            const cards = list.filter(fulfillment => column.statuses.includes(fulfillment.status));
            return (
              <section key={column.key} aria-label={column.label} className={cx('min-w-0 rounded-2xl border-t-4 bg-slate-50 p-3', column.ring)}>
                <h2 className="mb-3 flex items-center gap-2 px-1 text-sm font-semibold">
                  <span className={cx('size-2 rounded-full', column.dot)} aria-hidden="true" />
                  {column.label}
                  <span className="ml-auto rounded-full bg-white px-2 py-0.5 text-xs text-ink-600">{cards.length}</span>
                </h2>
                <div className="space-y-3">
                  {cards.length === 0 && <p className="px-1 py-6 text-center text-sm text-ink-500">Rien ici.</p>}
                  {cards.map(fulfillment => (
                    <FulfillmentCard
                      key={fulfillment.id}
                      fulfillment={fulfillment}
                      order={orders.get(fulfillment.orderId)}
                      now={now}
                      restaurant={restaurant}
                      onChanged={onChanged}
                      onEdit={() => setEditing(fulfillment)}
                      onCourier={() => setCourier(fulfillment)}
                      onDelivered={() => setDelivering(fulfillment)}
                      onFail={() => setFailing(fulfillment)}
                      onCancel={() => setCancelling(fulfillment)}
                    />
                  ))}
                </div>
              </section>
            );
          })}
        </div>
      )}

      <EditDialog fulfillment={editing} onClose={() => setEditing(null)} onDone={onChanged} />
      <CourierDialog fulfillment={courier} couriers={couriers} onClose={() => setCourier(null)} onDone={onChanged} />
      <DeliveredDialog fulfillment={delivering} currency={delivering?.feeCurrencyCode ?? currency} onClose={() => setDelivering(null)} onDone={onChanged} />
      <ReasonDialog fulfillment={failing} kind="fail" onClose={() => setFailing(null)} onDone={onChanged} />
      <ReasonDialog fulfillment={cancelling} kind="cancel" onClose={() => setCancelling(null)} onDone={onChanged} />
    </>
  );
}

function HistoryList({fulfillments, orders, mode}: {fulfillments: Fulfillment[]; orders: Map<string, OrderSummary>; mode: ModeFilter}) {
  const list = fulfillments
    .filter(fulfillment => mode === 'all' || fulfillment.mode === mode)
    .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc));
  if (list.length === 0) return <Empty icon={<History className="size-8" />} title="Aucune fiche aujourd’hui" />;
  const doneAt = (fulfillment: Fulfillment) => fulfillment.completedAtUtc ?? fulfillment.cancelledAtUtc ?? fulfillment.failedAtUtc;
  return (
    <div className="relative overflow-x-auto">
      <table className="w-full min-w-[860px] text-left text-sm">
        <thead className="border-b border-slate-200 text-xs text-ink-500">
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">Commande</th>
            <th scope="col" className="px-3 py-2 font-medium">Type</th>
            <th scope="col" className="px-3 py-2 font-medium">Client</th>
            <th scope="col" className="px-3 py-2 font-medium">Promise</th>
            <th scope="col" className="px-3 py-2 font-medium">Terminée</th>
            <th scope="col" className="px-3 py-2 font-medium">Livreur / zone</th>
            <th scope="col" className="px-3 py-2 font-medium">Encaissé</th>
            <th scope="col" className="px-3 py-2 font-medium">Statut</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {list.map(fulfillment => {
            const order = orders.get(fulfillment.orderId);
            const end = doneAt(fulfillment);
            const lateBy = fulfillment.completedAtUtc ? Math.round((new Date(fulfillment.completedAtUtc).getTime() - new Date(fulfillment.promisedAtUtc).getTime()) / 60_000) : 0;
            return (
              <tr key={fulfillment.id}>
                <td className="px-3 py-3 font-semibold">{order ? <Link href={`/commandes/${order.id}`} className="hover:underline">#{order.number}</Link> : '—'}</td>
                <td className="px-3 py-3"><Badge tone={fulfillment.mode === 'Delivery' ? 'blue' : 'amber'}>{modeLabel[fulfillment.mode]}</Badge></td>
                <td className="px-3 py-3">{fulfillment.customerName}</td>
                <td className="px-3 py-3">{time(fulfillment.promisedAtUtc)}</td>
                <td className="px-3 py-3">
                  {end ? time(end) : '—'}
                  {lateBy > 0 && <span className="ml-1 text-xs font-semibold text-red-600">+{lateBy} min</span>}
                </td>
                <td className="px-3 py-3 text-ink-600">{fulfillment.mode === 'Delivery' ? [fulfillment.courierName, fulfillment.zoneName].filter(Boolean).join(' · ') || '—' : '—'}</td>
                <td className="px-3 py-3 text-ink-600">
                  {fulfillment.collectionMethod && fulfillment.amountCollected !== null
                    ? `${collectionLabel[fulfillment.collectionMethod]} ${money(fulfillment.amountCollected, fulfillment.feeCurrencyCode ?? order?.currencyCode ?? 'XOF')}`
                    : '—'}
                  {fulfillment.remittedAtUtc && <span className="ml-1 text-xs text-emerald-700">remis</span>}
                </td>
                <td className="px-3 py-3"><Badge tone={statusTone[fulfillment.status]}>{statusLabel[fulfillment.status]}</Badge></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function DeliveryScreen({location}: {location: LocationSummary}) {
  const {can, tenant} = useConsole();
  const router = useRouter();
  const params = useSearchParams();
  const now = useNow(20_000);
  const [tab, setTab] = useState<Tab>(params.get('onglet') === 'reglages' ? 'settings' : params.get('onglet') === 'livreurs' ? 'couriers' : 'board');
  const [mode, setMode] = useState<ModeFilter>('all');
  const [creating, setCreating] = useState<{orderId: string | null} | null>(null);
  const requestedOrder = params.get('commande');

  const allowed = can('delivery.operate');
  const data = useResource(
    async signal => {
      if (!allowed) return null;
      const today = startOfDay(new Date());
      const [open, history, waiting, todayOrders, settings, couriers] = await Promise.all([
        openFulfillments(location.id, signal),
        fulfillmentsSince(location.id, today, signal),
        openOffsiteOrders(location.id, signal),
        ordersSince(location.id, today, signal).catch(() => [] as OrderSummary[]),
        loadSettings(location.id, signal),
        listCouriers(true, signal).catch(() => [] as Courier[]),
      ]);
      const oldest = waiting.reduce<Date>((min, order) => (new Date(order.createdAtUtc) < min ? new Date(order.createdAtUtc) : min), today);
      const linked = oldest < today ? await fulfillmentsSince(location.id, oldest, signal) : history;
      const taken = new Set([...open, ...linked].filter(fulfillment => fulfillment.status !== 'Cancelled').map(fulfillment => fulfillment.orderId));
      return {open, history, waiting, todayOrders, settings, couriers, taken};
    },
    [location.id, allowed],
    allowed ? 20_000 : 0,
  );

  useEffect(() => {
    if (requestedOrder && data.data) {
      setCreating({orderId: requestedOrder});
      router.replace('/livraison');
    }
  }, [requestedOrder, data.data, router]);

  const orders = useMemo(() => {
    const map = new Map<string, OrderSummary>();
    for (const order of [...(data.data?.todayOrders ?? []), ...(data.data?.waiting ?? [])]) map.set(order.id, order);
    return map;
  }, [data.data]);
  const withoutFulfillment = useMemo(() => {
    const taken = data.data?.taken ?? new Set<string>();
    return (data.data?.waiting ?? []).filter(order => !taken.has(order.id));
  }, [data.data]);

  if (!allowed) {
    return <Empty icon={<Truck className="size-8" />} title="Livraison & retrait non disponibles">Réservé aux membres qui suivent les retraits et les livraisons.</Empty>;
  }

  const open = data.data?.open ?? [];
  const history = data.data?.history ?? [];
  const count = (...statuses: FulfillmentStatus[]) => open.filter(fulfillment => statuses.includes(fulfillment.status)).length;
  const late = open.filter(fulfillment => isLate(fulfillment, now)).length;
  const completed = history.filter(fulfillment => fulfillment.status === 'Completed');
  const settings = data.data?.settings ?? null;
  const couriers = data.data?.couriers ?? [];
  const currency = settings?.currencyCode ?? tenant.defaultCurrencyCode;
  const restaurant = tenant.branding?.displayName || tenant.name;
  const tabs = [
    ['board', 'Suivi en cours', Timer],
    ['history', 'Aujourd’hui', History],
    ['couriers', 'Livreurs', Users],
    ['settings', 'Réglages', Settings2],
  ] as const;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Livraison & retrait</h1>
          <p className="mt-1 text-sm text-ink-500">
            Suivez les commandes à emporter et les livraisons de {location.name}
            {settings && (
              <span className="ml-1">
                ({settings.pickupEnabled ? `retrait ${settings.pickupLeadMinutes} min` : 'retrait désactivé'}
                {' · '}
                {settings.deliveryEnabled ? `livraison ${settings.deliveryLeadMinutes} min + trajet` : 'livraison désactivée'})
              </span>
            )}
          </p>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2">
          <Button variant="secondary" onClick={() => setTab('settings')}><Settings2 className="size-4" aria-hidden="true" />Réglages</Button>
          <Button onClick={() => setCreating({orderId: null})} disabled={!data.data}><Plus className="size-4" aria-hidden="true" />Nouvelle fiche</Button>
        </div>
      </div>

      {data.error && <Alert>{data.error}</Alert>}
      {!data.data && !data.error && <Spinner label="Chargement des retraits et livraisons…" />}
      {data.data && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
            <Kpi icon={<ShoppingBag className="size-6" />} tone="bg-amber-50 text-amber-600" label="À préparer" value={count('Pending')} hint={withoutFulfillment.length ? `${withoutFulfillment.length} commande(s) sans fiche` : undefined} />
            <Kpi icon={<Truck className="size-6" />} tone="bg-sky-50 text-sky-600" label="Prêtes ou en livraison" value={count('ReadyForPickup', 'Assigned', 'OutForDelivery')} />
            <Kpi icon={<AlarmClock className="size-6" />} accent={late ? 'red' : 'neutral'} label="En retard" value={late} hint={count('Failed') ? `${count('Failed')} échec(s) à traiter` : undefined} />
            <Kpi icon={<CheckCircle2 className="size-6" />} tone="bg-emerald-50 text-emerald-600" label="Terminées aujourd’hui" value={completed.length}
              hint={completed.length ? `${completed.filter(fulfillment => fulfillment.mode === 'Delivery').length} livraison(s) · ${completed.filter(fulfillment => fulfillment.mode === 'Pickup').length} retrait(s)` : undefined} />
          </div>

          <Card className="p-4 sm:p-5">
            <div className="mb-4 flex flex-col gap-3 border-b border-slate-200 sm:flex-row sm:items-end sm:justify-between">
              <div role="tablist" aria-label="Livraison et retrait" className="-mx-1 flex gap-1 overflow-x-auto">
                {tabs.map(([value, label, Icon]) => (
                  <button key={value} type="button" role="tab" aria-selected={tab === value} onClick={() => setTab(value)}
                    className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                    <Icon className="size-4" aria-hidden="true" />{label}
                  </button>
                ))}
              </div>
              {(tab === 'board' || tab === 'history') && (
                <div className="pb-2 [&>div]:mb-0">
                  <Tabs<ModeFilter> label="Type" value={mode} onChange={setMode} items={[
                    {value: 'all', label: 'Tout'},
                    {value: 'Pickup', label: 'Retraits'},
                    {value: 'Delivery', label: 'Livraisons'},
                  ]} />
                </div>
              )}
            </div>
            {tab === 'board' && (
              <Board fulfillments={open} orders={orders} waiting={withoutFulfillment} couriers={couriers} restaurant={restaurant} currency={currency} mode={mode} now={now}
                onChanged={data.reload} onCreate={orderId => setCreating({orderId})} />
            )}
            {tab === 'history' && <HistoryList fulfillments={history} orders={orders} mode={mode} />}
            {tab === 'couriers' && (
              <CouriersPanel locationId={location.id} currency={currency} couriers={couriers} canRemit={can('delivery.remit')} canManage={can('delivery.couriers')} onChanged={data.reload} />
            )}
            {tab === 'settings' && settings && <SettingsPanel settings={settings} editable={can('delivery.configure')} onChanged={data.reload} />}
          </Card>
          <p className="text-xs text-ink-500">
            Les frais de livraison sont ajoutés au total de la commande. Au départ du livreur, le reste à payer devient le montant à encaisser ; à la livraison, l’encaissement est enregistré sur la commande, puis les espèces entrent dans la caisse à la remise du livreur.
          </p>
        </>
      )}

      <NewFulfillmentDialog
        open={creating !== null}
        orders={withoutFulfillment}
        settings={settings}
        initialOrderId={creating?.orderId ?? null}
        onClose={() => setCreating(null)}
        onDone={data.reload}
        onOpenSettings={() => {
          setCreating(null);
          setTab('settings');
        }}
      />
    </div>
  );
}

export default function DeliveryPage() {
  return <WithLocation>{location => <DeliveryScreen location={location} />}</WithLocation>;
}
