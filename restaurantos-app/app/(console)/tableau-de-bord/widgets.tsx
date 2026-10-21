'use client';
import type {ReactNode} from 'react';
import Link from 'next/link';
import {Star} from 'lucide-react';
import type {Period} from '@/lib/dashboard';
import {Skeleton, cx} from '@/components/ui';





const periods: {value: Period; label: string}[] = [
  {value: 'day', label: 'Aujourd’hui'},
  {value: 'week', label: '7 jours'},
  {value: 'month', label: '30 jours'},
];

export function PeriodPicker({value, onChange}: {value: Period; onChange: (value: Period) => void}) {
  return (
    <div role="radiogroup" aria-label="Période" className="inline-flex rounded-xl border border-slate-200 bg-white p-1 shadow-[0_1px_2px_rgba(15,23,42,0.04)]">
      {periods.map(item => (
        <button
          key={item.value}
          type="button"
          role="radio"
          aria-checked={value === item.value}
          onClick={() => onChange(item.value)}
          className={cx(
            'rounded-lg px-3 py-1.5 text-sm font-medium whitespace-nowrap transition',
            value === item.value ? 'bg-ink-900 text-white shadow-sm' : 'text-ink-600 hover:bg-slate-100 hover:text-ink-900',
          )}>
          {item.label}
        </button>
      ))}
    </div>
  );
}

/** Une case du bandeau « En ce moment » : chiffre, libellé, lien. */
export function LiveStat({href, value, label, hint, tone = 'neutral', icon}: {
  href: string;
  value: ReactNode;
  label: string;
  hint?: string;
  tone?: 'neutral' | 'amber' | 'brand' | 'blue';
  icon: ReactNode;
}) {
  const tones = {
    neutral: 'bg-slate-100 text-ink-600',
    amber: 'bg-amber-100 text-amber-700',
    brand: 'bg-brand-100 text-brand-700',
    blue: 'bg-sky-100 text-sky-700',
  };
  return (
    <Link href={href} className="group flex min-w-0 items-center gap-3 rounded-xl px-3 py-2.5 transition hover:bg-slate-50">
      <span className={cx('grid size-9 shrink-0 place-items-center rounded-lg', tones[tone])}>{icon}</span>
      <span className="min-w-0">
        <span className="block text-lg leading-tight font-bold text-ink-900 tabular-nums">{value}</span>
        <span className="block truncate text-xs text-ink-500">{label}{hint ? ` · ${hint}` : ''}</span>
      </span>
    </Link>
  );
}

/** Classement en barres horizontales (part de la valeur la plus haute). */
export function RankedBars({items, color = 'var(--color-brand-500)', empty}: {items: {key: string; label: string; value: number; display: string; hint?: string}[]; color?: string; empty: string}) {
  if (items.length === 0) return <p className="py-8 text-center text-sm text-ink-500">{empty}</p>;
  const max = Math.max(...items.map(item => item.value)) || 1;
  return (
    <ol className="space-y-3.5">
      {items.map((item, index) => (
        <li key={item.key}>
          <div className="mb-1.5 flex items-baseline justify-between gap-3 text-sm">
            <span className="flex min-w-0 items-baseline gap-2">
              <span className="w-4 shrink-0 text-xs font-semibold text-ink-500 tabular-nums">{index + 1}</span>
              <span className="truncate font-medium text-ink-800">{item.label}</span>
            </span>
            <span className="shrink-0 font-semibold text-ink-900 tabular-nums">
              {item.display}
              {item.hint && <span className="ml-1.5 text-xs font-normal text-ink-500">{item.hint}</span>}
            </span>
          </div>
          <div className="ml-6 h-1.5 rounded-full bg-slate-100">
            <div className="h-1.5 rounded-full" style={{width: `${Math.max(3, (item.value / max) * 100)}%`, background: color}} />
          </div>
        </li>
      ))}
    </ol>
  );
}

export function Stars({rating, size = 'size-3.5'}: {rating: number; size?: string}) {
  return (
    <span className="inline-flex items-center gap-0.5" aria-label={`${rating} sur 5`}>
      {[1, 2, 3, 4, 5].map(i => (
        <Star key={i} className={cx(size, i <= Math.round(rating) ? 'fill-amber-400 text-amber-400' : 'fill-slate-200 text-slate-200')} aria-hidden="true" />
      ))}
    </span>
  );
}


/** Ce que l'on montre pendant le premier chargement : la silhouette de la page. */
export function DashboardSkeleton() {
  return (
    <div className="space-y-6" role="status" aria-label="Chargement du tableau de bord">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="space-y-2"><Skeleton className="h-8 w-56" /><Skeleton className="h-4 w-80" /></div>
        <Skeleton className="h-10 w-72" />
      </div>
      <Skeleton className="h-20 w-full rounded-2xl" />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{[0, 1, 2, 3].map(i => <Skeleton key={i} className="h-32 rounded-2xl" />)}</div>
      <div className="grid gap-6 xl:grid-cols-3"><Skeleton className="h-96 rounded-2xl xl:col-span-2" /><Skeleton className="h-96 rounded-2xl" /></div>
    </div>
  );
}
