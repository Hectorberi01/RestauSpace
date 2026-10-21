'use client';
import {useState} from 'react';
import {CalendarPlus, Check, Clock, MapPin, Repeat, Users, X} from 'lucide-react';
import {isoDate} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  answerSwap, cancelMySwap, cancelMyTimeOff, getMySchedule, hours, longDayLabel, mondayOf, period, requestSwap, requestTimeOff, shiftTime,
  timeOffKindLabel, timeOffStatusLabel, timeOffStatusTone, type MyShift, type TimeOffKind,
} from '@/lib/schedule';
import {Alert, Badge, Button, Empty, Field, Input, Modal, Select, Spinner, Textarea, useAction} from '@/components/ui';
import {SwapLine} from './swaps';

const WEEKS = 4;

/** Le planning publié du membre connecté sur quatre semaines, ses remplacements et ses absences. */
export function MySchedule() {
  const {busy, run} = useAction();
  const from = isoDate(mondayOf(new Date()));
  const data = useResource(signal => getMySchedule(from, WEEKS, signal), [from]);
  const [offering, setOffering] = useState<MyShift | null>(null);
  const [asking, setAsking] = useState(false);

  if (data.error) return <Alert>{data.error}</Alert>;
  const schedule = data.data;
  if (!schedule) return <Spinner label="Chargement de votre planning…" />;

  const now = Date.now();
  const upcoming = schedule.shifts.filter(item => new Date(item.startsAtUtc).getTime() + item.shift.plannedMinutes * 60_000 > now);
  const days = [...new Set(upcoming.map(item => item.shift.date))];
  const minutesThisWeek = upcoming.filter(item => item.shift.date < isoDate(new Date(mondayOf(new Date()).getTime() + 7 * 86_400_000))).reduce((sum, item) => sum + item.shift.plannedMinutes, 0);
  const incoming = schedule.swaps.filter(swap => swap.toMemberId === schedule.staffMemberId && swap.status === 'AwaitingColleague');
  const outgoing = schedule.swaps.filter(swap => swap.fromMemberId === schedule.staffMemberId || (swap.toMemberId === schedule.staffMemberId && swap.status !== 'AwaitingColleague'));
  const openOn = (shiftId: string) => schedule.swaps.find(swap => swap.shiftId === shiftId && (swap.status === 'AwaitingColleague' || swap.status === 'AwaitingManager'));

  return (
    <div className="space-y-6">
      {incoming.length > 0 && (
        <section>
          <h2 className="mb-2 text-base font-semibold">On vous propose un créneau</h2>
          <ul className="divide-y divide-slate-100 rounded-xl border border-blue-200 bg-blue-50/40">
            {incoming.map(swap => (
              <SwapLine key={swap.id} swap={swap}>
                <Button variant="secondary" size="sm" busy={busy === `no-${swap.id}`} onClick={() => void run(`no-${swap.id}`, async () => {
                  await answerSwap(swap.id, false);
                  await data.reload();
                }, 'Proposition refusée.')}><X className="size-4" aria-hidden="true" />Refuser</Button>
                <Button variant="success" size="sm" busy={busy === `ok-${swap.id}`} onClick={() => void run(`ok-${swap.id}`, async () => {
                  await answerSwap(swap.id, true);
                  await data.reload();
                }, 'Accepté : le gérant doit encore valider.')}><Check className="size-4" aria-hidden="true" />Accepter</Button>
              </SwapLine>
            ))}
          </ul>
        </section>
      )}

      <section>
        <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="text-base font-semibold">Mes prochains créneaux</h2>
          <p className="text-sm text-ink-500">Cette semaine : {hours(minutesThisWeek)} prévues</p>
        </div>
        {days.length === 0 ? (
          <Empty title="Aucun créneau publié">Votre planning apparaît ici dès que le gérant le publie.</Empty>
        ) : (
          <div className="space-y-4">
            {days.map(date => (
              <div key={date}>
                <p className="mb-1.5 text-sm font-semibold text-ink-700 first-letter:uppercase">{longDayLabel(date)}</p>
                <ul className="space-y-2">
                  {upcoming.filter(item => item.shift.date === date).map(item => {
                    const open = openOn(item.shift.id);
                    return (
                      <li key={item.shift.id} className="flex flex-col gap-3 rounded-xl border border-slate-200 p-4 sm:flex-row sm:items-center">
                        <div className="min-w-0 flex-1 text-sm">
                          <p className="flex items-center gap-2 text-base font-semibold tabular-nums"><Clock className="size-4 text-ink-500" aria-hidden="true" />{shiftTime(item.shift)}
                            {item.shift.position && <Badge tone="brand">{item.shift.position}</Badge>}
                          </p>
                          <p className="mt-1 flex items-center gap-2 text-ink-600"><MapPin className="size-4 text-ink-400" aria-hidden="true" />{item.locationName}
                            {item.shift.breakMinutes > 0 && <span>· pause {item.shift.breakMinutes} min</span>}
                          </p>
                          {item.colleagues.length > 0 && (
                            <p className="mt-1 flex items-center gap-2 text-ink-600"><Users className="size-4 text-ink-400" aria-hidden="true" />Avec {item.colleagues.join(', ')}</p>
                          )}
                          {item.shift.note && <p className="mt-1 text-ink-500">{item.shift.note}</p>}
                        </div>
                        {open ? (
                          <Badge tone="blue">Remplacement en cours</Badge>
                        ) : new Date(item.startsAtUtc).getTime() > now && (
                          <Button variant="secondary" size="sm" onClick={() => setOffering(item)}><Repeat className="size-4" aria-hidden="true" />Proposer à un collègue</Button>
                        )}
                      </li>
                    );
                  })}
                </ul>
              </div>
            ))}
          </div>
        )}
      </section>

      {outgoing.length > 0 && (
        <section>
          <h2 className="mb-2 text-base font-semibold">Mes remplacements</h2>
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {outgoing.map(swap => (
              <SwapLine key={swap.id} swap={swap}>
                {swap.fromMemberId === schedule.staffMemberId && (swap.status === 'AwaitingColleague' || swap.status === 'AwaitingManager') && (
                  <Button variant="ghost" size="sm" busy={busy === `cancel-${swap.id}`} onClick={() => void run(`cancel-${swap.id}`, async () => {
                    await cancelMySwap(swap.id);
                    await data.reload();
                  }, 'Proposition retirée.')}>Retirer</Button>
                )}
              </SwapLine>
            ))}
          </ul>
        </section>
      )}

      <section>
        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-base font-semibold">Mes absences</h2>
          <Button variant="secondary" size="sm" onClick={() => setAsking(true)}><CalendarPlus className="size-4" aria-hidden="true" />Demander une absence</Button>
        </div>
        {schedule.timeOff.length === 0 ? (
          <p className="rounded-xl bg-slate-50 px-4 py-3 text-sm text-ink-500">Aucune absence demandée.</p>
        ) : (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {schedule.timeOff.map(absence => (
              <li key={absence.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
                <span className="min-w-0 flex-1">
                  {timeOffKindLabel[absence.kind]} · {period(absence)}
                  {absence.decisionNote && <span className="block text-xs text-ink-500">Réponse : {absence.decisionNote}</span>}
                </span>
                <Badge tone={timeOffStatusTone[absence.status]}>{timeOffStatusLabel[absence.status]}</Badge>
                {absence.status === 'Pending' && (
                  <Button variant="ghost" size="sm" busy={busy === `withdraw-${absence.id}`} onClick={() => void run(`withdraw-${absence.id}`, async () => {
                    await cancelMyTimeOff(absence.id);
                    await data.reload();
                  }, 'Demande retirée.')}>Retirer</Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {offering && (
        <OfferDialog shift={offering} colleagues={schedule.locations.find(place => place.locationId === offering.locationId)?.colleagues ?? []}
          onClose={() => setOffering(null)} onDone={async () => {
            setOffering(null);
            await data.reload();
          }} />
      )}
      {asking && <TimeOffDialog onClose={() => setAsking(false)} onDone={async () => {
        setAsking(false);
        await data.reload();
      }} />}
    </div>
  );
}

function OfferDialog({shift, colleagues, onClose, onDone}: {
  shift: MyShift;
  colleagues: {staffMemberId: string; name: string}[];
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [to, setTo] = useState(colleagues[0]?.staffMemberId ?? '');
  const [note, setNote] = useState('');
  return (
    <Modal open onClose={onClose} title="Proposer mon créneau" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button disabled={!to} busy={busy === 'offer'} onClick={() => void run('offer', async () => {
          await requestSwap({weekId: shift.weekId, shiftId: shift.shift.id, toStaffMemberId: to, note: note.trim() || null});
          await onDone();
        }, 'Proposition envoyée à votre collègue.')}>Envoyer</Button>
      </>
    }>
      <p className="mb-4 text-sm text-ink-700"><span className="inline-block first-letter:uppercase">{longDayLabel(shift.shift.date)}</span> · {shiftTime(shift.shift)} · {shift.locationName}</p>
      {colleagues.length === 0 ? <Alert tone="amber">Aucun collègue ne travaille dans cet établissement.</Alert> : (
        <div className="space-y-4">
          <Field label="Collègue">
            <Select value={to} onChange={event => setTo(event.target.value)}>
              {colleagues.map(colleague => <option key={colleague.staffMemberId} value={colleague.staffMemberId}>{colleague.name}</option>)}
            </Select>
          </Field>
          <Field label="Message (facultatif)">
            <Textarea rows={2} maxLength={200} value={note} onChange={event => setNote(event.target.value)} />
          </Field>
          <p className="text-xs text-ink-500">Votre collègue accepte, puis le gérant valide. Jusque-là, le créneau reste le vôtre.</p>
        </div>
      )}
    </Modal>
  );
}

function TimeOffDialog({onClose, onDone}: {onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const today = isoDate(new Date());
  const [form, setForm] = useState({firstDay: today, lastDay: today, kind: 'Leave' as TimeOffKind, comment: ''});
  return (
    <Modal open onClose={onClose} title="Demander une absence" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'ask'} onClick={() => void run('ask', async () => {
          await requestTimeOff({firstDay: form.firstDay, lastDay: form.lastDay < form.firstDay ? form.firstDay : form.lastDay, kind: form.kind, comment: form.comment.trim() || null});
          await onDone();
        }, 'Demande envoyée au gérant.')}>Envoyer</Button>
      </>
    }>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Premier jour"><Input type="date" min={today} value={form.firstDay} onChange={event => setForm({...form, firstDay: event.target.value})} /></Field>
        <Field label="Dernier jour"><Input type="date" min={form.firstDay} value={form.lastDay} onChange={event => setForm({...form, lastDay: event.target.value})} /></Field>
        <Field label="Type" className="sm:col-span-2">
          <Select value={form.kind} onChange={event => setForm({...form, kind: event.target.value as TimeOffKind})}>
            <option value="Leave">Congé</option>
            <option value="Unavailable">Indisponible</option>
          </Select>
        </Field>
        <Field label="Commentaire (facultatif)" hint="Inutile de donner un motif personnel." className="sm:col-span-2">
          <Textarea rows={2} maxLength={200} value={form.comment} onChange={event => setForm({...form, comment: event.target.value})} />
        </Field>
      </div>
    </Modal>
  );
}
