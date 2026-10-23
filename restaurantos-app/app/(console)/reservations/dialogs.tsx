'use client';
import {useEffect, useState} from 'react';
import {CalendarCheck, Check, Contact, X} from 'lucide-react';
import {isoDate, money} from '@/lib/format';
import {depositFor, getDepositRule} from '@/lib/deposits';
import {joinWaitlist} from '@/lib/waitlist';
import {useResource} from '@/lib/hooks';
import {
  actionLabel, changePartySize, checkAvailability, createReservation, getPolicy, hhmm, localDateTime, normalizePhone, reschedule, runAction, seat,
  type Availability, type FloorState, type Reservation,
} from '@/lib/reservations';
import type {LocationSummary} from '@/lib/types';
import type {Customer} from '@/lib/customers';
import {useConsole} from '@/components/console';
import {CustomerPickerDialog} from '@/components/customer-picker';
import {Alert, Button, Field, Input, Modal, Textarea, cx, useAction} from '@/components/ui';

export function CancelDialog({reservation, onClose, onDone}: {reservation: Reservation | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [reason, setReason] = useState('');
  const [keepDeposit, setKeepDeposit] = useState(false);
  useEffect(() => {
    setReason('');
    setKeepDeposit(false);
  }, [reservation?.id]);
  const paid = reservation?.deposit?.status === 'Paid' ? reservation.deposit : null;
  return (
    <Modal
      open={!!reservation}
      onClose={onClose}
      title="Annuler la réservation"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Retour</Button>
          <Button
            variant="danger"
            busy={busy === 'cancel'}
            onClick={() =>
              reservation &&
              void run('cancel', async () => {
                await runAction(reservation.id, 'cancel', reason, keepDeposit);
                await onDone();
                onClose();
              }, actionLabel.cancel.success)
            }>
            Annuler la réservation
          </Button>
        </>
      }>
      {reservation && (
        <div className="space-y-4">
          <p className="text-sm text-ink-600">
            {reservation.guestName || 'Client'} · {reservation.partySize} pers. · {hhmm(new Date(reservation.startAtUtc))}
          </p>
          <Field label="Motif (facultatif)">
            <Textarea rows={3} maxLength={500} value={reason} onChange={event => setReason(event.target.value)} placeholder="Ex. : demande du client par téléphone" />
          </Field>
          {paid && (
            <div className="rounded-xl bg-slate-50 px-3 py-2 text-sm">
              <label className="flex items-center gap-2 font-medium">
                <input type="checkbox" checked={keepDeposit} onChange={event => setKeepDeposit(event.target.checked)} />
                Garder l’acompte de {money(paid.amount, paid.currencyCode)} (annulation tardive)
              </label>
              <p className="mt-1 text-xs text-ink-500">{keepDeposit ? 'Le restaurant garde l’acompte ; il reste remboursable depuis la fiche.' : 'L’acompte est remboursé au client par le même moyen.'}</p>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

export function NewReservationDialog({location, open, day, onClose, onCreated}: {location: LocationSummary; open: boolean; day: Date; onClose: () => void; onCreated: (at: Date) => Promise<void>}) {
  const {busy, run} = useAction();
  const {can} = useConsole();
  const policy = useResource(signal => getPolicy(location.id, signal).catch(() => null), [location.id]);
  const depositRule = useResource(signal => getDepositRule(location.id, signal).catch(() => null), [location.id]);
  const [withoutDeposit, setWithoutDeposit] = useState(false);
  const [date, setDate] = useState(isoDate(day));
  const [start, setStart] = useState('20:00');
  const [party, setParty] = useState('2');
  const [duration, setDuration] = useState('');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [email, setEmail] = useState('');
  const [notes, setNotes] = useState('');
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [picking, setPicking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [availability, setAvailability] = useState<Availability | null>(null);

  useEffect(() => {
    if (!open) return;
    setDate(isoDate(day));
    setError(null);
  }, [open, day]);

  const at = localDateTime(date, start);
  const size = Number(party);
  const minutes = duration ? Number(duration) : undefined;

  useEffect(() => {
    setAvailability(null);
    if (!open || !at || !Number.isInteger(size) || size < 1) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      checkAvailability(location.id, at, size, minutes, controller.signal).then(setAvailability).catch(() => undefined);
    }, 300);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [open, location.id, at?.getTime(), size, minutes]);

  function reset() {
    setName('');
    setPhone('');
    setEmail('');
    setNotes('');
    setCustomer(null);
    setParty('2');
    setDuration('');
    setWithoutDeposit(false);
    setError(null);
  }

  const deposit = depositFor(depositRule.data, size);

  async function waitlist() {
    setError(null);
    const normalizedPhone = normalizePhone(phone);
    if (!at || !name.trim() || !normalizedPhone) return setError('Pour la liste d’attente : date, heure, nom et téléphone (format international).');
    const ok = await run('waitlist', async () => {
      await joinWaitlist({locationId: location.id, desiredStart: at, partySize: size, flexibilityMinutes: 30, guestName: name.trim(), guestPhone: normalizedPhone, notes: notes.trim() || null});
      await onCreated(at);
    }, 'Client inscrit en liste d’attente : il sera prévenu si une table se libère.');
    if (ok) {
      reset();
      onClose();
    }
  }

  async function submit() {
    setError(null);
    if (!at) return setError('Indiquez une date et une heure valides.');
    if (!Number.isInteger(size) || size < 1) return setError('Indiquez le nombre de couverts.');
    if (policy.data && size > policy.data.maxPartySize) return setError(`Au plus ${policy.data.maxPartySize} couverts par réservation.`);
    if (!name.trim()) return setError('Indiquez le nom du client.');
    const normalizedPhone = normalizePhone(phone);
    if (normalizedPhone === undefined) return setError('Téléphone au format international, par exemple +229 01 97 00 00 00.');
    const trimmedEmail = email.trim();
    if (trimmedEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) return setError('Adresse email invalide.');
    if (!normalizedPhone && !trimmedEmail) return setError('Indiquez au moins un téléphone ou un email.');
    const ok = await run('create', async () => {
      await createReservation({
        locationId: location.id,
        startAt: at,
        partySize: size,
        guestName: name.trim(),
        guestPhone: normalizedPhone,
        guestEmail: trimmedEmail || null,
        notes: notes.trim() || null,
        durationMinutes: minutes && minutes > 0 ? minutes : null,
        customerId: customer?.id ?? null,
        withoutDeposit,
      });
      await onCreated(at);
    }, 'Réservation enregistrée.');
    if (ok) {
      reset();
      onClose();
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Nouvelle réservation"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Annuler</Button>
          {availability && availability.openDuringSlot && !availability.isAvailable && (
            <Button variant="secondary" busy={busy === 'waitlist'} onClick={() => void waitlist()}>Liste d’attente</Button>
          )}
          <Button busy={busy === 'create'} onClick={() => void submit()}>Enregistrer</Button>
        </>
      }>
      <form
        className="space-y-4"
        onSubmit={event => {
          event.preventDefault();
          void submit();
        }}>
        {error && <Alert>{error}</Alert>}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
          <Field label="Date" className="col-span-2"><Input type="date" required value={date} min={isoDate(new Date())} onChange={event => setDate(event.target.value)} /></Field>
          <Field label="Heure" className="sm:col-span-2"><Input type="time" required step={900} value={start} onChange={event => setStart(event.target.value)} /></Field>
          <Field label="Couverts" className="col-span-2 sm:col-span-1"><Input type="number" required min={1} max={policy.data?.maxPartySize} value={party} onChange={event => setParty(event.target.value)} /></Field>
        </div>
        <Field label="Durée (minutes)" hint={policy.data ? `Par défaut : ${policy.data.defaultDurationMinutes} min.` : undefined}>
          <Input type="number" min={policy.data?.minDurationMinutes ?? 15} max={policy.data?.maxDurationMinutes} step={15} value={duration} placeholder={policy.data ? String(policy.data.defaultDurationMinutes) : 'Durée par défaut'} onChange={event => setDuration(event.target.value)} />
        </Field>
        {availability && (
          <p className={cx('flex items-center gap-2 rounded-xl px-3 py-2 text-sm', availability.isAvailable ? 'bg-emerald-50 text-emerald-800' : 'bg-amber-50 text-amber-900')} role="status">
            <CalendarCheck className="size-4 shrink-0" aria-hidden="true" />
            {!availability.openDuringSlot
              ? 'L’établissement est fermé sur ce créneau.'
              : availability.isAvailable
                ? `Disponible : ${availability.remainingSeats} place${availability.remainingSeats > 1 ? 's' : ''} libre${availability.remainingSeats > 1 ? 's' : ''} sur ${availability.capacity}.`
                : `Complet : ${availability.remainingSeats} place${availability.remainingSeats > 1 ? 's' : ''} restante${availability.remainingSeats > 1 ? 's' : ''} sur ${availability.capacity}.`}
          </p>
        )}
        {deposit !== null && depositRule.data && (
          <div className="rounded-xl bg-amber-50 px-3 py-2 text-sm text-amber-900">
            <p>Acompte demandé : <strong>{money(deposit, depositRule.data.currencyCode ?? 'XOF')}</strong>, à payer sous {depositRule.data.paymentWindowMinutes} min (sinon la réservation est annulée). Le client reçoit le lien de paiement.</p>
            <label className="mt-1 flex items-center gap-2 text-xs font-medium">
              <input type="checkbox" checked={withoutDeposit} onChange={event => setWithoutDeposit(event.target.checked)} />Réserver sans acompte (client connu…)
            </label>
          </div>
        )}
        {can('customers.view') && (
          <div className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 text-sm">
            <Contact className="size-4 shrink-0 text-ink-500" aria-hidden="true" />
            <span className="min-w-0 flex-1 truncate">{customer ? <>Fiche : <strong>{customer.name}</strong></> : 'Client habitué ? Rattachez sa fiche.'}</span>
            {customer && <button type="button" aria-label="Détacher la fiche client" onClick={() => setCustomer(null)} className="rounded-lg p-1 text-ink-500 hover:bg-slate-200"><X className="size-4" /></button>}
            <Button size="sm" variant="secondary" onClick={() => setPicking(true)}>{customer ? 'Changer' : 'Choisir'}</Button>
          </div>
        )}
        <Field label="Nom du client"><Input required maxLength={200} value={name} onChange={event => setName(event.target.value)} autoComplete="off" /></Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Téléphone"><Input type="tel" inputMode="tel" value={phone} placeholder="+229 01 97 00 00 00" onChange={event => setPhone(event.target.value)} autoComplete="off" /></Field>
          <Field label="Email"><Input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="off" /></Field>
        </div>
        <Field label="Note (facultatif)"><Textarea rows={2} maxLength={1000} value={notes} onChange={event => setNotes(event.target.value)} placeholder="Allergies, occasion, placement souhaité…" /></Field>
        <button type="submit" hidden />
      </form>
      <CustomerPickerDialog open={picking} initial={{name, phone, email}} onClose={() => setPicking(false)} onPick={picked => {
        setCustomer(picked);
        setName(picked.name);
        setPhone(picked.phone ?? '');
        setEmail(picked.email ?? '');
      }} />
    </Modal>
  );
}

export function EditDialog({reservation, onClose, onDone}: {reservation: Reservation | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [date, setDate] = useState('');
  const [start, setStart] = useState('');
  const [duration, setDuration] = useState('');
  const [party, setParty] = useState('');
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (!reservation) return;
    const at = new Date(reservation.startAtUtc);
    setDate(isoDate(at));
    setStart(hhmm(at));
    setDuration(String(reservation.durationMinutes));
    setParty(String(reservation.partySize));
    setError(null);
  }, [reservation]);

  async function save() {
    if (!reservation) return;
    setError(null);
    const at = localDateTime(date, start);
    const minutes = Number(duration);
    const size = Number(party);
    if (!at) return setError('Indiquez une date et une heure valides.');
    if (!Number.isInteger(minutes) || minutes < 15) return setError('Indiquez une durée d’au moins 15 minutes.');
    if (!Number.isInteger(size) || size < 1) return setError('Indiquez le nombre de couverts.');
    const ok = await run('save', async () => {
      if (at.getTime() !== new Date(reservation.startAtUtc).getTime() || minutes !== reservation.durationMinutes) {
        await reschedule(reservation.id, at, minutes);
      }
      if (size !== reservation.partySize) await changePartySize(reservation.id, size);
      await onDone();
    }, 'Réservation modifiée.');
    if (ok) onClose();
  }

  return (
    <Modal
      open={!!reservation}
      onClose={onClose}
      title="Modifier la réservation"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Annuler</Button>
          <Button busy={busy === 'save'} onClick={() => void save()}>Enregistrer</Button>
        </>
      }>
      {reservation && (
        <form
          className="space-y-4"
          onSubmit={event => {
            event.preventDefault();
            void save();
          }}>
          {error && <Alert>{error}</Alert>}
          <p className="text-sm text-ink-600">{reservation.guestName || 'Client'}</p>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Date"><Input type="date" value={date} onChange={event => setDate(event.target.value)} /></Field>
            <Field label="Heure"><Input type="time" step={900} value={start} onChange={event => setStart(event.target.value)} /></Field>
            <Field label="Durée (minutes)"><Input type="number" min={15} step={15} value={duration} onChange={event => setDuration(event.target.value)} /></Field>
            <Field label="Couverts"><Input type="number" min={1} value={party} onChange={event => setParty(event.target.value)} /></Field>
          </div>
          <button type="submit" hidden />
        </form>
      )}
    </Modal>
  );
}

/** Choix de la table à l'installation : tables en service et libres, celles assez grandes d'abord. */
export function SeatDialog({reservation, floor, onClose, onDone}: {reservation: Reservation | null; floor: FloorState | null | undefined; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [tableId, setTableId] = useState<string | null>(null);
  useEffect(() => setTableId(null), [reservation?.id]);
  const party = reservation?.partySize ?? 0;
  const areas = (floor?.areas ?? [])
    .map(area => ({
      ...area,
      free: area.tables
        .filter(table => table.isInService && table.status === 'Available' && !table.openSessionId)
        .sort((a, b) => Number(b.capacity >= party) - Number(a.capacity >= party) || a.capacity - b.capacity),
    }))
    .filter(area => area.free.length > 0);

  async function confirm(withTable: boolean) {
    if (!reservation) return;
    const ok = await run(withTable ? 'table' : 'none', async () => {
      await seat(reservation, withTable ? tableId : null);
      await onDone();
    }, actionLabel.seat.success);
    if (ok) onClose();
  }

  return (
    <Modal
      open={!!reservation}
      onClose={onClose}
      title="Installer les clients"
      footer={
        <>
          <Button variant="secondary" disabled={!!busy} busy={busy === 'none'} onClick={() => void confirm(false)}>Installer sans table</Button>
          <Button disabled={!tableId || !!busy} busy={busy === 'table'} onClick={() => void confirm(true)}>Installer à cette table</Button>
        </>
      }>
      {reservation && (
        <div className="space-y-4">
          <p className="text-sm text-ink-600">{reservation.guestName || 'Client'} · {reservation.partySize} pers. · {hhmm(new Date(reservation.startAtUtc))}</p>
          {areas.length === 0 ? (
            <Alert tone="amber">Aucune table libre en ce moment. Vous pouvez installer les clients sans table.</Alert>
          ) : (
            areas.map(area => (
              <section key={area.id}>
                <h3 className="mb-2 text-sm font-semibold">{area.name}</h3>
                <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
                  {area.free.map(table => {
                    const small = table.capacity < party;
                    const selected = tableId === table.id;
                    return (
                      <button
                        key={table.id}
                        type="button"
                        aria-pressed={selected}
                        onClick={() => setTableId(table.id)}
                        className={cx(
                          'relative rounded-xl border p-3 text-left transition',
                          selected ? 'border-brand-600 bg-brand-50 ring-2 ring-brand-200' : 'border-slate-200 hover:border-slate-300',
                        )}>
                        {selected && <Check className="absolute top-2 right-2 size-4 text-brand-700" aria-hidden="true" />}
                        <span className="block font-semibold">{table.label}</span>
                        <span className={cx('block text-xs', small ? 'text-amber-700' : 'text-ink-500')}>{table.capacity} places{small ? ' · trop petite' : ''}</span>
                      </button>
                    );
                  })}
                </div>
              </section>
            ))
          )}
        </div>
      )}
    </Modal>
  );
}
