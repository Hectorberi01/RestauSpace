'use client';
import {useCallback, useEffect, useRef, useState} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {ClipboardList, Coins, MessageCircle, Plus, Receipt, ShoppingBag, Store, Timer, Truck} from 'lucide-react';
import {all, api, ApiError, describe, type Page} from '@/lib/api';
import {dateTime, elapsed, money, number, orderStatusLabel, orderStatusTone, orderTypeColor, orderTypeLabel, orderTypeTone, time} from '@/lib/format';
import {ordersSince, startOfDay, summarize} from '@/lib/dashboard';
import {useNow, useResource} from '@/lib/hooks';
import {armChime, playChime} from '@/lib/chime';
import {advance, createOrder, nextStep, platformName, tablesBySession, type TableInfo} from '@/lib/orders';
import type {LocationSummary, OrderChannel, OrderStatus, OrderSummary, OrderType} from '@/lib/types';
import {useConsole, WithLocation} from '@/components/console';
import {OrderHistory, type TypeFilter} from './history';
import {RemoteOrderDialog} from './remote-order';
import {Alert, Badge, Button, Card, Empty, Modal, Spinner, StatCard, Tabs, cx, useAction, useToast} from '@/components/ui';

type View = 'live' | 'history' | 'drafts';

const columns: {status: OrderStatus; label: string}[] = [
  {status: 'Placed', label: 'Nouvelles'},
  {status: 'Accepted', label: 'Acceptées'},
  {status: 'Preparing', label: 'En préparation'},
  {status: 'Ready', label: 'Prêtes'},
];

function tableLabel(order: OrderSummary, tables: Map<string, TableInfo> | undefined) {
  if (order.type !== 'DineIn') return orderTypeLabel[order.type];
  const table = order.tableSessionId ? tables?.get(order.tableSessionId) : undefined;
  return table ? `Table ${table.label}` : orderTypeLabel.DineIn;
}


const onlineChannelLabel: Partial<Record<OrderChannel, string>> = {Web: 'Site web', MobileApp: 'Application', Kiosk: 'Borne'};
const isOnline = (order: OrderSummary) => !!order.channel && order.channel in onlineChannelLabel;
const remoteChannelLabel: Partial<Record<OrderChannel, string>> = {WhatsApp: 'WhatsApp', Phone: 'Téléphone'};

/** Une commande en ligne qui attend son paiement ou d'être acceptée, avec son échéance. */
interface WaitingOrder {
  orderId: string;
  state: 'AwaitingAcceptance' | 'AwaitingPayment';
  deadline: string;
}

const columnStyle: Record<string, {dot: string; ring: string}> = {
  Placed: {dot: 'bg-sky-500', ring: 'border-t-sky-500'},
  Accepted: {dot: 'bg-brand-500', ring: 'border-t-brand-500'},
  Preparing: {dot: 'bg-amber-500', ring: 'border-t-amber-500'},
  Ready: {dot: 'bg-emerald-500', ring: 'border-t-emerald-500'},
};

function OrderCard({order, tables, onChanged, now, waiting}: {order: OrderSummary; tables?: Map<string, TableInfo>; onChanged: () => Promise<void>; now: number; waiting?: WaitingOrder}) {
  const {can} = useConsole();
  const router = useRouter();
  const toast = useToast();
  const {busy, run} = useAction();
  const unpaid = waiting?.state === 'AwaitingPayment';
  const step = unpaid ? undefined : nextStep[order.status];
  const minutes = (now - new Date(order.createdAtUtc).getTime()) / 60_000;
  const late = order.status !== 'Ready' && minutes > 20;
  const warning = order.status !== 'Ready' && minutes > 10 && !late;
  return (
    <Card className={cx('p-4 shadow-sm transition hover:shadow-md', late && 'border-red-300 ring-1 ring-red-200')}>
      <Link href={`/commandes/${order.id}`} className="block">
        <div className="flex items-center justify-between gap-2">
          <span className="text-lg font-bold">#{order.number}</span>
          <span className={cx('flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold', late ? 'bg-red-50 text-red-700' : warning ? 'bg-amber-50 text-amber-700' : 'bg-slate-100 text-ink-600')}>
            <Timer className="size-3" aria-hidden="true" />{elapsed(order.createdAtUtc, now)}
          </span>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <Badge tone={orderTypeTone[order.type]}>{tableLabel(order, tables)}</Badge>
          {isOnline(order) && <Badge tone="brand">{onlineChannelLabel[order.channel!]}</Badge>}
          {order.channel && remoteChannelLabel[order.channel] && <Badge tone="green">{remoteChannelLabel[order.channel]}</Badge>}
          {order.channel === 'DeliveryPlatform' && <Badge tone="blue">{platformName(order.platform)}{order.externalReference ? ` · ${order.externalReference}` : ''}</Badge>}
          {order.scheduledForUtc && <Badge tone="amber">Pour {time(order.scheduledForUtc)}</Badge>}
          {unpaid && <Badge tone="amber">Paiement en ligne en attente</Badge>}
          {waiting?.state === 'AwaitingAcceptance' && <Badge tone="red">À accepter avant {time(waiting.deadline)}</Badge>}
          <span className="text-xs text-ink-500">{order.itemCount} article{order.itemCount > 1 ? 's' : ''} · {time(order.createdAtUtc)}</span>
        </div>
        <p className="mt-2 text-base font-bold">{money(order.total, order.currencyCode)}</p>
      </Link>
      {step && can(step.permission) && (
        <Button size="sm" className="mt-3 w-full" variant={order.status === 'Ready' ? 'success' : 'primary'} busy={busy === 'next'}
          onClick={() => run('next', async () => {
            try {
              await advance(order.id, order.status);
            } catch (error) {
              if (error instanceof ApiError && error.code === 'orders.order.not_settled') {
                router.push(`/commandes/${order.id}?encaisser=1`);
                return;
              }
              throw error;
            }
            await onChanged();
            toast('success', step.success);
          })}>
          {step.label}
        </Button>
      )}
    </Card>
  );
}

function Board({location, type}: {location: LocationSummary; type: TypeFilter}) {
  const now = useNow(20_000);
  const {data, error, reload} = useResource(
    async signal => {
      const [pages, tables, waiting] = await Promise.all([
        Promise.all(columns.map(column => api<Page<OrderSummary>>('/orders', {query: {locationId: location.id, status: column.status, pageSize: 100}, signal}))),
        tablesBySession(location.id, signal),
        api<WaitingOrder[]>(`/online-sales/locations/${location.id}/waiting-orders`, {signal}).catch(() => [] as WaitingOrder[]),
      ]);
      return {
        columns: pages.map(page => [...page.items].sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc))),
        tables,
        waiting: new Map(waiting.map(item => [item.orderId, item])),
      };
    },
    [location.id],
    10_000,
  );
  const toast = useToast();
  const seen = useRef<Set<string> | null>(null);
  useEffect(() => armChime(), []);
  useEffect(() => {
    if (!data) return;
    const placed = data.columns[0].filter(order => isOnline(order) && data.waiting.get(order.id)?.state !== 'AwaitingPayment');
    const known = seen.current;
    seen.current = new Set(placed.map(order => order.id));
    if (!known) return;
    const fresh = placed.filter(order => !known.has(order.id));
    if (fresh.length === 0) return;
    playChime();
    toast('success', fresh.length > 1 ? `${fresh.length} nouvelles commandes en ligne à accepter.` : `Nouvelle commande en ligne #${fresh[0].number} à accepter.`);
  }, [data, toast]);
  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  const visible = data.columns.map(list => (type === 'all' ? list : list.filter(order => order.type === type)));
  return (
    <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
      {columns.map((column, i) => (
        <section key={column.status} aria-label={column.label} className={cx('min-w-0 rounded-2xl border-t-4 bg-slate-100/80 p-3', columnStyle[column.status].ring)}>
          <h2 className="mb-3 flex items-center justify-between px-1 text-sm font-semibold">
            <span className="flex items-center gap-2"><span className={cx('size-2 rounded-full', columnStyle[column.status].dot)} aria-hidden="true" />{column.label}</span>
            <Badge tone={orderStatusTone[column.status]}>{visible[i].length}</Badge>
          </h2>
          <div className="space-y-3">
            {visible[i].length === 0 && <p className="px-1 py-6 text-center text-sm text-ink-500">Rien pour l’instant</p>}
            {visible[i].map(order => <OrderCard key={order.id} order={order} tables={data.tables} onChanged={reload} now={now} waiting={data.waiting.get(order.id)} />)}
          </div>
        </section>
      ))}
    </div>
  );
}

function Summary({location}: {location: LocationSummary}) {
  const {currency} = useConsole();
  const {data} = useResource(signal => ordersSince(location.id, startOfDay(new Date()), signal, 4), [location.id], 30_000);
  if (!data) return null;
  const day = summarize(data);
  const live = data.filter(order => ['Placed', 'Accepted', 'Preparing', 'Ready'].includes(order.status)).length;
  const stats = [
    {label: 'Commandes du jour', value: number(day.orders), icon: ClipboardList, tone: 'bg-emerald-50 text-emerald-600'},
    {label: 'En cours', value: number(live), icon: Timer, tone: 'bg-amber-50 text-amber-600'},
    {label: 'Chiffre d’affaires', value: money(day.revenue, currency), icon: Coins, tone: 'bg-violet-50 text-violet-600'},
    {label: 'Panier moyen', value: money(day.averageTicket, currency), icon: Receipt, tone: 'bg-sky-50 text-sky-600'},
  ];
  return (
    <div className="mb-6 grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
      {stats.map(stat => <StatCard key={stat.label} icon={<stat.icon />} label={stat.label} value={stat.value} />)}
    </div>
  );
}

function TypeChips({value, onChange}: {value: TypeFilter; onChange: (value: TypeFilter) => void}) {
  const items: {value: TypeFilter; label: string}[] = [
    {value: 'all', label: 'Tous les canaux'},
    {value: 'DineIn', label: orderTypeLabel.DineIn},
    {value: 'Takeaway', label: orderTypeLabel.Takeaway},
    {value: 'Delivery', label: orderTypeLabel.Delivery},
  ];
  return (
    <div className="flex flex-wrap gap-2" role="group" aria-label="Filtrer par canal">
      {items.map(item => (
        <button
          key={item.value}
          type="button"
          aria-pressed={value === item.value}
          onClick={() => onChange(item.value)}
          className={cx('flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm font-medium transition', value === item.value ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-200 bg-white text-ink-600 hover:border-slate-300')}>
          {item.value !== 'all' && <span className="size-2 rounded-full" style={{background: value === item.value ? 'white' : orderTypeColor[item.value]}} aria-hidden="true" />}
          {item.label}
        </button>
      ))}
    </div>
  );
}

const types: {type: OrderType; label: string; text: string; icon: typeof Store}[] = [
  {type: 'Takeaway', label: 'À emporter', text: 'Le client repart avec sa commande.', icon: ShoppingBag},
  {type: 'Delivery', label: 'Livraison', text: 'Commande livrée au client.', icon: Truck},
  {type: 'DineIn', label: 'Sur place', text: 'Ouvrez d’abord la table depuis l’écran Salle.', icon: Store},
];

function NewOrder({location, open, onClose}: {location: LocationSummary; open: boolean; onClose: () => void}) {
  const router = useRouter();
  const {busy, run} = useAction();
  const [remote, setRemote] = useState(false);
  return (
    <>
    <RemoteOrderDialog location={location} open={remote} onClose={() => setRemote(false)} />
    <Modal open={open && !remote} onClose={onClose} title="Nouvelle commande">
      <div className="grid gap-3">
        {types.map(item => (
          <button
            key={item.type}
            type="button"
            disabled={!!busy}
            onClick={() => {
              if (item.type === 'DineIn') {
                router.push('/salle');
                return;
              }
              void run(item.type, async () => {
                const id = await createOrder(location.id, item.type);
                router.push(`/commandes/${id}`);
              });
            }}
            className="flex items-center gap-4 rounded-xl border border-slate-200 p-4 text-left transition hover:border-brand-300 hover:bg-brand-50/40 disabled:opacity-60">
            <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600"><item.icon className="size-5" aria-hidden="true" /></span>
            <span>
              <span className="block font-semibold">{busy === item.type ? 'Création…' : item.label}</span>
              <span className="block text-sm text-ink-500">{item.text}</span>
            </span>
          </button>
        ))}
        <button type="button" disabled={!!busy} onClick={() => setRemote(true)}
          className="flex items-center gap-4 rounded-xl border border-slate-200 p-4 text-left transition hover:border-brand-300 hover:bg-brand-50/40 disabled:opacity-60">
          <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-emerald-50 text-emerald-600"><MessageCircle className="size-5" aria-hidden="true" /></span>
          <span>
            <span className="block font-semibold">Par WhatsApp, téléphone ou plateforme</span>
            <span className="block text-sm text-ink-500">Client retrouvé par son numéro, message collé lu automatiquement ; Glovo, Gozem… avec leur numéro de commande.</span>
          </span>
        </button>
      </div>
    </Modal>
    </>
  );
}

/** Recherche par numéro (barre de recherche) : filtre serveur sur le numéro complet ou sa fin. */
function useNumberSearch(locationId: string | undefined, onSeveral: (number: string) => void) {
  const params = useSearchParams();
  const router = useRouter();
  const toast = useToast();
  const wanted = params.get('numero');
  useEffect(() => {
    if (!wanted || !locationId) return;
    let active = true;
    api<Page<OrderSummary>>('/orders', {query: {locationId, number: wanted, pageSize: 2}}).then(page => {
      if (!active) return;
      if (page.items.length === 1) router.replace(`/commandes/${page.items[0].id}`);
      else if (page.items.length > 1) {
        onSeveral(wanted);
        router.replace('/commandes');
      } else {
        toast('error', `Aucune commande n° ${wanted}.`);
        router.replace('/commandes');
      }
    }).catch(error => {
      if (!active) return;
      toast('error', error instanceof ApiError ? describe(error) : `Aucune commande n° ${wanted}.`);
      router.replace('/commandes');
    });
    return () => {
      active = false;
    };
  }, [wanted, locationId, router, toast, onSeveral]);
}

export default function OrdersPage() {
  const {can, location: selected} = useConsole();
  const params = useSearchParams();
  const [view, setView] = useState<View>('live');
  const [type, setType] = useState<TypeFilter>('all');
  const [creating, setCreating] = useState(() => params.get('nouvelle') === '1');
  const [searched, setSearched] = useState<string | null>(null);
  const showSeveral = useCallback((number: string) => {
    setSearched(number);
    setView('history');
  }, []);
  useNumberSearch(selected?.id, showSeveral);
  return (
    <WithLocation>
      {location => (
        <>
          <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Commandes</h1>
              <p className="mt-1 text-sm text-ink-500">Suivi en direct de {location.name}, mis à jour toutes les 10 secondes.</p>
            </div>
            {can('orders.take') && location.status === 'Active' && (
              <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouvelle commande</Button>
            )}
          </div>
          {location.status !== 'Active' && <div className="mb-4"><Alert tone="amber">Cet établissement est fermé : ouvrez-le depuis le tableau de bord ou « Établissements » pour prendre des commandes.</Alert></div>}
          <Summary location={location} />
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <Tabs<View>
              label="Vue"
              value={view}
              onChange={setView}
              items={[
                {value: 'live', label: 'En cours'},
                {value: 'history', label: 'Historique'},
                {value: 'drafts', label: 'Brouillons'},
              ]}
            />
            <div className="mb-5"><TypeChips value={type} onChange={setType} /></div>
          </div>
          {view === 'live' && <Board location={location} type={type} />}
          {view === 'history' && <OrderHistory key={searched ?? 'history'} location={location} type={type} initialNumber={searched ?? undefined} />}
          {view === 'drafts' && <OrderHistory key="drafts" location={location} type={type} drafts />}
          <NewOrder location={location} open={creating} onClose={() => setCreating(false)} />
        </>
      )}
    </WithLocation>
  );
}
