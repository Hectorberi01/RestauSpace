'use client';
import {use, useEffect, useState} from 'react';
import QRCode from 'qrcode';
import Link from 'next/link';
import {ArrowLeft, MessageCircle, Printer, Send} from 'lucide-react';
import {api} from '@/lib/api';
import {money, orderTypeLabel} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {loadLocation} from '@/lib/locations';
import {loadOrderPayments, methodLabel} from '@/lib/payments';
import {printReceipt} from '@/lib/printing';
import {fiscalFooter, orderFiscal} from '@/lib/fiscal';
import {receiptLines} from '@/lib/tickets';
import type {Order} from '@/lib/types';
import {DevicePrintButton} from '@/components/device-print';
import {SendReceiptDialog} from '@/components/send-receipt';
import {useConsole} from '@/components/console';
import {Alert, Button, Spinner, cx, useAction} from '@/components/ui';
import {componentLabel, itemUnitPrice} from '@/lib/formulas';

type Format = 'ticket' | 'a4';

const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

/** Taux en pourcentage lisible : 0.18 -> « 18 % ». */
const rate = (value: number) => `${new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 2}).format(value * 100)} %`;

export default function ReceiptPage({params}: {params: Promise<{id: string}>}) {
  const {id} = use(params);
  const {tenant, locations, can} = useConsole();
  const [sending, setSending] = useState(false);
  const [format, setFormat] = useState<Format>('ticket');
  const {busy, run} = useAction();
  const order = useResource(signal => api<Order>(`/orders/${id}`, {signal}), [id]);
  const payments = useResource(signal => loadOrderPayments(id, signal).catch(() => null), [id]);
  const fiscal = useResource(signal => orderFiscal(id, signal), [id]);
  const [qr, setQr] = useState<string | null>(null);
  const footer = fiscalFooter(fiscal.data ?? null);
  useEffect(() => {
    if (!footer?.qr) return setQr(null);
    let active = true;
    void QRCode.toString(footer.qr, {type: 'svg', errorCorrectionLevel: 'M', margin: 0}).then(svg => active && setQr(svg));
    return () => {
      active = false;
    };
  }, [footer?.qr]);
  const location = useResource(signal => (order.data ? loadLocation(order.data.locationId, signal).catch(() => null) : Promise.resolve(null)), [order.data?.locationId]);

  if (order.error) return <Alert>{order.error}</Alert>;
  if (!order.data || payments.loading) return <Spinner />;
  const o = order.data;
  const fmt = (value: number) => money(value, o.currencyCode);
  const site = location.data;
  const locationName = site?.name ?? locations.find(item => item.id === o.locationId)?.name ?? '';
  const captured = (payments.data?.payments ?? []).filter(payment => payment.capturedAmount > 0);
  const refunded = captured.reduce((sum, payment) => sum + payment.refundedAmount, 0);
  const taxes = [...o.items.reduce((map, item) => {
    const current = map.get(item.taxRate) ?? {tax: 0};
    current.tax += item.taxAmount;
    map.set(item.taxRate, current);
    return map;
  }, new Map<number, {tax: number}>())].filter(([, value]) => value.tax > 0).sort(([a], [b]) => a - b);
  const draft = o.status === 'Draft';

  return (
    <div>
      <style>{`@media print { @page { size: ${format === 'ticket' ? '80mm auto' : 'A4'}; margin: ${format === 'ticket' ? '4mm' : '15mm'}; } }`}</style>
      <div className="mb-6 flex flex-wrap items-center gap-2 print:hidden">
        <Link href={`/commandes/${o.id}`} className="mr-auto inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Commande #{o.number}</Link>
        <div className="flex rounded-xl bg-slate-100 p-1" role="radiogroup" aria-label="Format">
          {([['ticket', 'Ticket 80 mm'], ['a4', 'Page A4']] as [Format, string][]).map(([value, label]) => (
            <button key={value} type="button" role="radio" aria-checked={format === value} onClick={() => setFormat(value)}
              className={cx('rounded-lg px-3 py-1.5 text-sm font-semibold', format === value ? 'bg-white shadow-sm' : 'text-ink-500')}>{label}</button>
          ))}
        </div>
        <DevicePrintButton disabled={draft} lines={() => receiptLines(o, payments.data ?? null, tenant, {name: locationName, address: site ? `${site.address.street}, ${site.address.city}` : undefined}, footer)} />
        <Button variant="secondary" disabled={draft} busy={busy === 'send'} onClick={() => void run('send', () => printReceipt(o.id), 'Reçu envoyé à l’imprimante du restaurant.')}><Send className="size-4" aria-hidden="true" />Imprimante du restaurant</Button>
        {can('storefront.receipts') && (
          <Button variant="secondary" disabled={draft} onClick={() => setSending(true)}><MessageCircle className="size-4" aria-hidden="true" />Envoyer au client</Button>
        )}
        <Button onClick={() => window.print()} disabled={draft}><Printer className="size-4" aria-hidden="true" />Imprimer</Button>
      </div>
      {!draft && <SendReceiptDialog orderId={o.id} customerId={o.customerId} open={sending} onClose={() => setSending(false)} />}
      {draft && <div className="mb-4 print:hidden"><Alert tone="amber">Commande en brouillon : validez-la avant d’imprimer un reçu.</Alert></div>}

      <article className={cx('mx-auto bg-white text-ink-900 shadow-sm ring-1 ring-slate-200 print:shadow-none print:ring-0',
        format === 'ticket' ? 'w-[80mm] max-w-full p-4 font-mono text-[12px] leading-snug print:w-full print:p-0' : 'w-full max-w-2xl p-10 text-sm print:max-w-none print:p-0')}>
        <header className="text-center">
          {tenant.branding.logoUrl && format === 'a4' && <img src={tenant.branding.logoUrl} alt="" className="mx-auto mb-2 h-14 w-auto object-contain" />}
          <p className={cx('font-bold', format === 'ticket' ? 'text-base' : 'text-xl')}>{tenant.branding.displayName || tenant.name}</p>
          {locationName && <p>{locationName}</p>}
          {site && <p>{site.address.street}, {site.address.city}</p>}
        </header>

        <div className="my-3 border-t border-dashed border-slate-400" />
        <div className="space-y-0.5">
          <p className="flex justify-between gap-2"><span>{o.status === 'Cancelled' ? 'Commande annulée' : 'Reçu'} n°</span><strong>{o.number}</strong></p>
          <p className="flex justify-between gap-2"><span>Date</span><span>{stamp(o.completedAtUtc ?? o.placedAtUtc ?? o.createdAtUtc)}</span></p>
          <p className="flex justify-between gap-2"><span>Type</span><span>{orderTypeLabel[o.type]}</span></p>
        </div>
        <div className="my-3 border-t border-dashed border-slate-400" />

        <table className="w-full">
          {format === 'a4' && (
            <thead><tr className="border-b border-slate-300 text-left"><th className="py-1">Article</th><th className="py-1 text-right">Qté</th><th className="py-1 text-right">P.U.</th><th className="py-1 text-right">Montant</th></tr></thead>
          )}
          <tbody>
            {o.items.map(item => {
              const unit = itemUnitPrice(item);
              const name = `${item.productName}${item.variantName ? ` (${item.variantName})` : ''}`;
              return format === 'a4' ? (
                <tr key={item.id} className="align-top">
                  <td className="py-1">{name}{item.components?.map(component => <span key={component.id} className="block text-xs text-ink-600">{componentLabel(component)}</span>)}{item.modifiers.length > 0 && <span className="block text-xs text-ink-500">{item.modifiers.map(modifier => modifier.name).join(', ')}</span>}</td>
                  <td className="py-1 text-right">{item.quantity}</td>
                  <td className="py-1 text-right">{fmt(unit)}</td>
                  <td className="py-1 text-right">{fmt(item.lineTotal)}</td>
                </tr>
              ) : (
                <tr key={item.id} className="align-top">
                  <td className="py-0.5">{item.quantity} × {name}{item.components?.map(component => <span key={component.id} className="block pl-4">{componentLabel(component)}</span>)}{item.modifiers.length > 0 && <span className="block pl-4">+ {item.modifiers.map(modifier => modifier.name).join(', ')}</span>}</td>
                  <td className="py-0.5 text-right whitespace-nowrap">{fmt(item.lineTotal)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>

        <div className="my-3 border-t border-dashed border-slate-400" />
        <dl className="space-y-0.5">
          <div className="flex justify-between"><dt>Sous-total</dt><dd>{fmt(o.subTotal)}</dd></div>
          {o.discounts.map(discount => (
            <div key={`${discount.source}-${discount.reference}`} className="flex justify-between"><dt>Remise{discount.label ? ` ${discount.label}` : ''}</dt><dd>−{fmt(discount.amount)}</dd></div>
          ))}
          {o.discounts.length > 1 && <div className="flex justify-between"><dt>Total remises</dt><dd>−{fmt(o.discountTotal)}</dd></div>}
          {o.priceTaxMode === 'TaxExclusive' && taxes.map(([taxRate, value]) => (
            <div key={taxRate} className="flex justify-between"><dt>TVA {rate(taxRate)}</dt><dd>{fmt(value.tax)}</dd></div>
          ))}
          {o.deliveryFee ? <div className="flex justify-between"><dt>Frais de livraison</dt><dd>{fmt(o.deliveryFee)}</dd></div> : null}
          <div className={cx('flex justify-between pt-1 font-bold', format === 'ticket' ? 'text-sm' : 'text-lg')}><dt>TOTAL</dt><dd>{fmt(o.total)}</dd></div>
          {o.priceTaxMode === 'TaxInclusive' && taxes.length > 0 && (
            <div className="pt-1 text-xs">
              {taxes.map(([taxRate, value]) => <p key={taxRate} className="flex justify-between"><span>dont TVA {rate(taxRate)}</span><span>{fmt(value.tax)}</span></p>)}
            </div>
          )}
        </dl>

        {captured.length > 0 && (
          <>
            <div className="my-3 border-t border-dashed border-slate-400" />
            <dl className="space-y-0.5">
              {captured.map(payment => (
                <div key={payment.id} className="flex justify-between gap-2"><dt>{methodLabel[payment.method]}</dt><dd>{fmt(payment.capturedAmount)}</dd></div>
              ))}
              {refunded > 0 && <div className="flex justify-between"><dt>Remboursé</dt><dd>−{fmt(refunded)}</dd></div>}
              {payments.data && payments.data.amountDue > 0 && <div className="flex justify-between font-semibold"><dt>Reste à payer</dt><dd>{fmt(payments.data.amountDue)}</dd></div>}
            </dl>
          </>
        )}

        {footer && (
          <>
            <div className="my-3 border-t border-dashed border-slate-400" />
            <section className="space-y-0.5 text-center text-[11px]" aria-label="Mentions fiscales">
              {footer.lines.map(line => <p key={line} className={line === 'FACTURE NORMALISÉE' ? 'font-bold' : undefined}>{line}</p>)}
              {qr && <div className="mx-auto mt-2 size-28 [&_svg]:size-full" role="img" aria-label="QR code de la facture normalisée" dangerouslySetInnerHTML={{__html: qr}} />}
            </section>
          </>
        )}

        <div className="my-3 border-t border-dashed border-slate-400" />
        <footer className="text-center">
          {o.status === 'Cancelled' ? <p className="font-bold">COMMANDE ANNULÉE</p> : <p>Merci de votre visite !</p>}
          {o.priceTaxMode === 'TaxInclusive' ? <p className="text-xs">Prix TTC</p> : <p className="text-xs">Prix HT, TVA en sus</p>}
        </footer>
      </article>
    </div>
  );
}
