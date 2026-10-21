'use client';
import {useEffect, useRef, useState} from 'react';
import Link from 'next/link';
import {ArrowLeft, Maximize2, Minimize2, Volume2} from 'lucide-react';
import {api, type Page} from '@/lib/api';
import {armChime, playChime} from '@/lib/chime';
import {time} from '@/lib/format';
import {useNow, useResource} from '@/lib/hooks';
import type {LocationSummary, OrderStatus, OrderSummary} from '@/lib/types';
import {WithLocation} from '@/components/console';
import {Alert, cx} from '@/components/ui';

const preparing: OrderStatus[] = ['Accepted', 'Preparing'];
const maxAgeMs = 12 * 3_600_000;
const highlightMs = 15_000;

/** Commandes à appeler : uniquement les numéros, jamais le nom du client (T16). */
async function loadCalls(locationId: string, withDineIn: boolean, signal?: AbortSignal) {
  const pages = await Promise.all(
    [...preparing, 'Ready' as OrderStatus].map(status => api<Page<OrderSummary>>('/orders', {query: {locationId, status, pageSize: 100}, signal})),
  );
  const since = Date.now() - maxAgeMs;
  const keep = (order: OrderSummary) => (withDineIn || order.type !== 'DineIn') && new Date(order.createdAtUtc).getTime() >= since;
  const orders = pages.flatMap(page => page.items).filter(keep).sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc));
  return {
    preparing: orders.filter(order => preparing.includes(order.status)),
    ready: orders.filter(order => order.status === 'Ready'),
  };
}

function Screen({location}: {location: LocationSummary}) {
  const [withDineIn, setWithDineIn] = useState(false);
  const {data, error} = useResource(signal => loadCalls(location.id, withDineIn, signal), [location.id, withDineIn], 5_000);
  const now = useNow(1_000);
  const area = useRef<HTMLDivElement>(null);
  const [fullscreen, setFullscreen] = useState(false);
  const seen = useRef<Set<string> | null>(null);
  const [fresh, setFresh] = useState<Record<string, number>>({});

  useEffect(() => armChime(), []);

  useEffect(() => {
    const update = () => setFullscreen(document.fullscreenElement === area.current);
    document.addEventListener('fullscreenchange', update);
    return () => document.removeEventListener('fullscreenchange', update);
  }, []);

  useEffect(() => {
    if (!data) return;
    const ids = new Set(data.ready.map(order => order.id));
    if (seen.current) {
      const added = [...ids].filter(id => !seen.current!.has(id));
      if (added.length) {
        playChime();
        const at = Date.now();
        setFresh(current => ({...current, ...Object.fromEntries(added.map(id => [id, at]))}));
      }
    }
    seen.current = ids;
  }, [data]);

  const toggleFullscreen = async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await area.current?.requestFullscreen();
    } catch {
      setFullscreen(false);
    }
  };

  const isFresh = (id: string) => (fresh[id] ?? 0) + highlightMs > now;

  return (
    <div className="space-y-4">
      {!fullscreen && (
        <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
          <div>
            <Link href="/cuisine" className="mb-2 inline-flex items-center gap-1 text-sm font-medium text-ink-500 hover:text-ink-800"><ArrowLeft className="size-4" aria-hidden="true" />Cuisine</Link>
            <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Écran d’appel</h1>
            <p className="mt-1 text-sm text-ink-500">À afficher face aux clients : les numéros en préparation et prêts, mis à jour toutes les 5 secondes. Un signal sonore annonce chaque commande prête.</p>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2 text-sm font-medium">
              <input type="checkbox" checked={withDineIn} onChange={event => setWithDineIn(event.target.checked)} className="size-4 rounded border-slate-300" />
              Inclure le sur place
            </label>
            <button type="button" onClick={() => void toggleFullscreen()} className="flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 text-sm font-semibold text-ink-800 hover:bg-slate-50">
              <Maximize2 className="size-4" aria-hidden="true" />Plein écran
            </button>
          </div>
        </div>
      )}
      {error && <Alert>{error}</Alert>}
      <p className="flex items-center gap-2 text-xs text-ink-500"><Volume2 className="size-4" aria-hidden="true" />Touchez l’écran une fois pour activer le son.</p>

      <div ref={area} className="grid min-h-[60vh] grid-cols-1 gap-4 rounded-3xl bg-slate-900 p-6 text-white md:grid-cols-[2fr_3fr] [&:fullscreen]:min-h-screen [&:fullscreen]:rounded-none">
        {fullscreen && (
          <div className="col-span-full flex items-center justify-between">
            <p className="text-2xl font-bold">{location.name} <span className="ml-2 text-lg font-normal text-slate-400">{time(new Date(now).toISOString())}</span></p>
            <button type="button" onClick={() => void toggleFullscreen()} className="flex h-10 items-center gap-2 rounded-xl bg-white/10 px-4 text-sm font-semibold hover:bg-white/20"><Minimize2 className="size-4" aria-hidden="true" />Quitter</button>
          </div>
        )}
        <section aria-label="En préparation" className="rounded-2xl bg-white/5 p-5">
          <h2 className="mb-4 text-xl font-bold tracking-wide text-amber-300 uppercase">En préparation</h2>
          <ul className="flex flex-wrap gap-3">
            {data?.preparing.map(order => (
              <li key={order.id} className="rounded-xl bg-white/10 px-4 py-2 text-3xl font-bold tabular-nums">{order.number}</li>
            ))}
          </ul>
          {data && data.preparing.length === 0 && <p className="text-slate-400">—</p>}
        </section>
        <section aria-label="Prêtes" aria-live="polite" className="rounded-2xl bg-emerald-500/10 p-5">
          <h2 className="mb-4 text-xl font-bold tracking-wide text-emerald-300 uppercase">Prêtes, à retirer</h2>
          <ul className="flex flex-wrap gap-4">
            {data?.ready.map(order => (
              <li
                key={order.id}
                className={cx('rounded-2xl px-6 py-4 text-6xl font-extrabold tabular-nums transition', isFresh(order.id) ? 'animate-pulse bg-emerald-400 text-slate-900' : 'bg-emerald-500/20 text-emerald-100')}>
                {order.number}
              </li>
            ))}
          </ul>
          {data && data.ready.length === 0 && <p className="text-slate-400">—</p>}
        </section>
      </div>
    </div>
  );
}

export default function CallScreenPage() {
  return <WithLocation>{location => <Screen location={location} />}</WithLocation>;
}
