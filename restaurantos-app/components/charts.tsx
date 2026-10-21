'use client';
import {useEffect, useId, useRef, useState} from 'react';
import {cx} from './ui';

/** Courbe avec aire, un point par valeur ; l'axe vertical est gradué en 4 intervalles. */
export function AreaChart({points, format, axisFormat = format, height = 260}: {points: {label: string; value: number}[]; format: (value: number) => string; axisFormat?: (value: number) => string; height?: number}) {
  const gradient = useId();
  const [hover, setHover] = useState<number | null>(null);
  const width = 520;
  const pad = {top: 12, right: 14, bottom: 30, left: 52};
  const max = niceMax(Math.max(0, ...points.map(point => point.value)));
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;
  const x = (i: number) => pad.left + (points.length <= 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
  const y = (value: number) => pad.top + innerH - (max ? (value / max) * innerH : 0);
  const line = points.map((point, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(point.value).toFixed(1)}`).join(' ');
  const area = points.length ? `${line} L${x(points.length - 1).toFixed(1)},${pad.top + innerH} L${x(0).toFixed(1)},${pad.top + innerH} Z` : '';
  const ticks = [0, 1, 2, 3, 4].map(i => (max / 4) * i);
  const labelEvery = Math.max(1, Math.ceil(points.length / 8));

  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img" aria-label="Évolution du chiffre d’affaires" onMouseLeave={() => setHover(null)}>
      <defs>
        <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor="var(--color-brand-500)" stopOpacity="0.28" />
          <stop offset="100%" stopColor="var(--color-brand-500)" stopOpacity="0.02" />
        </linearGradient>
      </defs>
      {ticks.map(tick => (
        <g key={tick}>
          <line x1={pad.left} x2={width - pad.right} y1={y(tick)} y2={y(tick)} stroke="#e2e8f0" strokeDasharray={tick ? '3 4' : undefined} />
          <text x={pad.left - 8} y={y(tick) + 4} textAnchor="end" className="fill-slate-500 text-[13px]">{axisFormat(tick)}</text>
        </g>
      ))}
      {points.map((point, i) =>
        i % labelEvery === 0 ? (
          <text key={point.label} x={x(i)} y={height - 8} textAnchor="middle" className="fill-slate-500 text-[13px]">{point.label}</text>
        ) : null,
      )}
      <path d={area} fill={`url(#${gradient})`} />
      <path d={line} fill="none" stroke="var(--color-brand-600)" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
      {points.map((point, i) => (
        <g key={point.label} onMouseEnter={() => setHover(i)}>
          <rect x={x(i) - innerW / Math.max(1, points.length) / 2} y={pad.top} width={innerW / Math.max(1, points.length)} height={innerH} fill="transparent" />
          <circle cx={x(i)} cy={y(point.value)} r={hover === i ? 5 : 3.5} fill="white" stroke="var(--color-brand-600)" strokeWidth="2" />
        </g>
      ))}
      {hover !== null && points[hover] && (
        <g pointerEvents="none">
          <rect x={Math.min(x(hover) - 60, width - 132)} y={Math.max(0, y(points[hover].value) - 46)} width="120" height="36" rx="8" fill="#0f172a" />
          <text x={Math.min(x(hover), width - 72)} y={Math.max(14, y(points[hover].value) - 24)} textAnchor="middle" className="fill-white text-[12px] font-semibold">
            {points[hover].label} · {format(points[hover].value)}
          </text>
        </g>
      )}
    </svg>
  );
}

/** Anneau de répartition avec total au centre. */
export function Donut({segments, total, caption}: {segments: {label: string; value: number; color: string}[]; total: string; caption: string}) {
  const sum = segments.reduce((acc, segment) => acc + segment.value, 0);
  const radius = 70;
  const circumference = 2 * Math.PI * radius;
  let offset = 0;
  return (
    <svg viewBox="0 0 200 200" className="size-44 shrink-0" role="img" aria-label={`${total} ${caption}`}>
      <circle cx="100" cy="100" r={radius} fill="none" stroke="#e2e8f0" strokeWidth="26" />
      {sum > 0 &&
        segments.map(segment => {
          const length = (segment.value / sum) * circumference;
          const dash = `${Math.max(0, length - 2)} ${circumference}`;
          const element = (
            <circle
              key={segment.label}
              cx="100"
              cy="100"
              r={radius}
              fill="none"
              stroke={segment.color}
              strokeWidth="26"
              strokeDasharray={dash}
              strokeDashoffset={-offset}
              transform="rotate(-90 100 100)"
            />
          );
          offset += length;
          return element;
        })}
      <text x="100" y="98" textAnchor="middle" className="fill-slate-900 text-[30px] font-bold">{total}</text>
      <text x="100" y="122" textAnchor="middle" className="fill-slate-500 text-[13px]">{caption}</text>
    </svg>
  );
}

/** Histogramme groupé : une barre par série et par catégorie. */
export function GroupedBars({categories, series, height = 200}: {categories: string[]; series: {label: string; color: string; values: number[]}[]; height?: number}) {
  const width = 420;
  const pad = {top: 10, right: 8, bottom: 28, left: 34};
  const max = niceMax(Math.max(0, ...series.flatMap(serie => serie.values)));
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;
  const group = innerW / Math.max(1, categories.length);
  const bar = Math.min(14, (group * 0.7) / Math.max(1, series.length));
  const y = (value: number) => pad.top + innerH - (max ? (value / max) * innerH : 0);
  const ticks = [0, 1, 2, 3].map(i => (max / 3) * i);
  const labelEvery = Math.max(1, Math.ceil(categories.length / 7));
  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img" aria-label="Commandes par jour et par canal">
      {ticks.map(tick => (
        <g key={tick}>
          <line x1={pad.left} x2={width - pad.right} y1={y(tick)} y2={y(tick)} stroke="#eef2f7" />
          <text x={pad.left - 6} y={y(tick) + 4} textAnchor="end" className="fill-slate-500 text-[13px]">{Math.round(tick)}</text>
        </g>
      ))}
      {categories.map((category, i) => {
        const start = pad.left + i * group + (group - bar * series.length) / 2;
        return (
          <g key={category}>
            {series.map((serie, s) => {
              const value = serie.values[i] ?? 0;
              return (
                <rect key={serie.label} x={start + s * bar} y={y(value)} width={bar - 2} height={pad.top + innerH - y(value)} rx="2" fill={serie.color}>
                  <title>{`${category} · ${serie.label} : ${value}`}</title>
                </rect>
              );
            })}
            {i % labelEvery === 0 && <text x={pad.left + i * group + group / 2} y={height - 8} textAnchor="middle" className="fill-slate-500 text-[13px] capitalize">{category}</text>}
          </g>
        );
      })}
    </svg>
  );
}

export function Legend({items, className}: {items: {label: string; color: string; value?: string}[]; className?: string}) {
  return (
    <ul className={cx('space-y-3 text-sm', className)}>
      {items.map(item => (
        <li key={item.label} className="flex items-center gap-3">
          <span className="size-2.5 shrink-0 rounded-full" style={{background: item.color}} />
          <span className="flex-1 text-ink-600">{item.label}</span>
          {item.value && <span className="font-semibold">{item.value}</span>}
        </li>
      ))}
    </ul>
  );
}

function niceMax(value: number): number {
  if (value <= 0) return 4;
  const exponent = Math.pow(10, Math.floor(Math.log10(value)));
  const fraction = value / exponent;
  const nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
  return nice * exponent;
}

/** Barres verticales, une couleur par barre, valeur affichée au-dessus. */
export function Bars({bars, height = 220, format = (value: number) => String(Math.round(value))}: {bars: {label: string; value: number; color: string}[]; height?: number; format?: (value: number) => string}) {
  const width = 460;
  const pad = {top: 24, right: 8, bottom: 30, left: 8};
  const max = niceMax(Math.max(0, ...bars.map(bar => bar.value)));
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;
  const slot = innerW / Math.max(1, bars.length);
  const barWidth = Math.min(64, slot * 0.6);
  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img" aria-label={bars.map(bar => `${bar.label} : ${format(bar.value)}`).join(', ')}>
      <line x1={pad.left} x2={width - pad.right} y1={pad.top + innerH} y2={pad.top + innerH} stroke="#e2e8f0" />
      {bars.map((bar, i) => {
        const h = max ? (bar.value / max) * innerH : 0;
        const x = pad.left + i * slot + (slot - barWidth) / 2;
        const y = pad.top + innerH - h;
        return (
          <g key={bar.label}>
            <rect x={x} y={y} width={barWidth} height={Math.max(h, bar.value ? 2 : 0)} rx="6" fill={bar.color}>
              <title>{`${bar.label} : ${format(bar.value)}`}</title>
            </rect>
            <text x={x + barWidth / 2} y={y - 6} textAnchor="middle" className="fill-slate-900 text-[13px] font-semibold">{format(bar.value)}</text>
            <text x={x + barWidth / 2} y={height - 8} textAnchor="middle" className="fill-slate-500 text-[12px]">{bar.label}</text>
          </g>
        );
      })}
    </svg>
  );
}

/** Carte de chaleur jours × heures : plus la case est foncée, plus la valeur est forte. */
export function Heatmap({rows, columns, values, color = '22, 168, 95'}: {rows: string[]; columns: string[]; values: number[][]; color?: string}) {
  const max = Math.max(1, ...values.flat());
  return (
    <div className="relative overflow-x-auto">
      <table className="w-full border-separate border-spacing-1 text-xs" aria-label="Commandes par jour et par heure">
        <thead>
          <tr>
            <th scope="col"><span className="sr-only">Jour</span></th>
            {columns.map(column => <th key={column} scope="col" className="font-normal text-ink-500">{column}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, r) => (
            <tr key={row}>
              <th scope="row" className="pr-1 text-left font-normal text-ink-500">{row}</th>
              {columns.map((column, c) => {
                const value = values[r]?.[c] ?? 0;
                const alpha = value ? 0.12 + (value / max) * 0.88 : 0.05;
                return (
                  <td key={column} title={`${row} ${column} : ${value}`} className="h-6 min-w-6 rounded" style={{background: `rgba(${color}, ${alpha})`}}>
                    <span className="sr-only">{value}</span>
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Petite courbe sans axe, pour une tuile d'indicateur. */
export function Sparkline({values, color = 'var(--color-brand-600)', label}: {values: number[]; color?: string; label: string}) {
  const gradient = useId();
  const width = 120;
  const height = 36;
  if (values.length < 2) return null;
  const max = Math.max(...values);
  const min = Math.min(0, ...values);
  const span = max - min || 1;
  const x = (i: number) => (i / (values.length - 1)) * width;
  const y = (value: number) => 3 + (height - 6) * (1 - (value - min) / span);
  const line = values.map((value, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(value).toFixed(1)}`).join(' ');
  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="h-9 w-28" role="img" aria-label={label} preserveAspectRatio="none">
      <defs>
        <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.22" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={`${line} L${width},${height} L0,${height} Z`} fill={`url(#${gradient})`} />
      <path d={line} fill="none" stroke={color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
      <circle cx={x(values.length - 1)} cy={y(values[values.length - 1])} r="2.5" fill={color} />
    </svg>
  );
}

/**
 * Évolution sur la période (aire pleine) comparée à la période précédente (trait pointillé gris), même échelle. Au
 * survol : un repère vertical et les deux valeurs.
 */
export function TrendChart({labels, current, previous, format, axisFormat = format, currentLabel, previousLabel, height = 280}: {
  labels: string[];
  current: number[];
  previous: number[];
  format: (value: number) => string;
  axisFormat?: (value: number) => string;
  currentLabel: string;
  previousLabel: string;
  height?: number;
}) {
  const gradient = useId();
  const [hover, setHover] = useState<number | null>(null);
  const box = useRef<HTMLDivElement>(null);
  const [measured, setMeasured] = useState(720);
  useEffect(() => {
    const element = box.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(entries => setMeasured(Math.max(280, Math.round(entries[0].contentRect.width))));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const width = measured;
  if (measured < 480) height = Math.min(height, 220);
  const pad = {top: 16, right: 16, bottom: 30, left: 48};
  const max = niceMax(Math.max(0, ...current, ...previous));
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;
  const count = labels.length;
  const x = (i: number) => pad.left + (count <= 1 ? innerW / 2 : (i / (count - 1)) * innerW);
  const y = (value: number) => pad.top + innerH - (max ? (value / max) * innerH : 0);
  const path = (values: number[]) => values.map((value, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(value).toFixed(1)}`).join(' ');
  const line = path(current);
  const last = current.length - 1;
  const area = last >= 0 ? `${line} L${x(last).toFixed(1)},${pad.top + innerH} L${x(0).toFixed(1)},${pad.top + innerH} Z` : '';
  const ticks = [0, 1, 2, 3, 4].map(i => (max / 4) * i);
  const labelEvery = Math.max(1, Math.ceil(count / Math.max(3, Math.floor(innerW / 70))));
  const step = innerW / Math.max(1, count - 1);
  const tipX = hover === null ? 0 : Math.min(Math.max(x(hover) - 80, pad.left), width - pad.right - 160);

  return (
    <div ref={box} className="relative">
      <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img" aria-label={`${currentLabel} comparé à ${previousLabel}`} onMouseLeave={() => setHover(null)}>
        <defs>
          <linearGradient id={gradient} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="var(--color-brand-500)" stopOpacity="0.24" />
            <stop offset="100%" stopColor="var(--color-brand-500)" stopOpacity="0.02" />
          </linearGradient>
        </defs>
        {ticks.map(tick => (
          <g key={tick}>
            <line x1={pad.left} x2={width - pad.right} y1={y(tick)} y2={y(tick)} stroke="#e2e8f0" strokeDasharray={tick ? '3 5' : undefined} />
            <text x={pad.left - 10} y={y(tick) + 4} textAnchor="end" className="fill-slate-500 text-[12px] tabular-nums">{axisFormat(tick)}</text>
          </g>
        ))}
        {labels.map((label, i) =>
          (i % labelEvery === 0 && count - 1 - i >= labelEvery / 2) || i === count - 1 ? (
            <text key={label + i} x={x(i)} y={height - 8} textAnchor={i === count - 1 && count > 1 ? 'end' : i === 0 && count > 1 ? 'start' : 'middle'} className="fill-slate-500 text-[12px]">{label}</text>
          ) : null,
        )}
        <path d={path(previous)} fill="none" stroke="#94a3b8" strokeWidth="2" strokeDasharray="5 5" strokeLinejoin="round" strokeLinecap="round" />
        <path d={area} fill={`url(#${gradient})`} />
        <path d={line} fill="none" stroke="var(--color-brand-600)" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
        {hover !== null && (
          <g pointerEvents="none">
            <line x1={x(hover)} x2={x(hover)} y1={pad.top} y2={pad.top + innerH} stroke="#cbd5e1" />
            <circle cx={x(hover)} cy={y(previous[hover] ?? 0)} r="4" fill="white" stroke="#94a3b8" strokeWidth="2" />
            {hover <= last && <circle cx={x(hover)} cy={y(current[hover])} r="5" fill="white" stroke="var(--color-brand-600)" strokeWidth="2.5" />}
          </g>
        )}
        {last >= 0 && hover === null && <circle cx={x(last)} cy={y(current[last])} r="4.5" fill="white" stroke="var(--color-brand-600)" strokeWidth="2.5" />}
        {labels.map((label, i) => (
          <rect key={`hit-${label}-${i}`} x={x(i) - step / 2} y={pad.top} width={Math.max(step, 8)} height={innerH} fill="transparent" onMouseEnter={() => setHover(i)} />
        ))}
      </svg>
      {hover !== null && (
        <div className="pointer-events-none absolute top-2 w-40 rounded-lg bg-ink-900 px-3 py-2 text-xs text-white shadow-lg" style={{left: `${(tipX / width) * 100}%`}}>
          <p className="font-semibold">{labels[hover]}</p>
          <p className="mt-1 flex items-center justify-between gap-2"><span className="flex items-center gap-1.5"><span className="size-2 rounded-full bg-brand-400" />{currentLabel}</span><span className="font-semibold tabular-nums">{hover <= last ? format(current[hover]) : '—'}</span></p>
          <p className="mt-0.5 flex items-center justify-between gap-2 text-slate-300"><span className="flex items-center gap-1.5"><span className="size-2 rounded-full bg-slate-400" />{previousLabel}</span><span className="tabular-nums">{format(previous[hover] ?? 0)}</span></p>
        </div>
      )}
    </div>
  );
}
