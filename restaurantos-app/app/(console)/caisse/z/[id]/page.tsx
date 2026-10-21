'use client';
import {use} from 'react';
import Link from 'next/link';
import {ArrowLeft, Printer} from 'lucide-react';
import {getSession, movementLabel} from '@/lib/cash';
import {money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {methodLabel} from '@/lib/payments';
import {zLines} from '@/lib/tickets';
import {Difference} from '@/components/cash';
import {DevicePrintButton} from '@/components/device-print';
import {useConsole} from '@/components/console';
import {Alert, Button, Spinner} from '@/components/ui';

const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

function Row({label, value, strong}: {label: string; value: string; strong?: boolean}) {
  return <p className={strong ? 'flex justify-between gap-2 font-bold' : 'flex justify-between gap-2'}><span>{label}</span><span className="text-right">{value}</span></p>;
}

const Rule = () => <div className="my-3 border-t border-dashed border-slate-400" />;

/** Ticket Z : la clôture figée d'une session de caisse, au format ticket 80 mm. */
export default function ZTicketPage({params}: {params: Promise<{id: string}>}) {
  const {id} = use(params);
  const {tenant, locations} = useConsole();
  const session = useResource(signal => getSession(id, signal), [id]);

  if (session.error) return <Alert>{session.error}</Alert>;
  if (!session.data) return <Spinner />;
  const z = session.data;
  const fmt = (value: number) => money(value, z.currencyCode);
  const open = z.status === 'Open';
  const informative = z.takings.filter(item => !item.toCount);

  return (
    <div>
      <style>{'@media print { @page { size: 80mm auto; margin: 4mm; } }'}</style>
      <div className="mb-6 flex flex-wrap items-center gap-2 print:hidden">
        <Link href="/caisse" className="mr-auto inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Caisse</Link>
        <DevicePrintButton disabled={open} lines={() => zLines(z, tenant, locations.find(item => item.id === z.locationId)?.name ?? '')} />
        <Button onClick={() => window.print()} disabled={open}><Printer className="size-4" aria-hidden="true" />Imprimer</Button>
      </div>
      {open && <div className="mb-4 print:hidden"><Alert tone="amber">Session encore ouverte : le ticket Z s’imprime après la clôture.</Alert></div>}

      <article className="mx-auto w-[80mm] max-w-full bg-white p-4 font-mono text-[12px] leading-snug text-ink-900 shadow-sm ring-1 ring-slate-200 print:w-full print:p-0 print:shadow-none print:ring-0">
        <header className="text-center">
          <p className="text-base font-bold">{tenant.branding.displayName || tenant.name}</p>
          <p>{locations.find(item => item.id === z.locationId)?.name ?? ''}</p>
          <p className="mt-2 text-sm font-bold">TICKET Z n°{z.number}</p>
        </header>
        <Rule />
        <Row label="Ouverture" value={stamp(z.openedAtUtc)} />
        <Row label="Clôture" value={z.closedAtUtc ? stamp(z.closedAtUtc) : '—'} />
        <Rule />
        <p className="mb-1 font-bold">ESPÈCES</p>
        <Row label="Fond de caisse" value={fmt(z.openingFloat)} />
        <Row label="Ventes nettes" value={fmt(z.takings.find(item => item.method === 'Cash')?.net ?? 0)} />
        <Row label="Entrées" value={`+${fmt(z.paidIn)}`} />
        <Row label="Sorties" value={`−${fmt(z.paidOut)}`} />
        <Row label="Attendu" value={fmt(z.expectedCash)} strong />
        {z.movements.length > 0 && (
          <div className="mt-2 space-y-0.5">
            {z.movements.map((item, index) => (
              <p key={`${item.atUtc}-${index}`} className="flex justify-between gap-2 text-[11px]"><span className="truncate">{movementLabel[item.kind]} · {item.reason}</span><span>{item.kind === 'PayIn' ? '+' : '−'}{fmt(item.amount)}</span></p>
            ))}
          </div>
        )}
        <Rule />
        <p className="mb-1 font-bold">COMPTAGE</p>
        <table className="w-full">
          <thead><tr className="text-left"><th className="font-normal">Moyen</th><th className="text-right font-normal">Attendu</th><th className="text-right font-normal">Compté</th></tr></thead>
          <tbody>
            {z.lines.map(line => (
              <tr key={line.method} className="align-top">
                <td>{methodLabel[line.method]}<span className="block text-[11px]">écart {line.difference === 0 ? '0' : `${line.difference > 0 ? '+' : '−'}${fmt(Math.abs(line.difference))}`}</span></td>
                <td className="text-right">{fmt(line.expected)}</td>
                <td className="text-right">{fmt(line.counted)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {z.denominations.length > 0 && (
          <div className="mt-2 text-[11px]">
            {z.denominations.map(coin => <Row key={coin.value} label={`${new Intl.NumberFormat('fr-FR').format(coin.value)} × ${coin.count}`} value={fmt(coin.value * coin.count)} />)}
          </div>
        )}
        <Rule />
        <div className="flex items-center justify-between font-bold"><span>ÉCART TOTAL</span><span className="print:hidden"><Difference value={z.totalDifference} currency={z.currencyCode} /></span><span className="hidden print:inline">{fmt(z.totalDifference)}</span></div>
        {informative.length > 0 && (
          <>
            <Rule />
            <p className="mb-1 font-bold">AUTRES ENCAISSEMENTS</p>
            {informative.map(item => <Row key={item.method} label={methodLabel[item.method]} value={fmt(item.net)} />)}
          </>
        )}
        {z.closingNote && (<><Rule /><p>Note : {z.closingNote}</p></>)}
      </article>
    </div>
  );
}
