'use client';
import {useEffect, useState} from 'react';
import {useSearchParams} from 'next/navigation';
import QRCode from 'qrcode';
import {Printer} from 'lucide-react';
import {errorMessage, loadReceipt, type PublicReceipt} from '@/lib/storefront';
import {Notice, Spinner, cx} from '@/components/order-ui';
import {tr, useLang} from '@/lib/i18n';

export function Receipt() {
  useLang();
  const params = useSearchParams();
  const token = params.get('jeton') ?? '';
  const [receipt, setReceipt] = useState<PublicReceipt | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [qr, setQr] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;
    const controller = new AbortController();
    loadReceipt(token, controller.signal)
      .then(found => found && setReceipt(found))
      .catch(reason => !controller.signal.aborted && setError(errorMessage(reason)));
    return () => controller.abort();
  }, [token]);

  useEffect(() => {
    if (!receipt?.qrCode) return setQr(null);
    let active = true;
    void QRCode.toString(receipt.qrCode, {type: 'svg', errorCorrectionLevel: 'M', margin: 0}).then(svg => active && setQr(svg));
    return () => {
      active = false;
    };
  }, [receipt?.qrCode]);

  if (!token) return <Shell><Notice tone="error">{tr('Ce lien de reçu n’est pas valable.')}</Notice></Shell>;
  if (error) return <Shell><Notice tone="error">{error}</Notice></Shell>;
  if (!receipt) return <Spinner />;

  return (
    <Shell>
      <style>{'@media print { @page { size: A4; margin: 15mm; } body { background: white; } }'}</style>
      <div className="mb-4 flex justify-center print:hidden">
        <button type="button" onClick={() => window.print()}
          className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-ink-800 shadow-sm hover:bg-slate-50">
          <Printer className="size-4" aria-hidden="true" />{tr('Imprimer ou enregistrer en PDF')}
        </button>
      </div>
      <article className="rounded-3xl bg-white p-6 text-sm text-ink-900 shadow-sm print:rounded-none print:p-0 print:shadow-none" aria-label={tr('Reçu')}>
        <header className="text-center">
          <h1 className="text-xl font-bold">{receipt.restaurantName}</h1>
          <p>{receipt.locationName}</p>
          {receipt.address && <p className="text-ink-500">{receipt.address}</p>}
        </header>

        <hr className="my-4 border-dashed border-slate-300" />
        <dl className="space-y-1">
          <div className="flex justify-between gap-2"><dt>{receipt.isCancelled ? tr('Commande annulée n°') : tr('Reçu n°')}</dt><dd className="font-semibold">{receipt.orderNumber}</dd></div>
          <div className="flex justify-between gap-2"><dt>{tr('Date')}</dt><dd>{receipt.date}</dd></div>
          <div className="flex justify-between gap-2"><dt>{tr('Type')}</dt><dd>{receipt.serviceMode}</dd></div>
        </dl>
        <hr className="my-4 border-dashed border-slate-300" />

        <ul className="space-y-2">
          {receipt.lines.map((line, index) => (
            <li key={index} className="flex justify-between gap-3">
              <span className="min-w-0">
                {line.quantity} × {line.name}
                {line.details && <span className="block pl-4 text-xs text-ink-500">{line.details}</span>}
              </span>
              <span className="shrink-0 tabular-nums">{line.amount}</span>
            </li>
          ))}
        </ul>

        <hr className="my-4 border-dashed border-slate-300" />
        <dl className="space-y-1">
          {receipt.totals.map(total => (
            <div key={total.label} className="flex justify-between gap-2"><dt>{total.label}</dt><dd className="tabular-nums">{total.amount}</dd></div>
          ))}
          <div className="flex justify-between gap-2 pt-1 text-lg font-bold"><dt>{tr('TOTAL')}</dt><dd className="tabular-nums">{receipt.total}</dd></div>
        </dl>

        {(receipt.payments.length > 0 || receipt.amountDue) && (
          <>
            <hr className="my-4 border-dashed border-slate-300" />
            <dl className="space-y-1">
              {receipt.payments.map((payment, index) => (
                <div key={index} className="flex justify-between gap-2"><dt>{payment.label}</dt><dd className="tabular-nums">{payment.amount}</dd></div>
              ))}
              {receipt.amountDue && <div className="flex justify-between gap-2 font-semibold"><dt>{tr('Reste à payer')}</dt><dd className="tabular-nums">{receipt.amountDue}</dd></div>}
            </dl>
          </>
        )}

        {(receipt.footer.length > 0 || qr) && (
          <>
            <hr className="my-4 border-dashed border-slate-300" />
            <section className="space-y-0.5 text-center text-xs" aria-label={tr('Mentions fiscales')}>
              {receipt.footer.map(line => <p key={line} className={cx(line === 'FACTURE NORMALISÉE' && 'font-bold')}>{line}</p>)}
              {qr && <div className="mx-auto mt-2 size-28 [&_svg]:size-full" role="img" aria-label={tr('QR code de la facture normalisée')} dangerouslySetInnerHTML={{__html: qr}} />}
            </section>
          </>
        )}

        <hr className="my-4 border-dashed border-slate-300" />
        <footer className="text-center">
          {receipt.isCancelled ? <p className="font-bold">{tr('COMMANDE ANNULÉE')}</p> : <p>{tr('Merci de votre visite !')}</p>}
          <p className="text-xs text-ink-500">{receipt.pricesIncludeTax ? tr('Prix TTC') : tr('Prix HT, TVA en sus')}</p>
        </footer>
      </article>
    </Shell>
  );
}

function Shell({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl bg-slate-50 px-4 py-10 print:max-w-none print:bg-white print:p-0">{children}</div>;
}
