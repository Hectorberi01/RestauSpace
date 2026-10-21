'use client';
import {useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {
  BarChart3, BookOpen, Boxes, CalendarDays, ChefHat, CircleDollarSign, ClipboardList, Clock, Inbox, LayoutGrid, MessageSquareText, Plus,
  Receipt, ShoppingBag, ThumbsDown, Users,
} from 'lucide-react';
import {api, post, type Page} from '@/lib/api';
import {
  addDays, byDimension, change, cumulativeByHour, dailyFromOrders, dailyValues, indicator, loadDashboard, ordersBetween, ordersSince, periodRange,
  sameDay, startOfDay, summarize, topProducts, typeDimension, type Feedback, type Period, type Reservation,
} from '@/lib/dashboard';
import {ago, compact, isoDate, money, number, orderStatusLabel, orderStatusTone, orderTypeColor, orderTypeLabel, orderTypeTone, time} from '@/lib/format';
import {useNow, useResource} from '@/lib/hooks';
import {listItems} from '@/lib/inventory';
import {loadMe} from '@/lib/profile';
import {labelOf} from '@/lib/reports';
import {reservationStatusLabel, reservationStatusTone} from '@/lib/reservations';
import type {DiningArea, LocationSummary, OrderType, ReportRow, TableSession} from '@/lib/types';
import {Donut, Sparkline, TrendChart} from '@/components/charts';
import {useConsole, WithLocation} from '@/components/console';
import {Alert, Badge, Card, Delta, Panel, SeeAll, Spinner, StatCard, cx, useAction} from '@/components/ui';
import {DashboardSkeleton, LiveStat, PeriodPicker, RankedBars, Stars} from './widgets';

interface LocationDetail {
  id: string;
  status: LocationSummary['status'];
  weeklyHours: {day: string; start: string; end: string; endsNextDay: boolean}[];
}

const weekdays = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const orderTypes = Object.keys(orderTypeColor) as OrderType[];
const PERIOD_KEY = 'restaurantos.dashboard.period';
const dailyMetrics = ['sales.revenue', 'sales.orders_placed', 'sales.orders_completed', 'reservations.covers_seated'];
const sum = (values: number[]) => values.reduce((total, value) => total + value, 0);

const indicatorDefs: {metric: string; label: string; kind: 'money' | 'count' | 'rate' | 'minutes'; better: 'up' | 'down'}[] = [
  {metric: 'sales.orders_completed', label: 'Commandes terminées', kind: 'count', better: 'up'},
  {metric: 'payments.captured_amount', label: 'Encaissé', kind: 'money', better: 'up'},
  {metric: 'sales.cancellation_rate', label: 'Taux d’annulation', kind: 'rate', better: 'down'},
  {metric: 'kitchen.average_preparation_minutes', label: 'Préparation moyenne', kind: 'minutes', better: 'down'},
  {metric: 'sales.discount_amount', label: 'Remises accordées', kind: 'money', better: 'down'},
  {metric: 'payments.refunded_amount', label: 'Remboursé', kind: 'money', better: 'down'},
  {metric: 'reservations.no_show_rate', label: 'Absences (no-show)', kind: 'rate', better: 'down'},
  {metric: 'loyalty.points_earned', label: 'Points de fidélité', kind: 'count', better: 'up'},
];

function usePeriod(): [Period, (value: Period) => void] {
  const [period, setPeriod] = useState<Period>('day');
  useEffect(() => {
    try {
      const saved = window.localStorage.getItem(PERIOD_KEY);
      if (saved === 'day' || saved === 'week' || saved === 'month') setPeriod(saved);
    } catch {
      // Stockage indisponible : aujourd'hui.
    }
  }, []);
  const choose = (value: Period) => {
    setPeriod(value);
    try {
      window.localStorage.setItem(PERIOD_KEY, value);
    } catch {
      // Le choix vaut pour cette visite.
    }
  };
  return [period, choose];
}

/** Ce qui demande une action : stock sous le seuil, avis négatifs (2 étoiles ou moins) encore à traiter. */
function Alerts({location}: {location: LocationSummary}) {
  const {can} = useConsole();
  const stock = useResource(
    signal => (can('inventory.operate') || can('inventory.manage') ? listItems(location.id, true, signal) : Promise.resolve(null)),
    [location.id],
    120_000,
  );
  const negative = useResource(
    signal =>
      can('reviews.view')
        ? api<Page<Feedback>>('/reviews/feedbacks', {query: {locationId: location.id, status: 'Submitted', maxRating: 2, pageSize: 5}, signal})
        : Promise.resolve(null),
    [location.id],
    120_000,
  );
  const low = stock.data ?? [];
  const reviews = negative.data?.items ?? [];
  if (low.length === 0 && reviews.length === 0) return null;
  const names = low.slice(0, 3).map(item => item.ingredientName ?? 'Article').join(', ');

  return (
    <section aria-label="Alertes" className="grid gap-3 lg:grid-cols-2">
      {low.length > 0 && (
        <Link href="/stock" className="group flex min-w-0 items-center gap-3 rounded-xl border border-amber-200 bg-amber-50/70 px-4 py-3 transition hover:border-amber-300 hover:bg-amber-50">
          <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-amber-100 text-amber-700"><Boxes className="size-4.5" aria-hidden="true" /></span>
          <span className="min-w-0 flex-1">
            <span className="block text-sm font-semibold text-amber-900">Stock bas : {low.length} article{low.length > 1 ? 's' : ''} à réapprovisionner</span>
            <span className="block truncate text-xs text-amber-800">{names}{low.length > 3 ? '…' : ''}</span>
          </span>
          <span className="hidden shrink-0 text-xs font-semibold text-amber-800 group-hover:underline sm:inline">Voir le stock</span>
        </Link>
      )}
      {reviews.length > 0 && (
        <Link href="/avis" className="group flex min-w-0 items-center gap-3 rounded-xl border border-red-200 bg-red-50/70 px-4 py-3 transition hover:border-red-300 hover:bg-red-50">
          <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-red-100 text-red-700"><ThumbsDown className="size-4.5" aria-hidden="true" /></span>
          <span className="min-w-0 flex-1">
            <span className="block text-sm font-semibold text-red-900">
              {negative.data?.hasMore ? `Plus de ${reviews.length}` : reviews.length} avis négatif{reviews.length > 1 ? 's' : ''} à traiter
            </span>
            <span className="block truncate text-xs text-red-800">{reviews[0].comment ?? `Note de ${reviews[0].rating}/5`}</span>
          </span>
          <span className="hidden shrink-0 text-xs font-semibold text-red-800 group-hover:underline sm:inline">Répondre</span>
        </Link>
      )}
    </section>
  );
}

/** « En ce moment » : ouverture, commandes à traiter, salle et prochaine réservation. Toujours sur aujourd'hui. */
function LiveBar({location, detail, todayOrders, reservations, floor, now, onChanged}: {
  location: LocationSummary;
  detail: LocationDetail;
  todayOrders: {status: string}[];
  reservations: Reservation[];
  floor: {occupied: number; tables: number; guests: number} | null;
  now: number;
  onChanged: () => Promise<void>;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const open = location.status === 'Active';
  const slots = detail.weeklyHours.filter(slot => slot.day === weekdays[new Date(now).getDay()]);
  const toPlace = todayOrders.filter(order => order.status === 'Placed').length;
  const inKitchen = todayOrders.filter(order => order.status === 'Accepted' || order.status === 'Preparing').length;
  const ready = todayOrders.filter(order => order.status === 'Ready').length;
  const next = reservations.find(r => (r.status === 'Confirmed' || r.status === 'Pending') && new Date(r.startAtUtc).getTime() >= now - 15 * 60_000);
  const toggle = () =>
    run('toggle', async () => {
      if (open) await post(`/locations/${location.id}/temporarily-close`, {reason: null});
      else await post(`/locations/${location.id}/${location.status === 'Draft' ? 'activate' : 'reopen'}`);
      await onChanged();
    }, open ? 'Établissement fermé temporairement.' : 'Établissement ouvert.');

  return (
    <Card className="overflow-hidden">
      <div className="grid divide-y divide-slate-100 lg:grid-cols-[minmax(0,15rem)_minmax(0,1fr)] lg:divide-x lg:divide-y-0">
        <div className="flex items-center justify-between gap-4 px-5 py-4">
          <div className="min-w-0">
            <p className="flex items-center gap-2 text-sm font-semibold text-ink-900">
              <span className={cx('relative flex size-2.5', open ? 'text-brand-500' : 'text-slate-400')} aria-hidden="true">
                {open && <span className="absolute inline-flex size-full animate-ping rounded-full bg-brand-400 opacity-60" />}
                <span className="relative inline-flex size-2.5 rounded-full bg-current" />
              </span>
              {open ? 'Ouvert' : location.status === 'Draft' ? 'Pas encore ouvert' : 'Fermé'}
            </p>
            <p className="mt-1 flex items-start gap-1.5 text-xs text-ink-500">
              <Clock className="mt-px size-3.5 shrink-0" aria-hidden="true" />
              <span>{slots.length ? slots.map(slot => <span key={slot.start} className="block tabular-nums">{slot.start} – {slot.end}</span>) : 'Fermé ce jour'}</span>
            </p>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={open}
            aria-label={open ? 'Fermer temporairement' : 'Ouvrir'}
            title={can('locations.manage') ? (open ? 'Fermer temporairement' : 'Ouvrir') : 'Réservé aux gérants'}
            disabled={!can('locations.manage') || busy === 'toggle'}
            onClick={toggle}
            className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', open ? 'bg-brand-600' : 'bg-slate-300')}>
            <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', open ? 'left-5.5' : 'left-0.5')} />
          </button>
        </div>
        <div className="grid grid-cols-2 gap-1 p-2 sm:grid-cols-4">
          <LiveStat href="/commandes" icon={<Inbox className="size-4.5" aria-hidden="true" />} tone={toPlace ? 'amber' : 'neutral'} value={toPlace} label="À accepter" />
          <LiveStat href="/cuisine" icon={<ChefHat className="size-4.5" aria-hidden="true" />} tone={inKitchen ? 'blue' : 'neutral'} value={inKitchen} label="En cuisine" hint={ready ? `${ready} prête${ready > 1 ? 's' : ''}` : undefined} />
          <LiveStat
            href="/salle"
            icon={<LayoutGrid className="size-4.5" aria-hidden="true" />}
            tone={floor?.occupied ? 'brand' : 'neutral'}
            value={floor ? <>{floor.occupied}<span className="text-sm font-semibold text-ink-500">/{floor.tables}</span></> : '—'}
            label="Tables occupées"
            hint={floor?.guests ? `${floor.guests} couvert${floor.guests > 1 ? 's' : ''}` : undefined}
          />
          <LiveStat
            href="/reservations"
            icon={<CalendarDays className="size-4.5" aria-hidden="true" />}
            value={next ? time(next.startAtUtc) : '—'}
            label={next ? 'À venir' : 'Pas de réservation à venir'}
            hint={next ? `${next.partySize} pers.` : undefined}
          />
        </div>
      </div>
    </Card>
  );
}

function Dashboard({location}: {location: LocationSummary}) {
  const {can, currency, reloadLocations} = useConsole();
  const now = useNow(60_000);
  const [period, setPeriod] = usePeriod();
  const [today] = useState(() => startOfDay(new Date()));
  const yesterday = addDays(today, -1);
  const tomorrow = addDays(today, 1);
  const range = useMemo(() => periodRange(period, today), [period, today]);
  const reporting = can('reporting.view');
  const formatMoney = (value: number) => money(value, currency);

  const me = useResource(signal => loadMe(signal).catch(() => null), []);

  const main = useResource(
    async signal => {
      const [orders, reservations, detail, sessions, areas] = await Promise.all([
        ordersSince(location.id, yesterday, signal),
        api<Reservation[]>('/reservations', {query: {locationId: location.id, from: today.toISOString(), to: tomorrow.toISOString()}, signal}).catch(() => [] as Reservation[]),
        api<LocationDetail>(`/locations/${location.id}`, {signal}),
        api<TableSession[]>('/floor/table-sessions', {query: {locationId: location.id}, signal}).catch(() => null),
        api<DiningArea[]>('/floor/dining-areas', {query: {locationId: location.id}, signal}).catch(() => null),
      ]);
      return {orders, reservations: reservations.sort((a, b) => a.startAtUtc.localeCompare(b.startAtUtc)), detail, sessions, areas};
    },
    [location.id, today.getTime()],
    30_000,
  );

  const report = useResource(
    async signal => {
      if (!reporting) return null;
      const windowFrom = addDays(today, -6) < range.previousFrom ? addDays(today, -6) : range.previousFrom;
      const [current, previous, daily] = await Promise.all([
        loadDashboard(location.id, range.from, range.to, signal),
        loadDashboard(location.id, range.previousFrom, range.previousTo, signal),
        api<{rows: ReportRow[]}>('/reporting/run', {
          method: 'POST',
          body: {metrics: dailyMetrics, filters: [{field: 'location.id', operator: 'EqualTo', value: location.id}], from: isoDate(windowFrom), to: isoDate(today), granularity: 'Day', byLocation: false},
          signal,
        }).then(result => result.rows),
      ]);
      return {current: current.totals, previous: previous.totals, daily};
    },
    [location.id, period, today.getTime(), reporting],
    60_000,
  );

  const history = useResource(
    signal => (!reporting && period !== 'day' ? ordersSince(location.id, range.previousFrom, signal, 20) : Promise.resolve(null)),
    [location.id, period, today.getTime(), reporting],
    120_000,
  );

  const reviews = useResource(
    async signal => {
      if (!can('reviews.view')) return null;
      const page = await api<Page<Feedback>>('/reviews/feedbacks', {query: {locationId: location.id, pageSize: 5}, signal});
      const names = new Map<string, string>();
      if (can('customers.view')) {
        await Promise.all(
          [...new Set(page.items.map(item => item.customerId).filter((id): id is string => !!id))].map(id =>
            api<{name: string | null}>(`/customers/${id}`, {signal}).then(customer => customer.name && names.set(id, customer.name)).catch(() => undefined),
          ),
        );
      }
      return page.items.map(item => ({...item, author: (item.customerId && names.get(item.customerId)) || 'Client'}));
    },
    [location.id],
    120_000,
  );

  const top = useResource(signal => topProducts(location.id, 20, signal), [location.id]);

  if (main.error) return <Alert>{main.error}</Alert>;
  if (!main.data) return <DashboardSkeleton />;

  const {orders, reservations, detail, sessions, areas} = main.data;
  const todayOrders = orders.filter(order => sameDay(order.createdAtUtc, today));
  const yesterdayOrders = orders.filter(order => sameDay(order.createdAtUtc, yesterday));
  const live = summarize(todayOrders);
  const liveBefore = summarize(yesterdayOrders);
  const activeReservations = reservations.filter(r => r.status !== 'Cancelled' && r.status !== 'NoShow');
  const covers = activeReservations.reduce((total, r) => total + r.partySize, 0);
  const tables = areas?.flatMap(area => area.tables).filter(table => table.isInService) ?? [];
  const openSessions = sessions?.filter(session => session.isOpen) ?? [];
  const floor = areas && sessions ? {occupied: openSessions.length, tables: tables.length, guests: openSessions.reduce((total, s) => total + s.guestCount, 0)} : null;

  // --- Chiffres de la période : reporting si possible, sinon les commandes elles-mêmes.
  const rows = report.data?.daily ?? [];
  const sparkDays = period === 'day' ? Array.from({length: 7}, (_, i) => addDays(today, i - 6)) : range.days;
  const series = (metric: string, days: Date[], fallback: 'revenue' | 'orders') =>
    report.data ? dailyValues(rows, metric, days) : dailyFromOrders(period === 'day' ? orders : history.data ?? [], days, fallback);
  const totalOf = (rowsOf: ReportRow[] | undefined, metric: string) => (rowsOf ? indicator(rowsOf, metric) : null);

  let revenue: number;
  let revenueBefore: number;
  let orderCount: number;
  let orderCountBefore: number;
  let averageTicket: number;
  let averageTicketBefore: number;
  let byType: Record<OrderType, number>;
  if (period === 'day') {
    revenue = totalOf(report.data?.current, 'sales.revenue') ?? live.revenue;
    revenueBefore = totalOf(report.data?.previous, 'sales.revenue') ?? liveBefore.revenue;
    orderCount = live.orders;
    orderCountBefore = liveBefore.orders;
    averageTicket = totalOf(report.data?.current, 'sales.average_ticket') ?? live.averageTicket;
    averageTicketBefore = totalOf(report.data?.previous, 'sales.average_ticket') ?? liveBefore.averageTicket;
    byType = live.byType;
  } else if (report.data) {
    revenue = sum(dailyValues(rows, 'sales.revenue', range.days));
    revenueBefore = sum(dailyValues(rows, 'sales.revenue', range.previousDays));
    orderCount = sum(dailyValues(rows, 'sales.orders_placed', range.days));
    orderCountBefore = sum(dailyValues(rows, 'sales.orders_placed', range.previousDays));
    const completed = sum(dailyValues(rows, 'sales.orders_completed', range.days));
    const completedBefore = sum(dailyValues(rows, 'sales.orders_completed', range.previousDays));
    averageTicket = completed ? revenue / completed : 0;
    averageTicketBefore = completedBefore ? revenueBefore / completedBefore : 0;
    const inRange = rows.filter(row => row.metric === 'sales.orders_placed' && row.periodStart.slice(0, 10) >= isoDate(range.from));
    byType = Object.fromEntries(orderTypes.map(type => [type, inRange.filter(row => row.dimension === typeDimension[type]).reduce((total, row) => total + row.value, 0)])) as Record<OrderType, number>;
  } else {
    const current = summarize(ordersBetween(history.data ?? [], range.from, range.to));
    const previous = summarize(ordersBetween(history.data ?? [], range.previousFrom, range.previousTo));
    ({revenue, orders: orderCount, averageTicket, byType} = current);
    ({revenue: revenueBefore, orders: orderCountBefore, averageTicket: averageTicketBefore} = previous);
  }
  const typeTotal = sum(orderTypes.map(type => byType[type]));
  const seated = report.data && period !== 'day' ? sum(dailyValues(rows, 'reservations.covers_seated', range.days)) : null;
  const seatedBefore = report.data && period !== 'day' ? sum(dailyValues(rows, 'reservations.covers_seated', range.previousDays)) : null;

  // --- Courbe : cumul horaire du jour contre hier, ou CA par jour contre la période précédente.
  const dayFormat = new Intl.DateTimeFormat('fr-FR', period === 'week' ? {weekday: 'short', day: 'numeric'} : {day: 'numeric', month: 'short'});
  let chart: {labels: string[]; current: number[]; previous: number[]};
  if (period === 'day') {
    const todaySlots = detail.weeklyHours.filter(slot => slot.day === weekdays[today.getDay()]);
    const opening = todaySlots.length ? Math.min(...todaySlots.map(slot => Number(slot.start.slice(0, 2)))) : 8;
    const closing = todaySlots.length ? Math.max(...todaySlots.map(slot => (slot.endsNextDay ? 23 : Number(slot.end.slice(0, 2))))) : 22;
    const hour = new Date(now).getHours();
    const start = Math.min(opening, hour);
    const end = Math.max(closing, hour);
    const previous = cumulativeByHour(yesterdayOrders, start, end);
    chart = {labels: previous.map(point => point.label), current: cumulativeByHour(todayOrders, start, hour).map(point => point.value), previous: previous.map(point => point.value)};
  } else {
    chart = {
      labels: range.days.map(day => dayFormat.format(day).replace('.', '')),
      current: series('sales.revenue', range.days, 'revenue'),
      previous: series('sales.revenue', range.previousDays, 'revenue'),
    };
  }

  const payments = report.data ? byDimension(report.data.current, 'payments.captured_amount') : [];
  const paymentsTotal = sum(payments.map(item => item.value));

  const activity = [
    ...orders.slice(0, 8).map(order => ({at: order.createdAtUtc, icon: ShoppingBag, tone: 'bg-brand-50 text-brand-700', text: `Commande #${order.number}`, detail: `${orderTypeLabel[order.type]} · ${money(order.total, order.currencyCode)}`, href: `/commandes/${order.id}`})),
    ...reservations.slice(0, 6).map(r => ({at: r.createdAtUtc, icon: CalendarDays, tone: 'bg-violet-50 text-violet-700', text: `Réservation pour ${r.partySize}`, detail: `${time(r.startAtUtc)} · ${r.guestName ?? 'Client'}`, href: '/reservations'})),
    ...(reviews.data ?? []).map(review => ({at: review.submittedAtUtc, icon: MessageSquareText, tone: 'bg-amber-50 text-amber-700', text: `Avis ${review.rating}/5`, detail: review.author, href: '/avis'})),
  ]
    .sort((a, b) => b.at.localeCompare(a.at))
    .slice(0, 6);

  const hour = new Date(now).getHours();
  const firstName = me.data?.firstName?.trim();
  const greeting = `${hour >= 18 || hour < 5 ? 'Bonsoir' : 'Bonjour'}${firstName ? `, ${firstName}` : ''}`;
  const longDate = new Intl.DateTimeFormat('fr-FR', {weekday: 'long', day: 'numeric', month: 'long'}).format(today);
  const periodNoun = period === 'day' ? 'aujourd’hui' : period === 'week' ? 'sur 7 jours' : 'sur 30 jours';
  const reviewAverage = reviews.data?.length ? reviews.data.reduce((total, review) => total + review.rating, 0) / reviews.data.length : null;

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div className="min-w-0">
          <p className="text-sm font-medium text-ink-500 first-letter:uppercase">{longDate}</p>
          <h1 className="mt-1 text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{greeting}</h1>
          <p className="mt-1 text-sm text-ink-500">L’activité de <span className="font-medium text-ink-800">{location.name}</span> {periodNoun}.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <PeriodPicker value={period} onChange={setPeriod} />
          <Link href="/salle" className="hidden h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 sm:inline-flex text-sm font-medium text-ink-800 shadow-[0_1px_2px_rgba(15,23,42,0.04)] hover:bg-slate-50">
            <Users className="size-4" aria-hidden="true" />Salle
          </Link>
          <Link href="/menu" className="hidden h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 sm:inline-flex text-sm font-medium text-ink-800 shadow-[0_1px_2px_rgba(15,23,42,0.04)] hover:bg-slate-50">
            <BookOpen className="size-4" aria-hidden="true" />Menu
          </Link>
          {can('orders.take') && (
            <Link href="/commandes?nouvelle=1" className="inline-flex h-10 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-sm hover:bg-brand-700">
              <Plus className="size-4" aria-hidden="true" />Nouvelle commande
            </Link>
          )}
        </div>
      </header>

      <LiveBar location={location} detail={detail} todayOrders={todayOrders} reservations={activeReservations} floor={floor} now={now} onChanged={async () => {
        await reloadLocations();
        await main.reload();
      }} />

      <Alerts location={location} />

      <section aria-label="Chiffres clés" className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <StatCard
          icon={<CircleDollarSign className="size-4.5" aria-hidden="true" />}
          label="Chiffre d’affaires"
          value={formatMoney(revenue)}
          delta={<Delta value={change(revenue, revenueBefore)} label={range.compareLabel} />}
          chart={<Sparkline values={series('sales.revenue', sparkDays, 'revenue')} label="Chiffre d’affaires par jour" />}
        />
        <StatCard
          icon={<ClipboardList className="size-4.5" aria-hidden="true" />}
          label="Commandes"
          value={number(orderCount)}
          delta={<Delta value={change(orderCount, orderCountBefore)} label={range.compareLabel} />}
          chart={<Sparkline values={series('sales.orders_placed', sparkDays, 'orders')} color="#3b82f6" label="Commandes par jour" />}
        />
        <StatCard
          icon={<Receipt className="size-4.5" aria-hidden="true" />}
          label="Panier moyen"
          value={formatMoney(averageTicket)}
          delta={<Delta value={change(averageTicket, averageTicketBefore)} label={range.compareLabel} />}
        />
        {period === 'day' ? (
          <StatCard
            icon={<CalendarDays className="size-4.5" aria-hidden="true" />}
            label="Couverts réservés"
            value={number(covers)}
            hint={<span className="text-xs text-ink-500">{activeReservations.length} réservation{activeReservations.length > 1 ? 's' : ''} aujourd’hui</span>}
          />
        ) : (
          <StatCard
            icon={<Users className="size-4.5" aria-hidden="true" />}
            label="Couverts installés"
            value={seated === null ? '—' : number(seated)}
            delta={seated === null ? <span className="text-xs text-ink-500">Disponible avec l’accès aux rapports</span> : <Delta value={change(seated, seatedBefore ?? 0)} label={range.compareLabel} />}
          />
        )}
      </section>

      <div className="grid gap-6 xl:grid-cols-3">
        <Panel
          className="xl:col-span-2"
          title="Chiffre d’affaires"
          subtitle={period === 'day' ? 'Cumul heure par heure des commandes terminées, comparé à hier' : `Par jour, comparé aux ${range.days.length} jours précédents`}
          action={
            <div className="hidden items-center gap-4 text-xs text-ink-600 sm:flex">
              <span className="flex items-center gap-1.5"><span className="h-0.5 w-4 rounded bg-brand-600" />{period === 'day' ? 'Aujourd’hui' : 'Période'}</span>
              <span className="flex items-center gap-1.5"><span className="h-0 w-4 border-t-2 border-dashed border-slate-400" />{period === 'day' ? 'Hier' : 'Période précédente'}</span>
            </div>
          }>
          <div className="mb-3 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <span className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900 tabular-nums">{formatMoney(revenue)}</span>
            <Delta value={change(revenue, revenueBefore)} label={range.compareLabel} />
          </div>
          {period !== 'day' && !reporting && !history.data ? (
            <Spinner />
          ) : (
            <TrendChart
              labels={chart.labels}
              current={chart.current}
              previous={chart.previous}
              format={formatMoney}
              axisFormat={compact}
              currentLabel={period === 'day' ? 'Aujourd’hui' : 'Période'}
              previousLabel={period === 'day' ? 'Hier' : 'Précédente'}
            />
          )}
        </Panel>

        <div className="grid gap-6">
          <Panel title="Commandes par canal" subtitle={`${number(typeTotal)} commande${typeTotal > 1 ? 's' : ''} ${periodNoun}`}>
            <div className="flex items-center gap-4">
              <div className="shrink-0 [&_svg]:size-28">
                <Donut segments={orderTypes.map(type => ({label: orderTypeLabel[type], value: byType[type], color: orderTypeColor[type]}))} total={number(typeTotal)} caption="commandes" />
              </div>
              <ul className="min-w-0 flex-1 space-y-2.5 text-sm">
                {orderTypes.map(type => (
                  <li key={type} className="flex items-center gap-2.5">
                    <span className="size-2.5 shrink-0 rounded-full" style={{background: orderTypeColor[type]}} />
                    <span className="min-w-0 flex-1 truncate text-ink-600">{orderTypeLabel[type]}</span>
                    <span className="shrink-0 text-right tabular-nums"><span className="font-semibold text-ink-900">{typeTotal ? Math.round((byType[type] / typeTotal) * 100) : 0} %</span><span className="ml-1.5 text-xs text-ink-500">{number(byType[type])}</span></span>
                  </li>
                ))}
              </ul>
            </div>
          </Panel>
          {reporting && (
            <Panel title="Encaissements par moyen" subtitle={paymentsTotal ? `${formatMoney(paymentsTotal)} encaissés ${periodNoun}` : undefined} action={<SeeAll href="/caisse" label="Caisse" />}>
              {!report.data ? <Spinner /> : (
                <RankedBars
                  empty="Aucun encaissement sur la période."
                  items={payments.slice(0, 4).map(item => ({
                    key: item.dimension,
                    label: labelOf(item.dimension),
                    value: item.value,
                    display: formatMoney(item.value),
                    hint: paymentsTotal ? `${Math.round((item.value / paymentsTotal) * 100)} %` : undefined,
                  }))}
                />
              )}
            </Panel>
          )}
        </div>
      </div>

      {reporting && report.data && (
        <Panel title="Indicateurs" subtitle={period === 'day' ? 'Calculés par les rapports, comparés à hier' : `Calculés par les rapports, comparés aux ${range.days.length} jours précédents`} action={<SeeAll href="/rapports" label="Rapports" />}>
          <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-slate-100 bg-slate-100 sm:grid-cols-4">
            {indicatorDefs.map(def => {
              const value = indicator(report.data!.current, def.metric) ?? 0;
              const before = indicator(report.data!.previous, def.metric) ?? 0;
              const format = (amount: number) =>
                def.kind === 'money' ? formatMoney(amount) : def.kind === 'rate' ? `${Math.round(amount * 1000) / 10} %` : def.kind === 'minutes' ? `${Math.round(amount)} min` : number(amount);
              return (
                <div key={def.metric} className="min-w-0 bg-white px-4 py-3.5">
                  <dt className="truncate text-xs text-ink-500">{def.label}</dt>
                  <dd className="mt-1 flex flex-wrap items-baseline gap-x-2 gap-y-1">
                    <span className="text-lg font-semibold text-ink-900 tabular-nums">{format(value)}</span>
                    <span title={`Avant : ${format(before)}`}><Delta value={change(value, before)} better={def.better} /></span>
                  </dd>
                </div>
              );
            })}
          </dl>
        </Panel>
      )}

      <div className="grid gap-6 xl:grid-cols-3">
        <Panel className="xl:col-span-2" title="Commandes récentes" action={<SeeAll href="/commandes" />} bodyClassName="-mx-5 -mb-5">
          {orders.length === 0 ? (
            <p className="px-5 py-10 text-center text-sm text-ink-500">Aucune commande depuis hier.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[520px] text-left text-sm">
                <thead className="border-y border-slate-100 bg-slate-50/70 text-xs text-ink-500">
                  <tr>
                    <th scope="col" className="px-5 py-2 font-medium">Commande</th>
                    <th scope="col" className="px-3 py-2 font-medium">Canal</th>
                    <th scope="col" className="px-3 py-2 font-medium">Statut</th>
                    <th scope="col" className="px-5 py-2 text-right font-medium">Montant</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {orders.slice(0, 6).map(order => (
                    <tr key={order.id} className="transition hover:bg-slate-50/80">
                      <td className="px-5 py-3">
                        <Link href={`/commandes/${order.id}`} className="font-semibold text-ink-900 hover:text-brand-700">#{order.number}</Link>
                        <span className="ml-2 text-xs text-ink-500">{sameDay(order.createdAtUtc, today) ? time(order.createdAtUtc) : `hier ${time(order.createdAtUtc)}`}</span>
                      </td>
                      <td className="px-3 py-3"><Badge tone={orderTypeTone[order.type]}>{orderTypeLabel[order.type]}</Badge></td>
                      <td className="px-3 py-3"><Badge tone={orderStatusTone[order.status]}>{orderStatusLabel[order.status]}</Badge></td>
                      <td className="px-5 py-3 text-right font-semibold whitespace-nowrap text-ink-900 tabular-nums">{money(order.total, order.currencyCode)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Panel>

        <Panel title="Réservations du jour" subtitle={activeReservations.length ? `${covers} couvert${covers > 1 ? 's' : ''} attendu${covers > 1 ? 's' : ''}` : undefined} action={<SeeAll href="/reservations" />}>
          {activeReservations.length === 0 ? (
            <p className="py-10 text-center text-sm text-ink-500">Aucune réservation aujourd’hui.</p>
          ) : (
            <ol className="relative space-y-1 before:absolute before:top-2 before:bottom-2 before:left-[2.85rem] before:w-px before:bg-slate-200">
              {activeReservations.slice(0, 6).map(r => {
                const past = new Date(r.startAtUtc).getTime() + r.durationMinutes * 60_000 < now;
                return (
                  <li key={r.id} className={cx('relative flex items-center gap-3 rounded-lg py-1.5 text-sm', past && 'opacity-55')}>
                    <span className="w-10 shrink-0 text-right font-semibold text-ink-900 tabular-nums">{time(r.startAtUtc)}</span>
                    <span className="relative z-10 size-2 shrink-0 rounded-full border-2 border-white bg-brand-500 ring-1 ring-brand-200" aria-hidden="true" />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium text-ink-800">{r.guestName ?? 'Client'}</span>
                      <span className="flex items-center gap-1 text-xs text-ink-500"><Users className="size-3" aria-hidden="true" />{r.partySize} pers.</span>
                    </span>
                    <Badge tone={reservationStatusTone[r.status]}>{reservationStatusLabel[r.status]}</Badge>
                  </li>
                );
              })}
            </ol>
          )}
        </Panel>
      </div>

      <div className="grid gap-6 lg:grid-cols-2 xl:grid-cols-3">
        <Panel title="Plats les plus vendus" subtitle={top.data?.[0]?.source === 'reporting' ? 'Sur les 30 derniers jours' : 'Sur les dernières commandes terminées'} action={<SeeAll href="/menu" label="Menu" />}>
          {!top.data ? <Spinner /> : (
            <RankedBars
              empty="Pas encore de commande terminée."
              items={top.data.slice(0, 5).map(product => ({key: product.productId, label: product.name, value: product.quantity, display: number(product.quantity), hint: 'vendus'}))}
            />
          )}
        </Panel>

        <Panel title="Avis clients" action={can('reviews.view') ? <SeeAll href="/avis" /> : undefined}>
          {!can('reviews.view') ? (
            <p className="text-sm text-ink-500">Réservé aux membres qui peuvent consulter les avis.</p>
          ) : !reviews.data ? (
            <Spinner />
          ) : reviews.data.length === 0 ? (
            <p className="py-10 text-center text-sm text-ink-500">Aucun avis pour l’instant.</p>
          ) : (
            <>
              {reviewAverage !== null && (
                <div className="mb-4 flex items-center gap-3 rounded-xl bg-slate-50 px-4 py-3">
                  <span className="text-2xl font-bold text-ink-900 tabular-nums">{reviewAverage.toFixed(1).replace('.', ',')}</span>
                  <span>
                    <Stars rating={reviewAverage} size="size-4" />
                    <span className="block text-xs text-ink-500">Moyenne des {reviews.data.length} derniers avis</span>
                  </span>
                </div>
              )}
              <ul className="space-y-3.5">
                {reviews.data.slice(0, 3).map(review => (
                  <li key={review.id} className="flex gap-3">
                    <span className="grid size-8 shrink-0 place-items-center rounded-full bg-slate-100 text-xs font-semibold text-ink-600">{review.author.slice(0, 1).toUpperCase()}</span>
                    <div className="min-w-0">
                      <p className="flex flex-wrap items-center gap-x-2 text-sm">
                        <span className="font-medium text-ink-900">{review.author}</span>
                        <Stars rating={review.rating} size="size-3" />
                        <span className="text-xs text-ink-500">{ago(review.submittedAtUtc, now)}</span>
                      </p>
                      {review.comment && <p className="mt-0.5 line-clamp-2 text-sm text-ink-600">{review.comment}</p>}
                    </div>
                  </li>
                ))}
              </ul>
            </>
          )}
        </Panel>

        <Panel title="Activité récente" className="lg:col-span-2 xl:col-span-1">
          {activity.length === 0 ? (
            <p className="py-10 text-center text-sm text-ink-500">Rien de nouveau aujourd’hui.</p>
          ) : (
            <ul className="-mx-2 space-y-0.5">
              {activity.map(item => (
                <li key={`${item.text}-${item.at}`}>
                  <Link href={item.href} className="flex items-center gap-3 rounded-lg px-2 py-2 transition hover:bg-slate-50">
                    <span className={cx('grid size-8 shrink-0 place-items-center rounded-lg', item.tone)}><item.icon className="size-4" aria-hidden="true" /></span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium text-ink-900">{item.text}</span>
                      <span className="block truncate text-xs text-ink-500">{item.detail}</span>
                    </span>
                    <span className="shrink-0 text-xs text-ink-500">{ago(item.at, now)}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>

      {!reporting && (
        <p className="flex items-center gap-2 text-xs text-ink-500">
          <BarChart3 className="size-3.5" aria-hidden="true" />
          Les indicateurs détaillés et les encaissements par moyen sont réservés aux membres qui ont accès aux rapports.
        </p>
      )}
    </div>
  );
}

export default function DashboardPage() {
  return <WithLocation>{location => <Dashboard location={location} />}</WithLocation>;
}
