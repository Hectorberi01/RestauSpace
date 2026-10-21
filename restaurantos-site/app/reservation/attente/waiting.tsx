'use client';
import {useCallback, useEffect, useState} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {BellRing, CalendarClock, Hourglass, Users} from 'lucide-react';
import {
  acceptWaitlistOffer, accentOf, cancelWaitlist, errorMessage, loadWaitlist, localDay, localTime, rememberBooking, storefront,
  type GuestWaitlist, type Storefront, type WaitlistStatus,
} from '@/lib/storefront';
import {AccentButton, Notice, Spinner} from '@/components/order-ui';
import {tr, translated, useLang} from '@/lib/i18n';

const statusLabel: Record<WaitlistStatus, string> = translated({
  Waiting: 'Inscrit en liste d’attente',
  Offered: 'Une table s’est libérée',
  Booked: 'Table réservée',
  Expired: 'Aucune table ne s’est libérée',
  Cancelled: 'Inscription retirée',
});

export function WaitlistEntry() {
  useLang();
  const params = useSearchParams();
  const router = useRouter();
  const token = params.get('jeton') ?? '';
  const fresh = params.has('nouvelle');
  const [entry, setEntry] = useState<GuestWaitlist | null>(null);
  const [front, setFront] = useState<Storefront | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await loadWaitlist(token, signal);
      if (result) setEntry(result);
    } catch (reason) {
      if (!signal?.aborted) setError(errorMessage(reason));
    }
  }, [token]);

  useEffect(() => {
    if (!token) return;
    const controller = new AbortController();
    void load(controller.signal);
    storefront<Storefront>('/', {signal: controller.signal}).then(result => result && setFront(result)).catch(() => {});
    return () => controller.abort();
  }, [token, load]);

  // Tant que l'inscription vit, elle est relue (une table peut se libérer à tout moment).
  const live = entry?.status === 'Waiting' || entry?.status === 'Offered';
  useEffect(() => {
    if (!live) return;
    const timer = window.setInterval(() => {
      setNow(Date.now());
      if (document.visibilityState === 'visible') void load();
    }, 10_000);
    return () => window.clearInterval(timer);
  }, [live, load]);

  const accept = async () => {
    setBusy('accept');
    setActionError(null);
    try {
      const booked = await acceptWaitlistOffer(token);
      if (booked) {
        rememberBooking(booked.token);
        router.push(`/reservation?jeton=${encodeURIComponent(booked.token)}&nouvelle=1`);
      }
    } catch (reason) {
      setActionError(errorMessage(reason));
      await load();
    } finally {
      setBusy(null);
    }
  };

  const cancel = async (question: string) => {
    if (!window.confirm(question)) return;
    setBusy('cancel');
    setActionError(null);
    try {
      await cancelWaitlist(token);
      await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  };

  if (!token) return <Shell><Notice tone="error">{tr('Lien de liste d’attente incomplet.')}</Notice></Shell>;
  if (error) return <Shell><Notice tone="error">{error}</Notice></Shell>;
  if (!entry) return <Shell><Spinner /></Shell>;
  const zone = front?.locations.find(item => item.id === entry.locationId)?.timeZoneId ?? 'Africa/Porto-Novo';
  const offered = entry.status === 'Offered' && entry.offeredStartUtc;
  const minutesLeft = entry.offerExpiresAtUtc ? Math.max(0, Math.ceil((new Date(entry.offerExpiresAtUtc).getTime() - now) / 60_000)) : null;

  return (
    <div style={{'--accent': accentOf(front?.restaurant.primaryColor)} as React.CSSProperties}>
      <Shell>
        {front && <p className="text-sm font-semibold text-[var(--accent)]">{front.restaurant.name}</p>}
        <h1 className="mt-1 text-2xl font-bold tracking-tight">{tr('Liste d’attente')}</h1>
        {fresh && entry.status === 'Waiting' && (
          <div className="mt-4"><Notice tone="success">{tr('C’est noté ! Si une table se libère, vous recevez un message WhatsApp ou SMS avec un lien pour la confirmer. Gardez cette page.')}</Notice></div>
        )}

        {offered && (
          <div className="mt-6 space-y-3 rounded-3xl bg-white p-5 shadow-sm ring-2 ring-[var(--accent)]">
            <p className="flex items-center gap-2 text-lg font-semibold"><BellRing className="size-6 text-[var(--accent)]" aria-hidden="true" />{tr('Une table est libre pour vous')}</p>
            <p className="text-ink-700">
              {upperFirst(localDay(entry.offeredStartUtc!, zone))} {tr('à')} <strong>{localTime(entry.offeredStartUtc!, zone)}</strong> · {entry.partySize} {tr('personne')}{entry.partySize > 1 ? 's' : ''}
            </p>
            {minutesLeft !== null && <p className="text-sm text-ink-500">{tr('Confirmez dans les')} {minutesLeft} {tr('minutes : ensuite, elle est proposée à la personne suivante.')}</p>}
            <AccentButton className="w-full" busy={busy === 'accept'} disabled={!!busy} onClick={() => void accept()}>{tr('Je prends cette table')}</AccentButton>
            <button type="button" disabled={!!busy} onClick={() => void cancel(tr('Laisser passer cette table et quitter la liste d’attente ?'))}
              className="w-full rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-ink-700 hover:bg-slate-50 disabled:opacity-50">
              {tr('Non merci')}
            </button>
          </div>
        )}

        <div className="mt-6 space-y-3 rounded-3xl bg-white p-5 shadow-sm">
          <p className="flex items-center gap-3 text-lg font-semibold">
            <CalendarClock className="size-6 text-[var(--accent)]" aria-hidden="true" />
            <span>{upperFirst(localDay(entry.desiredStartUtc, zone))}</span> {tr('vers')} {localTime(entry.desiredStartUtc, zone)}
          </p>
          <p className="flex items-center gap-3 text-ink-600"><Users className="size-5" aria-hidden="true" />{entry.partySize} {tr('personne')}{entry.partySize > 1 ? 's' : ''} {tr('· au nom de')} {entry.guestName}</p>
          {entry.flexibilityMinutes > 0 && <p className="flex items-center gap-3 text-ink-600"><Hourglass className="size-5" aria-hidden="true" />{tr('Jusqu’à')} {entry.flexibilityMinutes} {tr('minutes avant ou après')}</p>}
          {entry.locationName && <p className="text-ink-600">{entry.locationName}</p>}
          <p className={entry.status === 'Booked' ? 'font-semibold text-emerald-700' : entry.status === 'Waiting' || entry.status === 'Offered' ? 'font-semibold text-amber-700' : 'font-semibold text-ink-500'}>{statusLabel[entry.status]}</p>
        </div>

        {actionError && <div className="mt-4"><Notice tone="error">{actionError}</Notice></div>}
        {entry.status === 'Booked' && entry.reservationToken && (
          <p className="mt-4 text-center"><Link href={`/reservation?jeton=${encodeURIComponent(entry.reservationToken)}`} className="font-semibold text-[var(--accent)] hover:underline">{tr('Voir ma réservation')}</Link></p>
        )}
        {entry.status === 'Waiting' && (
          <button type="button" onClick={() => void cancel(tr('Quitter la liste d’attente ?'))} disabled={!!busy} className="mt-4 w-full rounded-xl border border-red-200 bg-white px-4 py-3 text-sm font-semibold text-red-700 hover:bg-red-50 disabled:opacity-50">
            {tr('Quitter la liste d’attente')}
          </button>
        )}
        <p className="mt-6 text-center text-sm"><Link href="/reserver" className="font-medium text-[var(--accent)] hover:underline">{tr('Voir les autres horaires')}</Link></p>
      </Shell>
    </div>
  );
}

function Shell({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl bg-slate-50 px-4 py-10">{children}</div>;
}

const upperFirst = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);
