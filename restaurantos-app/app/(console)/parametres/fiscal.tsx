'use client';
import {useEffect, useState, type ReactNode} from 'react';
import {FileCheck2, KeyRound, Landmark, RotateCcw, ShieldCheck} from 'lucide-react';
import {
  aibLabel, invoiceStatusLabel, listInvoices, loadProfile, modeLabel, regimeLabel, retryInvoice, saveProfile, saveToken, setMode, testConnection,
  type AibRate, type FiscalConnection, type FiscalInvoiceStatus, type FiscalMode, type TaxRegime,
} from '@/lib/fiscal';
import {dateTime, money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Select, Spinner, cx, useAction} from '@/components/ui';

function Section({icon, title, description, children}: {icon: ReactNode; title: string; description: string; children: ReactNode}) {
  return (
    <Card className="p-5">
      <div className="mb-4 flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true">{icon}</span>
        <div>
          <h2 className="font-semibold">{title}</h2>
          <p className="text-sm text-ink-500">{description}</p>
        </div>
      </div>
      {children}
    </Card>
  );
}

const statusTone: Record<FiscalInvoiceStatus, 'green' | 'amber' | 'red' | 'blue'> = {Pending: 'blue', Submitted: 'amber', Certified: 'green', Failed: 'red'};

/** Facture normalisée e-MECeF (DGI du Bénin) : identité fiscale, jeton, environnement et suivi des factures. */
export function FiscalSettings() {
  const {can, currency} = useConsole();
  const editable = can('fiscal.configure');
  const {busy, run} = useAction();
  const profile = useResource(signal => loadProfile(signal), []);
  const [ifu, setIfu] = useState('');
  const [rccm, setRccm] = useState('');
  const [regime, setRegime] = useState<TaxRegime>('Vat');
  const [aib, setAib] = useState<AibRate>('None');
  const [token, setToken] = useState('');
  const [connection, setConnection] = useState<FiscalConnection | null>(null);
  const [filter, setFilter] = useState<FiscalInvoiceStatus | ''>('');
  const invoices = useResource(signal => (profile.data ? listInvoices(filter || null, signal) : Promise.resolve([])), [profile.data, filter], 30_000);

  useEffect(() => {
    if (!profile.data) return;
    setIfu(profile.data.ifu);
    setRccm(profile.data.rccm ?? '');
    setRegime(profile.data.regime);
    setAib(profile.data.aib);
  }, [profile.data]);

  if (!can('fiscal.view')) return <Alert tone="amber">La facture normalisée est réservée aux membres autorisés (permission « Fiscalité »).</Alert>;
  if (currency !== 'XOF') return <Alert tone="blue">La facture normalisée e-MECeF concerne les restaurants qui facturent en francs CFA au Bénin.</Alert>;
  if (profile.loading && profile.data === undefined) return <Spinner />;
  if (profile.error) return <Alert>{profile.error}</Alert>;

  const current = profile.data;
  const ifuValid = /^\d{13}$/.test(ifu.trim());
  const modes: FiscalMode[] = ['Disabled', 'Sandbox', 'Production'];

  return (
    <div className="space-y-6">
      <Alert tone="blue">
        Au Bénin, une facture n’est valable qu’une fois normalisée par la DGI (e-MECeF). Chaque commande terminée est envoyée automatiquement ; le reçu porte ensuite le code MECeF et le QR code. Un remboursement produit une facture d’avoir.
      </Alert>

      <Section icon={<Landmark className="size-5" />} title="Identité fiscale" description="IFU et régime du restaurant, tels qu’enregistrés à la DGI.">
        <form className="grid gap-4 sm:grid-cols-2" onSubmit={event => {
          event.preventDefault();
          void run('profile', async () => {
            await saveProfile({ifu: ifu.trim(), rccm: rccm.trim() || null, regime, aib});
            await profile.reload();
          }, 'Identité fiscale enregistrée.');
        }}>
          <Field label="IFU" hint="13 chiffres.">
            <Input inputMode="numeric" maxLength={13} value={ifu} disabled={!editable} onChange={event => setIfu(event.target.value.replace(/\D/g, ''))} />
          </Field>
          <Field label="RCCM (facultatif)">
            <Input maxLength={40} value={rccm} disabled={!editable} onChange={event => setRccm(event.target.value)} />
          </Field>
          <Field label="Régime fiscal">
            <Select value={regime} disabled={!editable} onChange={event => setRegime(event.target.value as TaxRegime)}>
              {(Object.keys(regimeLabel) as TaxRegime[]).map(value => <option key={value} value={value}>{regimeLabel[value]}</option>)}
            </Select>
          </Field>
          <Field label="AIB" hint="Acompte sur impôt, calculé par la DGI. À confirmer avec votre comptable.">
            <Select value={aib} disabled={!editable} onChange={event => setAib(event.target.value as AibRate)}>
              {(Object.keys(aibLabel) as AibRate[]).map(value => <option key={value} value={value}>{aibLabel[value]}</option>)}
            </Select>
          </Field>
          {current && ifu.trim() !== current.ifu && current.hasToken && (
            <div className="sm:col-span-2"><Alert tone="amber">Changer d’IFU coupe la facturation et efface le jeton actuel : il faudra saisir celui du nouvel IFU.</Alert></div>
          )}
          {editable && <div className="sm:col-span-2"><Button type="submit" busy={busy === 'profile'} disabled={!ifuValid}>Enregistrer</Button></div>}
        </form>
      </Section>

      {current && (
        <Section icon={<KeyRound className="size-5" />} title="Jeton e-MECeF" description="Délivré par la DGI sur le portail e-MECeF. Il est chiffré et n’est plus jamais affiché.">
          <p className="mb-3 text-sm">{current.hasToken ? <>Jeton enregistré{current.tokenUpdatedAtUtc ? ` le ${dateTime(current.tokenUpdatedAtUtc)}` : ''}.</> : 'Aucun jeton enregistré.'}</p>
          {editable && (
            <form className="flex flex-col gap-3 sm:flex-row sm:items-end" onSubmit={event => {
              event.preventDefault();
              void run('token', async () => {
                await saveToken(token.trim());
                setToken('');
                await profile.reload();
              }, 'Jeton enregistré.');
            }}>
              <Field label={current.hasToken ? 'Remplacer le jeton' : 'Jeton'} className="flex-1">
                <Input type="password" autoComplete="off" value={token} onChange={event => setToken(event.target.value)} placeholder="eyJhbGciOi…" />
              </Field>
              <Button type="submit" busy={busy === 'token'} disabled={token.trim().length < 20}>Enregistrer le jeton</Button>
            </form>
          )}
        </Section>
      )}

      {current && (
        <Section icon={<ShieldCheck className="size-5" />} title="Environnement" description="Commencez par le bac à sable : les factures y sont de test, sans valeur légale.">
          <div className="flex flex-wrap gap-2" role="radiogroup" aria-label="Environnement e-MECeF">
            {modes.map(mode => (
              <button key={mode} type="button" role="radio" aria-checked={current.mode === mode} disabled={!editable || busy === 'mode' || (mode !== 'Disabled' && !current.hasToken)}
                onClick={() => void run('mode', async () => {
                  await setMode(mode);
                  await profile.reload();
                }, `Facture normalisée : ${modeLabel[mode].toLowerCase()}.`)}
                className={cx('rounded-xl border px-4 py-2 text-sm font-semibold disabled:opacity-50', current.mode === mode ? 'border-brand-500 bg-brand-50 text-brand-800 ring-1 ring-brand-500' : 'border-slate-200 hover:bg-slate-50')}>
                {modeLabel[mode]}
              </button>
            ))}
          </div>
          {editable && current.hasToken && (
            <div className="mt-4 flex flex-wrap items-center gap-3">
              <Button variant="secondary" busy={busy === 'test'} onClick={() => void run('test', async () => setConnection(await testConnection()))}>Tester la connexion</Button>
              {connection && (
                connection.ifuMatches
                  ? <Badge tone="green">Jeton accepté ({modeLabel[connection.testedMode].toLowerCase()}){connection.nim ? ` · NIM ${connection.nim}` : ''}</Badge>
                  : <Badge tone="red">Jeton accepté, mais pour un autre IFU</Badge>
              )}
            </div>
          )}
        </Section>
      )}

      {current && (
        <Section icon={<FileCheck2 className="size-5" />} title="Factures normalisées" description="Les dernières factures et leur état à la DGI. Une facture en échec se relance après correction.">
          <div className="mb-3 flex items-center gap-2">
            <Select value={filter} onChange={event => setFilter(event.target.value as FiscalInvoiceStatus | '')} className="max-w-56" aria-label="État">
              <option value="">Tous les états</option>
              {(Object.keys(invoiceStatusLabel) as FiscalInvoiceStatus[]).map(value => <option key={value} value={value}>{invoiceStatusLabel[value]}</option>)}
            </Select>
          </div>
          {!invoices.data ? <Spinner /> : invoices.data.length === 0 ? (
            <Empty title="Aucune facture">Les factures apparaissent ici dès qu’une commande est terminée avec la facturation activée.</Empty>
          ) : (
            <ul className="divide-y divide-slate-100">
              {invoices.data.map(invoice => (
                <li key={invoice.id} className="flex flex-col gap-1 py-3 text-sm sm:flex-row sm:items-center sm:gap-3">
                  <span className="min-w-0 flex-1">
                    <span className="font-medium">{invoice.kind === 'Sale' ? 'Vente' : 'Avoir'} · commande {invoice.orderNumber}</span>
                    <span className="block text-xs text-ink-500">
                      {dateTime(invoice.createdAtUtc)} · {money(invoice.total, 'XOF')}{invoice.mode === 'Sandbox' ? ' · test' : ''}
                      {invoice.codeMecef ? ` · ${invoice.codeMecef}` : ''}
                      {invoice.status !== 'Certified' && invoice.errorMessage ? ` · ${invoice.errorMessage}` : ''}
                    </span>
                  </span>
                  <Badge tone={statusTone[invoice.status]}>{invoiceStatusLabel[invoice.status]}</Badge>
                  {editable && invoice.status === 'Failed' && (
                    <Button size="sm" variant="ghost" busy={busy === invoice.id} onClick={() => void run(invoice.id, async () => {
                      await retryInvoice(invoice.id);
                      await invoices.reload();
                    }, 'Facture relancée.')}><RotateCcw className="size-4" aria-hidden="true" />Relancer</Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Section>
      )}
    </div>
  );
}
