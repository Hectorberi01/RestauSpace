'use client';
import type {ReactNode} from 'react';
import {compact, money, number} from '@/lib/format';
import {byDimension, labelOf, palette, series, total, type Granularity, type Period, type SalesBreakdown} from '@/lib/reports';
import type {ReportRow} from '@/lib/types';
import {AreaChart, Bars, Donut, GroupedBars, Heatmap, Legend} from '@/components/charts';
import {Delta, Panel, Spinner, StatCard, cx} from '@/components/ui';

export interface ReportData {
  totals: ReportRow[];
  before: ReportRow[];
  chart: ReportRow[];
}

export interface Sample extends SalesBreakdown {
  categoryOf: Map<string, string[]>;
}

export const HOURS = Array.from({length: 16}, (_, i) => i + 8);
export const WEEKDAYS = ['Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam', 'Dim'];

export function Trend({current, previous, lowerIsBetter}: {current: number; previous: number; lowerIsBetter?: boolean}) {
  return <Delta value={previous ? (current - previous) / previous : null} better={lowerIsBetter ? 'down' : 'up'} label="vs période précédente" />;
}

export interface KpiDef {
  label: string;
  metric: string;
  icon: ReactNode;
  tone: string;
  format: 'money' | 'number' | 'percent' | 'minutes';
  lowerIsBetter?: boolean;
  hint?: string;
}

export function formatValue(value: number, format: KpiDef['format'], currency: string) {
  if (format === 'money') return money(value, currency);
  if (format === 'percent') return `${number(value, 1)} %`;
  if (format === 'minutes') return value ? `${number(value, 1)} min` : '—';
  return number(value);
}

export function Kpis({defs, data, currency}: {defs: KpiDef[]; data: ReportData; currency: string}) {
  return (
    <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
      {defs.map(def => {
        const value = total(data.totals, def.metric);
        return (
          <StatCard
            key={def.metric}
            icon={def.icon}
            label={def.label}
            value={formatValue(value, def.format, currency)}
            hint={def.hint}
            delta={def.hint ? undefined : <Trend current={value} previous={total(data.before, def.metric)} lowerIsBetter={def.lowerIsBetter} />}
          />
        );
      })}
    </div>
  );
}

export {Panel};

export function GranularitySelect({value, onChange}: {value: Granularity; onChange: (value: Granularity) => void}) {
  return (
    <select aria-label="Regroupement" value={value} onChange={event => onChange(event.target.value as Granularity)} className="h-9 rounded-lg border border-slate-200 bg-white pr-8 pl-3 text-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none">
      <option value="Day">Par jour</option>
      <option value="Week">Par semaine</option>
      <option value="Month">Par mois</option>
    </select>
  );
}

export function TrendChart({data, metric, period, granularity, onGranularity, format, title, headline}: {
  data: ReportData;
  metric: string;
  period: Period;
  granularity: Granularity;
  onGranularity: (value: Granularity) => void;
  format: (value: number) => string;
  title: string;
  headline?: ReactNode;
}) {
  const points = series(data.chart, metric, period, granularity);
  return (
    <Panel title={title} action={<GranularitySelect value={granularity} onChange={onGranularity} />}>
      {headline}
      {points.length === 0 ? <p className="py-16 text-center text-sm text-ink-500">Aucune donnée sur la période.</p> : <AreaChart points={points} format={format} axisFormat={compact} />}
    </Panel>
  );
}

export function Split({rows, metric, center, caption, format}: {rows: ReportRow[]; metric: string; center: string; caption: string; format: (value: number) => string}) {
  const parts = byDimension(rows, metric).filter(part => part.value > 0);
  const sum = parts.reduce((acc, part) => acc + part.value, 0);
  if (!sum) return <p className="py-16 text-center text-sm text-ink-500">Aucune donnée sur la période.</p>;
  const segments = parts.map((part, i) => ({label: labelOf(part.dimension), value: part.value, color: palette[i % palette.length]}));
  return (
    <div className="flex flex-col items-center gap-6 sm:flex-row 2xl:flex-col">
      <Donut segments={segments} total={center} caption={caption} />
      <Legend
        className="w-full min-w-0 flex-1"
        items={segments.map(segment => ({label: segment.label, color: segment.color, value: `${Math.round((segment.value / sum) * 100)} % · ${format(segment.value)}`}))}
      />
    </div>
  );
}

export function ChannelBars({rows, metric, format}: {rows: ReportRow[]; metric: string; format?: (value: number) => string}) {
  const channels = ['dine_in', 'takeaway', 'delivery'];
  const others = byDimension(rows, metric).filter(part => !channels.includes(part.dimension)).reduce((sum, part) => sum + part.value, 0);
  const bars = [
    ...channels.map((dimension, i) => ({label: labelOf(dimension), value: total(rows, metric, dimension), color: palette[i]})),
    ...(others ? [{label: 'Autres', value: others, color: palette[6]}] : []),
  ];
  return <Bars bars={bars} format={format} />;
}

export function ChannelSeries({data, period, granularity}: {data: ReportData; period: Period; granularity: Granularity}) {
  const channels = ['dine_in', 'takeaway', 'delivery'];
  const base = series(data.chart, 'sales.orders_placed', period, granularity);
  if (base.length === 0) return <p className="py-16 text-center text-sm text-ink-500">Aucune donnée sur la période.</p>;
  const tail = base.slice(-14);
  return (
    <>
      <div className="mb-2 flex flex-wrap gap-4 text-xs text-ink-600">
        {channels.map((channel, i) => <span key={channel} className="flex items-center gap-1.5"><span className="size-2.5 rounded-full" style={{background: palette[i]}} />{labelOf(channel)}</span>)}
      </div>
      <GroupedBars
        categories={tail.map(point => point.label)}
        series={channels.map((channel, i) => {
          const values = new Map(series(data.chart, 'sales.orders_placed', period, granularity, channel).map(point => [point.start, point.value]));
          return {label: labelOf(channel), color: palette[i], values: tail.map(point => values.get(point.start) ?? 0)};
        })}
      />
      {base.length > tail.length && <p className="mt-2 text-xs text-ink-500">Les {tail.length} dernières périodes.</p>}
    </>
  );
}

export function Peaks({sample}: {sample: Sample | null | undefined}) {
  if (sample === undefined) return <Spinner />;
  if (sample === null) return null;
  return (
    <>
      <Heatmap rows={WEEKDAYS} columns={HOURS.map(hour => `${hour}h`)} values={sample.heat} />
      <p className="mt-2 text-xs text-ink-500">Commandes terminées par heure d’encaissement : {number(sample.orders)} sur la période.</p>
    </>
  );
}

export function TopProducts({sample, currency, limit = 5}: {sample: Sample | null | undefined; currency: string; limit?: number}) {
  if (sample === undefined) return <Spinner />;
  if (sample === null) return null;
  if (sample.products.length === 0) return <p className="py-10 text-center text-sm text-ink-500">Pas de commande terminée sur la période.</p>;
  return (
    <>
      <ol className="space-y-3">
        {sample.products.slice(0, limit).map((product, i) => (
          <li key={product.productId} className="flex items-center gap-3 text-sm">
            <span className={cx('grid size-7 shrink-0 place-items-center rounded-full text-xs font-bold', i === 0 ? 'bg-amber-400 text-white' : 'bg-slate-100 text-ink-600')}>{i + 1}</span>
            <span className="min-w-0 flex-1">
              <span className="block truncate font-semibold">{product.name}</span>
              <span className="text-xs text-ink-500">{product.quantity} vendu{product.quantity > 1 ? 's' : ''}</span>
            </span>
            <span className="font-semibold whitespace-nowrap">{money(product.amount, currency)}</span>
          </li>
        ))}
      </ol>
    </>
  );
}

export function Categories({sample, currency}: {sample: Sample | null | undefined; currency: string}) {
  if (sample === undefined) return <Spinner />;
  if (sample === null) return null;
  const totals = new Map<string, {quantity: number; amount: number}>();
  sample.products.forEach(product => {
    const name = sample.categoryOf.get(product.productId)?.[0] ?? 'Hors carte';
    const current = totals.get(name) ?? {quantity: 0, amount: 0};
    current.quantity += product.quantity;
    current.amount += product.amount;
    totals.set(name, current);
  });
  const rows = [...totals.entries()].sort((a, b) => b[1].amount - a[1].amount);
  const sum = rows.reduce((acc, [, row]) => acc + row.amount, 0);
  if (!rows.length) return <p className="py-10 text-center text-sm text-ink-500">Pas de commande terminée sur la période.</p>;
  return (
    <>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[420px] text-left text-sm">
          <thead className="text-xs text-ink-500">
            <tr>
              <th scope="col" className="pb-2 font-medium">Catégorie</th>
              <th scope="col" className="pb-2 text-right font-medium">Ventes</th>
              <th scope="col" className="pb-2 text-right font-medium">Chiffre d’affaires</th>
              <th scope="col" className="pb-2 pl-4 font-medium">% du CA</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {rows.map(([name, row], i) => (
              <tr key={name}>
                <td className="py-2.5 font-medium">{name}</td>
                <td className="py-2.5 text-right">{row.quantity}</td>
                <td className="py-2.5 text-right whitespace-nowrap">{money(row.amount, currency)}</td>
                <td className="py-2.5 pl-4">
                  <span className="flex items-center gap-2">
                    <span className="h-2 w-24 overflow-hidden rounded-full bg-slate-100"><span className="block h-full rounded-full" style={{width: `${sum ? (row.amount / sum) * 100 : 0}%`, background: palette[i % palette.length]}} /></span>
                    <span className="w-12 text-right text-xs">{sum ? number((row.amount / sum) * 100, 1) : 0} %</span>
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-3 text-xs text-ink-500">Toutes les commandes terminées de la période ; un produit présent dans plusieurs catégories compte dans la première.</p>
    </>
  );
}

export function Figures({items}: {items: {label: string; value: string; tone?: string; icon?: ReactNode}[]}) {
  return (
    <ul className="space-y-3">
      {items.map(item => (
        <li key={item.label} className="flex items-center gap-3 text-sm">
          {item.icon && <span className={cx('grid size-9 shrink-0 place-items-center rounded-xl', item.tone)} aria-hidden="true">{item.icon}</span>}
          <span className="flex-1 text-ink-600">{item.label}</span>
          <span className="font-semibold">{item.value}</span>
        </li>
      ))}
    </ul>
  );
}
