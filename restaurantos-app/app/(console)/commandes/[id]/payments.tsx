'use client';
import {useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {Banknote, Bike, Check, Copy, CreditCard, ExternalLink, Gift, Globe, Landmark, Minus, Plus, RotateCcw, Search, Smartphone, Ticket, Undo2, Wallet} from 'lucide-react';
import {currencyDigits, money, parseAmount, time} from '@/lib/format';
import {
  cancelPayment, counterReference, coveredUnits, itemShare, methodLabel, pushProviders, recordOffline, refund, refundAsStoreCredit, retryPayment, startOnline, statusLabel, statusTone,
  type OnlineStarted, type OrderPayments, type Payment, type PaymentMethod, type Tender,
} from '@/lib/payments';
import {lookupStoredValueCard, type StoredValueCard} from '@/lib/marketing';
import type {Order} from '@/lib/types';
import {IssuedCode} from '@/components/issued-code';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Field, Input, Modal, Textarea, cx, useAction} from '@/components/ui';

const methodIcon: Record<PaymentMethod, typeof Banknote> = {
  Cash: Banknote,
  CardOnSite: CreditCard,
  MobileMoneyOnSite: Smartphone,
  MealVoucher: Ticket,
  GiftCard: Gift,
  DeliveryPlatform: Bike,
  CashOnDelivery: Bike,
  BankTransfer: Landmark,
  Card: CreditCard,
  ApplePay: Wallet,
  GooglePay: Wallet,
  PayPal: Globe,
  MobileMoney: Smartphone,
};

const round = (value: number, currency: string) => {
  const factor = 10 ** currencyDigits(currency);
  return Math.round(value * factor) / factor;
};

/** Parts égales d'un montant, la dernière absorbant l'arrondi : la première part est renvoyée. */
const share = (due: number, parts: number, currency: string) => {
  const factor = 10 ** currencyDigits(currency);
  return Math.ceil((due / parts) * factor) / factor;
};

export function CollectDialog({order, payments, tenders, open, onClose, onDone}: {
  order: Order;
  payments: OrderPayments;
  tenders: Tender[];
  open: boolean;
  onClose: () => void;
  onDone: (started?: OnlineStarted) => Promise<void>;
}) {
  const {busy, run} = useAction();
  const currency = payments.currencyCode;
  const [tenderKey, setTenderKey] = useState('');
  const [amount, setAmount] = useState('');
  const [received, setReceived] = useState('');
  const [reference, setReference] = useState('');
  const [parts, setParts] = useState(1);
  const [phone, setPhone] = useState('');
  const [byItem, setByItem] = useState(false);
  const [picked, setPicked] = useState<Record<string, number>>({});
  const [giftCode, setGiftCode] = useState('');
  const [giftCard, setGiftCard] = useState<StoredValueCard | null>(null);
  const [giftError, setGiftError] = useState('');
  useEffect(() => {
    if (!open) return;
    setGiftCode('');
    setGiftCard(null);
    setGiftError('');
    setPhone('');
    setTenderKey((order.channel === 'DeliveryPlatform' ? tenders.find(item => item.method === 'DeliveryPlatform') : undefined)?.key ?? tenders[0]?.key ?? '');
    setAmount(String(payments.amountDue));
    setReceived('');
    setReference('');
    setParts(1);
    setByItem(false);
    setPicked({});
  }, [open, tenders, payments.amountDue, order.channel]);

  const covered = coveredUnits(payments.payments);
  const digits = currencyDigits(currency);
  const pickedItems = Object.entries(picked).filter(([, quantity]) => quantity > 0).map(([itemId, quantity]) => ({itemId, quantity}));
  const pickedAmount = pickedItems.reduce((sum, choice) => {
    const line = order.items.find(item => item.id === choice.itemId);
    return line ? sum + itemShare(line.totalWithTax, line.quantity, covered.get(line.id) ?? 0, choice.quantity, digits) : sum;
  }, 0);

  const tender = tenders.find(item => item.key === tenderKey);
  const value = byItem ? (pickedItems.length > 0 ? pickedAmount : null) : parseAmount(amount);
  const cash = tender?.method === 'Cash';
  const given = parseAmount(received);
  const change = cash && value !== null && given !== null ? round(given - value, currency) : null;
  const invalid = value === null || value <= 0 || value > payments.amountDue;
  const pushed = !!tender && pushProviders.has(tender.provider);
  const gift = tender?.method === 'GiftCard';
  const giftShort = gift && giftCard !== null && value !== null && value > giftCard.balance;

  const checkGift = () => run('gift', async () => {
    setGiftError('');
    setGiftCard(null);
    try {
      const card = await lookupStoredValueCard(giftCode);
      setGiftCard(card);
      if (!card.isUsable) setGiftError(card.status === 'Cancelled' ? 'Cette carte a été annulée.' : card.isExpired ? 'Cette carte a expiré.' : 'Cette carte est épuisée.');
      else if (!byItem) setAmount(String(Math.min(card.balance, payments.amountDue)));
    } catch (failure) {
      if ((failure as {status?: number}).status === 404) setGiftError('Aucune carte avec ce code.');
      else throw failure;
    }
  });

  return (
    <Modal open={open} onClose={onClose} title={`Encaisser la commande #${order.number}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="collect-form" variant="success" busy={busy === 'collect'} disabled={!tender || invalid || (change !== null && change < 0) || (pushed && !phone.trim()) || (gift && (!giftCode.trim() || giftShort))}>
          {tender?.type === 'Online' ? 'Envoyer la demande de paiement' : `Encaisser ${value && !invalid ? money(value, currency) : ''}`}
        </Button>
      </>
    }>
      {tenders.length === 0 ? (
        <Alert tone="amber">
          Aucun moyen de paiement n’est accepté pour l’instant.{' '}
          <Link href="/parametres?onglet=paiements" className="font-semibold underline">Choisir les moyens de paiement</Link>
        </Alert>
      ) : (
        <form id="collect-form" className="space-y-5" onSubmit={event => {
          event.preventDefault();
          if (!tender || value === null) return;
          void run('collect', async () => {
            const portion = byItem ? {items: pickedItems} : {amount: value};
            if (tender.type === 'Online') {
              const started = await startOnline({orderId: order.id, provider: tender.provider, ...portion, paymentMethod: tender.method, payerPhone: pushed ? phone.trim() : undefined});
              await onDone(started);
            } else {
              await recordOffline({
                orderId: order.id, provider: tender.provider, ...portion, paymentMethod: tender.method,
                reference: gift ? giftCode.trim()
                  : reference.trim() || (tender.method === 'DeliveryPlatform' && order.externalReference ? `${order.platform}-${order.externalReference}`.slice(0, 100) : counterReference(order.number, tender.method)),
              });
              await onDone();
            }
            onClose();
          }, tender.type === 'Online' ? 'Demande de paiement envoyée.' : 'Paiement enregistré.');
        }}>
          <div className="rounded-xl bg-slate-50 p-4 text-center">
            <p className="text-sm text-ink-500">Reste à payer</p>
            <p className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{money(payments.amountDue, currency)}</p>
            {payments.settledAmount > 0 && <p className="text-xs text-ink-500">sur {money(payments.total, currency)} · déjà réglé {money(payments.settledAmount, currency)}</p>}
          </div>

          <fieldset>
            <legend className="mb-2 text-sm font-semibold">Moyen de paiement</legend>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {tenders.map(item => {
                const Icon = methodIcon[item.method];
                const selected = item.key === tenderKey;
                return (
                  <button key={item.key} type="button" aria-pressed={selected} onClick={() => setTenderKey(item.key)}
                    className={cx('flex flex-col items-center gap-1.5 rounded-xl border px-2 py-3 text-center text-sm font-medium transition', selected ? 'border-brand-500 bg-brand-50 text-brand-800 ring-1 ring-brand-500' : 'border-slate-200 hover:border-slate-300')}>
                    <Icon className="size-5" aria-hidden="true" />
                    <span className="leading-tight">{item.label}</span>
                    {item.type === 'Online' && <span className="text-[11px] font-normal text-ink-500">en ligne</span>}
                  </button>
                );
              })}
            </div>
          </fieldset>

          <div>
            <div className="mb-2 flex items-center justify-between gap-2">
              <span className="text-sm font-semibold">Montant</span>
              <span className="flex items-center gap-1 text-xs text-ink-500">
                <button type="button" aria-pressed={byItem} onClick={() => setByItem(current => !current)}
                  className={cx('mr-2 rounded-lg border px-2 py-1 text-xs font-semibold', byItem ? 'border-brand-500 bg-brand-50 text-brand-800' : 'border-slate-200 hover:bg-slate-50')}>Par article</button>
                {!byItem && <>Partager en</>}
                {!byItem && [1, 2, 3, 4].map(count => (
                  <button key={count} type="button" aria-pressed={parts === count} onClick={() => {
                    setParts(count);
                    setAmount(String(count === 1 ? payments.amountDue : Math.min(payments.amountDue, share(payments.amountDue, count, currency))));
                  }} className={cx('grid size-7 place-items-center rounded-lg border text-xs font-semibold', parts === count ? 'border-brand-500 bg-brand-50 text-brand-800' : 'border-slate-200 hover:bg-slate-50')}>{count}</button>
                ))}
              </span>
            </div>
            {byItem ? (
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
                {order.items.map(item => {
                  const left = item.quantity - (covered.get(item.id) ?? 0);
                  const count = picked[item.id] ?? 0;
                  return (
                    <li key={item.id} className="flex items-center gap-2 px-3 py-2 text-sm">
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{item.quantity}× {item.productName}{item.variantName ? ` · ${item.variantName}` : ''}</span>
                        <span className="text-xs text-ink-500">{left === 0 ? 'déjà réglé' : `${left} à régler · ${money(item.totalWithTax / item.quantity, currency)} l’unité`}</span>
                      </span>
                      {left > 0 && (
                        <>
                          <button type="button" aria-label={`Moins de ${item.productName}`} disabled={count === 0} onClick={() => setPicked(current => ({...current, [item.id]: count - 1}))} className="grid size-8 place-items-center rounded-lg ring-1 ring-slate-200 disabled:opacity-40"><Minus className="size-4" /></button>
                          <span className="w-6 text-center font-semibold tabular-nums">{count}</span>
                          <button type="button" aria-label={`Plus de ${item.productName}`} disabled={count >= left} onClick={() => setPicked(current => ({...current, [item.id]: count + 1}))} className="grid size-8 place-items-center rounded-lg ring-1 ring-slate-200 disabled:opacity-40"><Plus className="size-4" /></button>
                        </>
                      )}
                    </li>
                  );
                })}
                <li className="flex justify-between px-3 py-2 text-sm font-semibold"><span>Part choisie</span><span className="tabular-nums">{money(pickedAmount, currency)}</span></li>
              </ul>
            ) : (
              <Input aria-label="Montant à encaisser" inputMode="decimal" value={amount} onChange={event => {
                setAmount(event.target.value);
                setParts(0);
              }} />
            )}
            {value !== null && value > payments.amountDue && <p className="mt-1 text-xs text-red-600">Le montant dépasse le reste à payer.</p>}
          </div>

          {cash && (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Field label="Montant reçu (facultatif)"><Input inputMode="decimal" value={received} onChange={event => setReceived(event.target.value)} placeholder={value !== null ? String(value) : amount} /></Field>
              <div className={cx('rounded-xl p-3', change !== null && change < 0 ? 'bg-red-50 text-red-700' : 'bg-emerald-50 text-emerald-800')}>
                <p className="text-xs">Monnaie à rendre</p>
                <p className="text-xl font-bold">{change === null ? '—' : change < 0 ? 'Montant insuffisant' : money(change, currency)}</p>
              </div>
            </div>
          )}

          {gift && (
            <div className="space-y-2">
              <Field label="Code de la carte cadeau ou de l’avoir">
                <div className="flex gap-2">
                  <Input autoComplete="off" spellCheck={false} className="font-mono uppercase" placeholder="XXXX-XXXX-XXXX-XXXX" value={giftCode} onChange={event => {
                    setGiftCode(event.target.value);
                    setGiftCard(null);
                    setGiftError('');
                  }} />
                  <Button variant="secondary" busy={busy === 'gift'} disabled={!giftCode.trim()} onClick={() => void checkGift()}><Search className="size-4" aria-hidden="true" />Solde</Button>
                </div>
              </Field>
              {giftCard && giftCard.isUsable && (
                <p className={cx('text-sm', giftShort ? 'text-red-600' : 'text-emerald-700')}>
                  Solde : {money(giftCard.balance, giftCard.currencyCode)}{giftShort ? ' : insuffisant pour ce montant, le reste se règle autrement.' : ''}
                </p>
              )}
              {giftError && <p className="text-sm text-red-600">{giftError}</p>}
            </div>
          )}

          {tender?.type === 'OnSite' && !cash && !gift && (
            <Field label="Référence (facultatif)" hint="N° de ticket du terminal ou de la transaction mobile money.">
              <Input maxLength={100} value={reference} onChange={event => setReference(event.target.value.replace(/\s+/g, '-'))} />
            </Field>
          )}
          {tender?.type === 'Online' && pushed && (
            <Field label="Numéro du client" hint="Avec l’indicatif du pays : la demande s’affiche sur son téléphone, il la valide avec son code.">
              <Input type="tel" inputMode="tel" autoComplete="off" maxLength={20} placeholder="+229 97 00 00 00" value={phone} onChange={event => setPhone(event.target.value)} />
            </Field>
          )}
          {tender?.type === 'Online' && (
            <p className="text-sm text-ink-600">{pushed ? 'La commande est réglée quand le client a validé sur son téléphone.' : 'Le client reçoit une demande de paiement ; la commande est réglée quand le prestataire confirme.'}</p>
          )}
        </form>
      )}
    </Modal>
  );
}

function RetryDialog({payment, onClose, onDone}: {payment: Payment | null; onClose: () => void; onDone: (started: OnlineStarted) => Promise<void>}) {
  const {busy, run} = useAction();
  const [phone, setPhone] = useState('');
  useEffect(() => setPhone(''), [payment]);
  return (
    <Modal open={!!payment} onClose={onClose} title="Relancer la demande" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="retry-form" busy={busy === 'retry'} disabled={!phone.trim()}>Envoyer la demande</Button>
      </>
    }>
      {payment && (
        <form id="retry-form" onSubmit={event => {
          event.preventDefault();
          void run('retry', async () => {
            await onDone(await retryPayment(payment.id, phone.trim()));
            onClose();
          }, 'Paiement relancé.');
        }}>
          <Field label="Numéro du client" hint="Avec l’indicatif du pays. Le numéro n’est pas conservé.">
            <Input type="tel" inputMode="tel" autoComplete="off" maxLength={20} placeholder="+229 97 00 00 00" value={phone} onChange={event => setPhone(event.target.value)} />
          </Field>
        </form>
      )}
    </Modal>
  );
}

function RefundDialog({payment, onClose, onDone}: {payment: Payment | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const [asCredit, setAsCredit] = useState(false);
  const [issued, setIssued] = useState<{code: string; amount: number; currency: string} | null>(null);
  useEffect(() => {
    if (!payment) return;
    setAmount(String(payment.refundableAmount));
    setReason('');
    setAsCredit(false);
    setIssued(null);
  }, [payment]);
  const value = parseAmount(amount);
  const invalid = !payment || value === null || value <= 0 || value > payment.refundableAmount;
  const giftCard = payment?.method === 'GiftCard';

  return (
    <Modal open={!!payment} onClose={onClose} title={issued ? 'Avoir émis' : 'Rembourser'} footer={issued ? <Button onClick={onClose}>Terminé</Button> : (
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="refund-form" variant="danger" busy={busy === 'refund'} disabled={invalid || !reason.trim()}>{asCredit ? 'Rembourser en avoir' : 'Rembourser'}</Button>
      </>
    )}>
      {payment && issued && <IssuedCode code={issued.code} amount={issued.amount} currency={issued.currency} kind="StoreCredit" />}
      {payment && !issued && (
        <form id="refund-form" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('refund', async () => {
            const partial = value === payment.refundableAmount ? null : value;
            if (asCredit) {
              const credit = await refundAsStoreCredit(payment.id, reason.trim(), partial);
              setIssued({code: credit.code, amount: credit.amount, currency: credit.currencyCode});
              await onDone();
              return;
            }
            await refund(payment.id, reason.trim(), partial);
            await onDone();
            onClose();
          }, asCredit ? 'Avoir émis.' : giftCard ? 'Montant recrédité sur la carte.' : payment.isOffline ? 'Remboursement enregistré.' : 'Remboursement demandé au prestataire.');
        }}>
          <p className="text-sm text-ink-600">
            {methodLabel[payment.method]} de {money(payment.capturedAmount, payment.currencyCode)} · remboursable : {money(payment.refundableAmount, payment.currencyCode)}.
            {asCredit ? ' Un avoir de ce montant est remis au client, à utiliser lors d’une prochaine commande.'
              : giftCard ? ' Le montant est recrédité sur la carte utilisée.'
                : payment.isOffline ? ' Rendez la somme au client de la main à la main.' : ' Le prestataire rembourse le client.'}
          </p>
          {!giftCard && (
            <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Forme du remboursement">
              {[{credit: false, label: payment.isOffline ? 'En argent' : 'Par le prestataire'}, {credit: true, label: 'En avoir'}].map(option => (
                <button key={option.label} type="button" role="radio" aria-checked={asCredit === option.credit} onClick={() => setAsCredit(option.credit)}
                  className={cx('rounded-xl border px-3 py-2 text-sm font-semibold', asCredit === option.credit ? 'border-brand-500 bg-brand-50 text-brand-800' : 'border-slate-200 hover:bg-slate-50')}>
                  {option.label}
                </button>
              ))}
            </div>
          )}
          <Field label="Montant"><Input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} /></Field>
          <Field label="Motif"><Textarea rows={2} required maxLength={200} value={reason} onChange={event => setReason(event.target.value)} placeholder="Plat manquant, erreur de caisse…" /></Field>
        </form>
      )}
    </Modal>
  );
}

function CancelDialog({payment, onClose, onDone}: {payment: Payment | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [reason, setReason] = useState('');
  useEffect(() => setReason(''), [payment]);
  return (
    <Modal open={!!payment} onClose={onClose} title="Annuler la demande de paiement" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Retour</Button>
        <Button variant="danger" busy={busy === 'cancel'} disabled={!reason.trim()} onClick={() => payment && void run('cancel', async () => {
          await cancelPayment(payment.id, reason.trim());
          await onDone();
          onClose();
        }, 'Demande annulée.')}>Annuler la demande</Button>
      </>
    }>
      <Field label="Motif" hint="Le montant redevient à payer.">
        <Textarea rows={2} maxLength={200} value={reason} onChange={event => setReason(event.target.value)} placeholder="Le client paie autrement…" />
      </Field>
    </Modal>
  );
}

function CheckoutLink({started, onDismiss}: {started: OnlineStarted; onDismiss: () => void}) {
  const [copied, setCopied] = useState(false);
  return (
    <Alert tone="blue">
      <p className="font-semibold">Paiement en ligne en attente</p>
      {started.checkoutUrl ? (
        <>
          <p className="mt-1">Envoyez ce lien au client ; la commande sera réglée dès la confirmation du prestataire.</p>
          <div className="mt-2 flex flex-wrap gap-2">
            <Button size="sm" variant="secondary" onClick={() => {
              void navigator.clipboard?.writeText(started.checkoutUrl!).then(() => setCopied(true));
            }}>{copied ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}{copied ? 'Lien copié' : 'Copier le lien'}</Button>
            <a href={started.checkoutUrl} target="_blank" rel="noreferrer" className="inline-flex h-8 items-center gap-1.5 rounded-lg px-3 text-sm font-semibold text-brand-700 hover:bg-white/60">
              <ExternalLink className="size-4" aria-hidden="true" />Ouvrir
            </a>
            <Button size="sm" variant="ghost" onClick={onDismiss}>Masquer</Button>
          </div>
        </>
      ) : (
        <p className="mt-1">La demande a été envoyée sur le téléphone du client ; la commande sera réglée dès sa validation.</p>
      )}
    </Alert>
  );
}

export function PaymentPanel({order, payments, tenders, collectOpen, onCollect, onCloseCollect, onChanged}: {
  order: Order;
  payments: OrderPayments;
  tenders: Tender[];
  collectOpen: boolean;
  onCollect: () => void;
  onCloseCollect: () => void;
  onChanged: () => Promise<void>;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [refunding, setRefunding] = useState<Payment | null>(null);
  const [retrying, setRetrying] = useState<Payment | null>(null);
  const [cancelling, setCancelling] = useState<Payment | null>(null);
  const [started, setStarted] = useState<OnlineStarted | null>(null);
  const currency = payments.currencyCode;
  const payable = ['Placed', 'Accepted', 'Preparing', 'Ready'].includes(order.status);
  const progress = payments.total > 0 ? Math.min(1, payments.settledAmount / payments.total) : 0;
  const list = useMemo(() => [...payments.payments].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc)), [payments.payments]);
  const pending = list.some(payment => payment.status === 'Pending' || payment.status === 'Authorized');

  useEffect(() => {
    if (!pending) {
      setStarted(null);
      return;
    }
    const timer = window.setInterval(() => void onChanged(), 5000);
    return () => window.clearInterval(timer);
  }, [pending, onChanged]);

  return (
    <Card className="p-5">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="flex items-center gap-2 font-semibold"><Wallet className="size-5 text-ink-500" aria-hidden="true" />Encaissement</h2>
        {payments.amountDue <= 0 && payments.settledAmount >= payments.total && payments.total > 0 && <Badge tone="green">Soldée</Badge>}
      </div>

      <dl className="grid grid-cols-3 gap-2 text-center">
        <div className="rounded-xl bg-slate-50 p-3"><dt className="text-xs text-ink-500">Total</dt><dd className="font-bold">{money(payments.total, currency)}</dd></div>
        <div className="rounded-xl bg-emerald-50 p-3"><dt className="text-xs text-emerald-700">Réglé</dt><dd className="font-bold text-emerald-800">{money(payments.settledAmount, currency)}</dd></div>
        <div className={cx('rounded-xl p-3', payments.amountDue > 0 ? 'bg-amber-50' : 'bg-slate-50')}><dt className={cx('text-xs', payments.amountDue > 0 ? 'text-amber-700' : 'text-ink-500')}>Reste</dt><dd className="font-bold">{money(payments.amountDue, currency)}</dd></div>
      </dl>
      <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-100" role="progressbar" aria-label="Part réglée" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress * 100)}>
        <div className="h-full rounded-full bg-emerald-500 transition-all" style={{width: `${progress * 100}%`}} />
      </div>

      {started && <div className="mt-4"><CheckoutLink started={started} onDismiss={() => setStarted(null)} /></div>}

      {payable && payments.amountDue > 0 && can('payments.collect') && (
        <Button className="mt-4 w-full" size="lg" variant="success" onClick={onCollect}><Banknote className="size-5" aria-hidden="true" />Encaisser {money(payments.amountDue, currency)}</Button>
      )}
      {!payable && order.status === 'Draft' && <p className="mt-4 text-sm text-ink-500">Envoyez la commande pour pouvoir l’encaisser.</p>}

      {list.length > 0 && (
        <ul className="mt-5 divide-y divide-slate-100 border-t border-slate-100">
          {list.map(payment => {
            const Icon = methodIcon[payment.method];
            const reference = payment.method === 'GiftCard' ? null : payment.attempts.at(-1)?.providerReference;
            const failure = payment.attempts.at(-1)?.failureReason;
            return (
              <li key={payment.id} className="py-3">
                <div className="flex items-start gap-3">
                  <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true"><Icon className="size-4" /></span>
                  <div className="min-w-0 flex-1 text-sm">
                    <p className="flex flex-wrap items-center gap-2 font-medium">
                      {methodLabel[payment.method]}
                      <Badge tone={statusTone[payment.status]}>{statusLabel[payment.status]}</Badge>
                    </p>
                    <p className="truncate text-xs text-ink-500">{time(payment.createdAtUtc)}{reference ? ` · ${reference}` : ''}</p>
                    {payment.status === 'Failed' && failure && <p className="text-xs text-red-600">{failure}</p>}
                    {payment.hasUnexpectedCapture && <p className="text-xs font-semibold text-red-600">Encaissé après annulation : à rembourser.</p>}
                    {payment.refunds.map(item => (
                      <p key={item.id} className="text-xs text-ink-500">Remboursement {money(item.amount, currency)} · {item.reason}{item.status === 'Requested' ? ' (en cours)' : item.status === 'Failed' ? ' (échoué)' : ''}</p>
                    ))}
                  </div>
                  <p className="shrink-0 text-right text-sm font-semibold">
                    {money(payment.amount, currency)}
                    {payment.refundedAmount > 0 && <span className="block text-xs font-normal text-red-600">−{money(payment.refundedAmount, currency)}</span>}
                  </p>
                </div>
                <div className="mt-2 flex flex-wrap justify-end gap-1">
                  {payment.status === 'Pending' && !payment.isOffline && can('payments.collect') && (
                    <Button size="sm" variant="ghost" onClick={() => setCancelling(payment)}>Annuler la demande</Button>
                  )}
                  {payment.status === 'Failed' && !payment.isOffline && can('payments.collect') && (
                    <Button size="sm" variant="ghost" busy={busy === payment.id} onClick={() => pushProviders.has(payment.provider) ? setRetrying(payment) : run(payment.id, async () => {
                      setStarted(await retryPayment(payment.id));
                      await onChanged();
                    }, 'Paiement relancé.')}><RotateCcw className="size-4" aria-hidden="true" />Relancer</Button>
                  )}
                  {payment.refundableAmount > 0 && can('payments.refund') && (
                    <Button size="sm" variant="ghost" className="text-red-600" onClick={() => setRefunding(payment)}><Undo2 className="size-4" aria-hidden="true" />Rembourser</Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      <CollectDialog order={order} payments={payments} tenders={tenders} open={collectOpen} onClose={onCloseCollect} onDone={async result => {
        if (result) setStarted(result);
        await onChanged();
      }} />
      <RefundDialog payment={refunding} onClose={() => setRefunding(null)} onDone={onChanged} />
      <RetryDialog payment={retrying} onClose={() => setRetrying(null)} onDone={async result => {
        setStarted(result);
        await onChanged();
      }} />
      <CancelDialog payment={cancelling} onClose={() => setCancelling(null)} onDone={onChanged} />
    </Card>
  );
}
