'use client';
import {useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {ClipboardList, Search, X} from 'lucide-react';
import {api, type Page} from '@/lib/api';
import {dateTime, money, orderStatusLabel, orderStatusTone, orderTypeLabel, orderTypeTone} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {searchCustomers, searchQuery, type Customer} from '@/lib/customers';
import {platformName} from '@/lib/orders';
import type {LocationSummary, OrderChannel, OrderStatus, OrderSummary, OrderType} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Select, Spinner, cx} from '@/components/ui';

export type TypeFilter = 'all' | OrderType;
type Period = 'today' | '7d' | '30d' | 'custom' | 'all';

export const channelLabel: Record<OrderChannel, string> = {
  Counter: 'Comptoir',
  QrCode: 'QR code de table',
  Web: 'Site web',
  MobileApp: 'Application',
  Kiosk: 'Borne',
  Api: 'API partenaire',
  Phone: 'Téléphone',
  WhatsApp: 'WhatsApp',
  DeliveryPlatform: 'Plateforme de livraison',
};

const historyStatuses: OrderStatus[] = ['Completed', 'Cancelled', 'Placed', 'Accepted', 'Preparing', 'Ready'];

const periods: {value: Period; label: string}[] = [
  {value: 'today', label: 'Aujourd’hui'},
  {value: '7d', label: '7 derniers jours'},
  {value: '30d', label: '30 derniers jours'},
  {value: 'all', label: 'Tout l’historique'},
  {value: 'custom', label: 'Dates précises'},
];

const startOf = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate());

/** Période en instants : [début, fin[ ; des dates saisies, la fin est incluse (jusqu'à minuit le lendemain). */
function range(period: Period, from: string, to: string): {from?: string; to?: string} {
  const today = startOf(new Date());
  const days = (count: number) => new Date(today.getTime() - count * 86_400_000);
  if (period === 'today') return {from: today.toISOString()};
  if (period === '7d') return {from: days(6).toISOString()};
  if (period === '30d') return {from: days(29).toISOString()};
  if (period === 'custom') {
    const start = from ? new Date(`${from}T00:00:00`) : undefined;
    const end = to ? new Date(new Date(`${to}T00:00:00`).getTime() + 86_400_000) : undefined;
    return {from: start?.toISOString(), to: end?.toISOString()};
  }
  return {};
}

const amount = (value: string) => {
  const parsed = Number(value.replace(/\s/g, '').replace(',', '.'));
  return value.trim() && Number.isFinite(parsed) && parsed >= 0 ? parsed : undefined;
};

function CustomerPicker({value, onChange}: {value: Customer | null; onChange: (customer: Customer | null) => void}) {
  const [text, setText] = useState('');
  const [found, setFound] = useState<Customer[]>([]);
  useEffect(() => {
    if (value || text.trim().length < 2) {
      setFound([]);
      return;
    }
    const controller = new AbortController();
    const timer = setTimeout(() => {
      searchCustomers(searchQuery(text, '', false, 1), controller.signal).then(page => setFound(page.items.slice(0, 6))).catch(() => setFound([]));
    }, 250);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [text, value]);

  if (value) {
    return (
      <div className="flex h-10 items-center justify-between gap-2 rounded-xl border border-brand-200 bg-brand-50 px-3 text-sm">
        <span className="truncate font-medium text-brand-800">{value.name}</span>
        <button type="button" aria-label="Retirer le client" onClick={() => onChange(null)} className="text-brand-700 hover:text-brand-900"><X className="size-4" /></button>
      </div>
    );
  }
  return (
    <div className="relative">
      <Input value={text} onChange={event => setText(event.target.value)} placeholder="Nom ou +229…" autoComplete="off" />
      {found.length > 0 && (
        <ul role="listbox" className="absolute top-11 right-0 left-0 z-20 max-h-64 overflow-y-auto rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          {found.map(customer => (
            <li key={customer.id}>
              <button type="button" role="option" aria-selected={false} onClick={() => {
                onChange(customer);
                setText('');
              }} className="block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50">
                <span className="block font-medium">{customer.name}</span>
                {customer.phone && <span className="block text-xs text-ink-500">{customer.phone}</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Historique des commandes de l'établissement, filtré côté serveur : période de création, numéro (ou sa fin), statut,
 * type, canal d'arrivée, client et total. <paramref name="drafts"/> : les brouillons, sans filtre de statut.
 */
export function OrderHistory({location, type, drafts = false, initialNumber}: {location: LocationSummary; type: TypeFilter; drafts?: boolean; initialNumber?: string}) {
  const {can} = useConsole();
  const [page, setPage] = useState(1);
  const [period, setPeriod] = useState<Period>(drafts ? 'all' : 'today');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [numberText, setNumberText] = useState(initialNumber ?? '');
  const [number, setNumber] = useState(initialNumber ?? '');
  const [status, setStatus] = useState<OrderStatus | ''>('');
  const [channel, setChannel] = useState<OrderChannel | ''>('');
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [minText, setMinText] = useState('');
  const [maxText, setMaxText] = useState('');
  const min = amount(minText);
  const max = amount(maxText);

  const query = useMemo(() => {
    const window = number ? {} : range(period, from, to);
    return {
      locationId: location.id,
      status: drafts ? 'Draft' : status || undefined,
      excludeDrafts: drafts || status ? undefined : true,
      type: type === 'all' ? undefined : type,
      channel: channel || undefined,
      number: number || undefined,
      from: window.from,
      to: window.to,
      minTotal: min,
      maxTotal: max,
      page,
      pageSize: 25,
    };
  }, [location.id, drafts, status, type, channel, number, period, from, to, min, max, page]);
  useEffect(() => setPage(1), [drafts, status, type, channel, number, period, from, to, min, max, customer]);

  const {data, error} = useResource(
    signal => api<Page<OrderSummary>>(customer ? `/orders/by-customer/${customer.id}` : '/orders', {query, signal}),
    [query, customer?.id],
    drafts ? 15_000 : 0,
  );
  const filtered = !!(number || status || channel || customer || min !== undefined || max !== undefined || type !== 'all' || (!drafts && period !== 'today'));
  const reset = () => {
    setPeriod(drafts ? 'all' : 'today');
    setFrom('');
    setTo('');
    setNumberText('');
    setNumber('');
    setStatus('');
    setChannel('');
    setCustomer(null);
    setMinText('');
    setMaxText('');
  };

  return (
    <div className="space-y-4">
      {!drafts && (
        <Card className="p-4">
          <form className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4" role="search" aria-label="Rechercher dans l’historique" onSubmit={event => {
            event.preventDefault();
            setNumber(numberText.trim());
          }}>
            <Field label="N° de commande" hint={number ? 'Toutes dates confondues (400 derniers jours pour une fin de numéro).' : 'Le numéro complet ou sa fin : 21, 021…'}>
              <div className="flex gap-2">
                <Input value={numberText} onChange={event => {
                  setNumberText(event.target.value);
                  if (!event.target.value.trim()) setNumber('');
                }} placeholder="021" inputMode="search" autoComplete="off" />
                <Button type="submit" variant="secondary" aria-label="Rechercher ce numéro"><Search className="size-4" aria-hidden="true" /></Button>
              </div>
            </Field>
            <Field label="Période">
              <Select value={period} disabled={!!number} onChange={event => setPeriod(event.target.value as Period)}>
                {periods.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
              </Select>
            </Field>
            {period === 'custom' && !number && (
              <div className="grid grid-cols-2 gap-2">
                <Field label="Du"><Input type="date" value={from} max={to || undefined} onChange={event => setFrom(event.target.value)} /></Field>
                <Field label="Au"><Input type="date" value={to} min={from || undefined} onChange={event => setTo(event.target.value)} /></Field>
              </div>
            )}
            <Field label="Statut">
              <Select value={status} onChange={event => setStatus(event.target.value as OrderStatus | '')}>
                <option value="">Tous (hors brouillons)</option>
                {historyStatuses.map(item => <option key={item} value={item}>{orderStatusLabel[item]}</option>)}
              </Select>
            </Field>
            <Field label="Arrivée par">
              <Select value={channel} onChange={event => setChannel(event.target.value as OrderChannel | '')}>
                <option value="">Tous les canaux</option>
                {(Object.keys(channelLabel) as OrderChannel[]).map(item => <option key={item} value={item}>{channelLabel[item]}</option>)}
              </Select>
            </Field>
            {can('customers.view') && <Field label="Client"><CustomerPicker value={customer} onChange={setCustomer} /></Field>}
            <Field label="Total entre">
              <div className="grid grid-cols-2 gap-2">
                <Input inputMode="decimal" aria-label="Total minimum" placeholder="min" value={minText} onChange={event => setMinText(event.target.value)} />
                <Input inputMode="decimal" aria-label="Total maximum" placeholder="max" value={maxText} onChange={event => setMaxText(event.target.value)} />
              </div>
            </Field>
            {filtered && (
              <div className="flex items-end">
                <Button type="button" variant="ghost" onClick={reset}><X className="size-4" aria-hidden="true" />Effacer les filtres</Button>
              </div>
            )}
          </form>
        </Card>
      )}

      {error && <Alert>{error}</Alert>}
      {!data && !error && <Spinner />}
      {data && (data.items.length === 0 && page === 1 ? (
        <Empty icon={<ClipboardList className="size-8" />} title={filtered ? 'Aucune commande ne correspond' : drafts ? 'Aucun brouillon' : 'Aucune commande sur cette période'}>
          {filtered ? 'Élargissez la période ou retirez un filtre.' : undefined}
        </Empty>
      ) : (
        <Card className="overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-left text-sm">
              <thead className="bg-slate-50 text-xs text-ink-500">
                <tr>
                  <th scope="col" className="px-4 py-3 font-medium">Commande</th>
                  <th scope="col" className="px-4 py-3 font-medium">Type</th>
                  <th scope="col" className="px-4 py-3 font-medium">Arrivée par</th>
                  <th scope="col" className="px-4 py-3 font-medium">Créée</th>
                  <th scope="col" className="px-4 py-3 font-medium">Articles</th>
                  <th scope="col" className="px-4 py-3 font-medium">Statut</th>
                  <th scope="col" className="px-4 py-3 text-right font-medium">Total</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map(order => (
                  <tr key={order.id} className="hover:bg-slate-50">
                    <td className="px-4 py-3 font-semibold"><Link href={`/commandes/${order.id}`} className="text-brand-700 hover:underline">#{order.number}</Link></td>
                    <td className="px-4 py-3"><Badge tone={orderTypeTone[order.type]}>{orderTypeLabel[order.type]}</Badge></td>
                    <td className="px-4 py-3 text-ink-600">
                      {order.channel === 'DeliveryPlatform' ? platformName(order.platform) : channelLabel[order.channel ?? 'Counter']}
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-ink-600">{dateTime(order.createdAtUtc)}</td>
                    <td className="px-4 py-3">{order.itemCount}</td>
                    <td className="px-4 py-3"><Badge tone={orderStatusTone[order.status]}>{orderStatusLabel[order.status]}</Badge></td>
                    <td className="px-4 py-3 text-right font-semibold whitespace-nowrap">{money(order.total, order.currencyCode)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className={cx('flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 px-4 py-3 text-sm')}>
            <span className="text-ink-500">Page {page}{data.totalCount != null && ` · ${data.totalCount} commande${data.totalCount > 1 ? 's' : ''}`}</span>
            <div className="flex gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(p => p - 1)}>Précédente</Button>
              <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(p => p + 1)}>Suivante</Button>
            </div>
          </div>
        </Card>
      ))}
    </div>
  );
}
