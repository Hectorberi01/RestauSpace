'use client';
import {useEffect, useRef, useState} from 'react';
import {usePathname, useRouter, useSearchParams} from 'next/navigation';
import {CalendarClock, Check, CreditCard, ExternalLink, FileText, Globe, Loader2, MonitorSmartphone, Plus, Receipt, Smartphone, Sparkles, X} from 'lucide-react';
import {openInvoice} from '@/lib/invoice';
import {money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  cancelAddOn, changeAddOnQuantity, changePlan, featureLabel, isCounted, isMonthly, listAddOns, listPayments, listPlans, loadOnlinePayment,
  loadSubscription, methodName, providerName, pushesToPhone, refreshCheckout, scheduleCancellation, startCheckout, statusLabel, statusTone, subscribe,
  subscribeAddOn, suspensionLabel, unscheduleCancellation, type Charge, type Checkout, type MyAddOns, type MySubscription, type OnlinePayment, type PaymentOption,
  type Plan, type TenantAddOn,
} from '@/lib/billing';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Field, Input, Modal, Spinner, cx, useAction, useToast} from '@/components/ui';

const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'long', year: 'numeric'}).format(new Date(iso));
const count = (value: number) => new Intl.NumberFormat('fr-FR').format(value);
const per = (plan: Plan) => (plan.billingInterval === 'Yearly' ? 'an' : 'mois');

async function mine(signal: AbortSignal): Promise<MySubscription | null> {
  try {
    return await loadSubscription(signal);
  } catch (error) {
    if ((error as {status?: number}).status === 404) return null;
    throw error;
  }
}

function Usage({data}: {data: MySubscription}) {
  const {locations} = useConsole();
  const rows = data.usage.filter(item => item.enabled && item.limit !== null || item.featureCode.endsWith('.count') || isMonthly(item.featureCode));
  if (rows.length === 0) return null;
  return (
    <Card className="p-5">
      <h2 className="mb-1 font-semibold">Utilisation</h2>
      <p className="mb-4 text-sm text-ink-500">Les quotas mensuels repartent à zéro le 1er de chaque mois.</p>
      <ul className="space-y-4">
        {rows.map(item => {
          const used = item.featureCode === 'locations.count' ? locations.length : item.used;
          const known = isMonthly(item.featureCode) || item.featureCode === 'locations.count';
          const ratio = item.limit ? Math.min(1, used / item.limit) : 0;
          return (
            <li key={item.featureCode} className="text-sm">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="font-medium">{featureLabel(item.featureCode)}</span>
                <span className="text-ink-600 tabular-nums">
                  {!item.enabled ? 'Non inclus' : item.limit === null ? (known ? `${count(used)} · illimité` : 'Illimité') : known ? `${count(used)} / ${count(item.limit)}` : `${count(item.limit)} max.`}
                </span>
              </div>
              {item.enabled && item.limit !== null && known && (
                <div className="h-2 overflow-hidden rounded-full bg-slate-100" aria-hidden="true">
                  <div className={cx('h-full rounded-full', ratio >= 1 ? 'bg-red-500' : ratio >= 0.8 ? 'bg-amber-500' : 'bg-brand-500')} style={{width: `${ratio * 100}%`}} />
                </div>
              )}
              {item.enabled && item.limit !== null && known && ratio >= 0.8 && (
                <p className={cx('mt-1 text-xs', ratio >= 1 ? 'text-red-700' : 'text-amber-700')}>{ratio >= 1 ? 'Quota atteint : passez à un plan supérieur.' : 'Bientôt atteint.'}</p>
              )}
            </li>
          );
        })}
      </ul>
    </Card>
  );
}

const addMonths = (iso: string, months: number) => {
  const date = new Date(iso);
  date.setUTCMonth(date.getUTCMonth() + months);
  return date;
};

function Plans({current, plans, canChange, freeAllowed, onChoose}: {current: MySubscription | null; plans: Plan[]; canChange: boolean; freeAllowed: boolean; onChoose: (plan: Plan) => void}) {
  const codes = [...new Set(plans.flatMap(plan => plan.entitlements.map(item => item.featureCode)))].sort();
  return (
    <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
      {plans.map(plan => {
        const isCurrent = current?.plan.id === plan.id;
        return (
          <Card key={plan.id} className={cx('flex flex-col p-5', isCurrent && 'border-brand-500 ring-1 ring-brand-500')}>
            <div className="flex items-start justify-between gap-2">
              <p className="text-lg font-semibold">{plan.name}</p>
              {isCurrent && <Badge tone="brand">Plan actuel</Badge>}
            </div>
            <p className="mt-2"><span className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{money(plan.price, plan.currencyCode)}</span><span className="text-ink-500"> / {per(plan)}</span></p>
            <ul className="mt-4 flex-1 space-y-1.5 text-sm">
              {codes.map(code => {
                const item = plan.entitlements.find(entry => entry.featureCode === code);
                const enabled = item?.enabled ?? false;
                return (
                  <li key={code} className={cx('flex items-start gap-2', !enabled && 'text-ink-400')}>
                    {enabled ? <Check className="mt-0.5 size-4 shrink-0 text-emerald-600" aria-hidden="true" /> : <X className="mt-0.5 size-4 shrink-0" aria-hidden="true" />}
                    <span>{featureLabel(code)}{enabled && item?.limit !== null && item?.limit !== undefined ? ` : ${count(item.limit)}` : enabled && (isMonthly(code) || code.endsWith('.count')) ? ' : illimité' : ''}</span>
                  </li>
                );
              })}
            </ul>
            {plan.price === 0 && <p className="mt-3 text-xs text-ink-500">Un mois, une seule fois, au début de l’abonnement.</p>}
            {canChange && !isCurrent && (plan.price > 0 || freeAllowed) && (
              <Button className="mt-5" variant={current ? 'secondary' : 'primary'} onClick={() => onChoose(plan)}>{current ? 'Passer à ce plan' : 'Choisir ce plan'}</Button>
            )}
          </Card>
        );
      })}
    </div>
  );
}

const optionIcon = (plan: Plan) =>
  plan.entitlements.some(item => item.featureCode === 'storefront.kiosk') ? MonitorSmartphone
    : plan.entitlements.some(item => item.featureCode === 'storefront.mobile_app') ? Smartphone : Globe;

const optionHint = (plan: Plan) => plan.entitlements.filter(item => item.enabled).map(item => featureLabel(item.featureCode)).join(' · ');

function NextCharge({charge}: {charge: Charge}) {
  if (charge.lines.length < 2) return null;
  return (
    <div className="mt-5 border-t border-slate-100 pt-4 text-sm">
      <p className="mb-2 font-medium">Prochaine période : plan et options</p>
      <ul className="space-y-1">
        {charge.lines.map(line => (
          <li key={line.planId} className="flex justify-between gap-3">
            <span className="text-ink-600">{line.label}{line.quantity > 1 ? ` × ${line.quantity}` : ''}</span>
            <span className="tabular-nums">{money(line.amount, charge.currencyCode)}</span>
          </li>
        ))}
        <li className="flex justify-between gap-3 border-t border-slate-100 pt-1 font-semibold">
          <span>Total / {charge.billingInterval === 'Yearly' ? 'an' : 'mois'}</span>
          <span className="tabular-nums">{money(charge.total, charge.currencyCode)}</span>
        </li>
      </ul>
    </div>
  );
}

type OptionAction = {kind: 'subscribe'; plan: Plan} | {kind: 'quantity'; addOn: TenantAddOn} | {kind: 'cancel'; addOn: TenantAddOn};

function Options({data, canChange, onChanged}: {data: MyAddOns; canChange: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [action, setAction] = useState<OptionAction | null>(null);
  const [quantity, setQuantity] = useState('1');
  // Ordre du catalogue ; une option active qui n'est plus proposée reste affichée à la fin.
  const plans = [...data.offered, ...data.active.map(item => item.addOn).filter(plan => !data.offered.some(offered => offered.id === plan.id))];
  if (plans.length === 0) return null;
  const open = (next: OptionAction) => {
    setQuantity(next.kind === 'quantity' ? String(next.addOn.quantity) : '1');
    setAction(next);
  };
  const amount = Math.max(1, Math.min(1000, Math.floor(Number(quantity) || 0)));
  const target = action?.kind === 'subscribe' ? action.plan : action?.addOn.addOn;
  const counted = target ? isCounted(target) : false;

  return (
    <section id="options" className="scroll-mt-24 space-y-3">
      <div>
        <h2 className="text-lg font-semibold">Options</h2>
        <p className="text-sm text-ink-500">Vente en ligne et bornes : actives tout de suite, facturées avec votre abonnement à la prochaine échéance.</p>
      </div>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
        {plans.map(plan => {
          const active = data.active.find(item => item.addOn.id === plan.id);
          const Icon = optionIcon(plan);
          return (
            <Card key={plan.id} className={cx('flex flex-col p-5', active && 'border-emerald-300')}>
              <div className="flex items-start gap-3">
                <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-700" aria-hidden="true"><Icon className="size-5" /></span>
                <div className="min-w-0 flex-1">
                  <p className="flex flex-wrap items-center gap-2 font-semibold">{plan.name}{active && <Badge tone="green">Active</Badge>}</p>
                  <p className="text-sm text-ink-500">{optionHint(plan)}</p>
                </div>
              </div>
              <p className="mt-3"><span className="text-2xl font-bold tracking-tight">{money(plan.price, plan.currencyCode)}</span><span className="text-ink-500"> / {per(plan)}{isCounted(plan) ? ' par unité' : ''}</span></p>
              {active && isCounted(plan) && <p className="mt-1 text-sm text-ink-600">Quantité : <strong>{count(active.quantity)}</strong> · {money(plan.price * active.quantity, plan.currencyCode)} / {per(plan)}</p>}
              <div className="mt-4 flex flex-1 flex-wrap items-end gap-2">
                {canChange && !active && <Button size="sm" onClick={() => open({kind: 'subscribe', plan})}><Plus className="size-4" aria-hidden="true" />Souscrire</Button>}
                {canChange && active && isCounted(plan) && <Button size="sm" variant="secondary" onClick={() => open({kind: 'quantity', addOn: active})}>Modifier la quantité</Button>}
                {canChange && active && <Button size="sm" variant="ghost" className="text-red-600" onClick={() => open({kind: 'cancel', addOn: active})}>Résilier</Button>}
              </div>
            </Card>
          );
        })}
      </div>

      <Modal open={action !== null} onClose={() => setAction(null)} title={
        action?.kind === 'subscribe' ? `Souscrire l’option ${action.plan.name}` : action?.kind === 'quantity' ? `Quantité : ${action.addOn.addOn.name}` : `Résilier l’option ${action?.addOn.addOn.name ?? ''}`
      } footer={
        <>
          <Button variant="secondary" onClick={() => setAction(null)}>Annuler</Button>
          {action?.kind === 'cancel' ? (
            <Button variant="danger" busy={busy === 'option'} onClick={() => run('option', async () => {
              await cancelAddOn(action.addOn.id);
              setAction(null);
              await onChanged();
            }, 'Option résiliée.')}>Résilier l’option</Button>
          ) : (
            <Button busy={busy === 'option'} onClick={() => action && run('option', async () => {
              if (action.kind === 'subscribe') await subscribeAddOn(action.plan.id, counted ? amount : 1);
              else if (action.kind === 'quantity') await changeAddOnQuantity(action.addOn.id, amount);
              setAction(null);
              await onChanged();
            }, action.kind === 'subscribe' ? 'Option activée.' : 'Quantité modifiée.')}>Confirmer</Button>
          )}
        </>
      }>
        {action && target && (
          <div className="space-y-3 text-sm text-ink-700">
            {action.kind === 'cancel' ? (
              <p>L’option cesse aussitôt : les canaux qu’elle ouvre sont fermés ({optionHint(target)}). Elle n’est plus facturée à la prochaine échéance. Vous pourrez la souscrire de nouveau.</p>
            ) : (
              <>
                {counted && (
                  <Field label="Quantité" hint="Par exemple le nombre de bornes.">
                    <Input type="number" inputMode="numeric" min={1} max={1000} step={1} value={quantity} onChange={event => setQuantity(event.target.value)} />
                  </Field>
                )}
                <p><strong>{target.name}</strong> : {money(target.price * (counted ? amount : 1), target.currencyCode)} / {per(target)}{counted && amount > 1 ? ` (${count(amount)} × ${money(target.price, target.currencyCode)})` : ''}.</p>
                <p>{action.kind === 'subscribe' ? 'L’option est active tout de suite ; elle est ajoutée au montant de votre abonnement à la prochaine échéance.' : 'La nouvelle quantité s’applique tout de suite et au montant de la prochaine échéance.'}</p>
              </>
            )}
          </div>
        )}
      </Modal>
    </section>
  );
}

const POLL_MS = 4000;

/**
 * Règlement en ligne de la période due : la page du prestataire s'ouvre dans un nouvel onglet (ou la demande arrive
 * sur le téléphone), puis l'état est relu jusqu'à la décision du prestataire.
 */
function PayOnline({info, manage, onPaid, autoOpen = false}: {info: OnlinePayment; manage: boolean; onPaid: () => Promise<void>; autoOpen?: boolean}) {
  const {busy, run} = useAction();
  const toast = useToast();
  const [open, setOpen] = useState(autoOpen);
  const [phone, setPhone] = useState('+229 ');
  const [checkout, setCheckout] = useState<Checkout | null>(info.pending);
  const [outcome, setOutcome] = useState<'failed' | 'expired' | null>(null);
  const options: PaymentOption[] = info.options?.length ? info.options : info.provider ? [{provider: info.provider, name: providerName[info.provider] ?? info.provider, methods: []}] : [];
  const [chosen, setChosen] = useState(options[0]?.provider ?? null);
  const needsPhone = pushesToPhone(chosen);
  const paid = useRef(onPaid);
  useEffect(() => {
    paid.current = onPaid;
  }, [onPaid]);

  useEffect(() => {
    if (!checkout || checkout.status !== 'Pending') return;
    let stopped = false;
    const timer = window.setInterval(async () => {
      try {
        const next = await refreshCheckout(checkout.id);
        if (stopped || next.status === 'Pending') return;
        setCheckout(next);
        if (next.status === 'Paid') {
          toast('success', 'Paiement reçu : merci ! Votre abonnement est à jour.');
          setOpen(false);
          await paid.current();
        } else {
          setOutcome(next.status === 'Failed' ? 'failed' : 'expired');
        }
      } catch {
        // Le prestataire ou le réseau ne répond pas : nouvel essai au tour suivant.
      }
    }, POLL_MS);
    return () => {
      stopped = true;
      window.clearInterval(timer);
    };
  }, [checkout, toast]);

  const waiting = checkout?.status === 'Pending';
  const amount = info.amount !== null && info.currencyCode ? money(info.amount, info.currencyCode) : null;
  const current = checkout?.provider ?? chosen;
  const provider = current ? options.find(option => option.provider === current)?.name ?? providerName[current] ?? current : '';
  const describeOption = (option: PaymentOption) => option.methods.map(method => methodName[method] ?? method).join(', ');

  if (!manage && !waiting) return null;
  return (
    <>
      <Button size="sm" onClick={() => setOpen(true)}>
        <Smartphone className="size-4" aria-hidden="true" />{waiting ? 'Paiement en cours…' : `Payer ${amount ?? ''} en ligne`}
      </Button>
      <Modal open={open} onClose={() => setOpen(false)} title="Payer l’abonnement" footer={waiting ? (
        <Button variant="secondary" onClick={() => setOpen(false)}>Fermer</Button>
      ) : (
        <>
          <Button variant="secondary" onClick={() => setOpen(false)}>Annuler</Button>
          <Button type="submit" form="pay-online" busy={busy === 'pay'} disabled={needsPhone && phone.replace(/\D/g, '').length < 8}>Payer {amount}</Button>
        </>
      )}>
        {waiting && checkout ? (
          <div className="space-y-3 text-sm text-ink-700" aria-live="polite">
            <p className="flex items-center gap-2 font-medium"><Loader2 className="size-4 animate-spin text-brand-600" aria-hidden="true" />En attente de la confirmation de {provider}…</p>
            {checkout.checkoutUrl ? (
              <p>Terminez le paiement dans la page {provider} ouverte dans un nouvel onglet. <a href={checkout.checkoutUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 font-semibold text-brand-700 hover:underline">Rouvrir la page<ExternalLink className="size-3.5" aria-hidden="true" /></a></p>
            ) : (
              <p>Validez la demande de paiement reçue sur votre téléphone (code secret Mobile Money).</p>
            )}
            <p className="text-ink-500">Vous pouvez fermer cette fenêtre : l’abonnement sera mis à jour dès la confirmation.</p>
          </div>
        ) : (
          <form id="pay-online" className="space-y-4" onSubmit={event => {
            event.preventDefault();
            const page = needsPhone ? null : window.open('', '_blank');
            void run('pay', async () => {
              try {
                const started = await startCheckout(needsPhone ? phone.replace(/[\s.\-()]/g, '') : null, chosen);
                setOutcome(null);
                setCheckout(started);
                if (page && started.checkoutUrl) page.location.href = started.checkoutUrl;
                else page?.close();
              } catch (error) {
                page?.close();
                throw error;
              }
            });
          }}>
            {outcome && <Alert tone="amber">{outcome === 'failed' ? 'Le paiement n’a pas abouti. Vous pouvez réessayer.' : 'Le paiement n’a pas été confirmé à temps. Vous pouvez réessayer.'}</Alert>}
            <p className="text-sm text-ink-700">Montant : <strong>{amount}</strong>{options.length > 1 ? '. Choisissez comment payer :' : `, via ${provider}${options[0]?.methods.length ? ` (${describeOption(options[0])})` : ''}.`}</p>
            {options.length > 1 && (
              <div role="radiogroup" aria-label="Moyen de paiement" className="space-y-2">
                {options.map(option => (
                  <label key={option.provider} className={cx('flex cursor-pointer items-center gap-3 rounded-xl border px-3.5 py-3 transition', chosen === option.provider ? 'border-brand-500 bg-brand-50/50 ring-1 ring-brand-500' : 'border-slate-200 hover:border-brand-200')}>
                    <input type="radio" name="provider" className="size-4 accent-brand-600" checked={chosen === option.provider} onChange={() => setChosen(option.provider)} />
                    <span className="min-w-0">
                      <span className="block font-semibold text-ink-900">{option.name}</span>
                      {option.methods.length > 0 && <span className="block text-xs text-ink-500">{describeOption(option)}</span>}
                    </span>
                  </label>
                ))}
              </div>
            )}
            {needsPhone && (
              <Field label="Numéro Mobile Money" hint="La demande de paiement arrive sur ce téléphone.">
                <Input type="tel" autoComplete="tel" value={phone} onChange={event => setPhone(event.target.value)} />
              </Field>
            )}
            <p className="text-xs text-ink-500">La facture est disponible dans l’historique dès la confirmation du paiement.</p>
          </form>
        )}
      </Modal>
    </>
  );
}

function Payments() {
  const {busy, run} = useAction();
  const [page, setPage] = useState(1);
  const {data, error} = useResource(signal => listPayments(page, signal), [page]);
  return (
    <Card className="p-5">
      <h2 className="mb-3 flex items-center gap-2 font-semibold"><Receipt className="size-5 text-ink-500" aria-hidden="true" />Historique des paiements</h2>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.items.length === 0 ? <p className="text-sm text-ink-500">Aucun paiement enregistré.</p> : (
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[32rem] text-sm">
            <thead className="text-left text-xs font-semibold tracking-wide text-ink-500 uppercase">
              <tr><th scope="col" className="py-2">Période</th><th scope="col" className="py-2">Plan</th><th scope="col" className="py-2 text-right">Montant</th><th scope="col" className="py-2 text-right">Payé le</th><th scope="col" className="py-2 text-right"><span className="sr-only">Facture</span></th></tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {data.items.map(payment => (
                <tr key={payment.id}>
                  <td className="py-2.5">{day(payment.paidFromUtc)} → {day(payment.paidUntilUtc)}</td>
                  <td className="py-2.5">{payment.lines && payment.lines.length > 1 ? (
                    <span className="block">
                      {payment.lines.map(line => <span key={line.planId} className="block">{line.label}{line.quantity > 1 ? ` × ${line.quantity}` : ''} <span className="text-ink-500 tabular-nums">· {money(line.amount, payment.currencyCode)}</span></span>)}
                    </span>
                  ) : payment.planName}</td>
                  <td className="py-2.5 text-right font-semibold tabular-nums">{money(payment.amount, payment.currencyCode)}</td>
                  <td className="py-2.5 text-right text-ink-500">
                    {day(payment.recordedAtUtc)}
                    {payment.collectedBy && <span className="block text-xs">{providerName[payment.collectedBy] ?? payment.collectedBy}</span>}
                  </td>
                  <td className="py-2.5 text-right">
                    <Button size="sm" variant="ghost" busy={busy === payment.id} onClick={() => run(payment.id, () => openInvoice(payment.id))}
                      aria-label={payment.invoiceNumber ? `Facture ${payment.invoiceNumber}` : 'Reçu'}>
                      <FileText className="size-4" aria-hidden="true" />{payment.invoiceNumber ?? 'Reçu'}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {data && (page > 1 || data.hasMore) && (
        <div className="mt-3 flex justify-end gap-2">
          <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récents</Button>
          <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(page + 1)}>Plus anciens</Button>
        </div>
      )}
    </Card>
  );
}

export default function SubscriptionPage() {
  const {can, tenant} = useConsole();
  const {busy, run} = useAction();
  const allowed = can('billing.view');
  const subscription = useResource(signal => (allowed ? mine(signal) : Promise.resolve(null)), [allowed]);
  const plans = useResource(signal => (allowed ? listPlans(signal) : Promise.resolve([])), [allowed]);
  const addOns = useResource(signal => (allowed ? listAddOns(signal) : Promise.resolve<MyAddOns>({offered: [], active: []})), [allowed]);
  const online = useResource(signal => (allowed ? loadOnlinePayment(signal).catch(() => null) : Promise.resolve(null)), [allowed]);
  const [paidAt, setPaidAt] = useState(0);
  const router = useRouter();
  const pathname = usePathname();
  const wantsToPay = useSearchParams().get('payer') === '1';
  const [payRequest, setPayRequest] = useState(0);
  const [choosing, setChoosing] = useState<Plan | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const manage = can('billing.manage');

  // « Payer maintenant » de la carte du plan : ouvre le règlement, même quand la page est déjà affichée.
  useEffect(() => {
    if (!wantsToPay || online.loading) return;
    setPayRequest(Date.now());
    router.replace(pathname, {scroll: false});
  }, [wantsToPay, online.loading, router, pathname]);

  if (!allowed) return <Alert tone="blue">Vous n’avez pas accès à l’abonnement (permission « Voir l’abonnement »).</Alert>;
  if (subscription.error) return <Alert>{subscription.error}</Alert>;
  if (subscription.loading || !plans.data) return <Spinner />;
  const data = subscription.data ?? null;
  const sub = data?.subscription;
  // Après un essai expiré (Découverte compris), choisir un plan payant reste possible.
  const trialExpired = sub?.status === 'Suspended' && sub.suspensionReason === 'TrialExpired';
  const canChangeOptions = manage && (!sub || sub.status === 'Trialing' || sub.status === 'Active');
  const canChange = canChangeOptions || (manage && trialExpired);
  // Le serveur vérifie aussi que c'est le premier abonnement.
  const freeAllowed = !sub || (sub.status === 'Trialing' && new Date(sub.currentPeriodEndUtc) <= addMonths(sub.startedAtUtc, 1));
  const offered = !!sub?.isComplimentary;
  const forever = offered && !sub?.complimentaryUntilUtc;
  const refresh = async () => {
    await subscription.reload();
    await plans.reload();
    await addOns.reload();
    await online.reload();
  };
  const payable = online.data?.available && (online.data.due || online.data.pending);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Abonnement</h1>
        <p className="mt-1 text-sm text-ink-500">Votre formule RestauSpace, son utilisation et vos paiements.</p>
      </div>

      {!data ? (
        <Alert tone="amber">{tenant.branding.displayName} n’a pas d’abonnement en cours : choisissez un plan pour continuer à utiliser la console.</Alert>
      ) : (
        <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)]">
          <Card className="p-5">
            <div className="flex flex-wrap items-start gap-3">
              <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-brand-50 text-brand-700" aria-hidden="true"><Sparkles className="size-6" /></span>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-xl font-bold">{data.plan.name}</h2>
                  {offered ? <Badge tone="brand">Offert</Badge> : <Badge tone={statusTone[sub!.status]}>{statusLabel[sub!.status]}</Badge>}
                </div>
                <p className="text-ink-600">
                  {offered
                    ? <>Offert par RestauSpace {forever ? 'sans limite de durée' : `jusqu’au ${day(sub!.complimentaryUntilUtc!)}`} <span className="text-ink-500">(au lieu de {money(data.plan.price, data.plan.currencyCode)} / {per(data.plan)})</span></>
                    : <>{money(data.plan.price, data.plan.currencyCode)} / {per(data.plan)}</>}
                </p>
              </div>
            </div>
            <dl className="mt-5 grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
              <div><dt className="text-ink-500">{offered ? 'Offre en cours' : 'Période en cours'}</dt><dd className="font-medium">{forever ? `Depuis le ${day(sub!.currentPeriodStartUtc)}` : `${day(sub!.currentPeriodStartUtc)} → ${day(sub!.currentPeriodEndUtc)}`}</dd></div>
              {!forever && <div><dt className="text-ink-500">{sub!.cancelAtPeriodEnd ? 'Fin de l’abonnement' : offered ? 'Fin de l’offre' : sub!.status === 'Trialing' ? (data.plan.price === 0 ? 'Fin de la période gratuite' : 'Fin de l’essai') : 'Prochaine échéance'}</dt><dd className="font-medium">{day(sub!.currentPeriodEndUtc)}</dd></div>}
              <div><dt className="text-ink-500">Client depuis</dt><dd className="font-medium">{day(sub!.startedAtUtc)}</dd></div>
            </dl>
            {data.nextCharge && !forever && <NextCharge charge={data.nextCharge} />}
            {offered && <div className="mt-4"><Alert tone="blue">{forever ? 'Votre plan est offert sans limite de durée : rien à payer. Pour changer de plan, ajouter une option ou résilier, contactez l’équipe RestauSpace.' : `Rien à payer jusqu’au ${day(sub!.complimentaryUntilUtc!)}. Ensuite, réglez la période suivante ici pour continuer sans interruption (vous serez prévenu 7 jours avant).`}</Alert></div>}
            {sub!.status === 'PastDue' && <div className="mt-4"><Alert tone="amber">Paiement en retard : régularisez pour éviter la suspension du compte.</Alert></div>}
            {sub!.status === 'Suspended' && <div className="mt-4"><Alert>Compte suspendu : {suspensionLabel[sub!.suspensionReason ?? 'Other']}. La console est en lecture seule.{trialExpired && (data.plan.price === 0 ? ' Choisissez un plan payant ci-dessous, puis payez pour continuer.' : ' Payez, ou choisissez un autre plan ci-dessous, pour continuer.')}</Alert></div>}
            {sub!.cancelAtPeriodEnd && (
              <div className="mt-4 flex flex-wrap items-center gap-3 rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
                <CalendarClock className="size-5 shrink-0" aria-hidden="true" />
                <span className="flex-1">Résiliation prévue le {day(sub!.currentPeriodEndUtc)}. Vous gardez l’accès jusque-là.</span>
                {manage && <Button size="sm" variant="secondary" busy={busy === 'keep'} onClick={() => run('keep', async () => {
                  await unscheduleCancellation();
                  await refresh();
                }, 'Résiliation annulée : votre abonnement continue.')}>Garder mon abonnement</Button>}
              </div>
            )}
            {payRequest > 0 && !payable && !offered && (
              <div className="mt-5"><Alert tone="amber">
                {!online.data?.available
                  ? 'Le paiement en ligne n’est pas encore ouvert pour votre devise. L’équipe RestauSpace vous indiquera comment régler (Mobile Money, virement).'
                  : 'Rien à payer pour le moment : votre période en cours est déjà réglée.'}
              </Alert></div>
            )}
            <div className="mt-5 flex flex-wrap items-center gap-2 border-t border-slate-100 pt-4 text-sm text-ink-600">
              <CreditCard className="size-4 shrink-0" aria-hidden="true" />
              <span className="flex-1">
                {offered
                  ? 'Rien à payer pendant l’offre.'
                  : online.data?.available
                  ? online.data.due || online.data.pending
                    ? `Réglez la période due en ligne${online.data.options?.length ? ` avec ${online.data.options.map(option => option.name).join(' ou ')}` : ''}.`
                    : sub!.status === 'Trialing' || sub!.status === 'Active' ? 'Paiement en ligne possible dès l’échéance.' : 'Paiement en ligne indisponible pour cet abonnement.'
                  : 'Le règlement est enregistré par l’équipe RestauSpace à réception de votre paiement (Mobile Money, virement).'}
              </span>
              {payable && online.data && (
                <PayOnline key={`${online.data.pending?.id ?? 'new'}-${payRequest}`} autoOpen={payRequest > 0} info={online.data} manage={manage} onPaid={async () => {
                  await refresh();
                  setPaidAt(Date.now());
                }} />
              )}
              {manage && !forever && !sub!.cancelAtPeriodEnd && (sub!.status === 'Active' || sub!.status === 'Trialing' || sub!.status === 'PastDue') && (
                <Button size="sm" variant="ghost" className="text-red-600" onClick={() => setCancelling(true)}>Résilier</Button>
              )}
            </div>
          </Card>
          <Usage data={data} />
        </div>
      )}

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">{data ? 'Changer de plan' : 'Choisir un plan'}</h2>
        {plans.data.length === 0 ? <p className="text-sm text-ink-500">Aucun plan disponible dans votre devise.</p> : (
          <Plans current={data} plans={plans.data} canChange={canChange && !offered} freeAllowed={freeAllowed} onChoose={setChoosing} />
        )}
        {data && offered && manage && <p className="text-sm text-ink-500">Votre plan est offert : pour en changer, contactez l’équipe RestauSpace.</p>}
        {data && !offered && !canChange && manage && <p className="text-sm text-ink-500">Le plan ne peut changer que pendant l’essai, un abonnement actif ou après la fin de l’essai.</p>}
      </section>

      {data && addOns.data && (
        <Options data={addOns.data} canChange={canChangeOptions && !forever} onChanged={refresh} />
      )}
      {data && addOns.data && addOns.data.offered.length + addOns.data.active.length > 0 && !canChangeOptions && manage && (
        <p className="text-sm text-ink-500">Les options se modifient pendant l’essai ou une période payée.</p>
      )}

      {data && <Payments key={paidAt} />}

      <Modal open={choosing !== null} onClose={() => setChoosing(null)} title={data ? `Passer au plan ${choosing?.name}` : `Choisir le plan ${choosing?.name}`} footer={
        <>
          <Button variant="secondary" onClick={() => setChoosing(null)}>Annuler</Button>
          <Button busy={busy === 'plan'} onClick={() => choosing && run('plan', async () => {
            if (data) await changePlan(choosing.id);
            else await subscribe(choosing.id);
            setChoosing(null);
            await refresh();
          }, data ? 'Plan changé.' : 'Abonnement démarré.')}>Confirmer</Button>
        </>
      }>
        {choosing && (
          <div className="space-y-2 text-sm text-ink-700">
            <p><strong>{choosing.name}</strong> : {money(choosing.price, choosing.currencyCode)} / {per(choosing)}.</p>
            <p>{data ? 'Le nouveau plan et ses limites s’appliquent tout de suite ; le nouveau tarif s’applique à la prochaine échéance.' : 'Votre abonnement démarre aujourd’hui.'}</p>
          </div>
        )}
      </Modal>

      <Modal open={cancelling} onClose={() => setCancelling(false)} title="Résilier l’abonnement" footer={
        <>
          <Button variant="secondary" onClick={() => setCancelling(false)}>Garder mon abonnement</Button>
          <Button variant="danger" busy={busy === 'cancel'} onClick={() => run('cancel', async () => {
            await scheduleCancellation();
            setCancelling(false);
            await refresh();
          }, 'Résiliation programmée.')}>Résilier à l’échéance</Button>
        </>
      }>
        {sub && <p className="text-sm text-ink-700">L’abonnement s’arrêtera le <strong>{day(sub.currentPeriodEndUtc)}</strong>. Jusque-là, tout fonctionne normalement et vous pouvez revenir sur votre décision.</p>}
      </Modal>
    </div>
  );
}
