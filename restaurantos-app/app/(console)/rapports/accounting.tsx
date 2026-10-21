'use client';
import {useEffect, useState} from 'react';
import {BookOpen, Download, RotateCcw} from 'lucide-react';
import {downloadFile} from '@/lib/api';
import {isoDate} from '@/lib/format';
import {defaultAccountingPlan, exportAccounting, paymentAccountLabel, type AccountingPlan, type Period} from '@/lib/reports';
import {Button, Field, Input, useAction} from '@/components/ui';

const storageKey = 'restaurantos.accounting-plan';
const code = /^[A-Za-z0-9]{1,20}$/;

function readPlan(): AccountingPlan {
  try {
    const saved = window.localStorage.getItem(storageKey);
    if (!saved) return defaultAccountingPlan;
    const parsed = JSON.parse(saved) as Partial<AccountingPlan>;
    return {...defaultAccountingPlan, ...parsed, paymentAccounts: {...defaultAccountingPlan.paymentAccounts, ...parsed.paymentAccounts}};
  } catch {
    return defaultAccountingPlan;
  }
}

function savePlan(plan: AccountingPlan) {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(plan));
  } catch {
    // Stockage indisponible : le plan vaut pour cette fois.
  }
}

const mainAccounts: {key: keyof Omit<AccountingPlan, 'paymentAccounts'>; label: string}[] = [
  {key: 'salesJournal', label: 'Journal des ventes'},
  {key: 'treasuryJournal', label: 'Journal de trésorerie'},
  {key: 'customersAccount', label: 'Clients'},
  {key: 'salesAccount', label: 'Ventes (HT)'},
  {key: 'vatAccount', label: 'TVA facturée'},
  {key: 'defaultPaymentAccount', label: 'Autres moyens de paiement'},
];

/**
 * Export comptable : journal des ventes (clients / ventes HT / TVA) et de la trésorerie (un compte par moyen de
 * paiement), par jour, en CSV pour le logiciel du comptable. Le plan de comptes est retenu sur ce poste.
 */
export function AccountingExport({period}: {period: Period}) {
  const {busy, run} = useAction();
  const [from, setFrom] = useState(isoDate(period.from));
  const [to, setTo] = useState(isoDate(period.to));
  const [plan, setPlan] = useState<AccountingPlan>(defaultAccountingPlan);
  const [editing, setEditing] = useState(false);
  useEffect(() => setPlan(readPlan()), []);

  const codes = [...mainAccounts.map(item => plan[item.key]), ...Object.values(plan.paymentAccounts)];
  const valid = codes.every(value => code.test(value));

  return (
    <form className="space-y-3" onSubmit={event => {
      event.preventDefault();
      void run('accounting', async () => {
        const start = new Date(`${from}T00:00:00`);
        const end = new Date(`${to}T00:00:00`);
        if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end < start) throw new Error('Période invalide.');
        savePlan(plan);
        const id = await exportAccounting({from: start, to: end}, plan);
        await downloadFile(`/reporting/exports/${id}/file`, `journal-comptable-${from}-${to}.csv`);
      }, 'Journal comptable téléchargé.');
    }}>
      <div className="grid grid-cols-2 gap-2">
        <Field label="Du"><Input type="date" required value={from} onChange={event => setFrom(event.target.value)} /></Field>
        <Field label="Au"><Input type="date" required value={to} onChange={event => setTo(event.target.value)} /></Field>
      </div>
      <button type="button" aria-expanded={editing} onClick={() => setEditing(current => !current)} className="flex items-center gap-2 text-sm font-semibold text-brand-700 hover:underline">
        <BookOpen className="size-4" aria-hidden="true" />{editing ? 'Masquer le plan de comptes' : 'Plan de comptes (SYSCOHADA par défaut)'}
      </button>
      {editing && (
        <div className="space-y-3 rounded-xl bg-slate-50 p-3">
          <div className="grid grid-cols-2 gap-2">
            {mainAccounts.map(item => (
              <Field key={item.key} label={item.label}>
                <Input value={plan[item.key]} maxLength={20} aria-invalid={!code.test(plan[item.key])}
                  onChange={event => setPlan(current => ({...current, [item.key]: event.target.value.trim()}))} />
              </Field>
            ))}
          </div>
          <p className="text-xs font-semibold tracking-wide text-ink-500 uppercase">Comptes de trésorerie</p>
          <div className="grid grid-cols-2 gap-2">
            {Object.keys(defaultAccountingPlan.paymentAccounts).map(method => (
              <Field key={method} label={paymentAccountLabel[method] ?? method}>
                <Input value={plan.paymentAccounts[method] ?? ''} maxLength={20} aria-invalid={!code.test(plan.paymentAccounts[method] ?? '')}
                  onChange={event => setPlan(current => ({...current, paymentAccounts: {...current.paymentAccounts, [method]: event.target.value.trim()}}))} />
              </Field>
            ))}
          </div>
          {!valid && <p className="text-xs text-red-600">Chaque code : 1 à 20 lettres ou chiffres.</p>}
          <Button type="button" size="sm" variant="ghost" onClick={() => setPlan(defaultAccountingPlan)}><RotateCcw className="size-4" aria-hidden="true" />Plan par défaut</Button>
        </div>
      )}
      <p className="text-xs text-ink-500">Par jour : ventes (clients au débit, ventes HT et TVA au crédit) et encaissements par moyen de paiement. Fichier CSV point-virgule, prêt à importer.</p>
      <Button type="submit" className="w-full" busy={busy === 'accounting'} disabled={!valid}><Download className="size-4" aria-hidden="true" />Télécharger le journal</Button>
    </form>
  );
}
