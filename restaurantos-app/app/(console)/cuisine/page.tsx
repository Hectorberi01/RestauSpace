'use client';
import {useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import Link from 'next/link';
import {AlarmClock, ChefHat, CircleCheck, ClipboardList, Flame, Maximize2, Megaphone, Minimize2, Settings, Timer} from 'lucide-react';
import {api} from '@/lib/api';
import {isoDate, time} from '@/lib/format';
import {useNow, useResource} from '@/lib/hooks';
import {ordersSince, startOfDay, summarize} from '@/lib/dashboard';
import {loadBoard, type BoardTicket} from '@/lib/kitchen';
import type {LocationSummary, OrderType, ReportRow, Station, TicketStatus} from '@/lib/types';
import {useConsole, WithLocation} from '@/components/console';
import {accentOf, Alert, Card, Empty, Spinner, StatCard, cx, type Accent} from '@/components/ui';
import {KitchenSettings} from './settings';
import {lateness, TicketCard} from './ticket';

type Filter = 'all' | 'Queued' | 'InProgress' | 'Ready' | OrderType;

const columns: {status: TicketStatus; label: string; icon: typeof Flame; head: string; badge: string}[] = [
  {status: 'Queued', label: 'À préparer', icon: AlarmClock, head: 'bg-red-50 text-red-700', badge: 'bg-red-500'},
  {status: 'InProgress', label: 'En cours', icon: Timer, head: 'bg-amber-50 text-amber-700', badge: 'bg-amber-500'},
  {status: 'Ready', label: 'Prêtes', icon: CircleCheck, head: 'bg-emerald-50 text-emerald-700', badge: 'bg-emerald-500'},
];

const typeLabel: Record<OrderType, string> = {DineIn: 'Sur place', Takeaway: 'À emporter', Delivery: 'Livraison'};

function Stat({icon, tone, value, label, accent}: {icon: ReactNode; tone?: string; value: string; label: string; accent?: Accent}) {
  return <StatCard icon={icon} label={label} value={value} accent={accent ?? accentOf(tone)} className="p-3 sm:p-4" />;
}

function Side({location, tickets, stations, now, onSettings}: {location: LocationSummary; tickets: BoardTicket[]; stations: Station[]; now: number; onSettings?: () => void}) {
  const {can} = useConsole();
  const [today] = useState(() => startOfDay(new Date()));
  const orders = useResource(signal => ordersSince(location.id, today, signal, 4), [location.id], 30_000);
  const average = useResource(
    signal =>
      can('reporting.view')
        ? api<{rows: ReportRow[]}>('/reporting/run', {
            method: 'POST',
            body: {metrics: ['kitchen.average_preparation_minutes'], filters: [{field: 'location.id', operator: 'EqualTo', value: location.id}], from: isoDate(today), to: isoDate(today), granularity: 'Total', byLocation: false},
            signal,
          }).then(result => result.rows.reduce((sum, row) => sum + row.value, 0) || null)
        : Promise.resolve(null),
    [location.id],
    60_000,
  );
  const day = orders.data ? summarize(orders.data) : null;
  const completed = orders.data?.filter(order => order.status === 'Completed').length ?? null;
  const late = tickets.filter(ticket => ticket.status !== 'Ready' && lateness(ticket, now) >= 20).length;

  const activity = tickets
    .flatMap(ticket => {
      const label = ticket.order ? `#${ticket.order.number}` : 'Commande';
      const where = ticket.order?.type === 'DineIn' ? (ticket.table ? `Table ${ticket.table.label}` : 'Sur place') : ticket.order ? typeLabel[ticket.order.type] : '';
      return [
        {at: ticket.createdAtUtc, text: `${label} reçue en cuisine`, where, dot: 'bg-red-500'},
        ...(ticket.startedAtUtc ? [{at: ticket.startedAtUtc, text: `${label} en préparation`, where, dot: 'bg-amber-500'}] : []),
        ...(ticket.readyAtUtc ? [{at: ticket.readyAtUtc, text: `${label} prête`, where, dot: 'bg-emerald-500'}] : []),
      ];
    })
    .sort((a, b) => b.at.localeCompare(a.at))
    .slice(0, 8);

  return (
    <div className="space-y-6">
      <Card className="p-5">
        <h2 className="mb-3 font-semibold">Statistiques du jour</h2>
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4 2xl:grid-cols-2">
          <Stat icon={<ClipboardList className="size-5" />} tone="bg-sky-50 text-sky-600" value={day ? String(day.orders) : '…'} label="Commandes" />
          <Stat icon={<CircleCheck className="size-5" />} tone="bg-emerald-50 text-emerald-600" value={completed === null ? '…' : String(completed)} label="Terminées" />
          <Stat icon={<Timer className="size-5" />} tone="bg-amber-50 text-amber-600" value={average.data ? `${Math.round(average.data)} min` : '—'} label="Préparation moyenne" />
          <Stat icon={<AlarmClock className="size-5" />} accent={late ? 'red' : 'neutral'} value={String(late)} label="En retard (20 min +)" />
        </div>
      </Card>

      <Card className="p-5">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="font-semibold">Postes</h2>
          {onSettings && <button type="button" onClick={onSettings} className="text-sm font-semibold text-brand-700 hover:underline">Gérer les postes</button>}
        </div>
        {stations.length === 0 ? (
          <p className="text-sm text-ink-500">Aucun poste configuré.</p>
        ) : (
          <ul className="space-y-3">
            {stations.map(station => {
              const items = tickets.flatMap(ticket => ticket.items.filter(item => item.stationId === station.id && item.status !== 'Ready'));
              const preparing = items.filter(item => item.status === 'Preparing').length;
              return (
                <li key={station.id} className="flex items-center gap-3">
                  <span className="grid size-10 shrink-0 place-items-center rounded-full bg-slate-100 text-ink-600"><ChefHat className="size-5" aria-hidden="true" /></span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-semibold">{station.name}</span>
                    <span className="text-xs text-ink-500">{items.length - preparing} à faire · {preparing} en cours</span>
                  </span>
                  <span className={cx('flex items-center gap-1.5 text-xs font-medium', !station.isActive ? 'text-ink-500' : items.length ? 'text-amber-700' : 'text-emerald-700')}>
                    <span className={cx('size-2 rounded-full', !station.isActive ? 'bg-slate-400' : items.length ? 'bg-amber-500' : 'bg-emerald-500')} aria-hidden="true" />
                    {!station.isActive ? 'Inactif' : items.length ? 'Occupé' : 'Libre'}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
      </Card>

      <Card className="p-5">
        <h2 className="mb-3 font-semibold">Activité récente</h2>
        {activity.length === 0 ? (
          <p className="text-sm text-ink-500">Rien pour l’instant.</p>
        ) : (
          <ol className="space-y-3">
            {activity.map((entry, i) => (
              <li key={`${entry.at}-${i}`} className="flex gap-3 text-sm">
                <span className={cx('mt-1.5 size-2 shrink-0 rounded-full', entry.dot)} aria-hidden="true" />
                <span className="w-11 shrink-0 text-ink-500">{time(entry.at)}</span>
                <span className="min-w-0">
                  <span className="block font-medium">{entry.text}</span>
                  {entry.where && <span className="text-xs text-ink-500">{entry.where}</span>}
                </span>
              </li>
            ))}
          </ol>
        )}
      </Card>
    </div>
  );
}

function Board({location}: {location: LocationSummary}) {
  const {can} = useConsole();
  const now = useNow(15_000);
  const [filter, setFilter] = useState<Filter>('all');
  const [stationId, setStationId] = useState('all');
  const [newestFirst, setNewestFirst] = useState(false);
  const [settings, setSettings] = useState(false);
  const [fullscreen, setFullscreen] = useState(false);
  const area = useRef<HTMLDivElement>(null);
  const {data: stations, reload: reloadStations} = useResource(signal => api<Station[]>('/kitchen/stations', {query: {locationId: location.id}, signal}), [location.id]);
  const {data, error, reload} = useResource(signal => loadBoard(location.id, signal), [location.id], 5_000);

  useEffect(() => {
    const update = () => setFullscreen(document.fullscreenElement === area.current);
    document.addEventListener('fullscreenchange', update);
    return () => document.removeEventListener('fullscreenchange', update);
  }, []);

  const tickets = useMemo(() => data ?? [], [data]);
  const active = stations?.filter(station => station.isActive) ?? [];
  const byStation = tickets.filter(ticket => stationId === 'all' || ticket.items.some(item => item.stationId === stationId));
  const byType = (type: OrderType) => byStation.filter(ticket => (ticket.order?.type ?? 'DineIn') === type).length;
  const visible = byStation.filter(ticket => filter === 'all' || filter === ticket.status || filter === (ticket.order?.type ?? 'DineIn'));
  const shownColumns = columns.filter(column => filter === 'all' || filter === column.status || !['Queued', 'InProgress', 'Ready'].includes(filter));

  const tabs: {value: Filter; label: string; count: number; dot?: string}[] = [
    {value: 'all', label: 'Toutes', count: byStation.length},
    ...columns.map(column => ({value: column.status as Filter, label: column.label, count: byStation.filter(ticket => ticket.status === column.status).length, dot: column.badge})),
    {value: 'DineIn', label: 'Sur place', count: byType('DineIn')},
    {value: 'Takeaway', label: 'À emporter', count: byType('Takeaway')},
    {value: 'Delivery', label: 'Livraison', count: byType('Delivery')},
  ];

  const toggleFullscreen = async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await area.current?.requestFullscreen();
    } catch {
      setFullscreen(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Cuisine</h1>
          <p className="mt-1 text-sm text-ink-500">Suivez et gérez les commandes en temps réel (mise à jour toutes les 5 secondes).</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {active.length > 1 && (
            <label className="relative">
              <span className="sr-only">Poste</span>
              <select value={stationId} onChange={event => setStationId(event.target.value)} className="h-10 rounded-xl border border-slate-200 bg-white pr-8 pl-3 text-sm font-medium focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none">
                <option value="all">Tous les postes</option>
                {active.map(station => <option key={station.id} value={station.id}>{station.name}</option>)}
              </select>
            </label>
          )}
          <button type="button" onClick={() => void toggleFullscreen()} className="flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 text-sm font-semibold text-ink-800 hover:bg-slate-50">
            <Maximize2 className="size-4" aria-hidden="true" />Mode plein écran
          </button>
          <Link href="/cuisine/appel" className="flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 text-sm font-semibold text-ink-800 hover:bg-slate-50">
            <Megaphone className="size-4" aria-hidden="true" />Écran d’appel
          </Link>
          {can('kitchen.configure') && (
            <button type="button" onClick={() => setSettings(true)} className="flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 text-sm font-semibold text-ink-800 hover:bg-slate-50">
              <Settings className="size-4" aria-hidden="true" />Postes
            </button>
          )}
        </div>
      </div>

      {stations && stations.length === 0 && (
        <Alert tone="amber">Aucun poste de cuisine : les commandes acceptées ne peuvent pas être envoyées en cuisine. {can('kitchen.configure') ? 'Créez au moins un poste avec le bouton « Postes ».' : 'Demandez au gérant de créer un poste.'}</Alert>
      )}
      {error && <Alert>{error}</Alert>}

      <div className="grid grid-cols-1 gap-6 2xl:grid-cols-[minmax(0,1fr)_20rem]">
        <div ref={area} className="min-w-0 space-y-4 [&:fullscreen]:overflow-auto [&:fullscreen]:bg-slate-50 [&:fullscreen]:p-6">
          {fullscreen && (
            <div className="flex items-center justify-between">
              <p className="text-2xl font-bold">Cuisine · {location.name} <span className="ml-2 text-base font-normal text-ink-500">{time(new Date(now).toISOString())}</span></p>
              <button type="button" onClick={() => void toggleFullscreen()} className="flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 text-sm font-semibold"><Minimize2 className="size-4" aria-hidden="true" />Quitter le plein écran</button>
            </div>
          )}
          <div role="tablist" aria-label="Filtrer les tickets" className="flex gap-1 overflow-x-auto rounded-2xl border border-slate-200 bg-white p-1">
            {tabs.map(tab => (
              <button
                key={tab.value}
                type="button"
                role="tab"
                aria-selected={filter === tab.value}
                onClick={() => setFilter(tab.value)}
                className={cx('flex items-center gap-2 rounded-xl px-3 py-2 text-sm font-semibold whitespace-nowrap transition', filter === tab.value ? 'bg-brand-50 text-brand-700' : 'text-ink-600 hover:bg-slate-50')}>
                {tab.dot && <span className={cx('size-2 rounded-full', tab.dot)} aria-hidden="true" />}
                {tab.label} ({tab.count})
              </button>
            ))}
          </div>

          {!data && !error && <Spinner />}
          {data && byStation.length === 0 && <Empty icon={<ChefHat className="size-8" />} title="Aucun ticket en cours">Les commandes acceptées apparaissent ici automatiquement.</Empty>}
          {data && byStation.length > 0 && (
            <div className={cx('grid gap-4', shownColumns.length === 3 ? 'lg:grid-cols-3' : shownColumns.length === 1 ? 'md:grid-cols-2 xl:grid-cols-3' : '')}>
              {shownColumns.map(column => {
                const list = visible
                  .filter(ticket => ticket.status === column.status)
                  .sort((a, b) => (newestFirst ? b.createdAtUtc.localeCompare(a.createdAtUtc) : a.createdAtUtc.localeCompare(b.createdAtUtc)));
                const single = shownColumns.length === 1;
                const content = list.map(ticket => <TicketCard key={ticket.id} ticket={ticket} stationId={stationId} now={now} onChanged={reload} />);
                return single ? (
                  list.length ? content : <p key={column.status} className="col-span-full py-10 text-center text-sm text-ink-500">Aucun ticket.</p>
                ) : (
                  <section key={column.status} aria-label={column.label} className="min-w-0 rounded-2xl bg-slate-100/70 p-3">
                    <h2 className={cx('mb-3 flex items-center gap-2 rounded-xl px-3 py-2.5 text-sm font-bold', column.head)}>
                      <column.icon className="size-4" aria-hidden="true" />
                      {column.label} ({list.length})
                      <button type="button" onClick={() => setNewestFirst(value => !value)} className="ml-auto text-xs font-medium opacity-80 hover:underline">
                        {newestFirst ? 'Plus récent' : 'Plus ancien'}
                      </button>
                    </h2>
                    <div className="space-y-3">
                      {list.length === 0 && <p className="py-8 text-center text-sm text-ink-500">Rien pour l’instant</p>}
                      {content}
                    </div>
                  </section>
                );
              })}
            </div>
          )}
        </div>

        <Side location={location} tickets={tickets} stations={stations ?? []} now={now} onSettings={can('kitchen.configure') ? () => setSettings(true) : undefined} />
      </div>

      {settings && stations && <KitchenSettings location={location} stations={stations} onClose={() => setSettings(false)} onChanged={reloadStations} />}
    </div>
  );
}

export default function KitchenPage() {
  return <WithLocation>{location => <Board location={location} />}</WithLocation>;
}
