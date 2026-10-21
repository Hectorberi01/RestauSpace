'use client';
import {CalendarDays, Clock, Mail, MessageSquareText, Minus, Pencil, Phone, Plus, Send, Trash2, Users, Armchair, X} from 'lucide-react';
import {
  actionLabel, actionsFor, avatarOf, changePartySize, durationLabel, endOf, hhmm, reservationNumber, reservationStatusLabel, reservationStatusTone,
  runAction, tableOf, type FloorState, type Reservation, type ReservationAction,
} from '@/lib/reservations';
import {useConsole} from '@/components/console';
import {Badge, Button, cx, useAction} from '@/components/ui';
import {LinkedCustomer} from '@/components/customer-picker';
import {setReservationCustomer} from '@/lib/customers';
import {DepositSection} from './deposit';

const longDate = new Intl.DateTimeFormat('fr-FR', {weekday: 'long', day: 'numeric', month: 'long', year: 'numeric'});

const targetLabel: Record<ReservationAction, string> = {
  confirm: 'Confirmée',
  seat: 'Installée',
  complete: 'Terminée',
  'no-show': 'Absent',
  cancel: 'Annulée',
};

const statusDot: Record<Reservation['status'], string> = {
  Pending: 'bg-amber-500',
  Confirmed: 'bg-emerald-500',
  Seated: 'bg-sky-500',
  Completed: 'bg-slate-400',
  Cancelled: 'bg-red-500',
  NoShow: 'bg-red-500',
};

export function Avatar({name, size = 'md'}: {name: string | null; size?: 'sm' | 'md' | 'lg'}) {
  const {initials, color} = avatarOf(name);
  return (
    <span className={cx('grid shrink-0 place-items-center rounded-full font-semibold', color, size === 'sm' ? 'size-8 text-xs' : size === 'lg' ? 'size-12 text-base' : 'size-10 text-sm')} aria-hidden="true">
      {initials}
    </span>
  );
}

const linkButton = 'inline-flex h-10 flex-1 items-center justify-center gap-2 rounded-xl border border-slate-200 bg-white px-3 text-sm font-semibold text-ink-800 transition hover:bg-slate-50';

export function DetailPanel({
  reservation, floor, onClose, onChanged, onCancel, onSeat, onEdit,
}: {
  reservation: Reservation;
  floor: FloorState | null | undefined;
  onClose?: () => void;
  onChanged: () => Promise<void>;
  onCancel: () => void;
  onSeat: () => void;
  onEdit: () => void;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const manage = can('reservations.manage');
  const start = new Date(reservation.startAtUtc);
  const table = tableOf(reservation, floor);
  const editable = manage && (reservation.status === 'Pending' || reservation.status === 'Confirmed');
  const actions = manage ? actionsFor(reservation.status) : [];
  const phone = reservation.guestPhone ?? null;
  const email = reservation.guestEmail ?? null;
  const message = phone ? `https://wa.me/${phone.replace(/^\+/, '')}` : email ? `mailto:${email}` : null;

  function apply(action: ReservationAction) {
    if (action === 'cancel') return onCancel();
    if (action === 'seat') return onSeat();
    void run(action, async () => {
      await runAction(reservation.id, action);
      await onChanged();
    }, actionLabel[action].success);
  }

  function resize(delta: number) {
    const size = reservation.partySize + delta;
    if (size < 1) return;
    void run('party', async () => {
      await changePartySize(reservation.id, size);
      await onChanged();
    });
  }

  return (
    <div className="flex flex-col">
      <div className="flex items-start justify-between gap-3 border-b border-slate-100 pb-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-bold">Réservation {reservationNumber(reservation)}</h2>
            <Badge tone={reservationStatusTone[reservation.status]}>{reservationStatusLabel[reservation.status]}</Badge>
          </div>
          <p className="mt-2 flex items-center gap-2 text-sm text-ink-600 capitalize"><CalendarDays className="size-4 text-ink-500" aria-hidden="true" />{longDate.format(start)}</p>
          <p className="mt-1 flex items-center gap-2 text-sm text-ink-600"><Clock className="size-4 text-ink-500" aria-hidden="true" />{hhmm(start)} – {hhmm(endOf(reservation))} ({durationLabel(reservation.durationMinutes)})</p>
        </div>
        {onClose && (
          <button type="button" onClick={onClose} className="rounded-lg p-1.5 text-ink-500 hover:bg-slate-100" aria-label="Fermer le détail">
            <X className="size-5" />
          </button>
        )}
      </div>

      <div className="flex items-center gap-3 border-b border-slate-100 py-4">
        <Avatar name={reservation.guestName} size="lg" />
        <div className="min-w-0 text-sm">
          <p className="truncate text-base font-semibold">{reservation.guestName || 'Client'}</p>
          {phone && <a href={`tel:${phone}`} className="block text-ink-600 hover:text-brand-700">{phone}</a>}
          {email && <a href={`mailto:${email}`} className="block truncate text-ink-600 hover:text-brand-700">{email}</a>}
        </div>
      </div>

      <LinkedCustomer className="border-b border-slate-100 py-3" customerId={reservation.customerId}
        initial={{name: reservation.guestName ?? '', phone: phone ?? '', email: email ?? ''}}
        editable={manage && !['Completed', 'Cancelled', 'NoShow'].includes(reservation.status)}
        onChange={async customerId => {
          await setReservationCustomer(reservation.id, customerId);
          await onChanged();
        }} />

      <div className="space-y-3 border-b border-slate-100 py-4 text-sm">
        <div className="flex items-center gap-3">
          <Users className="size-5 shrink-0 text-ink-500" aria-hidden="true" />
          <span className="flex-1">{reservation.partySize} personne{reservation.partySize > 1 ? 's' : ''}</span>
          {editable && (
            <span className="flex gap-1.5">
              <button type="button" disabled={!!busy || reservation.partySize <= 1} onClick={() => resize(-1)} className="grid size-9 place-items-center rounded-lg border border-slate-200 hover:bg-slate-50 disabled:opacity-40" aria-label="Retirer un couvert">
                <Minus className="size-4" />
              </button>
              <button type="button" disabled={!!busy} onClick={() => resize(1)} className="grid size-9 place-items-center rounded-lg border border-slate-200 hover:bg-slate-50 disabled:opacity-40" aria-label="Ajouter un couvert">
                <Plus className="size-4" />
              </button>
            </span>
          )}
        </div>
        <div className="flex items-center gap-3">
          <Armchair className="size-5 shrink-0 text-ink-500" aria-hidden="true" />
          <span className="flex-1">{table ? `Table ${table.label} · ${table.areaName}` : reservation.status === 'Seated' ? 'Installée sans table' : 'Table attribuée à l’installation'}</span>
          {manage && reservation.status === 'Confirmed' && <Button size="sm" variant="secondary" onClick={onSeat}>Installer</Button>}
        </div>
      </div>

      <DepositSection reservation={reservation} onChanged={onChanged} />

      {(reservation.notes || reservation.cancellationReason) && (
        <div className="space-y-3 border-b border-slate-100 py-4 text-sm">
          {reservation.notes && (
            <div className="flex gap-3">
              <MessageSquareText className="mt-0.5 size-5 shrink-0 text-ink-500" aria-hidden="true" />
              <div>
                <p className="font-semibold">Demandes spéciales</p>
                <p className="whitespace-pre-line text-ink-600">{reservation.notes}</p>
              </div>
            </div>
          )}
          {reservation.cancellationReason && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-red-800">
              Annulée par {reservation.cancelledBy === 'Guest' ? 'le client' : 'le restaurant'} : {reservation.cancellationReason}
            </p>
          )}
        </div>
      )}

      <div className="py-4">
        <label htmlFor={`statut-${reservation.id}`} className="mb-1.5 block text-sm font-medium">Statut</label>
        <div className="relative">
          <span className={cx('pointer-events-none absolute top-1/2 left-3 size-2.5 -translate-y-1/2 rounded-full', statusDot[reservation.status])} aria-hidden="true" />
          <select
            id={`statut-${reservation.id}`}
            value=""
            disabled={actions.length === 0 || !!busy}
            onChange={event => event.target.value && apply(event.target.value as ReservationAction)}
            className="h-11 w-full appearance-none rounded-xl border border-slate-300 bg-white pr-9 pl-8 text-sm font-semibold text-ink-900 focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none disabled:bg-slate-50">
            <option value="">{reservationStatusLabel[reservation.status]}</option>
            {actions.map(action => (
              <option key={action} value={action}>→ {targetLabel[action]}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="space-y-2">
        {editable && (
          <Button className="w-full" onClick={onEdit}><Pencil className="size-4" aria-hidden="true" />Modifier la réservation</Button>
        )}
        <div className="flex gap-2">
          {message ? (
            <a href={message} target={phone ? '_blank' : undefined} rel="noreferrer" className={linkButton}>
              {phone ? <Send className="size-4" aria-hidden="true" /> : <Mail className="size-4" aria-hidden="true" />}Message
            </a>
          ) : null}
          {phone && (
            <a href={`tel:${phone}`} className={linkButton}><Phone className="size-4" aria-hidden="true" />Appeler</a>
          )}
        </div>
        {manage && actions.includes('cancel') && (
          <button type="button" onClick={onCancel} className="inline-flex h-10 w-full items-center justify-center gap-2 rounded-xl border border-red-200 bg-white text-sm font-semibold text-red-600 transition hover:bg-red-50">
            <Trash2 className="size-4" aria-hidden="true" />Annuler la réservation
          </button>
        )}
      </div>
    </div>
  );
}
