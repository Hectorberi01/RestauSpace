'use client';
import {useCallback, useEffect, useState} from 'react';
import Link from 'next/link';
import {useSearchParams} from 'next/navigation';
import {CalendarCheck, CalendarX, Users, Wallet} from 'lucide-react';
import {absoluteUrl} from '@/lib/config';
import {goToCheckout, safeCheckoutUrl} from '@/lib/checkout';
import {clock, localDay, methodLabel, money} from '@/lib/format';
import {cancelBooking, errorMessage, loadBooking, payDeposit, type BookingStatus, type GuestBooking, type GuestDepositStatus} from '@/lib/storefront';
import {useSite} from '@/components/site-provider';
import {Button, Notice, PageShell, Spinner} from '@/components/ui';

const statusLabel: Record<BookingStatus, string> = {
  Pending: 'En attente de confirmation',
  Confirmed: 'Confirmée',
  Seated: 'Vous êtes installés',
  Completed: 'Terminée',
  Cancelled: 'Annulée',
  NoShow: 'Non honorée',
};

const depositLabel: Record<GuestDepositStatus, string> = {
  AwaitingPayment: 'Acompte à payer',
  Paid: 'Acompte payé : il sera déduit de l’addition.',
  Applied: 'Acompte déduit de l’addition.',
  RefundRequested: 'Remboursement de l’acompte en cours.',
  Refunded: 'Acompte remboursé.',
  Forfeited: 'Acompte conservé par le restaurant.',
  Waived: 'Pas d’acompte à payer.',
};

/** Une réservation par son lien de gestion : détail, acompte, annulation. */
export function ManageBooking() {
  const params = useSearchParams();
  const token = params.get('jeton') ?? '';
  const fresh = params.has('nouvelle');
  const {front} = useSite();
  const [booking, setBooking] = useState<GuestBooking | null>(null);
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
    return () => controller.abort();
  }, [token, load]);

  // Acompte en attente (retour du prestataire, demande sur le téléphone) : relu toutes les 5 secondes.
  const paying = booking?.deposit?.status === 'AwaitingPayment' && booking.status === 'Pending';
  useEffect(() => {
    if (!paying) return;
    const timer = window.setInterval(() => document.visibilityState === 'visible' && void load(), 5000);
    return () => window.clearInterval(timer);
  }, [paying, load]);

  const page = `/reservation/?jeton=${encodeURIComponent(token)}`;

  const pay = async (provider: string, method: string) => {
    setBusy(true);
    setActionError(null);
    try {
      const started = await payDeposit(token, provider, method, absoluteUrl(page));
      const checkout = safeCheckoutUrl(started?.checkoutUrl);
      if (checkout) goToCheckout(checkout, absoluteUrl(page));
      else await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  const cancel = async () => {
    if (!window.confirm('Annuler cette réservation ?')) return;
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

  if (!token) return <PageShell title="Ma réservation"><Notice tone="error">Lien de réservation incomplet.</Notice></PageShell>;
  if (error) return <PageShell title="Ma réservation"><Notice tone="error">{error}</Notice></PageShell>;
  if (!booking) return <PageShell title="Ma réservation"><Spinner /></PageShell>;

  const zone = front?.locations.find(item => item.id === booking.locationId)?.timeZoneId ?? 'Africa/Porto-Novo';
  const cancelled = booking.status === 'Cancelled';
  const deposit = booking.deposit ?? null;
  const awaitingDeposit = deposit?.status === 'AwaitingPayment' && booking.status === 'Pending';
  const title = fresh && !cancelled ? (awaitingDeposit ? 'Table retenue' : 'Table réservée !') : 'Ma réservation';

  return (
    <PageShell title={title} eyebrow={front?.restaurant.name}>
      {fresh && !cancelled && booking.status !== 'Pending' && <div className="mb-4"><Notice tone="success">Votre réservation est confirmée. Gardez cette page pour la retrouver ou l’annuler.</Notice></div>}
      {awaitingDeposit && deposit && (
        <div className="mb-4 space-y-3 rounded-[22px] border-2 border-primary/30 bg-white p-5">
          <p className="flex items-center gap-2 font-semibold"><Wallet className="size-5 text-primary" aria-hidden="true" />Acompte : {money(deposit.amount, deposit.currencyCode)}</p>
          <p className="text-sm text-muted">
            Votre table est retenue{deposit.dueBeforeUtc ? ` jusqu’à ${clock(deposit.dueBeforeUtc, zone)}` : ''}. Réglez l’acompte pour la confirmer ; il est déduit de l’addition et remboursé si vous annulez à temps.
          </p>
          {deposit.hasPendingPayment && <Notice>Paiement en cours de validation… Cette page se met à jour toute seule.</Notice>}
          {deposit.canPay && deposit.paymentOptions.length > 0 ? (
            <div className="space-y-2">
              {deposit.paymentOptions.flatMap(option => option.methods.map(method => (
                <Button key={`${option.provider}-${method}`} className="w-full" busy={busy} disabled={busy} onClick={() => void pay(option.provider, method)}>
                  Payer {money(deposit.amount, deposit.currencyCode)} · {methodLabel[method] ?? method} · {option.displayName}
                </Button>
              )))}
            </div>
          ) : !deposit.hasPendingPayment && <Notice tone="warning">Le paiement en ligne n’est pas disponible : appelez le restaurant pour régler l’acompte.</Notice>}
        </div>
      )}
      <div className="space-y-3 rounded-[22px] border border-line bg-white p-5">
        <p className="flex items-center gap-3 text-lg font-semibold">
          {cancelled ? <CalendarX className="size-6 text-red-600" aria-hidden="true" /> : <CalendarCheck className="size-6 text-primary" aria-hidden="true" />}
          {localDay(booking.startAtUtc, zone)} à {clock(booking.startAtUtc, zone)}
        </p>
        <p className="flex items-center gap-3 text-muted"><Users className="size-5" aria-hidden="true" />{booking.partySize} personne{booking.partySize > 1 ? 's' : ''} · au nom de {booking.guestName}</p>
        {booking.locationName && <p className="text-muted">{booking.locationName}</p>}
        <p className={cancelled ? 'font-semibold text-red-700' : booking.status === 'Pending' ? 'font-semibold text-amber-800' : 'font-semibold text-leaf'}>
          {awaitingDeposit ? 'En attente de l’acompte' : statusLabel[booking.status]}
        </p>
        {deposit && deposit.status !== 'AwaitingPayment' && <p className="text-sm text-muted">{money(deposit.amount, deposit.currencyCode)} · {depositLabel[deposit.status]}</p>}
      </div>
      {actionError && <div className="mt-4"><Notice tone="error">{actionError}</Notice></div>}
      {booking.canCancel ? (
        <>
          <Button variant="danger" className="mt-4 w-full" disabled={busy} onClick={() => void cancel()}>Annuler la réservation</Button>
          {booking.cancelBeforeUtc && <p className="mt-2 text-center text-xs text-muted">Annulation possible jusqu’au {localDay(booking.cancelBeforeUtc, zone).toLowerCase()} à {clock(booking.cancelBeforeUtc, zone)}.</p>}
        </>
      ) : !cancelled && booking.cancelBeforeUtc && new Date(booking.cancelBeforeUtc) < new Date() && (
        <p className="mt-4 text-center text-sm text-muted">L’annulation en ligne n’est plus possible : appelez le restaurant.</p>
      )}
      <p className="mt-6 text-center text-sm"><Link href="/reserver/" className="font-semibold text-primary hover:underline">Faire une autre réservation</Link></p>
    </PageShell>
  );
}
