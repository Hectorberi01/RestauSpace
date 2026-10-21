'use client';
import {useEffect, useState} from 'react';
import {CalendarOff, CalendarPlus, Plus, Trash2, X} from 'lucide-react';
import {isoDate} from '@/lib/format';
import {addClosure, addOpening, formatRange, overlaps, range, removeException, type LocationDetail, type TimeRange} from '@/lib/locations';
import {Badge, Button, Empty, Field, Input, Modal, cx, useAction} from '@/components/ui';
import {TimeInput} from './hours';

function longDate(date: string) {
  return new Date(`${date}T12:00:00`).toLocaleDateString('fr-FR', {weekday: 'long', day: 'numeric', month: 'long', year: 'numeric'});
}

function ExceptionDialog({location, open, onClose, onDone}: {location: LocationDetail; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [date, setDate] = useState('');
  const [closed, setClosed] = useState(true);
  const [ranges, setRanges] = useState<TimeRange[]>([range('11:00', '15:00')]);
  const [reason, setReason] = useState('');
  useEffect(() => {
    if (!open) return;
    setDate(isoDate(new Date()));
    setClosed(true);
    setRanges([range('11:00', '15:00')]);
    setReason('');
  }, [open]);
  const taken = location.exceptionalSchedules.some(item => item.date === date);
  const invalid = !date || taken || (!closed && (ranges.length === 0 || overlaps(ranges)));

  return (
    <Modal open={open} onClose={onClose} title="Jour exceptionnel" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="exception-form" busy={busy === 'save'} disabled={invalid}>Ajouter</Button>
      </>
    }>
      <form id="exception-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('save', async () => {
          if (closed) await addClosure(location.id, date, reason.trim() || null);
          else await addOpening(location.id, date, ranges, reason.trim() || null);
          await onDone();
          onClose();
        }, closed ? 'Fermeture exceptionnelle ajoutée.' : 'Horaires exceptionnels ajoutés.');
      }}>
        <Field label="Date" hint={taken ? 'Ce jour a déjà des horaires exceptionnels : retirez-les d’abord.' : undefined}>
          <Input type="date" required min={isoDate(new Date())} value={date} onChange={event => setDate(event.target.value)} />
        </Field>
        <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Type de jour">
          {[
            {value: true, label: 'Fermé toute la journée', icon: CalendarOff},
            {value: false, label: 'Horaires différents', icon: CalendarPlus},
          ].map(option => (
            <button key={String(option.value)} type="button" role="radio" aria-checked={closed === option.value} onClick={() => setClosed(option.value)}
              className={cx('flex items-center gap-2 rounded-xl border p-3 text-left text-sm font-medium', closed === option.value ? 'border-brand-500 bg-brand-50 ring-1 ring-brand-500' : 'border-slate-200 hover:border-slate-300')}>
              <option.icon className="size-4 shrink-0" aria-hidden="true" />{option.label}
            </button>
          ))}
        </div>
        {!closed && (
          <div className="space-y-2">
            {ranges.map((item, index) => (
              <div key={index} className="flex flex-wrap items-center gap-2">
                <TimeInput label={`Ouverture ${index + 1}`} disabled={false} value={item.start} onChange={value => setRanges(current => current.map((r, i) => (i === index ? range(value, r.end) : r)))} />
                <span aria-hidden="true">–</span>
                <TimeInput label={`Fermeture ${index + 1}`} disabled={false} value={item.end} onChange={value => setRanges(current => current.map((r, i) => (i === index ? range(r.start, value) : r)))} />
                {item.endsNextDay && <span className="text-xs text-violet-700">lendemain</span>}
                {ranges.length > 1 && (
                  <button type="button" aria-label={`Retirer la plage ${index + 1}`} onClick={() => setRanges(current => current.filter((_, i) => i !== index))} className="grid size-8 place-items-center rounded-lg text-ink-500 hover:bg-slate-100"><X className="size-4" /></button>
                )}
              </div>
            ))}
            {overlaps(ranges) && <p className="text-xs text-red-600">Les plages se chevauchent.</p>}
            {ranges.length < 3 && <Button size="sm" variant="ghost" onClick={() => setRanges(current => [...current, range('18:00', '23:00')])}><Plus className="size-4" aria-hidden="true" />Ajouter une plage</Button>}
          </div>
        )}
        <Field label="Motif (facultatif)" hint="Visible par l’équipe et par vos clients sur votre site et votre application.">
          <Input maxLength={200} value={reason} onChange={event => setReason(event.target.value)} placeholder="Fête de l’indépendance, soirée privée…" />
        </Field>
      </form>
    </Modal>
  );
}

export function Exceptions({location, editable, onChanged}: {location: LocationDetail; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [adding, setAdding] = useState(false);
  const today = isoDate(new Date());
  const upcoming = location.exceptionalSchedules.filter(item => item.date >= today).sort((a, b) => a.date.localeCompare(b.date));
  const past = location.exceptionalSchedules.filter(item => item.date < today).length;

  return (
    <div>
      {upcoming.length === 0 ? (
        <Empty icon={<CalendarOff className="size-8" />} title="Aucun jour exceptionnel à venir">
          Jours fériés, congés, événements : les réservations suivent ces horaires.
        </Empty>
      ) : (
        <ul className="divide-y divide-slate-100">
          {upcoming.map(item => (
            <li key={item.date} className="flex items-center gap-3 py-3">
              <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl', item.isClosed ? 'bg-rose-50 text-rose-600' : 'bg-sky-50 text-sky-600')} aria-hidden="true">
                {item.isClosed ? <CalendarOff className="size-5" /> : <CalendarPlus className="size-5" />}
              </span>
              <div className="min-w-0 flex-1 text-sm">
                <p className="font-medium first-letter:uppercase">{longDate(item.date)}</p>
                <p className="text-ink-500">
                  {item.isClosed ? <Badge tone="red">Fermé</Badge> : item.ranges.map(formatRange).join(', ')}
                  {item.reason && <span className="ml-2">{item.reason}</span>}
                </p>
              </div>
              {editable && (
                <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Retirer le ${longDate(item.date)}`} busy={busy === item.date}
                  onClick={() => run(item.date, async () => {
                    await removeException(location.id, item.date);
                    await onChanged();
                  }, 'Jour exceptionnel retiré.')}><Trash2 className="size-4" aria-hidden="true" /></Button>
              )}
            </li>
          ))}
        </ul>
      )}
      <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
        {editable && <Button variant="secondary" onClick={() => setAdding(true)}><Plus className="size-4" aria-hidden="true" />Ajouter un jour</Button>}
        {past > 0 && <p className="text-xs text-ink-500">{past} jour{past > 1 ? 's' : ''} passé{past > 1 ? 's' : ''} non affiché{past > 1 ? 's' : ''}.</p>}
      </div>
      <ExceptionDialog location={location} open={adding} onClose={() => setAdding(false)} onDone={onChanged} />
    </div>
  );
}
