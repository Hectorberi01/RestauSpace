'use client';
import {useMemo, useState} from 'react';
import Link from 'next/link';
import {
  Banknote, BarChart3, CalendarCheck, CalendarClock, CalendarDays, ChefHat, ChevronDown, CircleX, ClipboardList, Coins, CreditCard, Download, Percent, Receipt,
  RotateCcw, Scale, ShoppingCart, Timer, Undo2, Users, UserX,
} from 'lucide-react';
import {downloadFile} from '@/lib/api';
import {compact, currencySymbol, isoDate, money, number} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {addDays} from '@/lib/dashboard';
import {loadCatalog} from '@/lib/menu';
import {METRICS, exportFormats, exportReport, extensionOf, presets, previous, runReport, salesBreakdown, total, type ExportFormat, type Granularity, type Period} from '@/lib/reports';
import {useConsole} from '@/components/console';
import {Alert, Button, Card, Empty, Field, Input, Select, Spinner, cx, useAction} from '@/components/ui';
import {AccountingExport} from './accounting';
import {MetricPicker} from './metric-picker';
import {Reconciliation} from './reconciliation';
import {
  Categories, ChannelBars, ChannelSeries, Figures, HOURS, Kpis, Panel, Peaks, Split, TopProducts, TrendChart, type KpiDef, type ReportData, type Sample,
} from './panels';

type Tab = 'overview' | 'sales' | 'orders' | 'reservations' | 'kitchen' | 'payments' | 'reconciliation';

const ALL = Object.values(METRICS).flat();
const CHART = ['sales.revenue', 'sales.orders_placed', 'sales.orders_completed', 'sales.orders_cancelled', 'reservations.created', 'reservations.covers_booked', 'kitchen.tickets_ready', 'kitchen.average_preparation_minutes', 'payments.captured_amount'];

const tabs: {value: Tab; label: string; icon: typeof BarChart3}[] = [
  {value: 'overview', label: 'Vue d’ensemble', icon: BarChart3},
  {value: 'sales', label: 'Ventes', icon: Coins},
  {value: 'orders', label: 'Commandes', icon: ClipboardList},
  {value: 'reservations', label: 'Réservations', icon: CalendarDays},
  {value: 'kitchen', label: 'Cuisine', icon: ChefHat},
  {value: 'payments', label: 'Paiements', icon: CreditCard},
  {value: 'reconciliation', label: 'Rapprochement', icon: Scale},
];

const reportTypes: {value: Exclude<Tab, 'overview' | 'reconciliation'> | 'all' | 'custom'; label: string}[] = [
  {value: 'sales', label: 'Rapport des ventes'},
  {value: 'orders', label: 'Rapport des commandes'},
  {value: 'reservations', label: 'Rapport des réservations'},
  {value: 'kitchen', label: 'Rapport de la cuisine'},
  {value: 'payments', label: 'Rapport des paiements'},
  {value: 'all', label: 'Toutes les mesures'},
  {value: 'custom', label: 'Mesures au choix…'},
];

const periodLabel = new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'});

function CustomReport({period, locationId}: {period: Period; locationId: string | null}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [type, setType] = useState<(typeof reportTypes)[number]['value']>('sales');
  const [from, setFrom] = useState(isoDate(period.from));
  const [to, setTo] = useState(isoDate(period.to));
  const [granularity, setGranularity] = useState<Granularity>('Day');
  const [picked, setPicked] = useState<string[]>([]);
  const [format, setFormat] = useState<ExportFormat>('Xlsx');
  if (!can('reporting.export')) return <p className="text-sm text-ink-500">Réservé aux membres qui peuvent exporter les rapports.</p>;
  return (
    <form
      className="space-y-3"
      onSubmit={event => {
        event.preventDefault();
        void run('custom', async () => {
          const start = new Date(`${from}T00:00:00`);
          const end = new Date(`${to}T00:00:00`);
          if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end < start) throw new Error('Période invalide.');
          if (type === 'custom' && picked.length === 0) throw new Error('Cochez au moins une mesure.');
          const id = await exportReport(type === 'all' ? ALL : type === 'custom' ? picked : METRICS[type], {from: start, to: end}, granularity, locationId, format);
          await downloadFile(`/reporting/exports/${id}/file`, `rapport-${from}-${to}.${extensionOf(format)}`);
        }, 'Rapport téléchargé.');
      }}>
      <Field label="Type de rapport">
        <Select value={type} onChange={event => setType(event.target.value as typeof type)}>
          {reportTypes.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
        </Select>
      </Field>
      {type === 'custom' && <MetricPicker value={picked} onChange={setPicked} />}
      <div className="grid grid-cols-2 gap-2">
        <Field label="Du"><Input type="date" required value={from} onChange={event => setFrom(event.target.value)} /></Field>
        <Field label="Au"><Input type="date" required value={to} onChange={event => setTo(event.target.value)} /></Field>
      </div>
      <Field label="Détail">
        <Select value={granularity} onChange={event => setGranularity(event.target.value as Granularity)}>
          <option value="Day">Par jour</option>
          <option value="Week">Par semaine</option>
          <option value="Month">Par mois</option>
        </Select>
      </Field>
      <Field label="Format">
        <Select value={format} onChange={event => setFormat(event.target.value as ExportFormat)}>
          {exportFormats.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
        </Select>
      </Field>
      <Button type="submit" className="w-full" busy={busy === 'custom'}><Download className="size-4" aria-hidden="true" />Générer le rapport</Button>
    </form>
  );
}

function Reports() {
  const {can, currency, location, locations} = useConsole();
  const {busy, run} = useAction();
  const [tab, setTab] = useState<Tab>('overview');
  const [preset, setPreset] = useState('30d');
  const [custom, setCustom] = useState<Period | null>(null);
  const [scope, setScope] = useState<'location' | 'all'>('location');
  const [granularity, setGranularity] = useState<Granularity>('Day');
  const [format, setFormat] = useState<ExportFormat>('Xlsx');

  const period = useMemo<Period>(() => custom ?? presets.find(item => item.key === preset)!.period(), [preset, custom]);
  const locationId = scope === 'location' ? location?.id ?? null : null;
  const key = `${isoDate(period.from)}_${isoDate(period.to)}_${locationId ?? 'all'}`;

  const data = useResource<ReportData>(
    async signal => {
      const [totals, before, chart] = await Promise.all([
        runReport(ALL, period, 'Total', locationId, signal),
        runReport(ALL, previous(period), 'Total', locationId, signal),
        runReport(CHART, period, granularity, locationId, signal),
      ]);
      return {totals, before, chart};
    },
    [key, granularity],
  );

  const needsSample = tab === 'overview' || tab === 'sales' || tab === 'orders';
  const sample = useResource<Sample | null>(
    async signal => {
      if (!needsSample) return null;
      const categoryLocation = locationId ?? location?.id ?? null;
      const [sales, catalog] = await Promise.all([
        salesBreakdown(period, locationId, HOURS[0], HOURS[HOURS.length - 1], signal),
        categoryLocation ? loadCatalog(categoryLocation, signal).catch(() => null) : Promise.resolve(null),
      ]);
      return {...sales, categoryOf: catalog?.categoryOf ?? new Map()};
    },
    [key, needsSample],
  );

  if (!can('reporting.view')) {
    return <Empty icon={<BarChart3 className="size-8" />} title="Rapports non disponibles">Réservé aux membres qui ont accès aux rapports.</Empty>;
  }

  const fmtMoney = (value: number) => money(value, currency);
  const d = data.data;
  const sampleState = sample.data ?? undefined;

  const range = `${periodLabel.format(period.from)} – ${periodLabel.format(period.to)}`;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Rapports</h1>
          <p className="mt-1 text-sm text-ink-500">Analysez la performance de votre restaurant et prenez de meilleures décisions.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {locations.length > 1 && (
            <label className="relative">
              <span className="sr-only">Établissements</span>
              <select value={scope} onChange={event => setScope(event.target.value as 'location' | 'all')} className="h-10 appearance-none rounded-xl border border-slate-200 bg-white pr-9 pl-3 text-sm font-medium focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none">
                <option value="location">{location?.name ?? 'Cet établissement'}</option>
                <option value="all">Tous les établissements</option>
              </select>
              <ChevronDown className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
            </label>
          )}
          <label className="relative">
            <span className="sr-only">Période</span>
            <CalendarDays className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
            <select
              value={custom ? 'custom' : preset}
              onChange={event => {
                if (event.target.value === 'custom') setCustom({from: period.from, to: period.to});
                else {
                  setCustom(null);
                  setPreset(event.target.value);
                }
              }}
              className="h-10 appearance-none rounded-xl border border-slate-200 bg-white pr-9 pl-9 text-sm font-medium focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none">
              {presets.map(item => <option key={item.key} value={item.key}>{item.label}</option>)}
              <option value="custom">Personnalisée…</option>
            </select>
            <ChevronDown className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
          </label>
          {custom && (
            <span className="flex items-center gap-1">
              <Input type="date" aria-label="Du" className="h-10 w-40" value={isoDate(custom.from)} max={isoDate(custom.to)} onChange={event => event.target.value && setCustom({...custom, from: new Date(`${event.target.value}T00:00:00`)})} />
              <span className="text-ink-500">→</span>
              <Input type="date" aria-label="Au" className="h-10 w-40" value={isoDate(custom.to)} min={isoDate(custom.from)} max={isoDate(addDays(custom.from, 365))} onChange={event => event.target.value && setCustom({...custom, to: new Date(`${event.target.value}T00:00:00`)})} />
            </span>
          )}
          <Link href="/rapports/planifies" className="inline-flex h-10 items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 text-sm font-semibold text-ink-800 hover:bg-slate-50">
            <CalendarClock className="size-4" aria-hidden="true" />Planifiés
          </Link>
          {can('reporting.export') && tab !== 'reconciliation' && (
            <span className="flex items-center">
              <select aria-label="Format de l’export" value={format} onChange={event => setFormat(event.target.value as ExportFormat)}
                className="h-10 rounded-l-xl border border-r-0 border-slate-300 bg-white px-2 text-sm font-medium focus:border-brand-500 focus:outline-none">
                {exportFormats.map(item => <option key={item.value} value={item.value}>{item.value === 'Xlsx' ? 'Excel' : item.value.toUpperCase()}</option>)}
              </select>
              <Button className="rounded-l-none!" busy={busy === 'export'} onClick={() => run('export', async () => {
                const id = await exportReport(tab === 'overview' ? ALL : METRICS[tab], period, granularity, locationId, format);
                await downloadFile(`/reporting/exports/${id}/file`, `rapport-${isoDate(period.from)}-${isoDate(period.to)}.${extensionOf(format)}`);
              }, 'Export téléchargé.')}>
                <Download className="size-4" aria-hidden="true" />Exporter
              </Button>
            </span>
          )}
        </div>
      </div>

      <div role="tablist" aria-label="Rapports" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.filter(item => item.value !== 'reconciliation' || can('payments.collect')).map(item => (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={tab === item.value}
            onClick={() => setTab(item.value)}
            className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap transition', tab === item.value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
            <item.icon className="size-4" aria-hidden="true" />{item.label}
          </button>
        ))}
      </div>

      <p className="text-sm text-ink-500">{range}{locationId ? ` · ${location?.name}` : ' · tous les établissements'} — comparé aux {Math.round((period.to.getTime() - period.from.getTime()) / 86_400_000) + 1} jours précédents.</p>

      {data.error && <Alert>{data.error}</Alert>}
      {!d && !data.error && <Spinner label="Calcul des rapports…" />}

      {d && tab === 'overview' && (
        <>
          <Kpis currency={currency} data={d} defs={[
            {label: 'Chiffre d’affaires', metric: 'sales.revenue', icon: <Coins className="size-6" />, tone: 'bg-emerald-50 text-emerald-600', format: 'money'},
            {label: 'Commandes passées', metric: 'sales.orders_placed', icon: <ShoppingCart className="size-6" />, tone: 'bg-violet-50 text-violet-600', format: 'number'},
            {label: 'Couverts installés', metric: 'reservations.covers_seated', icon: <Users className="size-6" />, tone: 'bg-orange-50 text-orange-500', format: 'number'},
            {label: 'Panier moyen', metric: 'sales.average_ticket', icon: <Receipt className="size-6" />, tone: 'bg-rose-50 text-rose-500', format: 'money'},
          ]} />
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2 2xl:grid-cols-[minmax(0,2fr)_minmax(0,1.3fr)_minmax(0,1.2fr)]">
            <div className="xl:col-span-2 2xl:col-span-1">
              <TrendChart data={d} metric="sales.revenue" period={period} granularity={granularity} onGranularity={setGranularity} format={fmtMoney} title="Chiffre d’affaires"
                headline={<p className="mb-2 text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{fmtMoney(total(d.totals, 'sales.revenue'))}</p>} />
            </div>
            <Panel title="Répartition des ventes">
              <Split rows={d.totals} metric="sales.placed_amount" center={compact(total(d.totals, 'sales.placed_amount'))} caption={`${currencySymbol(currency)} commandés`} format={fmtMoney} />
            </Panel>
            <Panel title="Produits les plus vendus"><TopProducts sample={sampleState} currency={currency} /></Panel>
          </div>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2 2xl:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)_minmax(0,0.8fr)]">
            <Panel title="Commandes par canal"><ChannelBars rows={d.totals} metric="sales.orders_placed" /></Panel>
            <Panel title="Heures de pointe"><Peaks sample={sampleState} /></Panel>
            <Panel title="Réservations">
              <Figures items={[
                {label: 'Réservations', value: number(total(d.totals, 'reservations.created')), icon: <CalendarCheck className="size-4" />, tone: 'bg-emerald-50 text-emerald-600'},
                {label: 'Couverts réservés', value: number(total(d.totals, 'reservations.covers_booked')), icon: <Users className="size-4" />, tone: 'bg-violet-50 text-violet-600'},
                {label: 'Taux d’absence', value: `${number(total(d.totals, 'reservations.no_show_rate'), 1)} %`, icon: <UserX className="size-4" />, tone: 'bg-rose-50 text-rose-500'},
                {label: 'Annulations', value: number(total(d.totals, 'reservations.cancelled')), icon: <CircleX className="size-4" />, tone: 'bg-amber-50 text-amber-600'},
              ]} />
            </Panel>
          </div>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2 2xl:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)_minmax(0,0.9fr)]">
            <Panel title="Performance par catégorie"><Categories sample={sampleState} currency={currency} /></Panel>
            <Panel title="Moyens de paiement">
              <Split rows={d.totals} metric="payments.captured_amount" center={compact(total(d.totals, 'payments.captured_amount'))} caption={`${currencySymbol(currency)} encaissés`} format={fmtMoney} />
            </Panel>
            <Panel title="Rapport personnalisé" className="xl:col-span-2 2xl:col-span-1">
              <p className="-mt-2 mb-4 text-sm text-ink-500">Téléchargez un rapport détaillé (Excel, PDF ou CSV) selon vos besoins.</p>
              <CustomReport period={period} locationId={locationId} />
            </Panel>
            {can('reporting.export') && (
              <Panel title="Export comptable" className="xl:col-span-2 2xl:col-span-1">
                <p className="-mt-2 mb-4 text-sm text-ink-500">Le journal des ventes et des encaissements, pour votre comptable.</p>
                <AccountingExport period={period} />
              </Panel>
            )}
          </div>
        </>
      )}

      {d && tab === 'sales' && (
        <>
          <Kpis currency={currency} data={d} defs={[
            {label: 'Chiffre d’affaires', metric: 'sales.revenue', icon: <Coins className="size-6" />, tone: 'bg-emerald-50 text-emerald-600', format: 'money'},
            {label: 'Montant commandé', metric: 'sales.placed_amount', icon: <ShoppingCart className="size-6" />, tone: 'bg-violet-50 text-violet-600', format: 'money'},
            {label: 'Remises accordées', metric: 'sales.discount_amount', icon: <Percent className="size-6" />, tone: 'bg-amber-50 text-amber-600', format: 'money', lowerIsBetter: true},
            {label: 'Montant annulé', metric: 'sales.cancelled_amount', icon: <CircleX className="size-6" />, tone: 'bg-rose-50 text-rose-500', format: 'money', lowerIsBetter: true},
          ]} />
          <div className="grid grid-cols-1 gap-6 2xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <TrendChart data={d} metric="sales.revenue" period={period} granularity={granularity} onGranularity={setGranularity} format={fmtMoney} title="Chiffre d’affaires" />
            <Panel title="Ventes par canal">
              <Split rows={d.totals} metric="sales.placed_amount" center={compact(total(d.totals, 'sales.placed_amount'))} caption={`${currencySymbol(currency)} commandés`} format={fmtMoney} />
            </Panel>
          </div>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <Panel title="Produits les plus vendus"><TopProducts sample={sampleState} currency={currency} limit={10} /></Panel>
            <Panel title="Performance par catégorie"><Categories sample={sampleState} currency={currency} /></Panel>
          </div>
          <Panel title="Taxes collectées">
            <Figures items={[
              {label: 'Taxes sur les commandes passées', value: fmtMoney(total(d.totals, 'sales.tax_amount'))},
              {label: 'Panier moyen', value: fmtMoney(total(d.totals, 'sales.average_ticket'))},
            ]} />
          </Panel>
        </>
      )}

      {d && tab === 'orders' && (
        <>
          <Kpis currency={currency} data={d} defs={[
            {label: 'Commandes passées', metric: 'sales.orders_placed', icon: <ShoppingCart className="size-6" />, tone: 'bg-violet-50 text-violet-600', format: 'number'},
            {label: 'Commandes terminées', metric: 'sales.orders_completed', icon: <ClipboardList className="size-6" />, tone: 'bg-emerald-50 text-emerald-600', format: 'number'},
            {label: 'Commandes annulées', metric: 'sales.orders_cancelled', icon: <CircleX className="size-6" />, tone: 'bg-rose-50 text-rose-500', format: 'number', lowerIsBetter: true},
            {label: 'Taux d’annulation', metric: 'sales.cancellation_rate', icon: <Percent className="size-6" />, tone: 'bg-amber-50 text-amber-600', format: 'percent', lowerIsBetter: true},
          ]} />
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <TrendChart data={d} metric="sales.orders_placed" period={period} granularity={granularity} onGranularity={setGranularity} format={value => `${number(value)} commandes`} title="Commandes passées" />
            <Panel title="Commandes par canal"><ChannelBars rows={d.totals} metric="sales.orders_placed" /></Panel>
          </div>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <Panel title="Canaux par période"><ChannelSeries data={d} period={period} granularity={granularity} /></Panel>
            <Panel title="Heures de pointe"><Peaks sample={sampleState} /></Panel>
          </div>
        </>
      )}

      {d && tab === 'reservations' && (
        <>
          <Kpis currency={currency} data={d} defs={[
            {label: 'Réservations', metric: 'reservations.created', icon: <CalendarCheck className="size-6" />, tone: 'bg-emerald-50 text-emerald-600', format: 'number'},
            {label: 'Couverts réservés', metric: 'reservations.covers_booked', icon: <Users className="size-6" />, tone: 'bg-violet-50 text-violet-600', format: 'number'},
            {label: 'Couverts installés', metric: 'reservations.covers_seated', icon: <Users className="size-6" />, tone: 'bg-sky-50 text-sky-600', format: 'number'},
            {label: 'Taux d’absence', metric: 'reservations.no_show_rate', icon: <UserX className="size-6" />, tone: 'bg-rose-50 text-rose-500', format: 'percent', lowerIsBetter: true},
          ]} />
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <TrendChart data={d} metric="reservations.covers_booked" period={period} granularity={granularity} onGranularity={setGranularity} format={value => `${number(value)} couverts`} title="Couverts réservés" />
            <Panel title="Annulations">
              <Split rows={d.totals} metric="reservations.cancelled" center={number(total(d.totals, 'reservations.cancelled'))} caption="annulations" format={value => number(value)} />
              <div className="mt-4"><Figures items={[
                {label: 'Réservations installées', value: number(total(d.totals, 'reservations.seated'))},
                {label: 'Absences', value: number(total(d.totals, 'reservations.no_shows'))},
              ]} /></div>
            </Panel>
          </div>
        </>
      )}

      {d && tab === 'kitchen' && (
        <>
          <Kpis currency={currency} data={d} defs={[
            {label: 'Tickets envoyés', metric: 'kitchen.tickets_created', icon: <ClipboardList className="size-6" />, tone: 'bg-violet-50 text-violet-600', format: 'number'},
            {label: 'Tickets prêts', metric: 'kitchen.tickets_ready', icon: <ChefHat className="size-6" />, tone: 'bg-emerald-50 text-emerald-600', format: 'number'},
            {label: 'Tickets annulés', metric: 'kitchen.tickets_cancelled', icon: <CircleX className="size-6" />, tone: 'bg-rose-50 text-rose-500', format: 'number', lowerIsBetter: true},
            {label: 'Préparation moyenne', metric: 'kitchen.average_preparation_minutes', icon: <Timer className="size-6" />, tone: 'bg-amber-50 text-amber-600', format: 'minutes', lowerIsBetter: true},
          ]} />
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            <TrendChart data={d} metric="kitchen.tickets_ready" period={period} granularity={granularity} onGranularity={setGranularity} format={value => `${number(value)} tickets`} title="Tickets prêts" />
            <TrendChart data={d} metric="kitchen.average_preparation_minutes" period={period} granularity={granularity} onGranularity={setGranularity} format={value => `${number(value, 1)} min`} title="Temps de préparation moyen" />
          </div>
        </>
      )}

      {tab === 'reconciliation' && <Reconciliation period={period} locationId={locationId} locations={locations} currency={currency} />}

      {d && tab === 'payments' && (
        <>
          <Kpis currency={currency} data={d} defs={[
            {label: 'Montant encaissé', metric: 'payments.captured_amount', icon: <Banknote className="size-6" />, tone: 'bg-emerald-50 text-emerald-600', format: 'money'},
            {label: 'Paiements', metric: 'payments.captured_count', icon: <CreditCard className="size-6" />, tone: 'bg-violet-50 text-violet-600', format: 'number'},
            {label: 'Montant remboursé', metric: 'payments.refunded_amount', icon: <Undo2 className="size-6" />, tone: 'bg-amber-50 text-amber-600', format: 'money', lowerIsBetter: true},
            {label: 'Paiements échoués', metric: 'payments.failed_count', icon: <RotateCcw className="size-6" />, tone: 'bg-rose-50 text-rose-500', format: 'number', lowerIsBetter: true},
          ]} />
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <TrendChart data={d} metric="payments.captured_amount" period={period} granularity={granularity} onGranularity={setGranularity} format={fmtMoney} title="Encaissements" />
            <Panel title="Moyens de paiement">
              <Split rows={d.totals} metric="payments.captured_amount" center={compact(total(d.totals, 'payments.captured_amount'))} caption={`${currencySymbol(currency)} encaissés`} format={fmtMoney} />
            </Panel>
          </div>
          <Card className="p-5 text-sm text-ink-600">Taux de remboursement : <span className="font-semibold">{number(total(d.totals, 'payments.refund_rate'), 1)} %</span> · {number(total(d.totals, 'payments.refund_count'))} remboursement(s).</Card>
        </>
      )}
    </div>
  );
}

export default function ReportsPage() {
  return <Reports />;
}
