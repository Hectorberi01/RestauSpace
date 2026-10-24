'use client';
import {useCallback, useEffect, useState} from 'react';
import Link from 'next/link';
import {useSearchParams} from 'next/navigation';
import {Bike, Check, ChefHat, Clock, MessageSquareHeart, PackageCheck, Smartphone, XCircle} from 'lucide-react';
import {
  accentOf,
  answeredSurveys,
  clock,
  errorMessage,
  methodLabel,
  deliveryStatusLabel,
  money,
  componentLabel,
  loadSurvey,
  newIdempotencyKey,
  storefront,
  type GuestOrder,
  type GuestOrderStatus,
  type Storefront,
  type Survey,
} from '@/lib/storefront';
import {AccentButton, cx, Notice, Spinner} from '@/components/order-ui';
import {tr, translated, useLang} from '@/lib/i18n';
import {safeCheckoutUrl} from '@/lib/checkout';

const steps: {status: GuestOrderStatus; label: string}[] = [
  {status: 'Received', label: 'Reçue'},
  {status: 'Accepted', label: 'Acceptée'},
  {status: 'Preparing', label: 'En préparation'},
  {status: 'Ready', label: 'Prête'},
  {status: 'Completed', label: 'Terminée'},
];

const headline: Record<GuestOrderStatus, string> = translated({
  Received: 'Commande reçue',
  Accepted: 'Commande acceptée',
  Preparing: 'En préparation',
  Ready: 'Votre commande est prête',
  Completed: 'Commande remise. Bon appétit !',
  Cancelled: 'Commande annulée',
});

export function Tracking() {
  useLang();
  const params = useSearchParams();
  const token = params.get('jeton') ?? '';
  const returnedFromPayment = params.has('paiement');
  const pushedToPhone = params.has('telephone');
  const [order, setOrder] = useState<GuestOrder | null>(null);
  const [accent, setAccent] = useState('#5b3df5');
  const [locations, setLocations] = useState<Storefront['locations']>([]);
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
    storefront<Storefront>('/', {signal: controller.signal}).then(front => {
      if (!front) return;
      setAccent(accentOf(front.restaurant.primaryColor));
      setLocations(front.locations);
    }).catch(() => {});
    return () => controller.abort();
  }, [token, load]);

  // Commande remise : le questionnaire du restaurant est proposé, sauf s'il a déjà été rempli pour elle.
  const [survey, setSurvey] = useState<Survey | null>(null);
  const completed = order?.status === 'Completed';
  useEffect(() => {
    if (!completed) return;
    const controller = new AbortController();
    loadSurvey(null, controller.signal)
      .then(found => found && found.isOpen && !answeredSurveys().includes(`${found.id}:${token}`) && setSurvey(found))
      .catch(() => {});
    return () => controller.abort();
  }, [completed, token]);

  // Tant que la commande vit, elle est relue toutes les 5 secondes (page visible seulement).
  const live = order && order.status !== 'Completed' && order.status !== 'Cancelled';
  useEffect(() => {
    if (!live) return;
    const timer = window.setInterval(() => document.visibilityState === 'visible' && void load(), 5000);
    return () => window.clearInterval(timer);
  }, [live, load]);

  const pay = async (provider: string, method: string) => {
    setBusy(`${provider}-${method}`);
    setActionError(null);
    try {
      const started = await storefront<{paymentId: string; checkoutUrl: string | null}>(`/orders/${encodeURIComponent(token)}/payments`, {
        method: 'POST',
        idempotencyKey: newIdempotencyKey(),
        body: {provider, method, returnUrl: `${window.location.origin}/commande?jeton=${encodeURIComponent(token)}`},
      });
      const checkout = safeCheckoutUrl(started?.checkoutUrl);
      if (checkout) window.location.assign(checkout);
      else await load();
    } catch (reason) {
      setActionError(errorMessage(reason));
    } finally {
      setBusy(null);
    }
  };

  const cancel = async () => {
    if (!window.confirm(tr('Annuler cette commande ?'))) return;
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

  if (!token) return <Shell><Notice tone="error">{tr('Lien de suivi incomplet.')}</Notice></Shell>;
  if (error && !order) return <Shell><Notice tone="error">{error}</Notice></Shell>;
  if (!order) return <Shell><Spinner /></Shell>;

  const cancelled = order.status === 'Cancelled';
  const reached = steps.findIndex(step => step.status === order.status);
  // Heures dans le fuseau de l'établissement (navigateur tant que la vitrine n'est pas chargée).
  const zone = locations.find(item => item.id === order.locationId)?.timeZoneId;
  const waitingPayment = order.paymentState === 'AwaitingPayment' || order.paymentState === 'Processing';

  return (
    <div style={{'--accent': accent} as React.CSSProperties}>
      <Shell>
        <div className="rounded-3xl bg-white p-6 shadow-sm">
          <p className="text-sm text-ink-500">{order.locationName}</p>
          <h1 className="mt-1 text-2xl font-bold">{headline[order.status]}</h1>
          <p className="mt-4 text-sm text-ink-500">{tr('Numéro de commande')}</p>
          <p className="text-4xl font-black tracking-wide text-[var(--accent)]">{order.number}</p>
          {order.estimatedReadyAtUtc && !cancelled && (
            <p className="mt-3 flex items-center gap-2 text-sm text-ink-600"><Clock className="size-4" aria-hidden="true" />
              {order.delivery
                ? `${order.scheduledForUtc ? tr('Livraison prévue') : tr('Livraison estimée')} vers ${clock(order.estimatedReadyAtUtc, zone)}`
                : order.scheduledForUtc ? tr('Prévue pour {p0}', {p0: clock(order.scheduledForUtc, zone)}) : tr('Prête vers {p0}', {p0: clock(order.estimatedReadyAtUtc, zone)})}
            </p>
          )}
          {order.delivery && !cancelled && (
            <p className="mt-2 flex items-center gap-2 text-sm font-medium text-ink-800">
              <Bike className="size-4" aria-hidden="true" />{deliveryStatusLabel[order.delivery.status]} · {order.delivery.zoneName}
            </p>
          )}

          {!cancelled && (
            <ol className="mt-6 grid grid-cols-5 gap-1" aria-label={tr('Avancement')}>
              {steps.map((step, index) => (
                <li key={step.status} className="text-center">
                  <span className={cx('mx-auto grid size-8 place-items-center rounded-full text-white', index <= reached ? 'bg-[var(--accent)]' : 'bg-slate-200')}>
                    {index === 2 ? <ChefHat className="size-4" /> : index === 3 ? <PackageCheck className="size-4" /> : <Check className="size-4" />}
                  </span>
                  <span className={cx('mt-1 block text-[11px] leading-tight', index <= reached ? 'font-semibold text-ink-900' : 'text-ink-500')}>{tr(step.label)}</span>
                </li>
              ))}
            </ol>
          )}
          {cancelled && <p className="mt-4 flex items-center gap-2 text-sm text-red-700"><XCircle className="size-4" aria-hidden="true" />{tr('Cette commande ne sera pas préparée.')}</p>}
        </div>

        <div className="mt-4 space-y-3">
          {order.status === 'Received' && order.acceptBeforeUtc && !waitingPayment && (
            <Notice>{tr('Le restaurant confirme votre commande d’ici')} {clock(order.acceptBeforeUtc, zone)}.</Notice>
          )}
          {order.paymentState === 'Processing' && (
            <Notice>{pushedToPhone ? <span className="flex items-center gap-2"><Smartphone className="size-4" />{tr('Confirmez le paiement sur votre téléphone.')}</span> : tr('Paiement en cours de confirmation…')}</Notice>
          )}
          {order.paymentState === 'AwaitingPayment' && returnedFromPayment && <Notice tone="warning">{tr('Le paiement n’a pas abouti. Vous pouvez réessayer.')}</Notice>}
          {order.paymentState === 'Paid' && <Notice tone="success">{tr('Payée en ligne :')} {money(order.amountPaid, order.currencyCode)}.</Notice>}
          {order.paymentState === 'Refunded' && <Notice tone="info">{tr('Votre paiement est remboursé.')}</Notice>}
          {order.amountDueAtCounter > 0 && <Notice>{order.delivery ? tr('À payer à la livraison') : tr('À payer au comptoir')} : <strong>{money(order.amountDueAtCounter, order.currencyCode)}</strong></Notice>}
          {actionError && <Notice tone="error">{actionError}</Notice>}

          {order.canPay && (
            <div className="rounded-3xl bg-white p-5 shadow-sm">
              <h2 className="font-semibold">{tr('Payer')} {money(order.total, order.currencyCode)}</h2>
              {order.payBeforeUtc && <p className="mt-1 text-sm text-ink-500">{tr('Sans paiement avant')} {clock(order.payBeforeUtc, zone)}{tr(', la commande est annulée.')}</p>}
              <div className="mt-3 space-y-2">
                {order.paymentOptions.flatMap(option => option.methods.map(method => (
                  <AccentButton key={`${option.provider}-${method}`} className="w-full" busy={busy === `${option.provider}-${method}`} disabled={!!busy}
                    onClick={() => void pay(option.provider, method)}>
                    {methodLabel[method] ?? method} · {option.displayName}
                  </AccentButton>
                )))}
              </div>
            </div>
          )}
        </div>

        {survey && (
          <Link href={`/questionnaire/${survey.id}?jeton=${encodeURIComponent(token)}`}
            className="mt-4 flex items-center gap-4 rounded-3xl bg-white p-5 shadow-sm ring-1 ring-[var(--accent)]/30 hover:bg-slate-50">
            <MessageSquareHeart className="size-8 shrink-0 text-[var(--accent)]" aria-hidden="true" />
            <span>
              <span className="block font-semibold">{tr('Votre avis compte')}</span>
              <span className="block text-sm text-ink-500">{survey.title} {tr('· moins d’une minute, anonyme')}</span>
            </span>
          </Link>
        )}

        <div className="mt-4 rounded-3xl bg-white p-5 shadow-sm">
          <h2 className="font-semibold">{tr('Détail')}</h2>
          <ul className="mt-2 divide-y divide-slate-100 text-sm">
            {order.lines.map((line, index) => (
              <li key={index} className="flex justify-between gap-3 py-2">
                <span>
                  {line.quantity} {tr('×')} {line.name}{line.variantName ? ` · ${line.variantName}` : ''}
                  {line.components?.map((component, position) => <span key={position} className="block text-ink-600">{component.stepName} : {componentLabel(component)}</span>)}
                  {line.modifiers.length > 0 && <span className="block text-ink-500">{line.modifiers.map(modifier => modifier.name).join(', ')}</span>}
                </span>
                <span className="font-medium">{money(line.lineTotal, order.currencyCode)}</span>
              </li>
            ))}
          </ul>
          {order.discountTotal > 0 && (
            <p className="mt-2 flex justify-between border-t border-slate-100 pt-2 text-sm text-emerald-700"><span>{tr('Remise')}</span><span>−{money(order.discountTotal, order.currencyCode)}</span></p>
          )}
          {order.delivery && (
            <p className="mt-1 flex justify-between text-sm text-ink-600"><span>{tr('Livraison ·')} {order.delivery.zoneName}</span><span>{order.delivery.fee > 0 ? money(order.delivery.fee, order.currencyCode) : tr('Offerte')}</span></p>
          )}
          <p className="mt-2 flex justify-between border-t border-slate-100 pt-2 font-bold"><span>{tr('Total')}</span><span>{money(order.total, order.currencyCode)}</span></p>
        </div>

        {order.canCancel && (
          <button type="button" onClick={() => void cancel()} disabled={!!busy} className="mt-4 w-full rounded-xl border border-red-200 bg-white px-4 py-3 text-sm font-semibold text-red-700 hover:bg-red-50 disabled:opacity-50">
            {tr('Annuler la commande')}
          </button>
        )}
        <p className="mt-6 text-center text-sm">
          <Link href={`/commander/${order.locationId}`} className="font-medium text-[var(--accent)] hover:underline">{tr('Commander à nouveau')}</Link>
        </p>
      </Shell>
    </div>
  );
}

function Shell({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl bg-slate-50 px-4 py-10">{children}</div>;
}
