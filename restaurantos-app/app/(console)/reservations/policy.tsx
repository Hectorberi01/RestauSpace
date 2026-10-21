'use client';
import {useEffect, useState} from 'react';
import {put} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {getPolicy, type ReservationPolicy} from '@/lib/reservations';
import {getDepositRule, setDepositRule, type DepositBasis, type DepositMode, type DepositRule} from '@/lib/deposits';
import {getWaitlistSettings, setWaitlistSettings, type WaitlistSettings} from '@/lib/waitlist';
import {money, parseAmount} from '@/lib/format';
import type {LocationSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Button, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';

type Unit = 'min' | 'h' | 'j';
const factor: Record<Unit, number> = {min: 1, h: 60, j: 1440};
const unitLabel: Record<Unit, string> = {min: 'minutes', h: 'heures', j: 'jours'};

/** Une durée saisie dans l'unité la plus lisible ; la valeur échangée reste en minutes. */
interface Span {
  amount: string;
  unit: Unit;
}

function toSpan(minutes: number, units: Unit[]): Span {
  const unit = [...units].reverse().find(candidate => minutes % factor[candidate] === 0 && minutes >= factor[candidate]) ?? units[0];
  return {amount: String(minutes / factor[unit]), unit};
}

function minutesOf(span: Span): number | null {
  const value = Number(span.amount.replace(',', '.'));
  if (!span.amount.trim() || !Number.isFinite(value) || value < 0) return null;
  const minutes = value * factor[span.unit];
  return Number.isInteger(minutes) ? minutes : null;
}

function SpanInput({label, hint, value, units, onChange, invalid}: {label: string; hint?: string; value: Span; units: Unit[]; onChange: (span: Span) => void; invalid?: boolean}) {
  return (
    <Field label={label} hint={hint}>
      <div className="flex gap-2">
        <Input inputMode="decimal" aria-label={label} value={value.amount} onChange={event => onChange({...value, amount: event.target.value})}
          aria-invalid={invalid} className={cx('min-w-0 flex-1', invalid && 'border-red-400')} />
        {units.length > 1 && (
          <Select aria-label={`${label} : unité`} className="w-32! shrink-0" value={value.unit} onChange={event => {
            const unit = event.target.value as Unit;
            const minutes = minutesOf(value);
            onChange(minutes !== null && minutes % factor[unit] === 0 ? {amount: String(minutes / factor[unit]), unit} : {...value, unit});
          }}>
            {units.map(unit => <option key={unit} value={unit}>{unitLabel[unit]}</option>)}
          </Select>
        )}
      </div>
    </Field>
  );
}

const readable = (minutes: number) => {
  if (minutes === 0) return '0 min';
  if (minutes % 1440 === 0) return `${minutes / 1440} jour${minutes / 1440 > 1 ? 's' : ''}`;
  if (minutes % 60 === 0) return `${minutes / 60} h`;
  return minutes > 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60}` : `${minutes} min`;
};

interface Form {
  lead: Span;
  advance: string;
  cutoff: Span;
  min: Span;
  standard: Span;
  max: Span;
  party: string;
  online: boolean;
}

const formOf = (policy: ReservationPolicy): Form => ({
  lead: toSpan(policy.minLeadTimeMinutes, ['min', 'h', 'j']),
  advance: String(policy.maxAdvanceDays),
  cutoff: toSpan(policy.cancellationCutoffMinutes, ['min', 'h', 'j']),
  min: toSpan(policy.minDurationMinutes, ['min', 'h']),
  standard: toSpan(policy.defaultDurationMinutes, ['min', 'h']),
  max: toSpan(policy.maxDurationMinutes, ['min', 'h']),
  party: String(policy.maxPartySize),
  online: policy.acceptsOnlineBookings ?? false,
});

/** Les règles d'un établissement, avec les mêmes contrôles que le domaine pour signaler l'erreur avant l'envoi. */
function validate(form: Form): {policy: ReservationPolicy | null; errors: Partial<Record<keyof Form, string>>} {
  const errors: Partial<Record<keyof Form, string>> = {};
  const lead = minutesOf(form.lead);
  const cutoff = minutesOf(form.cutoff);
  const min = minutesOf(form.min);
  const standard = minutesOf(form.standard);
  const max = minutesOf(form.max);
  const advance = Number(form.advance);
  const party = Number(form.party);
  if (lead === null) errors.lead = 'Durée invalide.';
  if (!Number.isInteger(advance) || advance < 1 || advance > 730) errors.advance = 'Entre 1 et 730 jours.';
  else if (lead !== null && advance * 1440 <= lead) errors.advance = 'Doit dépasser le délai minimum.';
  if (cutoff === null) errors.cutoff = 'Durée invalide.';
  else if (cutoff > 730 * 1440) errors.cutoff = 'Deux ans au plus.';
  if (min === null || min <= 0) errors.min = 'Plus de 0 minute.';
  if (max === null || max <= 0 || max > 1440) errors.max = '24 h au plus.';
  else if (min !== null && min > max) errors.max = 'Doit être au moins égale à la durée minimum.';
  if (standard === null || standard <= 0) errors.standard = 'Plus de 0 minute.';
  else if (min !== null && max !== null && (standard < min || standard > max)) errors.standard = 'Entre la durée minimum et la durée maximum.';
  if (!Number.isInteger(party) || party < 1 || party > 10000) errors.party = 'Entre 1 et 10 000 couverts.';
  if (Object.keys(errors).length > 0) return {policy: null, errors};
  return {
    errors,
    policy: {
      minLeadTimeMinutes: lead!,
      maxAdvanceDays: advance,
      defaultDurationMinutes: standard!,
      cancellationCutoffMinutes: cutoff!,
      minDurationMinutes: min!,
      maxDurationMinutes: max!,
      maxPartySize: party,
      acceptsOnlineBookings: form.online,
    },
  };
}

export function PolicyDialog({location, open, onClose, onSaved}: {location: LocationSummary; open: boolean; onClose: () => void; onSaved?: () => void}) {
  const {busy, run} = useAction();
  const {currency} = useConsole();
  const policy = useResource(signal => (open ? getPolicy(location.id, signal) : Promise.resolve(null)), [location.id, open]);
  const extras = useResource(
    signal => (open ? Promise.all([getDepositRule(location.id, signal), getWaitlistSettings(location.id, signal)]) : Promise.resolve(null)),
    [location.id, open]);
  const [form, setForm] = useState<Form | null>(null);
  const [deposit, setDeposit] = useState<DepositRule | null>(null);
  const [depositAmount, setDepositAmount] = useState('');
  const [waitlist, setWaitlist] = useState<WaitlistSettings | null>(null);
  useEffect(() => {
    if (policy.data) setForm(formOf(policy.data));
  }, [policy.data]);
  useEffect(() => {
    if (!extras.data) return;
    setDeposit(extras.data[0]);
    setDepositAmount(extras.data[0].amount ? String(extras.data[0].amount) : '');
    setWaitlist(extras.data[1]);
  }, [extras.data]);
  const amount = parseAmount(depositAmount);
  const depositError = deposit && deposit.mode !== 'Off'
    ? (!amount || amount <= 0 ? 'Indiquez un montant d’acompte.'
      : deposit.paymentWindowMinutes < 5 || deposit.paymentWindowMinutes > 2880 ? 'Délai de paiement entre 5 minutes et 48 heures.'
        : deposit.minPartySize < 1 ? 'Taille de groupe invalide.' : null)
    : null;
  const waitlistError = waitlist && (waitlist.offerWindowMinutes < 5 || waitlist.offerWindowMinutes > 120) ? 'Temps pour accepter une offre entre 5 et 120 minutes.' : null;
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm(current => (current ? {...current, [key]: value} : current));
  const checked = form ? validate(form) : null;
  const result = checked?.policy;

  return (
    <Modal wide open={open} onClose={onClose} title={`Règles de réservation · ${location.name}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="policy-form" busy={busy === 'save'} disabled={!result || !!depositError || !!waitlistError}>Enregistrer</Button>
      </>
    }>
      {policy.error ? <Alert>{policy.error}</Alert> : !form || !checked ? <Spinner /> : (
        <form id="policy-form" className="space-y-6" onSubmit={event => {
          event.preventDefault();
          if (!result || depositError || waitlistError) return;
          void run('save', async () => {
            await put(`/reservations/policies/${location.id}`, result);
            if (deposit) await setDepositRule(location.id, {...deposit, amount: deposit.mode === 'Off' ? null : amount});
            if (waitlist) await setWaitlistSettings(location.id, waitlist);
            onSaved?.();
            onClose();
          }, 'Règles de réservation enregistrées.');
        }}>
          <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={form.online} onChange={event => set('online', event.target.checked)} />
            <span>
              <span className="block text-sm font-medium">Réservation en ligne</span>
              <span className="block text-sm text-ink-500">Vos clients réservent eux-mêmes depuis votre site, dans les règles ci-dessous. La réservation est confirmée tout de suite ; un téléphone est demandé.</span>
            </span>
          </label>

          <section className="space-y-3">
            <h3 className="text-sm font-semibold">Quand réserver</h3>
            <div className="grid gap-4 sm:grid-cols-2">
              <SpanInput label="Délai minimum avant l’arrivée" hint="0 : réservation possible jusqu’au dernier moment. S’applique aussi aux réservations saisies ici."
                value={form.lead} units={['min', 'h', 'j']} invalid={!!checked.errors.lead} onChange={value => set('lead', value)} />
              <Field label="Réservable jusqu’à (jours à l’avance)" hint="De 1 à 730 jours.">
                <Input type="number" min={1} max={730} value={form.advance} onChange={event => set('advance', event.target.value)}
                  aria-invalid={!!checked.errors.advance} className={cx(checked.errors.advance && 'border-red-400')} />
              </Field>
            </div>
          </section>

          <section className="space-y-3">
            <h3 className="text-sm font-semibold">Durée d’une table</h3>
            <div className="grid gap-4 sm:grid-cols-3">
              <SpanInput label="Minimum" value={form.min} units={['min', 'h']} invalid={!!checked.errors.min} onChange={value => set('min', value)} />
              <SpanInput label="Par défaut" hint="Proposée à chaque nouvelle réservation." value={form.standard} units={['min', 'h']} invalid={!!checked.errors.standard} onChange={value => set('standard', value)} />
              <SpanInput label="Maximum" hint="24 h au plus." value={form.max} units={['min', 'h']} invalid={!!checked.errors.max} onChange={value => set('max', value)} />
            </div>
          </section>

          <section className="space-y-3">
            <h3 className="text-sm font-semibold">Groupe et annulation</h3>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Couverts maximum par réservation">
                <Input type="number" min={1} max={10000} value={form.party} onChange={event => set('party', event.target.value)}
                  aria-invalid={!!checked.errors.party} className={cx(checked.errors.party && 'border-red-400')} />
              </Field>
              <SpanInput label="Annulation par le client jusqu’à" hint="Avant l’heure d’arrivée. Votre équipe peut toujours annuler."
                value={form.cutoff} units={['min', 'h', 'j']} invalid={!!checked.errors.cutoff} onChange={value => set('cutoff', value)} />
            </div>
          </section>

          {deposit && (
            <section className="space-y-3">
              <h3 className="text-sm font-semibold">Acompte</h3>
              <p className="text-sm text-ink-500">
                Demandé à la réservation, en ligne ou par votre équipe. Non payé dans le délai, la réservation est annulée et la table libérée.
                Il est déduit de l’addition ; remboursé si le client annule à temps ; gardé en cas d’absence.
              </p>
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Demander un acompte">
                  <Select value={deposit.mode} onChange={event => setDeposit({...deposit, mode: event.target.value as DepositMode})}>
                    <option value="Off">Jamais</option>
                    <option value="FromPartySize">À partir d’un nombre de couverts</option>
                    <option value="Always">Pour toute réservation</option>
                  </Select>
                </Field>
                {deposit.mode === 'FromPartySize' && (
                  <Field label="À partir de (couverts)">
                    <Input type="number" min={1} value={deposit.minPartySize} onChange={event => setDeposit({...deposit, minPartySize: Number(event.target.value) || 0})} />
                  </Field>
                )}
                {deposit.mode !== 'Off' && (
                  <>
                    <Field label={`Montant (${deposit.currencyCode ?? currency})`}>
                      <div className="flex gap-2">
                        <Input inputMode="decimal" className="min-w-0 flex-1" value={depositAmount} onChange={event => setDepositAmount(event.target.value)} />
                        <Select aria-label="Montant par" className="w-40! shrink-0" value={deposit.basis} onChange={event => setDeposit({...deposit, basis: event.target.value as DepositBasis})}>
                          <option value="PerGuest">par couvert</option>
                          <option value="PerReservation">par réservation</option>
                        </Select>
                      </div>
                    </Field>
                    <Field label="Délai pour payer (minutes)" hint="De 5 minutes à 48 heures. Jamais au-delà de l’heure du repas.">
                      <Input type="number" min={5} max={2880} value={deposit.paymentWindowMinutes} onChange={event => setDeposit({...deposit, paymentWindowMinutes: Number(event.target.value) || 0})} />
                    </Field>
                  </>
                )}
              </div>
              {depositError ? <Alert>{depositError}</Alert> : deposit.mode !== 'Off' && amount ? (
                <p className="rounded-xl bg-slate-50 p-3 text-sm text-ink-700">
                  Exemple : pour {Math.max(deposit.mode === 'FromPartySize' ? deposit.minPartySize : 4, 1)} couverts, acompte de{' '}
                  {money(deposit.basis === 'PerGuest' ? amount * Math.max(deposit.mode === 'FromPartySize' ? deposit.minPartySize : 4, 1) : amount, deposit.currencyCode ?? currency)}.
                </p>
              ) : null}
            </section>
          )}

          {waitlist && (
            <section className="space-y-3">
              <h3 className="text-sm font-semibold">Liste d’attente</h3>
              <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3">
                <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={waitlist.acceptsOnline} onChange={event => setWaitlist({...waitlist, acceptsOnline: event.target.checked})} />
                <span>
                  <span className="block text-sm font-medium">Inscription en ligne sur un créneau complet</span>
                  <span className="block text-sm text-ink-500">Quand une table se libère, elle est proposée au premier inscrit par WhatsApp ou SMS ; il a un temps limité pour l’accepter, puis elle passe au suivant. Votre équipe peut toujours inscrire un client.</span>
                </span>
              </label>
              <Field label="Temps pour accepter une table proposée (minutes)" hint="De 5 à 120 minutes. La table est gardée pendant ce temps.">
                <Input type="number" min={5} max={120} value={waitlist.offerWindowMinutes} onChange={event => setWaitlist({...waitlist, offerWindowMinutes: Number(event.target.value) || 0})} />
              </Field>
              {waitlistError && <Alert>{waitlistError}</Alert>}
            </section>
          )}

          {Object.keys(checked.errors).length > 0 ? (
            <Alert>
              <ul className="list-inside list-disc">
                {(Object.entries(checked.errors) as [keyof Form, string][]).map(([key, message]) => <li key={key}>{fieldLabel[key]} : {message}</li>)}
              </ul>
            </Alert>
          ) : result && (
            <p className="rounded-xl bg-slate-50 p-3 text-sm text-ink-700">
              Réservation de {readable(result.minLeadTimeMinutes)} à {result.maxAdvanceDays} jour{result.maxAdvanceDays > 1 ? 's' : ''} à l’avance,
              pour {result.maxPartySize} couvert{result.maxPartySize > 1 ? 's' : ''} au plus. Table gardée {readable(result.defaultDurationMinutes)} par défaut
              (de {readable(result.minDurationMinutes)} à {readable(result.maxDurationMinutes)}).
              {result.cancellationCutoffMinutes === 0 ? ' Le client peut annuler jusqu’à l’heure d’arrivée.' : ` Le client peut annuler jusqu’à ${readable(result.cancellationCutoffMinutes)} avant.`}
              {result.acceptsOnlineBookings ? ' Réservation en ligne ouverte sur votre site.' : ' Pas de réservation en ligne.'}
              {' '}Les réservations déjà prises gardent leur horaire et leur durée ; la limite d’annulation s’applique à toutes.
            </p>
          )}
        </form>
      )}
    </Modal>
  );
}

const fieldLabel: Record<keyof Form, string> = {
  lead: 'Délai minimum',
  advance: 'Réservable jusqu’à',
  cutoff: 'Annulation',
  min: 'Durée minimum',
  standard: 'Durée par défaut',
  max: 'Durée maximum',
  party: 'Couverts maximum',
  online: 'Réservation en ligne',
};
