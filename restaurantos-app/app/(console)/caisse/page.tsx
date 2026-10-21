'use client';
import {useMemo, useState} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {ArrowDownLeft, ArrowUpRight, Download, History, Lock, ReceiptText, ShoppingBasket, Wallet, Tablet} from 'lucide-react';
import {
  closeTill, denominationValues, journal, listSessions, movementLabel, openSession, openTill, recordMovement,
  type CashMovementKind, type CashSession, type JournalEntry,
} from '@/lib/cash';
import {dateTime, isoDate, money, parseAmount, time} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {methodLabel, type PaymentMethod} from '@/lib/payments';
import {listMembers} from '@/lib/staff';
import {Difference} from '@/components/cash';
import {useConsole, WithLocation} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, PageHeader, Spinner, StatCard, Tabs, Textarea, cx, useAction} from '@/components/ui';

type Tab = 'session' | 'history' | 'journal';

/** Nom lisible d'un membre à partir de son adresse ; null si le personnel ne peut pas être lu. */
function useCashiers() {
  const {can} = useConsole();
  const members = useResource(() => (can('staff.view') ? listMembers().catch(() => []) : Promise.resolve([])), []);
  return useMemo(() => {
    const names = new Map<string, string>();
    for (const member of members.data ?? []) {
      if (member.email) names.set(member.userId, member.email.split('@')[0].split(/[._-]+/).filter(Boolean).map(part => part[0].toUpperCase() + part.slice(1)).join(' '));
    }
    return (id: string | null) => (id ? names.get(id) ?? 'un membre de l’équipe' : '—');
  }, [members.data]);
}

export default function CashPage() {
  const {can} = useConsole();
  const [tab, setTab] = useState<Tab>('session');
  if (!can('payments.collect')) {
    return (
      <>
        <PageHeader title="Caisse" />
        <Alert tone="amber">La caisse est réservée aux membres qui encaissent (permission « Encaisser »).</Alert>
      </>
    );
  }
  return (
    <WithLocation>
      {location => (
        <>
          <PageHeader
            title="Caisse"
            description={`Ouverture, mouvements d’espèces et clôture de la caisse de ${location.name}.`}
            actions={
              <span className="flex flex-wrap gap-2">
                <Link href="/tactile" className="inline-flex h-10 items-center gap-2 rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white hover:bg-brand-700"><Tablet className="size-4" aria-hidden="true" />Caisse tactile</Link>
                <Link href="/caisse/vente" className="inline-flex h-10 items-center gap-2 rounded-xl border border-slate-300 bg-white px-4 text-sm font-semibold hover:bg-slate-50"><ShoppingBasket className="size-4" aria-hidden="true" />Vente hors ligne</Link>
              </span>
            }
          />
          <Tabs<Tab>
            label="Caisse"
            value={tab}
            onChange={setTab}
            items={[
              {value: 'session', label: 'Session en cours'},
              {value: 'history', label: 'Tickets Z'},
              {value: 'journal', label: 'Journal des encaissements'},
            ]}
          />
          {tab === 'session' && <SessionTab locationId={location.id} />}
          {tab === 'history' && <HistoryTab locationId={location.id} />}
          {tab === 'journal' && <JournalTab locationId={location.id} />}
        </>
      )}
    </WithLocation>
  );
}

function SessionTab({locationId}: {locationId: string}) {
  const session = useResource(signal => openSession(locationId, signal), [locationId], 30_000);
  if (session.error) return <Alert>{session.error}</Alert>;
  if (session.loading && !session.data) return <Spinner />;
  return session.data ? <OpenSession session={session.data} reload={session.reload} /> : <OpenForm locationId={locationId} onOpened={session.reload} />;
}

function OpenForm({locationId, onOpened}: {locationId: string; onOpened: () => void}) {
  const {currency} = useConsole();
  const {busy, run} = useAction();
  const [amount, setAmount] = useState('');
  const value = amount.trim() === '' ? 0 : parseAmount(amount);
  return (
    <Card className="mx-auto max-w-lg p-6">
      <span className="grid size-12 place-items-center rounded-2xl bg-brand-50 text-brand-600"><Wallet className="size-6" aria-hidden="true" /></span>
      <h2 className="mt-4 text-lg font-bold">La caisse est fermée</h2>
      <p className="mt-1 text-sm text-ink-500">Comptez les espèces mises dans le tiroir pour commencer la journée. Elles serviront à rendre la monnaie.</p>
      <form
        className="mt-5 space-y-4"
        onSubmit={async event => {
          event.preventDefault();
          if (value === null || value < 0) return;
          if (await run('open', () => openTill(locationId, value), 'Caisse ouverte.')) onOpened();
        }}
      >
        <Field label={`Fond de caisse (${currency})`} hint="0 si le tiroir est vide.">
          <Input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} placeholder="0" />
        </Field>
        {value === null && <Alert>Montant invalide.</Alert>}
        <Button type="submit" size="lg" busy={busy === 'open'} disabled={value === null} className="w-full">Ouvrir la caisse</Button>
      </form>
    </Card>
  );
}

function Stat({label, value, tone, hint}: {label: string; value: string; tone?: 'brand'; hint?: string}) {
  return <StatCard label={label} value={value} hint={hint} className={tone === 'brand' ? 'border-brand-200! bg-brand-50' : undefined} />;
}

function OpenSession({session, reload}: {session: CashSession; reload: () => void}) {
  const cashier = useCashiers();
  const [movement, setMovement] = useState<CashMovementKind | null>(null);
  const [closing, setClosing] = useState(false);
  const fmt = (value: number) => money(value, session.currencyCode);
  const cash = session.takings.find(item => item.method === 'Cash')?.net ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-3">
        <Badge tone="green">Ouverte</Badge>
        <p className="text-sm text-ink-500">Session n°{session.number}, ouverte à {time(session.openedAtUtc)} par {cashier(session.openedBy)}.</p>
        <div className="ml-auto flex flex-wrap gap-2">
          <Button variant="secondary" onClick={() => setMovement('PayIn')}><ArrowDownLeft className="size-4" aria-hidden="true" />Entrée</Button>
          <Button variant="secondary" onClick={() => setMovement('PayOut')}><ArrowUpRight className="size-4" aria-hidden="true" />Sortie</Button>
          <Button onClick={() => setClosing(true)}><Lock className="size-4" aria-hidden="true" />Clôturer</Button>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
        <Stat label="Fond de caisse" value={fmt(session.openingFloat)} />
        <Stat label="Ventes en espèces" value={fmt(cash)} hint="Remboursements déduits" />
        <Stat label="Entrées" value={fmt(session.paidIn)} />
        <Stat label="Sorties" value={fmt(session.paidOut)} />
        <Stat label="Espèces attendues" value={fmt(session.expectedCash)} tone="brand" />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="p-5">
          <h2 className="font-semibold">Encaissements de la session</h2>
          {session.takings.length === 0 ? (
            <p className="mt-3 text-sm text-ink-500">Aucun encaissement depuis l’ouverture.</p>
          ) : (
            <table className="mt-3 w-full text-sm">
              <thead className="text-left text-xs text-ink-500"><tr><th className="py-1.5 font-medium">Moyen</th><th className="py-1.5 text-right font-medium">Encaissé</th><th className="py-1.5 text-right font-medium">Remboursé</th><th className="py-1.5 text-right font-medium">Net</th></tr></thead>
              <tbody className="divide-y divide-slate-100">
                {session.takings.map(item => (
                  <tr key={item.method}>
                    <td className="py-2">{methodLabel[item.method]}{!item.toCount && <span className="ml-1.5 text-xs text-ink-500">(pour information)</span>}</td>
                    <td className="py-2 text-right tabular-nums">{fmt(item.collected)}</td>
                    <td className="py-2 text-right tabular-nums">{item.refunded ? `−${fmt(item.refunded)}` : '—'}</td>
                    <td className="py-2 text-right font-semibold tabular-nums">{fmt(item.net)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>

        <Card className="p-5">
          <h2 className="font-semibold">Mouvements d’espèces</h2>
          {session.movements.length === 0 ? (
            <p className="mt-3 text-sm text-ink-500">Ni entrée ni sortie. Notez ici la monnaie ajoutée, un achat payé avec la caisse ou un dépôt en banque.</p>
          ) : (
            <ul className="mt-3 divide-y divide-slate-100 text-sm">
              {[...session.movements].reverse().map((item, index) => (
                <li key={`${item.atUtc}-${index}`} className="flex items-center gap-3 py-2">
                  <span className={cx('grid size-8 shrink-0 place-items-center rounded-full', item.kind === 'PayIn' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700')} aria-hidden="true">
                    {item.kind === 'PayIn' ? <ArrowDownLeft className="size-4" /> : <ArrowUpRight className="size-4" />}
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">{item.reason}</span>
                    <span className="text-xs text-ink-500">{movementLabel[item.kind]} à {time(item.atUtc)} · {cashier(item.by)}</span>
                  </span>
                  <span className="font-semibold tabular-nums">{item.kind === 'PayIn' ? '+' : '−'}{fmt(item.amount)}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      {movement && <MovementModal session={session} kind={movement} onClose={() => setMovement(null)} onDone={reload} />}
      {closing && <CloseModal session={session} onClose={() => setClosing(false)} />}
    </div>
  );
}

function MovementModal({session, kind, onClose, onDone}: {session: CashSession; kind: CashMovementKind; onClose: () => void; onDone: () => void}) {
  const {busy, run} = useAction();
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const value = parseAmount(amount);
  const valid = value !== null && value > 0 && reason.trim().length > 0;
  const submit = async () => {
    if (!valid) return;
    if (await run('movement', () => recordMovement(session.id, kind, value, reason.trim()), kind === 'PayIn' ? 'Entrée enregistrée.' : 'Sortie enregistrée.')) {
      onDone();
      onClose();
    }
  };
  return (
    <Modal
      open
      onClose={onClose}
      title={kind === 'PayIn' ? 'Entrée d’espèces' : 'Sortie d’espèces'}
      footer={<><Button variant="secondary" onClick={onClose}>Annuler</Button><Button onClick={submit} busy={busy === 'movement'} disabled={!valid}>Enregistrer</Button></>}
    >
      <div className="space-y-4">
        <Field label={`Montant (${session.currencyCode})`}>
          <Input inputMode="decimal" value={amount} onChange={event => setAmount(event.target.value)} autoFocus />
        </Field>
        <Field label="Motif" hint={kind === 'PayIn' ? 'Par exemple : appoint de monnaie.' : 'Par exemple : achat de gaz, dépôt en banque.'}>
          <Input value={reason} maxLength={200} onChange={event => setReason(event.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}

function CloseModal({session, onClose}: {session: CashSession; onClose: () => void}) {
  const router = useRouter();
  const {busy, run} = useAction();
  const values = denominationValues(session.currencyCode);
  const [detail, setDetail] = useState(values.length > 0);
  const [coins, setCoins] = useState<Record<number, string>>({});
  const [cashTotal, setCashTotal] = useState('');
  const others = session.takings.filter(item => item.toCount && item.method !== 'Cash' && item.net !== 0);
  const [counted, setCounted] = useState<Partial<Record<PaymentMethod, string>>>({});
  const [note, setNote] = useState('');
  const fmt = (value: number) => money(value, session.currencyCode);

  const coinCounts = values.map(value => ({value, count: Number.parseInt(coins[value] ?? '', 10) || 0}));
  const coinTotal = coinCounts.reduce((sum, coin) => sum + coin.value * coin.count, 0);
  const cash = detail ? coinTotal : parseAmount(cashTotal);
  const otherAmounts = others.map(item => ({method: item.method, amount: parseAmount(counted[item.method] ?? '')}));
  const valid = cash !== null && cash >= 0 && (detail || cashTotal.trim() !== '') && otherAmounts.every(item => item.amount !== null && item.amount >= 0);

  const submit = async () => {
    if (!valid) return;
    const amounts = otherAmounts.map(item => ({method: item.method, amount: item.amount ?? 0}));
    const ok = await run(
      'close',
      () => closeTill(session.id, detail ? amounts : [{method: 'Cash', amount: cash ?? 0}, ...amounts], detail ? coinCounts.filter(coin => coin.count > 0) : null, note),
      'Caisse clôturée.',
    );
    if (ok) router.push(`/caisse/z/${session.id}`);
  };

  return (
    <Modal
      open
      wide
      onClose={onClose}
      title={`Clôturer la session n°${session.number}`}
      footer={<><Button variant="secondary" onClick={onClose}>Annuler</Button><Button onClick={submit} busy={busy === 'close'} disabled={!valid}>Clôturer et voir le ticket Z</Button></>}
    >
      <div className="space-y-5">
        <p className="text-sm text-ink-500">Comptez le tiroir sans regarder l’attendu : l’écart s’affiche sur le ticket Z. Une fois clôturée, la session ne se modifie plus.</p>
        <section>
          <div className="mb-2 flex items-center justify-between gap-2">
            <h3 className="font-semibold">Espèces</h3>
            {values.length > 0 && (
              <button type="button" className="text-sm font-medium text-brand-700 hover:underline" onClick={() => setDetail(value => !value)}>
                {detail ? 'Saisir le total' : 'Compter billet par billet'}
              </button>
            )}
          </div>
          {detail ? (
            <>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                {values.map(value => (
                  <label key={value} className="flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2">
                    <span className="w-16 shrink-0 text-sm font-semibold tabular-nums">{new Intl.NumberFormat('fr-FR').format(value)}</span>
                    <span className="text-ink-400" aria-hidden="true">×</span>
                    <Input inputMode="numeric" aria-label={`Nombre de ${value}`} className="h-9 min-w-0" value={coins[value] ?? ''} onChange={event => setCoins(current => ({...current, [value]: event.target.value.replace(/\D/g, '')}))} />
                  </label>
                ))}
              </div>
              <p className="mt-3 flex justify-between rounded-xl bg-slate-50 px-4 py-2.5 text-sm"><span>Total compté</span><strong className="tabular-nums">{fmt(coinTotal)}</strong></p>
            </>
          ) : (
            <Field label={`Total des espèces (${session.currencyCode})`}>
              <Input inputMode="decimal" value={cashTotal} onChange={event => setCashTotal(event.target.value)} />
            </Field>
          )}
        </section>
        {others.length > 0 && (
          <section className="space-y-3">
            <h3 className="font-semibold">Autres moyens</h3>
            <p className="text-sm text-ink-500">Reportez le total du terminal de paiement, du téléphone mobile money ou des titres reçus.</p>
            {others.map(item => (
              <Field key={item.method} label={methodLabel[item.method]}>
                <Input inputMode="decimal" value={counted[item.method] ?? ''} onChange={event => setCounted(current => ({...current, [item.method]: event.target.value}))} />
              </Field>
            ))}
          </section>
        )}
        <Field label="Note (facultatif)">
          <Textarea rows={2} maxLength={500} value={note} onChange={event => setNote(event.target.value)} />
        </Field>
      </div>
    </Modal>
  );
}

function HistoryTab({locationId}: {locationId: string}) {
  const sessions = useResource(signal => listSessions(locationId, signal), [locationId]);
  if (sessions.error) return <Alert>{sessions.error}</Alert>;
  if (!sessions.data) return <Spinner />;
  if (sessions.data.length === 0) return <Empty icon={<History className="size-8" />} title="Aucune session">Les tickets Z apparaissent ici après chaque clôture.</Empty>;
  return (
    <Card className="overflow-x-auto">
      <table className="w-full min-w-[640px] text-sm">
        <thead className="border-b border-slate-200 text-left text-xs text-ink-500">
          <tr><th className="px-4 py-3 font-medium">N°</th><th className="px-4 py-3 font-medium">Ouverture</th><th className="px-4 py-3 font-medium">Clôture</th><th className="px-4 py-3 text-right font-medium">Fond</th><th className="px-4 py-3 text-right font-medium">Espèces comptées</th><th className="px-4 py-3 text-right font-medium">Écart</th><th className="px-4 py-3" /></tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {sessions.data.map(item => (
            <tr key={item.id}>
              <td className="px-4 py-3 font-semibold">{item.number}</td>
              <td className="px-4 py-3">{dateTime(item.openedAtUtc)}</td>
              <td className="px-4 py-3">{item.closedAtUtc ? dateTime(item.closedAtUtc) : <Badge tone="green">Ouverte</Badge>}</td>
              <td className="px-4 py-3 text-right tabular-nums">{money(item.openingFloat, item.currencyCode)}</td>
              <td className="px-4 py-3 text-right tabular-nums">{item.countedCash === null ? '—' : money(item.countedCash, item.currencyCode)}</td>
              <td className="px-4 py-3 text-right">{item.status === 'Closed' ? <Difference value={item.totalDifference} currency={item.currencyCode} /> : '—'}</td>
              <td className="px-4 py-3 text-right">{item.status === 'Closed' && <Link href={`/caisse/z/${item.id}`} className="inline-flex items-center gap-1 font-semibold text-brand-700"><ReceiptText className="size-4" aria-hidden="true" />Ticket Z</Link>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  );
}

const csvCell = (value: string) => (/[";\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value);

function JournalTab({locationId}: {locationId: string}) {
  const [day, setDay] = useState(isoDate(new Date()));
  const [until, setUntil] = useState(day);
  const from = new Date(`${day}T00:00:00`);
  const to = new Date(`${until < day ? day : until}T00:00:00`);
  to.setDate(to.getDate() + 1);
  const data = useResource(signal => journal(locationId, from, to, signal), [locationId, day, until]);
  const entries = data.data?.entries ?? [];

  const exportCsv = () => {
    const rows = [['Date', 'Type', 'Moyen', 'Prestataire', 'Montant', 'Devise', 'Référence', 'Commande'], ...entries.map((entry: JournalEntry) => [
      new Date(entry.atUtc).toLocaleString('fr-FR'), entry.kind === 'Capture' ? 'Encaissement' : 'Remboursement', methodLabel[entry.method], entry.provider,
      String(entry.kind === 'Refund' ? -entry.amount : entry.amount), entry.currencyCode, entry.reference ?? '', entry.orderId,
    ])];
    const blob = new Blob(['﻿' + rows.map(row => row.map(csvCell).join(';')).join('\n')], {type: 'text/csv;charset=utf-8'});
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `encaissements-${day}${until > day ? `-au-${until}` : ''}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end gap-3">
        <Field label="Du"><Input type="date" value={day} max={isoDate(new Date())} onChange={event => setDay(event.target.value)} /></Field>
        <Field label="Au"><Input type="date" value={until} min={day} max={isoDate(new Date())} onChange={event => setUntil(event.target.value)} /></Field>
        <Button variant="secondary" className="ml-auto" onClick={exportCsv} disabled={entries.length === 0}><Download className="size-4" aria-hidden="true" />Exporter (CSV)</Button>
      </div>
      {data.error && <Alert>{data.error}</Alert>}
      {!data.data ? (
        !data.error && <Spinner />
      ) : (
        <>
          {data.data.truncated && <Alert tone="amber">Plus de 1 000 mouvements : réduisez la période pour tout voir.</Alert>}
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            {data.data.totals.length === 0 && <p className="col-span-full text-sm text-ink-500">Aucun encaissement sur la période.</p>}
            {data.data.totals.map(total => (
              <Stat key={total.method} label={methodLabel[total.method]} value={money(total.net, entries[0]?.currencyCode ?? 'XOF')} hint={total.refunded ? `dont ${money(total.refunded, entries[0]?.currencyCode ?? 'XOF')} remboursés` : undefined} />
            ))}
          </div>
          {entries.length > 0 && (
            <Card className="overflow-x-auto">
              <table className="w-full min-w-[640px] text-sm">
                <thead className="border-b border-slate-200 text-left text-xs text-ink-500">
                  <tr><th className="px-4 py-3 font-medium">Heure</th><th className="px-4 py-3 font-medium">Mouvement</th><th className="px-4 py-3 font-medium">Moyen</th><th className="px-4 py-3 font-medium">Référence</th><th className="px-4 py-3 text-right font-medium">Montant</th></tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {entries.map((entry, index) => (
                    <tr key={`${entry.paymentId}-${entry.kind}-${index}`}>
                      <td className="px-4 py-2.5 whitespace-nowrap">{dateTime(entry.atUtc)}</td>
                      <td className="px-4 py-2.5"><Link href={`/commandes/${entry.orderId}`} className="hover:underline">{entry.kind === 'Capture' ? 'Encaissement' : 'Remboursement'}</Link></td>
                      <td className="px-4 py-2.5">{methodLabel[entry.method]}</td>
                      <td className="px-4 py-2.5 font-mono text-xs">{entry.reference ?? '—'}</td>
                      <td className={cx('px-4 py-2.5 text-right font-semibold tabular-nums', entry.kind === 'Refund' && 'text-red-700')}>{entry.kind === 'Refund' ? '−' : ''}{money(entry.amount, entry.currencyCode)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Card>
          )}
        </>
      )}
    </div>
  );
}

