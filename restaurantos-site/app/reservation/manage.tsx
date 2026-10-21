'use client';
import {useCallback, useEffect, useState} from 'react';
import Link from 'next/link';
import {useSearchParams} from 'next/navigation';
import {CalendarCheck, CalendarX, Users, Wallet} from 'lucide-react';
import {
  accentOf, cancelBooking, errorMessage, loadBooking, localDay, localTime, methodLabel, money, payDeposit, storefront,
  type BookingStatus, type GuestBooking, type GuestDepositStatus, type Storefront,
} from '@/lib/storefront';
import {AccentButton, Notice, Spinner} from '@/components/order-ui';
import {tr, translated, useLang} from '@/lib/i18n';

const statusLabel: Record<BookingStatus, string> = translated({
  Pending: 'En attente de confirmation',
  Confirmed: 'Confirmée',
  Seated: 'Vous êtes installés',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
  NoShow: 'Non honorée',
});

const depositLabel: Record<GuestDepositStatus, string> = translated({
  AwaitingPayment: 'Acompte à payer',
  Paid: 'Acompte payé : il sera déduit de l’addition.',
  Applied: 'Acompte déduit de l’addition.',
  RefundRequested: 'Remboursement de l’acompte en cours.',
  Refunded: 'Acompte remboursé.',
  Forfeited: 'Acompte conservé par le restaurant.',
  Waived: 'Pas d’acompte à payer.',
});

export function ManageBooking() {
  useLang();
  const params = useSearchParams();
  const token = params.get('jeton') ?? '';
  const fresh = params.has('nouvelle');
  const [booking, setBooking] = useState<GuestBooking | null>(null);
  const [front, setFront] = useState<Storefront | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await loadBooking(token, signal);
      if (result) setBooking(result);
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

  // Paiement de l'acompte en cours (retour du prestataire, demande sur le téléphone) : relu toutes les 5 secondes.
  const paying = booking?.deposit?.status === 'AwaitingPayment' && booking.status === 'Pending';
  useEffect(() => {
    if (!paying) return;
    const timer = window.setInterval(() => document.visibilityState === 'visible' && void load(), 5000);
    return () => window.clearInterval(timer);
  }, [paying, load]);

  const pay = async (provider: string, method: string) => {
    setBusy(true);
    setActionError(null);
    try {
      const started = await payDeposit(token, provider, method, `${window.location.origin}/reservation?jeton=${encodeURIComponent(token)}`);
      if (started?.checkoutUrl) window.location.assign(started.checkoutUrl);
      else await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  const cancel = async () => {
    if (!window.confirm(tr('Annuler cette réservation ?'))) return;
    setBusy(true);
    setActionError(null);
    try {
      await cancelBooking(token);
      await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  if (!token) return <Shell><Notice tone="error">{tr('Lien de réservation incomplet.')}</Notice></Shell>;
  if (error) return <Shell><Notice tone="error">{error}</Notice></Shell>;
  if (!booking) return <Shell><Spinner /></Shell>;
  const zone = front?.locations.find(item => item.id === booking.locationId)?.timeZoneId ?? 'Africa/Porto-Novo';
  const cancelled = booking.status === 'Cancelled';
  const deposit = booking.deposit ?? null;

  return (
    <div style={{'--accent': accentOf(front?.restaurant.primaryColor)} as React.CSSProperties}>
      <Shell>
        {front && <p className="text-sm font-semibold text-[var(--accent)]">{front.restaurant.name}</p>}
        <h1 className="mt-1 text-2xl font-bold tracking-tight">{fresh && !cancelled ? (deposit?.status === 'AwaitingPayment' ? tr('Table retenue') : tr('Table réservée !')) : tr('Ma réservation')}</h1>
        {fresh && !cancelled && booking.status !== 'Pending' && <div className="mt-4"><Notice tone="success">{tr('Vous allez recevoir la confirmation par SMS ou WhatsApp. Gardez cette page pour la retrouver.')}</Notice></div>}
        {deposit?.status === 'AwaitingPayment' && booking.status === 'Pending' && (
          <div className="mt-4 space-y-3 rounded-3xl bg-white p-5 shadow-sm ring-1 ring-[var(--accent)]/30">
            <p className="flex items-center gap-2 font-semibold"><Wallet className="size-5 text-[var(--accent)]" aria-hidden="true" />{tr('Acompte')} : {money(deposit.amount, deposit.currencyCode)}</p>
            <p className="text-sm text-ink-600">
              {tr('Votre table est retenue')}{deposit.dueBeforeUtc ? ` ${tr('jusqu’à')} ${localTime(deposit.dueBeforeUtc, zone)}` : ''}. {tr('Réglez l’acompte pour la confirmer ; il est déduit de l’addition et remboursé si vous annulez à temps.')}
            </p>
            {deposit.hasPendingPayment && <Notice>{tr('Paiement en cours de validation… Cette page se met à jour toute seule.')}</Notice>}
            {deposit.canPay && deposit.paymentOptions.length > 0 ? (
              <div className="space-y-2">
                {deposit.paymentOptions.flatMap(option => option.methods.map(method => (
                  <AccentButton key={`${option.provider}-${method}`} className="w-full" busy={busy} disabled={busy} onClick={() => void pay(option.provider, method)}>
                    {tr('Payer')} {money(deposit.amount, deposit.currencyCode)} · {methodLabel[method] ?? method} · {option.displayName}
                  </AccentButton>
                )))}
              </div>
            ) : !deposit.hasPendingPayment && <Notice tone="warning">{tr('Le paiement en ligne n’est pas disponible : appelez le restaurant pour régler l’acompte.')}</Notice>}
          </div>
        )}
        <div className="mt-6 space-y-3 rounded-3xl bg-white p-5 shadow-sm">
          <p className="flex items-center gap-3 text-lg font-semibold">
            {cancelled ? <CalendarX className="size-6 text-red-600" aria-hidden="true" /> : <CalendarCheck className="size-6 text-[var(--accent)]" aria-hidden="true" />}
            <span>{upperFirst(localDay(booking.startAtUtc, zone))}</span> {tr('à')} {localTime(booking.startAtUtc, zone)}
          </p>
          <p className="flex items-center gap-3 text-ink-600"><Users className="size-5" aria-hidden="true" />{booking.partySize} {tr('personne')}{booking.partySize > 1 ? 's' : ''} {tr('· au nom de')} {booking.guestName}</p>
          {booking.locationName && <p className="text-ink-600">{booking.locationName}</p>}
          <p className={cancelled ? 'font-semibold text-red-700' : booking.status === 'Pending' ? 'font-semibold text-amber-700' : 'font-semibold text-emerald-700'}>
            {deposit?.status === 'AwaitingPayment' && booking.status === 'Pending' ? tr('En attente de l’acompte') : statusLabel[booking.status]}
          </p>
          {deposit && deposit.status !== 'AwaitingPayment' && <p className="text-sm text-ink-600">{money(deposit.amount, deposit.currencyCode)} · {depositLabel[deposit.status]}</p>}
        </div>
        {actionError && <div className="mt-4"><Notice tone="error">{actionError}</Notice></div>}
        {booking.canCancel ? (
          <button type="button" onClick={() => void cancel()} disabled={busy} className="mt-4 w-full rounded-xl border border-red-200 bg-white px-4 py-3 text-sm font-semibold text-red-700 hover:bg-red-50 disabled:opacity-50">
            {tr('Annuler la réservation')}
          </button>
        ) : !cancelled && booking.cancelBeforeUtc && new Date(booking.cancelBeforeUtc) < new Date() && (
          <p className="mt-4 text-center text-sm text-ink-500">{tr('L’annulation en ligne n’est plus possible : appelez le restaurant.')}</p>
        )}
        {booking.canCancel && booking.cancelBeforeUtc && (
          <p className="mt-2 text-center text-xs text-ink-500">{tr('Annulation possible jusqu’au')} {localDay(booking.cancelBeforeUtc, zone)} {tr('à')} {localTime(booking.cancelBeforeUtc, zone)}.</p>
        )}
        <p className="mt-6 text-center text-sm"><Link href="/reserver" className="font-medium text-[var(--accent)] hover:underline">{tr('Faire une autre réservation')}</Link></p>
      </Shell>
    </div>
  );
}

function Shell({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl bg-slate-50 px-4 py-10">{children}</div>;
}

const upperFirst = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);
