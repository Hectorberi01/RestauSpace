'use client';
import {useState} from 'react';
import Link from 'next/link';
import {CalendarDays, ChevronLeft, ChevronRight, ClipboardList} from 'lucide-react';
import {money, orderStatusLabel, orderStatusTone, orderTypeLabel} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {customerOrders, customerReservations, stamp} from '@/lib/customers';
import {reservationStatusLabel, reservationStatusTone} from '@/lib/reservations';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Empty, Spinner, Tabs} from '@/components/ui';

function Pager({page, hasMore, total, onChange}: {page: number; hasMore: boolean; total: number | null; onChange: (page: number) => void}) {
  if (page === 1 && !hasMore) return null;
  return (
    <div className="flex items-center justify-between gap-2 pt-3 text-sm text-ink-500">
      <span>Page {page}{total !== null ? ` · ${total} au total` : ''}</span>
      <div className="flex gap-2">
        <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => onChange(page - 1)} aria-label="Page précédente"><ChevronLeft className="size-4" /></Button>
        <Button size="sm" variant="secondary" disabled={!hasMore} onClick={() => onChange(page + 1)} aria-label="Page suivante"><ChevronRight className="size-4" /></Button>
      </div>
    </div>
  );
}

function Orders({customerId}: {customerId: string}) {
  const {locations} = useConsole();
  const [page, setPage] = useState(1);
  const {data, error} = useResource(signal => customerOrders(customerId, page, signal), [customerId, page]);
  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner label="Chargement des commandes…" />;
  if (data.items.length === 0) {
    return <Empty icon={<ClipboardList className="size-8" />} title="Aucune commande">Rattachez ce client à une commande pour suivre ses habitudes.</Empty>;
  }
  return (
    <div>
      <ul className="divide-y divide-slate-100">
        {data.items.map(order => (
          <li key={order.id}>
            <Link href={`/commandes/${order.id}`} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-3 text-sm hover:bg-slate-50 sm:flex-nowrap">
              <span className="w-24 shrink-0 font-semibold">#{order.number}</span>
              <span className="min-w-0 flex-1 text-ink-500">
                {stamp(order.createdAtUtc)} · {orderTypeLabel[order.type]}
                {locations.length > 1 && ` · ${locations.find(item => item.id === order.locationId)?.name ?? ''}`}
              </span>
              <Badge tone={orderStatusTone[order.status]}>{orderStatusLabel[order.status]}</Badge>
              <span className="w-28 shrink-0 text-right font-semibold tabular-nums">{money(order.total, order.currencyCode)}</span>
            </Link>
          </li>
        ))}
      </ul>
      <Pager page={page} hasMore={data.hasMore} total={data.totalCount} onChange={setPage} />
    </div>
  );
}

function Reservations({customerId}: {customerId: string}) {
  const {locations} = useConsole();
  const [page, setPage] = useState(1);
  const {data, error} = useResource(signal => customerReservations(customerId, page, signal), [customerId, page]);
  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner label="Chargement des réservations…" />;
  if (data.items.length === 0) {
    return <Empty icon={<CalendarDays className="size-8" />} title="Aucune réservation">Les réservations rattachées à cette fiche apparaîtront ici.</Empty>;
  }
  return (
    <div>
      <ul className="divide-y divide-slate-100">
        {data.items.map(reservation => (
          <li key={reservation.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-3 text-sm sm:flex-nowrap">
            <span className="w-44 shrink-0 font-semibold">{stamp(reservation.startAtUtc)}</span>
            <span className="min-w-0 flex-1 text-ink-500">
              {reservation.partySize} couvert{reservation.partySize > 1 ? 's' : ''}
              {locations.length > 1 && ` · ${locations.find(item => item.id === reservation.locationId)?.name ?? ''}`}
              {reservation.notes && ` · ${reservation.notes}`}
            </span>
            <Badge tone={reservationStatusTone[reservation.status]}>{reservationStatusLabel[reservation.status]}</Badge>
          </li>
        ))}
      </ul>
      <Pager page={page} hasMore={data.hasMore} total={data.totalCount} onChange={setPage} />
    </div>
  );
}

export function History({customerId}: {customerId: string}) {
  const [tab, setTab] = useState<'orders' | 'reservations'>('orders');
  return (
    <div>
      <Tabs label="Historique" value={tab} onChange={setTab} items={[{value: 'orders', label: 'Commandes'}, {value: 'reservations', label: 'Réservations'}]} />
      <div className="mt-3">{tab === 'orders' ? <Orders customerId={customerId} /> : <Reservations customerId={customerId} />}</div>
    </div>
  );
}
