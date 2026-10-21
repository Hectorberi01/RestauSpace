'use client';
import {useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import {Armchair, CalendarDays, CalendarX2, ChevronDown, ChevronLeft, ChevronRight, Plus, Search, Settings2, Users} from 'lucide-react';
import {isoDate, number} from '@/lib/format';
import {useNow, useResource} from '@/lib/hooks';
import {addDays, startOfDay} from '@/lib/dashboard';
import {holdsSeats, listReservations, loadFloor, localDateTime, peakCovers, tableOf, type Reservation, type ReservationStatus} from '@/lib/reservations';
import type {LocationSummary} from '@/lib/types';
import {useConsole, WithLocation} from '@/components/console';
import {accentOf, Alert, Button, Card, Delta, Modal, Spinner, StatCard, cx} from '@/components/ui';
import {CancelDialog, EditDialog, NewReservationDialog, SeatDialog} from './dialogs';
import {DetailPanel} from './detail';
import {PolicyDialog} from './policy';
import {WaitlistView} from './waitlist';
import {DayList, FloorPlan, ListView, Timeline, UNPLACED, type Handlers, type Sort} from './views';

type View = 'calendar' | 'list' | 'floor' | 'waitlist';
type StatusFilter = 'all' | 'active' | ReservationStatus;
type Service = 'all' | 'lunch' | 'dinner';

const longDate = new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'long', year: 'numeric'});
const weekday = new Intl.DateTimeFormat('fr-FR', {weekday: 'long'});

function useWide(query = '(min-width: 1536px)') {
  const [wide, setWide] = useState(false);
  useEffect(() => {
    const media = window.matchMedia(query);
    const update = () => setWide(media.matches);
    update();
    media.addEventListener('change', update);
    return () => media.removeEventListener('change', update);
  }, [query]);
  return wide;
}

function counted(items: Reservation[]) {
  const kept = items.filter(item => item.status !== 'Cancelled' && item.status !== 'NoShow');
  return {
    reservations: kept.length,
    covers: kept.reduce((sum, item) => sum + item.partySize, 0),
    cancelled: items.length - kept.length,
  };
}

function Trend({current, previous, label, lowerIsBetter}: {current: number | null; previous: number | null; label: string; lowerIsBetter?: boolean}) {
  return <Delta value={current === null || previous === null || previous === 0 ? null : (current - previous) / previous} better={lowerIsBetter ? 'down' : 'up'} label={label} />;
}

function Kpi({icon, tone, label, value, trend}: {icon: ReactNode; tone?: string; label: string; value: string; trend: ReactNode}) {
  return <StatCard icon={icon} label={label} value={value} delta={trend} accent={accentOf(tone)} />;
}

function FilterSelect({label, value, onChange, children}: {label: string; value: string; onChange: (value: string) => void; children: ReactNode}) {
  return (
    <label className="relative block sm:min-w-44">
      <span className="sr-only">{label}</span>
      <select value={value} onChange={event => onChange(event.target.value)} className="h-10 w-full appearance-none rounded-xl border border-slate-200 bg-white pr-9 pl-3 text-sm text-ink-800 focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none">
        {children}
      </select>
      <ChevronDown className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
    </label>
  );
}

function DatePicker({day, onChange}: {day: Date; onChange: (day: Date) => void}) {
  const input = useRef<HTMLInputElement>(null);
  function open() {
    const element = input.current;
    if (!element) return;
    try {
      element.showPicker();
    } catch {
      element.focus();
      element.click();
    }
  }
  return (
    <div className="relative">
      <button
        type="button"
        onClick={open}
        className="flex h-10 w-full items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 text-sm font-medium hover:border-slate-300 focus-visible:ring-2 focus-visible:ring-brand-200 focus-visible:outline-none">
        <CalendarDays className="size-4 text-ink-500" aria-hidden="true" />
        <span className="whitespace-nowrap"><span className="capitalize">{weekday.format(day)}</span> {longDate.format(day)}</span>
        <ChevronDown className="ml-auto size-4 text-ink-500" aria-hidden="true" />
      </button>
      <input
        ref={input}
        type="date"
        tabIndex={-1}
        aria-label="Choisir une date"
        className="pointer-events-none absolute bottom-0 left-0 h-0 w-full opacity-0"
        value={isoDate(day)}
        onChange={event => {
          const picked = localDateTime(event.target.value, '00:00');
          if (picked) onChange(picked);
        }}
      />
    </div>
  );
}

function Agenda({location}: {location: LocationSummary}) {
  const {can} = useConsole();
  const now = useNow(30_000);
  const wide = useWide();
  const [day, setDay] = useState(() => startOfDay(new Date()));
  const [view, setView] = useState<View>('calendar');
  const [status, setStatus] = useState<StatusFilter>('all');
  const [service, setService] = useState<Service>('all');
  const [area, setArea] = useState('all');
  const [search, setSearch] = useState('');
  const [sort, setSort] = useState<Sort>('time');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [rules, setRules] = useState(false);
  const [cancelId, setCancelId] = useState<string | null>(null);
  const [seatId, setSeatId] = useState<string | null>(null);
  const [editId, setEditId] = useState<string | null>(null);

  const data = useResource(
    async signal => {
      const [around, lastWeek] = await Promise.all([
        listReservations(location.id, addDays(day, -1), addDays(day, 1), signal),
        listReservations(location.id, addDays(day, -7), addDays(day, -6), signal),
      ]);
      const today = around.filter(item => new Date(item.startAtUtc) >= day);
      const yesterday = around.filter(item => new Date(item.startAtUtc) < day);
      return {today, yesterday, lastWeek};
    },
    [location.id, day.getTime()],
    30_000,
  );
  const floor = useResource(signal => loadFloor(location.id, signal).catch(() => null), [location.id], 30_000);

  const reloadAll = async () => {
    await Promise.all([data.reload(), floor.reload()]);
  };

  const items = useMemo(() => data.data?.today ?? [], [data.data]);
  const isToday = day.getTime() === startOfDay(new Date(now)).getTime();

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return items.filter(item => {
      if (status === 'active' && !holdsSeats(item.status)) return false;
      if (status !== 'all' && status !== 'active' && item.status !== status) return false;
      const hour = new Date(item.startAtUtc).getHours();
      if (service === 'lunch' && hour >= 16) return false;
      if (service === 'dinner' && hour < 16) return false;
      if (area !== 'all' && (tableOf(item, floor.data)?.areaId ?? UNPLACED) !== area) return false;
      if (term && ![item.guestName, item.guestPhone, item.guestEmail, item.notes].some(value => value?.toLowerCase().includes(term))) return false;
      return true;
    });
  }, [items, status, service, area, search, floor.data]);

  useEffect(() => {
    if (!wide || !data.data) return;
    if (selectedId && items.some(item => item.id === selectedId)) return;
    const upcoming = items.find(item => holdsSeats(item.status) && new Date(item.startAtUtc).getTime() + item.durationMinutes * 60_000 > now) ?? items[0];
    setSelectedId(upcoming?.id ?? null);
  }, [wide, data.data, items, selectedId, now]);

  const selected = items.find(item => item.id === selectedId) ?? null;
  const byId = (id: string | null) => (id ? items.find(item => item.id === id) ?? null : null);

  const handlers: Handlers = {
    selectedId,
    onSelect: setSelectedId,
    onCancel: setCancelId,
    onSeat: setSeatId,
    onChanged: reloadAll,
  };

  const today = counted(items);
  const before = data.data ? counted(data.data.yesterday) : null;
  const week = data.data ? counted(data.data.lastWeek) : null;
  const seats = floor.data?.seats ?? 0;
  const occupancy = seats ? peakCovers(items) / seats : null;
  const occupancyBefore = seats && data.data ? peakCovers(data.data.lastWeek) / seats : null;
  const dayWord = isToday ? 'aujourd’hui' : 'ce jour';

  const columns = [
    {key: UNPLACED, title: 'À placer', subtitle: `${filtered.filter(item => !tableOf(item, floor.data)).length} réservation(s)`},
    ...(floor.data?.areas ?? []).map(entry => ({key: entry.id, title: entry.name, subtitle: `${entry.tables.length} table${entry.tables.length > 1 ? 's' : ''}`})),
  ].filter(column => area === 'all' || column.key === area);

  const detail = selected && (
    <DetailPanel
      reservation={selected}
      floor={floor.data}
      onClose={wide ? () => setSelectedId(null) : undefined}
      onChanged={reloadAll}
      onCancel={() => setCancelId(selected.id)}
      onSeat={() => setSeatId(selected.id)}
      onEdit={() => setEditId(selected.id)}
    />
  );

  const tabs: {value: View; label: string}[] = [
    {value: 'calendar', label: 'Vue calendrier'},
    {value: 'list', label: 'Liste'},
    {value: 'floor', label: 'Plan de salle'},
    {value: 'waitlist', label: 'Liste d’attente'},
  ];

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Réservations</h1>
          <p className="mt-1 text-sm text-ink-500">Gérez vos réservations, organisez vos tables et optimisez votre service.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => setDay(startOfDay(new Date()))} aria-pressed={isToday}>
            <CalendarDays className="size-4" aria-hidden="true" />Aujourd’hui
          </Button>
          <div className="flex rounded-xl border border-slate-200 bg-white">
            <button type="button" className="grid size-10 place-items-center rounded-l-xl text-ink-600 hover:bg-slate-50" aria-label="Jour précédent" onClick={() => setDay(current => addDays(current, -1))}>
              <ChevronLeft className="size-4" />
            </button>
            <button type="button" className="grid size-10 place-items-center rounded-r-xl border-l border-slate-200 text-ink-600 hover:bg-slate-50" aria-label="Jour suivant" onClick={() => setDay(current => addDays(current, 1))}>
              <ChevronRight className="size-4" />
            </button>
          </div>
          {can('reservations.configure') && (
            <Button variant="secondary" onClick={() => setRules(true)}><Settings2 className="size-4" aria-hidden="true" />Règles</Button>
          )}
          {can('reservations.manage') && (
            <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouvelle réservation</Button>
          )}
        </div>
      </div>

      {data.error && <Alert>{data.error}</Alert>}

      <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <Kpi icon={<Users className="size-6" />} tone="bg-emerald-50 text-emerald-600" label={`Réservations ${dayWord}`} value={number(today.reservations)}
          trend={<Trend current={today.reservations} previous={before?.reservations ?? null} label="vs veille" />} />
        <Kpi icon={<CalendarDays className="size-6" />} tone="bg-amber-50 text-amber-600" label={`Couverts ${dayWord}`} value={number(today.covers)}
          trend={<Trend current={today.covers} previous={before?.covers ?? null} label="vs veille" />} />
        <Kpi icon={<Armchair className="size-6" />} tone="bg-sky-50 text-sky-600" label="Taux d’occupation (pic)" value={occupancy === null ? '—' : `${Math.round(occupancy * 100)} %`}
          trend={seats ? <Trend current={occupancy} previous={occupancyBefore} label="vs semaine dernière" /> : <span className="mt-2 block text-xs text-ink-500">Aucune table configurée</span>} />
        <Kpi icon={<CalendarX2 className="size-6" />} tone="bg-rose-50 text-rose-500" label="Annulations" value={number(today.cancelled)}
          trend={<Trend current={today.cancelled} previous={week?.cancelled ?? null} label="vs semaine dernière" lowerIsBetter />} />
      </div>

      <div className="grid gap-6 2xl:grid-cols-[minmax(0,1fr)_23rem]">
        <Card className="min-w-0 p-4 sm:p-5">
          <div role="tablist" aria-label="Affichage" className="-mx-1 mb-4 flex gap-1 overflow-x-auto border-b border-slate-200">
            {tabs.map(tab => (
              <button
                key={tab.value}
                type="button"
                role="tab"
                aria-selected={view === tab.value}
                onClick={() => setView(tab.value)}
                className={cx('-mb-px border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap transition', view === tab.value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                {tab.label}
              </button>
            ))}
          </div>

          <div className="mb-4 flex flex-wrap gap-2 [&>*]:w-full sm:[&>*]:w-auto">
            <DatePicker day={day} onChange={setDay} />
            {view !== 'waitlist' && <>
              <FilterSelect label="Salle" value={area} onChange={setArea}>
                <option value="all">Toutes les salles</option>
                <option value={UNPLACED}>À placer</option>
                {(floor.data?.areas ?? []).map(entry => <option key={entry.id} value={entry.id}>{entry.name}</option>)}
              </FilterSelect>
              <FilterSelect label="Statut" value={status} onChange={value => setStatus(value as StatusFilter)}>
                <option value="all">Tous les statuts</option>
                <option value="active">En cours ou à venir</option>
                <option value="Pending">À confirmer</option>
                <option value="Confirmed">Confirmées</option>
                <option value="Seated">Installées</option>
                <option value="Completed">Terminées</option>
                <option value="Cancelled">Annulées</option>
                <option value="NoShow">Absents</option>
              </FilterSelect>
              <FilterSelect label="Service" value={service} onChange={value => setService(value as Service)}>
                <option value="all">Tous les horaires</option>
                <option value="lunch">Midi (avant 16 h)</option>
                <option value="dinner">Soir (à partir de 16 h)</option>
              </FilterSelect>
              <label className="relative block sm:min-w-56 sm:flex-1">
                <span className="sr-only">Rechercher une réservation</span>
                <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
                <input
                  type="search"
                  value={search}
                  onChange={event => setSearch(event.target.value)}
                  placeholder="Nom, téléphone, email…"
                  className="h-10 w-full rounded-xl border border-slate-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-500 focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none"
                />
              </label>
            </>}
          </div>

          {!data.data ? (
            <Spinner label="Chargement des réservations…" />
          ) : view === 'calendar' ? (
            <div className="grid gap-5 min-[1900px]:grid-cols-[minmax(0,1fr)_21rem]">
              <Timeline items={filtered} floor={floor.data} day={day} now={now} handlers={handlers} columns={columns} />
              <DayList items={filtered} handlers={handlers} sort={sort} onSort={setSort} title={isToday ? 'Réservations du jour' : 'Réservations'} />
            </div>
          ) : view === 'list' ? (
            <ListView items={filtered} floor={floor.data} handlers={handlers} />
          ) : view === 'waitlist' ? (
            <WaitlistView location={location} day={day} isToday={isToday} now={now} floor={floor.data} onChanged={reloadAll} />
          ) : (
            <FloorPlan floor={floor.data} items={items} handlers={handlers} isToday={isToday} />
          )}
        </Card>

        {wide && (
          <aside className="min-w-0">
            <Card className="sticky top-24 p-5">
              {detail || (
                <div className="py-16 text-center text-sm text-ink-500">
                  <CalendarDays className="mx-auto mb-3 size-8" aria-hidden="true" />
                  Sélectionnez une réservation pour voir son détail.
                </div>
              )}
            </Card>
          </aside>
        )}
      </div>

      {!wide && (
        <Modal open={!!selected} onClose={() => setSelectedId(null)} title="Détail de la réservation">
          {detail}
        </Modal>
      )}

      <PolicyDialog location={location} open={rules} onClose={() => setRules(false)} />
      <NewReservationDialog
        location={location}
        open={creating}
        day={day}
        onClose={() => setCreating(false)}
        onCreated={async at => {
          const target = startOfDay(at);
          if (target.getTime() !== day.getTime()) setDay(target);
          else await data.reload();
        }}
      />
      <CancelDialog reservation={byId(cancelId)} onClose={() => setCancelId(null)} onDone={reloadAll} />
      <SeatDialog reservation={byId(seatId)} floor={floor.data} onClose={() => setSeatId(null)} onDone={reloadAll} />
      <EditDialog reservation={byId(editId)} onClose={() => setEditId(null)} onDone={reloadAll} />
    </div>
  );
}

export default function ReservationsPage() {
  return <WithLocation>{location => <Agenda location={location} />}</WithLocation>;
}
