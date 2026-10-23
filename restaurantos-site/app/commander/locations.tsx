'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {ChevronRight, MapPin} from 'lucide-react';
import {accentOf, clock, errorMessage, storefront, storefrontKey, type Storefront} from '@/lib/storefront';
import {Notice, Spinner} from '@/components/order-ui';
import {tr, useLang} from '@/lib/i18n';

/** Le choix de l'établissement : ceux que l'application sert, avec leur ouverture. Un seul : on y va directement. */
export function Locations() {
  useLang();
  const [data, setData] = useState<Storefront | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    storefront<Storefront>('/', {signal: controller.signal})
      .then(result => {
        setData(result);
        if (result?.locations.length === 1) window.location.replace(`/commander/${result.locations[0].id}`);
      })
      .catch(reason => !controller.signal.aborted && setError(errorMessage(reason)));
    return () => controller.abort();
  }, []);

  if (!storefrontKey) return <Shell><Notice tone="warning">{tr('La commande en ligne n’est pas configurée sur ce site (clé publique absente).')}</Notice></Shell>;
  if (error) return <Shell><Notice tone="error">{error}</Notice></Shell>;
  if (!data) return <Shell><Spinner /></Shell>;

  return (
    <div style={{'--accent': accentOf(data.restaurant.primaryColor)} as React.CSSProperties}>
      <Shell>
        <h1 className="text-2xl font-bold">{data.restaurant.name}</h1>
        <p className="mt-1 text-ink-600">{tr('Choisissez votre établissement.')}</p>
        {!data.orderingEnabled && <div className="mt-4"><Notice tone="warning">{tr('Les commandes en ligne sont momentanément indisponibles.')}</Notice></div>}
        <ul className="mt-6 space-y-3">
          {data.locations.map(location => (
            <li key={location.id}>
              <Link href={`/commander/${location.id}`} className="flex items-center gap-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm transition hover:border-[var(--accent)]">
                <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-slate-100 text-[var(--accent)]"><MapPin className="size-5" /></span>
                <span className="min-w-0 flex-1">
                  <span className="block font-semibold">{location.name}</span>
                  <span className="block truncate text-sm text-ink-500">{[location.street, location.city].filter(Boolean).join(', ')}</span>
                  <span className={location.acceptsOrdersNow ? 'text-sm font-medium text-emerald-700' : 'text-sm text-ink-500'}>
                    {location.acceptsOrdersNow
                      ? tr('Ouvert, commandes acceptées')
                      : location.isPaused
                        ? tr('Commandes en pause')
                        : location.nextOpeningUtc
                          ? tr('Fermé · ouvre à {p0}', {p0: clock(location.nextOpeningUtc, location.timeZoneId)})
                          : tr('Fermé')}
                  </span>
                </span>
                <ChevronRight className="size-5 text-ink-500" />
              </Link>
            </li>
          ))}
        </ul>
        <p className="mt-6 text-center text-sm"><Link href="/reserver" className="font-medium text-[var(--accent)] hover:underline">{tr('Réserver une table')}</Link></p>
      </Shell>
    </div>
  );
}

function Shell({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl bg-slate-50 px-4 py-10">{children}</div>;
}
