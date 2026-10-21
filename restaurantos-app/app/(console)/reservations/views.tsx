'use client';
import {useEffect, useRef, useState, type ReactNode} from 'react';
import {Armchair, MoreVertical, Users} from 'lucide-react';
import {
  actionLabel, actionsFor, endOf, hhmm, holdsSeats, reservationStatusLabel, reservationStatusTone, runAction, tableOf,
  type FloorState, type Reservation, type ReservationAction, type ReservationStatus,
} from '@/lib/reservations';
import {useConsole} from '@/components/console';
import {Badge, Button, Empty, cx, useAction} from '@/components/ui';
import {Avatar} from './detail';
import {depositStatusLabel} from '@/lib/deposits';

export const UNPLACED = 'none';

const blockStyle: Record<ReservationStatus, string> = {
  Pending: 'border-amber-300 bg-amber-50',
  Confirmed: 'border-emerald-300 bg-emerald-50',
  Seated: 'border-sky-300 bg-sky-50',
  Completed: 'border-slate-200 bg-slate-50',
  Cancelled: 'border-red-200 bg-red-50/60 opacity-60',
  NoShow: 'border-red-200 bg-red-50/60 opacity-60',
};

const HOUR = 64;
const LANE = 140;

export interface Handlers {
  selectedId: string | null;
  onSelect: (id: string) => void;
  onCancel: (id: string) => void;
  onSeat: (id: string) => void;
  onChanged: () => Promise<void>;
}

/** Actions rapides d'une réservation (menu « ⋮ »). */
export function RowMenu({reservation, handlers}: {reservation: Reservation; handlers: Handlers}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);
  const actions = can('reservations.manage') ? actionsFor(reservation.status) : [];

  function apply(action: ReservationAction) {
    setOpen(false);
    if (action === 'cancel') return handlers.onCancel(reservation.id);
    if (action === 'seat') return handlers.onSeat(reservation.id);
    void run(action, async () => {
      await runAction(reservation.id, action);
      await handlers.onChanged();
    }, actionLabel[action].success);
  }

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Actions pour ${reservation.guestName || 'la réservation'}`}
        disabled={!!busy}
        onClick={event => {
          event.stopPropagation();
          setOpen(value => !value);
        }}
        className="grid size-8 place-items-center rounded-lg text-ink-500 hover:bg-slate-100 disabled:opacity-50">
        <MoreVertical className="size-4" />
      </button>
      {open && (
        <div role="menu" className="absolute top-9 right-0 z-20 w-48 rounded-xl border border-slate-200 bg-white p-1 shadow-lg" onClick={event => event.stopPropagation()}>
          <button type="button" role="menuitem" className="block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50" onClick={() => {
            setOpen(false);
            handlers.onSelect(reservation.id);
          }}>
            Voir le détail
          </button>
          {actions.map(action => (
            <button
              key={action}
              type="button"
              role="menuitem"
              className={cx('block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50', action === 'cancel' || action === 'no-show' ? 'text-red-600' : '')}
              onClick={() => apply(action)}>
              {actionLabel[action].label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function lanes(items: Reservation[]): {item: Reservation; lane: number}[] {
  const ends: number[] = [];
  return [...items]
    .sort((a, b) => a.startAtUtc.localeCompare(b.startAtUtc))
    .map(item => {
      const start = new Date(item.startAtUtc).getTime();
      let lane = ends.findIndex(end => end <= start);
      if (lane === -1) lane = ends.length;
      ends[lane] = endOf(item).getTime();
      return {item, lane};
    });
}

/** Journée en colonnes (« À placer » puis une colonne par salle), une ligne par heure. */
export function Timeline({items, floor, day, now, handlers, columns}: {
  items: Reservation[];
  floor: FloorState | null | undefined;
  day: Date;
  now: number;
  handlers: Handlers;
  columns: {key: string; title: string; subtitle: string}[];
}) {
  const scroller = useRef<HTMLDivElement>(null);
  const hourOf = (date: Date) => (date.getTime() - day.getTime()) / 3_600_000;
  const first = Math.min(11, ...items.map(item => Math.floor(hourOf(new Date(item.startAtUtc)))));
  const last = Math.min(24, Math.max(23, ...items.map(item => Math.ceil(hourOf(endOf(item))))));
  const from = Math.max(0, first);
  const hours = Array.from({length: last - from}, (_, i) => from + i);
  const height = hours.length * HOUR;
  const nowHour = hourOf(new Date(now));
  const showNow = nowHour >= from && nowHour <= last;

  useEffect(() => {
    const target = showNow ? (nowHour - from - 1) * HOUR : 0;
    scroller.current?.scrollTo({top: Math.max(0, target)});
  }, [day]);

  const grouped = columns.map(column => {
    const own = items.filter(item => (tableOf(item, floor)?.areaId ?? UNPLACED) === column.key);
    const placed = lanes(own);
    const count = Math.max(1, ...placed.map(entry => entry.lane + 1));
    return {column, placed, count};
  });

  return (
    <div ref={scroller} className="max-h-[42rem] overflow-auto rounded-xl border border-slate-200">
      <div className="flex w-max min-w-full">
        <div className="sticky left-0 z-20 w-14 shrink-0 border-r border-slate-200 bg-white">
          <div className="sticky top-0 z-10 h-14 border-b border-slate-200 bg-slate-50" />
          <div className="relative" style={{height}}>
            {hours.map((hour, i) => (
              <span key={hour} className="absolute right-2 -translate-y-1/2 text-xs text-ink-500" style={{top: i * HOUR}}>
                {i === 0 ? '' : `${String(hour).padStart(2, '0')}:00`}
              </span>
            ))}
          </div>
        </div>
        {grouped.map(({column, placed, count}) => (
          <div key={column.key} className="flex-1 border-r border-slate-100 last:border-r-0" style={{minWidth: Math.max(170, count * LANE)}}>
            <div className="sticky top-0 z-10 flex h-14 flex-col items-center justify-center border-b border-slate-200 bg-slate-50 px-2 text-center">
              <span className="truncate text-sm font-semibold">{column.title}</span>
              <span className="text-xs text-ink-500">{column.subtitle}</span>
            </div>
            <div className="relative" style={{height}}>
              {hours.map((hour, i) => (
                <div key={hour} className="absolute inset-x-0 border-t border-slate-100" style={{top: i * HOUR}} />
              ))}
              {placed.map(({item, lane}) => {
                const top = (hourOf(new Date(item.startAtUtc)) - from) * HOUR;
                const blockHeight = Math.max(30, (item.durationMinutes / 60) * HOUR - 4);
                const short = blockHeight < 60;
                const table = tableOf(item, floor);
                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => handlers.onSelect(item.id)}
                    className={cx(
                      'absolute overflow-hidden rounded-lg border p-2 text-left text-xs shadow-sm transition hover:shadow-md focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:outline-none',
                      blockStyle[item.status],
                      handlers.selectedId === item.id && 'ring-2 ring-brand-600',
                    )}
                    style={{top: top + 2, height: blockHeight, left: `calc(${(lane / count) * 100}% + 4px)`, width: `calc(${100 / count}% - 8px)`}}>
                    <span className={cx('block truncate font-semibold text-ink-900', item.status === 'Cancelled' && 'line-through')}>{item.guestName || 'Client'}</span>
                    <span className="mt-0.5 flex items-center gap-1 truncate text-ink-600">
                      <Users className="size-3 shrink-0" aria-hidden="true" />{item.partySize} · {hhmm(new Date(item.startAtUtc))}–{hhmm(endOf(item))}
                    </span>
                    {!short && (
                      <span className="mt-0.5 block truncate text-ink-500">
                        {reservationStatusLabel[item.status]}{table ? ` · ${table.label}` : ''}
                      </span>
                    )}
                  </button>
                );
              })}
              {showNow && <div className="pointer-events-none absolute inset-x-0 z-[5] border-t-2 border-red-500" style={{top: (nowHour - from) * HOUR}} />}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export type Sort = 'time' | 'party' | 'name';

/** Liste compacte du jour à côté du calendrier. */
export function DayList({items, handlers, sort, onSort, title}: {items: Reservation[]; handlers: Handlers; sort: Sort; onSort: (sort: Sort) => void; title: string}) {
  const sorted = [...items].sort((a, b) =>
    sort === 'party' ? b.partySize - a.partySize : sort === 'name' ? (a.guestName ?? '').localeCompare(b.guestName ?? '', 'fr') : a.startAtUtc.localeCompare(b.startAtUtc),
  );
  return (
    <div className="flex min-w-0 flex-col">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 className="truncate font-semibold">{title} ({items.length})</h2>
        <label className="sr-only" htmlFor="tri-reservations">Trier</label>
        <select id="tri-reservations" value={sort} onChange={event => onSort(event.target.value as Sort)} className="rounded-lg border-0 bg-transparent py-1 pr-7 text-sm text-ink-600 focus:ring-2 focus:ring-brand-200">
          <option value="time">Trier par heure</option>
          <option value="party">Trier par couverts</option>
          <option value="name">Trier par nom</option>
        </select>
      </div>
      {sorted.length === 0 ? (
        <p className="py-10 text-center text-sm text-ink-500">Aucune réservation.</p>
      ) : (
        <ul className="max-h-[42rem] divide-y divide-slate-100 overflow-y-auto">
          {sorted.map(item => (
            <li key={item.id}>
              <div
                role="button"
                tabIndex={0}
                onClick={() => handlers.onSelect(item.id)}
                onKeyDown={event => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    handlers.onSelect(item.id);
                  }
                }}
                className={cx(
                  'flex items-center gap-3 rounded-lg px-2 py-2.5 transition hover:bg-slate-50 focus-visible:bg-slate-50 focus-visible:outline-none',
                  handlers.selectedId === item.id && 'bg-brand-50/60',
                  !holdsSeats(item.status) && item.status !== 'Completed' && 'opacity-60',
                )}>
                <span className="w-11 shrink-0 text-sm font-semibold">{hhmm(new Date(item.startAtUtc))}</span>
                <Avatar name={item.guestName} size="sm" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-semibold">{item.guestName || 'Client'}</span>
                  <span className="flex items-center gap-1 text-xs text-ink-500"><Users className="size-3" aria-hidden="true" />{item.partySize} pers.
                    {item.deposit && <span className={cx('ml-1', item.deposit.status === 'AwaitingPayment' ? 'text-amber-700' : 'text-ink-500')}>· {depositStatusLabel[item.deposit.status]}</span>}
                  </span>
                </span>
                <Badge tone={reservationStatusTone[item.status]}>{reservationStatusLabel[item.status]}</Badge>
                <RowMenu reservation={item} handlers={handlers} />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Tableau détaillé de toutes les réservations filtrées. */
export function ListView({items, floor, handlers}: {items: Reservation[]; floor: FloorState | null | undefined; handlers: Handlers}) {
  if (items.length === 0) return <Empty title="Aucune réservation pour ces filtres" />;
  return (
    <div className="relative overflow-x-auto">
      <table className="w-full min-w-[760px] text-left text-sm">
        <thead className="border-b border-slate-200 text-xs text-ink-500">
          <tr>
            <th scope="col" className="px-3 py-2 font-medium">Heure</th>
            <th scope="col" className="px-3 py-2 font-medium">Client</th>
            <th scope="col" className="px-3 py-2 font-medium">Contact</th>
            <th scope="col" className="px-3 py-2 font-medium">Couverts</th>
            <th scope="col" className="px-3 py-2 font-medium">Table</th>
            <th scope="col" className="px-3 py-2 font-medium">Statut</th>
            <th scope="col" className="px-3 py-2"><span className="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {items.map(item => {
            const table = tableOf(item, floor);
            return (
              <tr key={item.id} onClick={() => handlers.onSelect(item.id)} className={cx('cursor-pointer hover:bg-slate-50', handlers.selectedId === item.id && 'bg-brand-50/60')}>
                <td className="px-3 py-2.5 whitespace-nowrap">
                  <span className="font-semibold">{hhmm(new Date(item.startAtUtc))}</span>
                  <span className="text-ink-500"> – {hhmm(endOf(item))}</span>
                </td>
                <td className="px-3 py-2.5">
                  <span className="flex items-center gap-2">
                    <Avatar name={item.guestName} size="sm" />
                    <span className="min-w-0">
                      <span className="block font-semibold">{item.guestName || 'Client'}</span>
                      {item.notes && <span className="block max-w-56 truncate text-xs text-ink-500">{item.notes}</span>}
                    </span>
                  </span>
                </td>
                <td className="px-3 py-2.5 text-ink-600">{item.guestPhone || item.guestEmail || '—'}</td>
                <td className="px-3 py-2.5">{item.partySize}</td>
                <td className="px-3 py-2.5 text-ink-600">{table ? `${table.label} · ${table.areaName}` : '—'}</td>
                <td className="px-3 py-2.5"><Badge tone={reservationStatusTone[item.status]}>{reservationStatusLabel[item.status]}</Badge>
                  {item.deposit && <span className="mt-1 block text-xs text-ink-500">{depositStatusLabel[item.deposit.status]}</span>}</td>
                <td className="px-3 py-2.5" onClick={event => event.stopPropagation()}><RowMenu reservation={item} handlers={handlers} /></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/** Salles et tables en ce moment, et réservations confirmées à installer. */
export function FloorPlan({floor, items, handlers, isToday}: {floor: FloorState | null | undefined; items: Reservation[]; handlers: Handlers; isToday: boolean}) {
  const {can} = useConsole();
  if (!floor) return <p className="py-10 text-center text-sm text-ink-500">Chargement du plan de salle…</p>;
  if (floor.areas.length === 0) return <Empty icon={<Armchair className="size-8" />} title="Aucune salle configurée">Créez vos salles et vos tables depuis « Salle & tables ».</Empty>;
  const byId = new Map(items.map(item => [item.id, item]));
  const waiting = isToday ? items.filter(item => item.status === 'Confirmed').sort((a, b) => a.startAtUtc.localeCompare(b.startAtUtc)) : [];
  const sessionOf = new Map(floor.sessions.map(session => [session.id, session]));

  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_18rem]">
      <div className="space-y-5">
        {floor.areas.map(area => {
          const busyTables = area.tables.filter(table => table.openSessionId).length;
          return (
            <section key={area.id}>
              <h3 className="mb-2 flex items-baseline justify-between text-sm font-semibold">
                {area.name}
                <span className="font-normal text-ink-500">{busyTables}/{area.tables.length} occupée{busyTables > 1 ? 's' : ''} · {area.seatsInService} places</span>
              </h3>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-5">
                {area.tables.map(table => {
                  const session = table.openSessionId ? sessionOf.get(table.openSessionId) : undefined;
                  const reservation = session?.reservationId ? byId.get(session.reservationId) : undefined;
                  const state: ReactNode = !table.isInService
                    ? <span className="text-ink-500">Hors service</span>
                    : session
                      ? <span className="font-semibold text-sky-700">{reservation?.guestName || `${session.guestCount} pers.`}</span>
                      : <span className="text-emerald-700">Libre</span>;
                  return (
                    <div
                      key={table.id}
                      role={reservation ? 'button' : undefined}
                      tabIndex={reservation ? 0 : undefined}
                      onClick={() => reservation && handlers.onSelect(reservation.id)}
                      onKeyDown={event => reservation && event.key === 'Enter' && handlers.onSelect(reservation.id)}
                      className={cx(
                        'rounded-xl border p-3 text-sm',
                        !table.isInService ? 'border-dashed border-slate-300 bg-slate-50' : session ? 'border-sky-200 bg-sky-50' : 'border-emerald-200 bg-emerald-50/50',
                        reservation && 'cursor-pointer hover:shadow-sm',
                      )}>
                      <span className="flex items-center justify-between">
                        <span className="font-bold">{table.label}</span>
                        <span className="flex items-center gap-1 text-xs text-ink-500"><Users className="size-3" aria-hidden="true" />{table.capacity}</span>
                      </span>
                      <span className="mt-1 block truncate text-xs">{state}</span>
                    </div>
                  );
                })}
              </div>
            </section>
          );
        })}
      </div>
      <aside>
        <h3 className="mb-2 text-sm font-semibold">À installer ({waiting.length})</h3>
        {waiting.length === 0 ? (
          <p className="rounded-xl bg-slate-50 px-3 py-6 text-center text-sm text-ink-500">{isToday ? 'Aucune réservation confirmée en attente.' : 'Le plan de salle montre l’état actuel de la salle.'}</p>
        ) : (
          <ul className="space-y-2">
            {waiting.map(item => (
              <li key={item.id} className="flex items-center gap-2 rounded-xl border border-slate-200 p-2.5">
                <span className="w-11 text-sm font-semibold">{hhmm(new Date(item.startAtUtc))}</span>
                <button type="button" className="min-w-0 flex-1 text-left" onClick={() => handlers.onSelect(item.id)}>
                  <span className="block truncate text-sm font-semibold">{item.guestName || 'Client'}</span>
                  <span className="text-xs text-ink-500">{item.partySize} pers.</span>
                </button>
                {can('reservations.manage') && <Button size="sm" onClick={() => handlers.onSeat(item.id)}>Installer</Button>}
              </li>
            ))}
          </ul>
        )}
      </aside>
    </div>
  );
}
