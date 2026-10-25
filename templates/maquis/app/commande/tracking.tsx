'use client';
import {useCallback, useEffect, useState} from 'react';
import Link from 'next/link';
import {useSearchParams} from 'next/navigation';
import {Bike, Check, ChefHat, Clock, PackageCheck, Smartphone, Star, XCircle} from 'lucide-react';
import {absoluteUrl, config} from '@/lib/config';
import {goToCheckout, safeCheckoutUrl} from '@/lib/checkout';
import {clock, deliveryStatusLabel, methodLabel, money} from '@/lib/format';
import {componentLabel, errorMessage, sendReview, startOrderPayment, storefront, type GuestOrder, type GuestOrderStatus} from '@/lib/storefront';
import {useSite} from '@/components/site-provider';
import {Button, ButtonLink, Notice, PageShell, Spinner, cx, inputClass} from '@/components/ui';

const steps: {status: GuestOrderStatus; label: string}[] = [
  {status: 'Received', label: 'Reçue'},
  {status: 'Accepted', label: 'Acceptée'},
  {status: 'Preparing', label: 'En préparation'},
  {status: 'Ready', label: 'Prête'},
  {status: 'Completed', label: 'Terminée'},
];

const headline: Record<GuestOrderStatus, string> = {
  Received: 'Commande reçue',
  Accepted: 'Commande acceptée',
  Preparing: 'En préparation',
  Ready: 'Votre commande est prête',
  Completed: 'Commande remise. Bon appétit !',
  Cancelled: 'Commande annulée',
};

export function Tracking() {
  const params = useSearchParams();
  const token = params.get('jeton') ?? '';
  const returnedFromPayment = params.has('paiement');
  const pushedToPhone = params.has('telephone');
  const {front} = useSite();
  const [order, setOrder] = useState<GuestOrder | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await storefront<GuestOrder>(`/orders/${encodeURIComponent(token)}`, {signal});
      if (result) setOrder(result);
      setError(null);
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

  // Tant que la commande vit, elle est relue toutes les 5 secondes.
  const live = order && order.status !== 'Completed' && order.status !== 'Cancelled';
  useEffect(() => {
    if (!live) return;
    const timer = window.setInterval(() => document.visibilityState === 'visible' && void load(), 5000);
    return () => window.clearInterval(timer);
  }, [live, load]);

  const tracking = `/commande/?jeton=${encodeURIComponent(token)}`;

  const pay = async (provider: string, method: string) => {
    setBusy(`${provider}-${method}`);
    setActionError(null);
    try {
      const started = await startOrderPayment(token, {provider, method, payerPhone: null, returnUrl: absoluteUrl(tracking)});
      const checkout = safeCheckoutUrl(started?.checkoutUrl);
      if (checkout) goToCheckout(checkout, absoluteUrl(tracking));
      else await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  };

  const cancel = async () => {
    if (!window.confirm('Annuler cette commande ?')) return;
    setBusy('cancel');
    setActionError(null);
    try {
      await storefront(`/orders/${encodeURIComponent(token)}/cancel`, {method: 'POST'});
      await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  };

  if (!token) return <PageShell title="Suivi de commande"><Notice tone="error">Lien de suivi incomplet.</Notice></PageShell>;
  if (error && !order) return <PageShell title="Suivi de commande"><Notice tone="error">{error}</Notice><Button variant="ghost" className="mt-4" onClick={() => void load()}>Réessayer</Button></PageShell>;
  if (!order) return <PageShell title="Suivi de commande"><Spinner /></PageShell>;

  const cancelled = order.status === 'Cancelled';
  const reached = steps.findIndex(step => step.status === order.status);
  const zone = front?.locations.find(item => item.id === order.locationId)?.timeZoneId;
  const waitingPayment = order.paymentState === 'AwaitingPayment' || order.paymentState === 'Processing';

  return (
    <PageShell title={headline[order.status]} eyebrow={order.locationName}>
      <div className="rounded-[22px] border border-line bg-white p-6">
        <p className="text-sm text-muted">Numéro de commande</p>
        <p className="font-serif text-4xl font-bold tracking-wide text-primary">{order.number}</p>
        {order.estimatedReadyAtUtc && !cancelled && order.status !== 'Completed' && (
          <p className="mt-3 flex items-center gap-2 text-sm text-muted"><Clock className="size-4" aria-hidden="true" />
            {order.delivery
              ? `${order.scheduledForUtc ? 'Livraison prévue' : 'Livraison estimée'} vers ${clock(order.estimatedReadyAtUtc, zone)}`
              : order.scheduledForUtc ? `Prévue pour ${clock(order.scheduledForUtc, zone)}` : `Prête vers ${clock(order.estimatedReadyAtUtc, zone)}`}
          </p>
        )}
        {order.delivery && !cancelled && (
          <p className="mt-2 flex items-center gap-2 text-sm font-medium"><Bike className="size-4" aria-hidden="true" />{deliveryStatusLabel[order.delivery.status]} · {order.delivery.zoneName}</p>
        )}

        {!cancelled && (
          <ol className="mt-6 grid grid-cols-5 gap-1" aria-label="Avancement de la commande">
            {steps.map((step, index) => (
              <li key={step.status} className="text-center" aria-current={index === reached ? 'step' : undefined}>
                <span className={cx('mx-auto grid size-9 place-items-center rounded-full text-white transition', index <= reached ? 'bg-primary' : 'bg-line')}>
                  {index === 2 ? <ChefHat className="size-4" aria-hidden="true" /> : index === 3 ? <PackageCheck className="size-4" aria-hidden="true" /> : <Check className="size-4" aria-hidden="true" />}
                </span>
                <span className={cx('mt-1 block text-[11px] leading-tight sm:text-xs', index <= reached ? 'font-semibold' : 'text-muted')}>{step.label}</span>
              </li>
            ))}
          </ol>
        )}
        {cancelled && <p className="mt-4 flex items-center gap-2 text-sm text-red-700"><XCircle className="size-4" aria-hidden="true" />Cette commande ne sera pas préparée.</p>}
        {live && <p className="mt-4 text-xs text-muted" aria-live="polite">Cette page se met à jour toute seule.</p>}
      </div>

      <div className="mt-4 space-y-3">
        {order.status === 'Received' && order.acceptBeforeUtc && !waitingPayment && <Notice>Le restaurant confirme votre commande d’ici {clock(order.acceptBeforeUtc, zone)}.</Notice>}
        {order.paymentState === 'Processing' && (
          <Notice>{pushedToPhone ? <span className="flex items-center gap-2"><Smartphone className="size-4" aria-hidden="true" />Confirmez le paiement sur votre téléphone.</span> : 'Paiement en cours de confirmation…'}</Notice>
        )}
        {order.paymentState === 'AwaitingPayment' && returnedFromPayment && <Notice tone="warning">Le paiement n’a pas abouti. Vous pouvez réessayer.</Notice>}
        {order.paymentState === 'Paid' && <Notice tone="success">Payée en ligne : {money(order.amountPaid, order.currencyCode)}.</Notice>}
        {order.paymentState === 'Refunded' && <Notice>Votre paiement est remboursé.</Notice>}
        {order.amountDueAtCounter > 0 && !cancelled && <Notice>{order.delivery ? 'À payer à la livraison' : 'À payer au comptoir'} : <strong>{money(order.amountDueAtCounter, order.currencyCode)}</strong></Notice>}
        {actionError && <Notice tone="error">{actionError}</Notice>}

        {order.canPay && (
          <div className="rounded-[22px] border border-line bg-white p-5">
            <h2 className="font-serif text-xl font-bold">Payer {money(order.total, order.currencyCode)}</h2>
            {order.payBeforeUtc && <p className="mt-1 text-sm text-muted">Sans paiement avant {clock(order.payBeforeUtc, zone)}, la commande est annulée.</p>}
            <div className="mt-3 space-y-2">
              {order.paymentOptions.flatMap(option => option.methods.map(method => (
                <Button key={`${option.provider}-${method}`} className="w-full" busy={busy === `${option.provider}-${method}`} disabled={!!busy} onClick={() => void pay(option.provider, method)}>
                  {methodLabel[method] ?? method} · {option.displayName}
                </Button>
              )))}
            </div>
          </div>
        )}
      </div>

      {order.status === 'Completed' && <ReviewForm token={token} />}

      <div className="mt-4 rounded-[22px] border border-line bg-white p-5">
        <h2 className="font-serif text-xl font-bold">Détail</h2>
        <ul className="mt-2 divide-y divide-line text-sm">
          {order.lines.map((line, index) => (
            <li key={index} className="flex justify-between gap-3 py-2">
              <span>
                {line.quantity} × {line.name}{line.variantName ? ` · ${line.variantName}` : ''}
                {line.components?.map((component, position) => <span key={position} className="block text-muted">{component.stepName} : {componentLabel(component)}</span>)}
                {line.modifiers.length > 0 && <span className="block text-muted">{line.modifiers.map(modifier => modifier.name).join(', ')}</span>}
              </span>
              <span className="font-medium">{money(line.lineTotal, order.currencyCode)}</span>
            </li>
          ))}
        </ul>
        {order.discountTotal > 0 && <p className="mt-2 flex justify-between border-t border-line pt-2 text-sm text-leaf"><span>Remise</span><span>−{money(order.discountTotal, order.currencyCode)}</span></p>}
        {order.delivery && <p className="mt-1 flex justify-between text-sm text-muted"><span>Livraison · {order.delivery.zoneName}</span><span>{order.delivery.fee > 0 ? money(order.delivery.fee, order.currencyCode) : 'Offerte'}</span></p>}
        <p className="mt-2 flex justify-between border-t border-line pt-2 font-bold"><span>Total</span><span>{money(order.total, order.currencyCode)}</span></p>
      </div>

      {order.canCancel && (
        <Button variant="danger" className="mt-4 w-full" busy={busy === 'cancel'} disabled={!!busy} onClick={() => void cancel()}>Annuler la commande</Button>
      )}
      <div className="mt-6 text-center">
        <ButtonLink variant="ghost" href={`/carte/?etablissement=${order.locationId}`}>Commander à nouveau</ButtonLink>
      </div>
    </PageShell>
  );
}

/** Avis après une commande remise (POST /reviews). */
function ReviewForm({token}: {token: string}) {
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (done) return <div className="mt-4"><Notice tone="success">Merci pour votre avis !</Notice></div>;

  return (
    <form className="mt-4 rounded-[22px] border border-line bg-white p-5" onSubmit={async event => {
      event.preventDefault();
      setBusy(true);
      setError(null);
      try {
        await sendReview(token, rating, comment.trim() || null);
        setDone(true);
      } catch (reason) {
        setError(errorMessage(reason));
      } finally {
        setBusy(false);
      }
    }}>
      <h2 className="font-serif text-xl font-bold">Votre avis compte</h2>
      <div role="radiogroup" aria-label="Note sur 5" className="mt-2 flex gap-1">
        {[1, 2, 3, 4, 5].map(value => (
          <button key={value} type="button" role="radio" aria-checked={rating === value} aria-label={`${value} sur 5`} onClick={() => setRating(value)}
            className="grid size-11 place-items-center rounded-lg text-accent hover:bg-sand">
            <Star className={cx('size-7', value <= rating ? 'fill-current' : 'opacity-40')} aria-hidden="true" />
          </button>
        ))}
      </div>
      <label className="mt-3 block text-sm">
        <span className="mb-1 block font-medium">Un commentaire ? <span className="font-normal text-muted">(facultatif)</span></span>
        <textarea value={comment} maxLength={2000} rows={3} onChange={event => setComment(event.target.value)} className={inputClass} />
      </label>
      <p className="mt-2 text-xs text-muted">Votre avis pourra être publié sur notre site, sans votre nom.</p>
      {error && <div className="mt-3"><Notice tone="error">{error}</Notice></div>}
      <Button type="submit" className="mt-3" busy={busy} disabled={rating === 0}>Envoyer mon avis</Button>
    </form>
  );
}
