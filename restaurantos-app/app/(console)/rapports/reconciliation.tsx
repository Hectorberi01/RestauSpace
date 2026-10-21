'use client';
import {useMemo, useState} from 'react';
import {CheckCircle2, Download, FileUp} from 'lucide-react';
import {sessionsIn, journal, type JournalEntry} from '@/lib/cash';
import {downloadCsv, parseCsv} from '@/lib/csv';
import {addDays} from '@/lib/dashboard';
import {dateTime, money, number} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import type {Period} from '@/lib/reports';
import type {LocationSummary} from '@/lib/types';
import {Alert, Badge, Button, Field, Select, Spinner, cx} from '@/components/ui';
import {Panel} from './panels';

const providerNames: Record<string, string> = {fedapay: 'FedaPay', kkiapay: 'KKiaPay', cinetpay: 'CinetPay', mtn_momo: 'MTN MoMo', stripe: 'Stripe', paypal: 'PayPal', wave: 'Wave', flutterwave: 'Flutterwave', paystack: 'Paystack', paydunya: 'PayDunya', mollie: 'Mollie'};
const providerName = (code: string) => providerNames[code] ?? code;

/** Statuts d'un relevé qui valent « argent reçu » chez les agrégateurs courants. */
const PAID = /^(approved|approuv|success|succ[eè]s|successful|complete|completed|paid|pay[ée]|accepted|valid)/i;

const guess = (headers: string[], patterns: RegExp[]) => {
  for (const pattern of patterns) {
    const index = headers.findIndex(header => pattern.test(header));
    if (index >= 0) return index;
  }
  return -1;
};

/** Un montant de relevé : « 12 500 », « 12500.00 », « 12 500,00 XOF ». */
function amountOf(raw: string): number | null {
  const cleaned = raw.replace(/[^\d,.-]/g, '');
  if (!cleaned) return null;
  const normalized = /,\d{1,2}$/.test(cleaned) ? cleaned.replace(/\./g, '').replace(',', '.') : cleaned.replace(/,/g, '');
  const value = Number(normalized);
  return Number.isFinite(value) ? value : null;
}

interface Statement {
  fileName: string;
  headers: string[];
  rows: string[][];
}

interface Columns {
  reference: number;
  amount: number;
  status: number;
}

type Outcome =
  | {kind: 'matched'; reference: string; amount: number; entry: JournalEntry}
  | {kind: 'amount'; reference: string; amount: number; entry: JournalEntry}
  | {kind: 'statementOnly'; reference: string; amount: number}
  | {kind: 'paymentsOnly'; reference: string; entry: JournalEntry};

/** Rapprochement : sessions de caisse (attendu / compté) et relevé d'un agrégateur face aux paiements enregistrés. */
export function Reconciliation({period, locationId, locations, currency}: {period: Period; locationId: string | null; locations: LocationSummary[]; currency: string}) {
  const from = period.from;
  const to = addDays(period.to, 1);
  const tooLong = to.getTime() - from.getTime() > 93 * 86_400_000;
  const key = `${from.toISOString()}_${to.toISOString()}_${locationId ?? 'all'}`;
  const sessions = useResource(signal => (tooLong ? Promise.resolve(null) : sessionsIn(locationId, from, to, signal)), [key]);
  const payments = useResource(signal => (tooLong ? Promise.resolve(null) : journal(locationId, from, to, signal)), [key]);
  const nameOf = (id: string) => locations.find(location => location.id === id)?.name ?? '';

  if (tooLong) return <Alert tone="amber">Le rapprochement porte sur 93 jours au plus : choisissez une période plus courte.</Alert>;

  const closed = (sessions.data ?? []).filter(session => session.status === 'Closed');
  const gaps = closed.filter(session => session.totalDifference !== 0);
  const gapTotal = closed.reduce((sum, session) => sum + session.totalDifference, 0);

  return (
    <div className="grid grid-cols-1 gap-6 2xl:grid-cols-2">
      <Panel title="Caisse et comptage">
        {sessions.error ? <Alert>{sessions.error}</Alert> : !sessions.data ? <Spinner /> : closed.length === 0 ? (
          <p className="py-8 text-center text-sm text-ink-500">Aucune caisse clôturée sur la période.</p>
        ) : (
          <>
            <div className="mb-4 grid grid-cols-3 gap-3 text-sm">
              <Figure label="Caisses clôturées" value={number(closed.length)} />
              <Figure label="Avec un écart" value={number(gaps.length)} tone={gaps.length ? 'text-amber-600' : undefined} />
              <Figure label="Écart cumulé" value={money(gapTotal, currency)} tone={gapTotal < 0 ? 'text-red-600' : gapTotal > 0 ? 'text-amber-600' : 'text-emerald-700'} />
            </div>
            <div className="max-h-96 overflow-auto">
              <table className="w-full min-w-[440px] text-left text-sm">
                <thead className="sticky top-0 bg-white text-xs text-ink-500">
                  <tr>
                    <th scope="col" className="pb-2 font-medium">Caisse</th>
                    <th scope="col" className="pb-2 font-medium">Clôture</th>
                    <th scope="col" className="pb-2 text-right font-medium">Espèces comptées</th>
                    <th scope="col" className="pb-2 text-right font-medium">Écart</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {closed.map(session => (
                    <tr key={session.id}>
                      <td className="py-2">N° {session.number}{!locationId && <span className="block text-xs text-ink-500">{nameOf(session.locationId)}</span>}</td>
                      <td className="py-2">{session.closedAtUtc ? dateTime(session.closedAtUtc) : '—'}</td>
                      <td className="py-2 text-right whitespace-nowrap">{session.countedCash === null ? '—' : money(session.countedCash, session.currencyCode)}</td>
                      <td className={cx('py-2 text-right font-semibold whitespace-nowrap', session.totalDifference < 0 ? 'text-red-600' : session.totalDifference > 0 ? 'text-amber-600' : 'text-emerald-700')}>
                        {session.totalDifference > 0 ? '+' : ''}{money(session.totalDifference, session.currencyCode)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="mt-3 text-xs text-ink-500">Écart : compté moins attendu, tous moyens comptés à la clôture (espèces, TPE, Mobile Money sur place).</p>
          </>
        )}
      </Panel>

      <Panel title="Relevé de l’agrégateur">
        {payments.error ? <Alert>{payments.error}</Alert> : !payments.data ? <Spinner /> : (
          <StatementCheck entries={payments.data.entries} truncated={payments.data.truncated} currency={currency} />
        )}
      </Panel>
    </div>
  );
}

function Figure({label, value, tone}: {label: string; value: string; tone?: string}) {
  return (
    <div className="rounded-xl bg-slate-50 p-3">
      <p className="text-xs text-ink-500">{label}</p>
      <p className={cx('mt-1 font-bold', tone)}>{value}</p>
    </div>
  );
}

function StatementCheck({entries, truncated, currency}: {entries: JournalEntry[]; truncated: boolean; currency: string}) {
  const captures = useMemo(() => entries.filter(entry => entry.kind === 'Capture' && entry.provider !== 'cash'), [entries]);
  const providers = useMemo(() => [...new Set(captures.map(entry => entry.provider))].sort(), [captures]);
  const [provider, setProvider] = useState('');
  const [statement, setStatement] = useState<Statement | null>(null);
  const [columns, setColumns] = useState<Columns>({reference: -1, amount: -1, status: -1});
  const [error, setError] = useState<string | null>(null);
  const chosen = provider || providers[0] || '';

  const read = async (file: File) => {
    setError(null);
    if (file.size > 5 * 1024 * 1024) {
      setError('Fichier trop lourd (5 Mo au plus).');
      return;
    }
    const rows = parseCsv(await file.text()).filter(row => row.some(cell => cell.trim()));
    if (rows.length < 2) {
      setError('Le fichier ne contient aucune transaction.');
      return;
    }
    const headers = rows[0].map(header => header.trim());
    const lower = headers.map(header => header.toLowerCase());
    setColumns({
      reference: guess(lower, [/^r[ée]f[ée]rence/, /transaction.?id/, /^transaction/, /^ref/, /^id$/]),
      amount: guess(lower, [/^montant$/, /^amount$/, /montant/, /amount/]),
      status: guess(lower, [/^statut/, /^status/, /[ée]tat/]),
    });
    setStatement({fileName: file.name, headers, rows: rows.slice(1)});
  };

  const outcomes = useMemo<Outcome[] | null>(() => {
    if (!statement || columns.reference < 0 || columns.amount < 0) return null;
    const ours = new Map(captures.filter(entry => entry.provider === chosen && entry.reference).map(entry => [entry.reference!.trim().toLowerCase(), entry]));
    const seen = new Set<string>();
    const result: Outcome[] = [];
    statement.rows.forEach(row => {
      const reference = (row[columns.reference] ?? '').trim();
      const amount = amountOf(row[columns.amount] ?? '');
      if (!reference || amount === null) return;
      if (columns.status >= 0 && !PAID.test((row[columns.status] ?? '').trim())) return;
      const keyOf = reference.toLowerCase();
      if (seen.has(keyOf)) return;
      seen.add(keyOf);
      const entry = ours.get(keyOf);
      if (!entry) result.push({kind: 'statementOnly', reference, amount});
      else if (Math.abs(entry.amount - amount) >= 0.01) result.push({kind: 'amount', reference, amount, entry});
      else result.push({kind: 'matched', reference, amount, entry});
    });
    ours.forEach((entry, keyOf) => !seen.has(keyOf) && result.push({kind: 'paymentsOnly', reference: entry.reference!, entry}));
    return result;
  }, [statement, columns, captures, chosen]);

  if (providers.length === 0) {
    return <p className="py-8 text-center text-sm text-ink-500">Aucun paiement en ligne sur la période : rien à rapprocher.</p>;
  }

  const count = (kind: Outcome['kind']) => outcomes?.filter(outcome => outcome.kind === kind).length ?? 0;
  const anomalies = outcomes?.filter(outcome => outcome.kind !== 'matched') ?? [];
  const label: Record<Outcome['kind'], string> = {
    matched: 'Rapproché',
    amount: 'Montant différent',
    statementOnly: 'Absent de nos paiements',
    paymentsOnly: 'Absent du relevé',
  };

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Prestataire">
          <Select value={chosen} onChange={event => setProvider(event.target.value)}>
            {providers.map(code => <option key={code} value={code}>{providerName(code)}</option>)}
          </Select>
        </Field>
        <Field label="Relevé (CSV exporté du tableau de bord)">
          <label className="flex h-10 cursor-pointer items-center gap-2 rounded-xl border border-dashed border-slate-300 px-3 text-sm text-ink-600 hover:border-brand-500">
            <FileUp className="size-4 shrink-0" aria-hidden="true" />
            <span className="truncate">{statement?.fileName ?? 'Choisir un fichier'}</span>
            <input type="file" accept=".csv,text/csv" className="sr-only" aria-label="Relevé CSV" onChange={event => {
              const file = event.target.files?.[0];
              if (file) void read(file);
              event.target.value = '';
            }} />
          </label>
        </Field>
      </div>
      {truncated && <Alert tone="amber">Plus de 1 000 mouvements sur la période : seuls les 1 000 plus récents sont rapprochés. Réduisez la période.</Alert>}
      {error && <Alert>{error}</Alert>}

      {statement && (
        <div className="grid gap-3 sm:grid-cols-3">
          {(['reference', 'amount', 'status'] as const).map(column => (
            <Field key={column} label={column === 'reference' ? 'Colonne référence' : column === 'amount' ? 'Colonne montant' : 'Colonne statut'}>
              <Select value={columns[column]} onChange={event => setColumns({...columns, [column]: Number(event.target.value)})}>
                <option value={-1}>{column === 'status' ? 'Aucune (tout compte)' : 'Choisir…'}</option>
                {statement.headers.map((header, index) => <option key={index} value={index}>{header || `Colonne ${index + 1}`}</option>)}
              </Select>
            </Field>
          ))}
        </div>
      )}

      {statement && !outcomes && <Alert tone="amber">Indiquez la colonne de la référence et celle du montant.</Alert>}

      {outcomes && (
        <>
          <div className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
            <Figure label="Rapprochés" value={number(count('matched'))} tone="text-emerald-700" />
            <Figure label="Montant différent" value={number(count('amount'))} tone={count('amount') ? 'text-amber-600' : undefined} />
            <Figure label="Absents de nos paiements" value={number(count('statementOnly'))} tone={count('statementOnly') ? 'text-red-600' : undefined} />
            <Figure label="Absents du relevé" value={number(count('paymentsOnly'))} tone={count('paymentsOnly') ? 'text-red-600' : undefined} />
          </div>
          {anomalies.length === 0 ? (
            <p className="flex items-center gap-2 rounded-xl bg-emerald-50 p-3 text-sm font-medium text-emerald-800"><CheckCircle2 className="size-5" aria-hidden="true" />Le relevé et vos paiements concordent.</p>
          ) : (
            <>
              <div className="max-h-80 overflow-auto">
                <table className="w-full min-w-[480px] text-left text-sm">
                  <thead className="sticky top-0 bg-white text-xs text-ink-500">
                    <tr>
                      <th scope="col" className="pb-2 font-medium">Référence</th>
                      <th scope="col" className="pb-2 font-medium">Constat</th>
                      <th scope="col" className="pb-2 text-right font-medium">Relevé</th>
                      <th scope="col" className="pb-2 text-right font-medium">Enregistré</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {anomalies.map(outcome => (
                      <tr key={`${outcome.kind}-${outcome.reference}`}>
                        <td className="max-w-[12rem] truncate py-2 font-mono text-xs" title={outcome.reference}>{outcome.reference}</td>
                        <td className="py-2"><Badge tone={outcome.kind === 'amount' ? 'amber' : 'red'}>{label[outcome.kind]}</Badge></td>
                        <td className="py-2 text-right whitespace-nowrap">{'amount' in outcome ? money(outcome.amount, currency) : '—'}</td>
                        <td className="py-2 text-right whitespace-nowrap">{'entry' in outcome ? money(outcome.entry.amount, outcome.entry.currencyCode) : '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Button variant="secondary" onClick={() => downloadCsv(`rapprochement-${chosen}.csv`, [
                ['Référence', 'Constat', 'Montant du relevé', 'Montant enregistré', 'Date du paiement'],
                ...anomalies.map(outcome => [
                  outcome.reference,
                  label[outcome.kind],
                  'amount' in outcome ? String(outcome.amount) : '',
                  'entry' in outcome ? String(outcome.entry.amount) : '',
                  'entry' in outcome ? outcome.entry.atUtc : '',
                ]),
              ])}><Download className="size-4" aria-hidden="true" />Exporter les écarts</Button>
            </>
          )}
          <p className="text-xs text-ink-500">
            Les lignes du relevé sont comparées par référence de transaction aux paiements {providerName(chosen)} encaissés sur la période. Une transaction
            payée en fin de période peut figurer sur le relevé du jour suivant.
          </p>
        </>
      )}
    </div>
  );
}
