'use client';
import {useMemo, useState} from 'react';
import {AlertTriangle, ChevronLeft, ChevronRight, Copy, Plus, Send, Settings2, Trash2} from 'lucide-react';
import {isoDate} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  addDays, addShift, copyPreviousWeek, dayLabel, getScheduleSettings, getWeek, hm, hours, mondayOf, parseDay, publishWeek, removeShift, setScheduleSettings,
  shiftTime, timeOffKindLabel, updateShift, warningLabel, weekDays, weekLabel, type ScheduleWeek, type Shift, type ShiftInput,
} from '@/lib/schedule';
import type {LocationSummary} from '@/lib/types';
import {Alert, Badge, Button, Empty, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction} from '@/components/ui';

const positionTone = ['bg-sky-50 text-sky-800 ring-sky-200', 'bg-amber-50 text-amber-800 ring-amber-200', 'bg-violet-50 text-violet-800 ring-violet-200',
  'bg-emerald-50 text-emerald-800 ring-emerald-200', 'bg-rose-50 text-rose-800 ring-rose-200', 'bg-teal-50 text-teal-800 ring-teal-200'];

/** Une couleur stable par poste, dans l'ordre des réglages. */
export function toneOf(position: string | null, positions: string[]): string {
  if (!position) return 'bg-slate-50 text-ink-700 ring-slate-200';
  const index = positions.findIndex(item => item.toLowerCase() === position.toLowerCase());
  return positionTone[(index >= 0 ? index : position.length) % positionTone.length];
}

export function WeekNav({weekStart, onChange}: {weekStart: string; onChange: (weekStart: string) => void}) {
  const current = isoDate(mondayOf(new Date()));
  const move = (days: number) => onChange(isoDate(addDays(parseDay(weekStart), days)));
  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="flex">
        <button type="button" onClick={() => move(-7)} className="grid size-10 place-items-center rounded-l-xl border border-slate-200 bg-white hover:bg-slate-50" aria-label="Semaine précédente">
          <ChevronLeft className="size-4" />
        </button>
        <button type="button" onClick={() => move(7)} className="-ml-px grid size-10 place-items-center rounded-r-xl border border-slate-200 bg-white hover:bg-slate-50" aria-label="Semaine suivante">
          <ChevronRight className="size-4" />
        </button>
      </div>
      <p className="text-sm font-semibold text-ink-900">{weekLabel(weekStart)}</p>
      {weekStart !== current && (
        <button type="button" onClick={() => onChange(current)} className="text-sm font-medium text-brand-700 hover:underline">Cette semaine</button>
      )}
    </div>
  );
}

function PublicationStatus({week}: {week: ScheduleWeek}) {
  if (week.publishedVersion === 0) {
    return <Badge tone="amber">{week.shifts.length ? 'Brouillon, pas encore publié' : 'Semaine vide'}</Badge>;
  }
  const at = week.publishedAtUtc ? new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'}).format(new Date(week.publishedAtUtc)) : '';
  return week.hasUnpublishedChanges ? (
    <Badge tone="amber">Modifications non publiées · {week.changedMembers.length} membre{week.changedMembers.length > 1 ? 's' : ''}</Badge>
  ) : (
    <Badge tone="green">Publié {at && `le ${at}`}</Badge>
  );
}

type Editing = {shift: Shift | null; staffMemberId: string; date: string};

/**
 * La semaine en brouillon : une ligne par membre, une colonne par jour (une liste par jour sur mobile). Les absences et
 * les dépassements du plafond hebdomadaire sont signalés ; publier prévient ceux dont les créneaux ont changé.
 */
export function WeekBoard({location, weekStart, onWeekChange, onPendingChanged}: {
  location: LocationSummary;
  weekStart: string;
  onWeekChange: (weekStart: string) => void;
  onPendingChanged: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const data = useResource(signal => getWeek(location.id, weekStart, signal), [location.id, weekStart]);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [settings, setSettings] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const days = useMemo(() => weekDays(weekStart), [weekStart]);
  const today = isoDate(new Date());
  const [mobileDay, setMobileDay] = useState(() => Math.max(0, days.indexOf(today)));
  const week = data.data;

  const reload = async () => {
    await data.reload();
    await onPendingChanged();
  };

  if (data.error) return <Alert>{data.error}</Alert>;
  if (!week) return <Spinner label="Chargement du planning…" />;

  const byCell = new Map<string, Shift[]>();
  for (const shift of week.shifts) {
    const key = `${shift.staffMemberId}|${shift.date}`;
    byCell.set(key, [...(byCell.get(key) ?? []), shift]);
  }
  const absenceOn = (memberId: string, date: string) =>
    week.absences.find(absence => absence.staffMemberId === memberId && absence.firstDay <= date && absence.lastDay >= date && absence.status === 'Approved')
    ?? week.absences.find(absence => absence.staffMemberId === memberId && absence.firstDay <= date && absence.lastDay >= date);
  const perDay = days.map(date => {
    const shifts = week.shifts.filter(shift => shift.date === date);
    return {people: new Set(shifts.map(shift => shift.staffMemberId)).size, minutes: shifts.reduce((sum, shift) => sum + shift.plannedMinutes, 0)};
  });
  const totalMinutes = week.shifts.reduce((sum, shift) => sum + shift.plannedMinutes, 0);
  const members = week.members.filter(member => member.isAssigned || member.plannedMinutes > 0);

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
        <div className="flex flex-wrap items-center gap-3">
          <WeekNav weekStart={weekStart} onChange={onWeekChange} />
          <PublicationStatus week={week} />
          {week.openSwaps > 0 && <Badge tone="blue">{week.openSwaps} remplacement{week.openSwaps > 1 ? 's' : ''} en cours</Badge>}
        </div>
        <div className="flex flex-wrap gap-2">
          {week.shifts.length === 0 && (
            <Button variant="secondary" busy={busy === 'copy'} onClick={() => void run('copy', async () => {
              const {copied} = await copyPreviousWeek(location.id, weekStart);
              await reload();
              return copied;
            }, 'Semaine précédente copiée.')}><Copy className="size-4" aria-hidden="true" />Copier la semaine précédente</Button>
          )}
          <Button variant="secondary" onClick={() => setSettings(true)}><Settings2 className="size-4" aria-hidden="true" />Réglages</Button>
          <Button disabled={!week.hasUnpublishedChanges} onClick={() => setPublishing(true)}><Send className="size-4" aria-hidden="true" />Publier</Button>
        </div>
      </div>

      {members.length === 0 ? (
        <Empty title="Personne à planifier">Affectez des membres à cet établissement dans Personnel.</Empty>
      ) : (
        <>
          <div className="hidden overflow-x-auto rounded-xl border border-slate-200 md:block">
            <table className="w-full min-w-[56rem] table-fixed border-collapse text-sm">
              <thead>
                <tr className="bg-slate-50 text-left text-xs font-semibold text-ink-500">
                  <th className="w-44 border-b border-slate-200 px-3 py-2.5">Membre</th>
                  {days.map((date, index) => (
                    <th key={date} className={cx('border-b border-l border-slate-200 px-2 py-2.5', date === today && 'bg-brand-50')}>
                      <span className={cx('block first-letter:uppercase', date === today ? 'text-brand-800' : 'text-ink-800')}>{dayLabel(date)}</span>
                      <span className="font-normal">{perDay[index].people} pers. · {hours(perDay[index].minutes)}</span>
                    </th>
                  ))}
                  <th className="w-28 border-b border-l border-slate-200 px-2 py-2.5 text-right">Total</th>
                </tr>
              </thead>
              <tbody>
                {members.map(member => (
                  <tr key={member.staffMemberId} className="align-top">
                    <td className="border-b border-slate-100 px-3 py-2">
                      <span className="block truncate font-semibold text-ink-900">{member.name}</span>
                      {!member.isAssigned && <span className="text-xs text-red-700">Ne travaille plus ici</span>}
                      {week.changedMembers.includes(member.staffMemberId) && week.publishedVersion > 0 && <span className="text-xs text-amber-700">Modifié</span>}
                    </td>
                    {days.map(date => {
                      const shifts = byCell.get(`${member.staffMemberId}|${date}`) ?? [];
                      const absence = absenceOn(member.staffMemberId, date);
                      return (
                        <td key={date} className={cx('group border-b border-l border-slate-100 p-1.5', absence?.status === 'Approved' && 'bg-[repeating-linear-gradient(135deg,#f8fafc_0,#f8fafc_6px,#eef2f6_6px,#eef2f6_12px)]')}>
                          {absence && (
                            <p className={cx('mb-1 truncate rounded px-1.5 text-[11px] font-semibold', absence.status === 'Approved' ? 'text-ink-600' : 'bg-amber-50 text-amber-800')}>
                              {timeOffKindLabel[absence.kind]}{absence.status === 'Pending' ? ' demandé' : ''}
                            </p>
                          )}
                          <div className="space-y-1">
                            {shifts.map(shift => (
                              <ShiftChip key={shift.id} shift={shift} positions={week.positions} onClick={() => setEditing({shift, staffMemberId: shift.staffMemberId, date: shift.date})} />
                            ))}
                          </div>
                          {member.isAssigned && (
                            <button type="button" onClick={() => setEditing({shift: null, staffMemberId: member.staffMemberId, date})}
                              className={cx('mt-1 flex h-7 w-full items-center justify-center rounded-md border border-dashed border-slate-300 text-ink-500 hover:border-brand-400 hover:text-brand-700',
                                shifts.length > 0 && 'opacity-0 group-hover:opacity-100 focus:opacity-100')}
                              aria-label={`Ajouter un créneau pour ${member.name}, ${dayLabel(date)}`}>
                              <Plus className="size-3.5" />
                            </button>
                          )}
                        </td>
                      );
                    })}
                    <td className="border-b border-l border-slate-100 px-2 py-2 text-right">
                      <span className={cx('font-semibold', member.overLimit ? 'text-red-700' : 'text-ink-900')}>{hours(member.plannedMinutes)}</span>
                      {member.overLimit && <span className="block text-[11px] leading-tight text-red-700">plafond {week.weeklyHoursLimit} h</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="bg-slate-50 text-xs text-ink-600">
                  <td className="px-3 py-2 font-semibold">Total</td>
                  {perDay.map((day, index) => <td key={days[index]} className="border-l border-slate-200 px-2 py-2">{hours(day.minutes)}</td>)}
                  <td className="border-l border-slate-200 px-2 py-2 text-right font-semibold text-ink-900">{hours(totalMinutes)}</td>
                </tr>
              </tfoot>
            </table>
          </div>

          <div className="md:hidden">
            <div className="-mx-1 flex gap-1 overflow-x-auto pb-2" role="tablist" aria-label="Jour">
              {days.map((date, index) => (
                <button key={date} type="button" role="tab" aria-selected={mobileDay === index} onClick={() => setMobileDay(index)}
                  className={cx('shrink-0 rounded-xl border px-3 py-2 text-left text-xs', mobileDay === index ? 'border-brand-500 bg-brand-50 text-brand-800' : 'border-slate-200 bg-white text-ink-600')}>
                  <span className="block font-semibold first-letter:uppercase">{dayLabel(date)}</span>
                  {perDay[index].people} pers.
                </button>
              ))}
            </div>
            <ul className="mt-2 divide-y divide-slate-100">
              {members.map(member => {
                const date = days[mobileDay];
                const shifts = byCell.get(`${member.staffMemberId}|${date}`) ?? [];
                const absence = absenceOn(member.staffMemberId, date);
                return (
                  <li key={member.staffMemberId} className="flex items-start gap-3 py-3">
                    <div className="min-w-0 flex-1">
                      <p className="flex flex-wrap items-center gap-x-2 font-semibold">{member.name}
                        <span className={cx('text-xs font-normal', member.overLimit ? 'text-red-700' : 'text-ink-500')}>{hours(member.plannedMinutes)} / sem.</span>
                      </p>
                      {absence && <p className="text-xs text-amber-800">{timeOffKindLabel[absence.kind]}{absence.status === 'Pending' ? ' demandé' : ''}</p>}
                      <div className="mt-1.5 space-y-1">
                        {shifts.map(shift => (
                          <ShiftChip key={shift.id} shift={shift} positions={week.positions} onClick={() => setEditing({shift, staffMemberId: shift.staffMemberId, date})} />
                        ))}
                      </div>
                    </div>
                    {member.isAssigned && (
                      <button type="button" onClick={() => setEditing({shift: null, staffMemberId: member.staffMemberId, date})}
                        className="grid size-9 shrink-0 place-items-center rounded-lg border border-slate-200 text-ink-600 hover:bg-slate-50" aria-label={`Ajouter un créneau pour ${member.name}`}>
                        <Plus className="size-4" />
                      </button>
                    )}
                  </li>
                );
              })}
            </ul>
          </div>
        </>
      )}

      {editing && (
        <ShiftDialog location={location} week={week} days={days} editing={editing} onClose={() => setEditing(null)} onDone={async () => {
          setEditing(null);
          await reload();
        }} />
      )}
      {settings && <SettingsDialog location={location} onClose={() => setSettings(false)} onDone={async () => {
        setSettings(false);
        await reload();
      }} />}
      <Modal open={publishing} onClose={() => setPublishing(false)} title="Publier le planning" footer={
        <>
          <Button variant="secondary" onClick={() => setPublishing(false)}>Annuler</Button>
          <Button busy={busy === 'publish'} onClick={() => void run('publish', async () => {
            const {notified} = await publishWeek(location.id, weekStart);
            setPublishing(false);
            await reload();
            return notified;
          }, 'Planning publié : l’équipe est prévenue.')}><Send className="size-4" aria-hidden="true" />Publier</Button>
        </>
      }>
        <p className="text-sm text-ink-700">{weekLabel(weekStart)} · {location.name}</p>
        <p className="mt-2 text-sm text-ink-600">
          {week.publishedVersion === 0
            ? `Toute l’équipe planifiée (${new Set(week.shifts.map(shift => shift.staffMemberId)).size} membre${week.shifts.length > 1 ? 's' : ''}) voit sa semaine dans « Mon planning » et reçoit un message.`
            : `Seuls les ${week.changedMembers.length} membre${week.changedMembers.length > 1 ? 's' : ''} dont les créneaux ont changé reçoivent un message.`}
        </p>
        <p className="mt-2 text-xs text-ink-500">Message dans la console, puis WhatsApp (à défaut SMS) au numéro du profil de chacun. Il ne contient que ses propres horaires.</p>
        {week.shifts.some(shift => shift.warnings.includes('time_off')) && (
          <div className="mt-3"><Alert tone="amber">Des créneaux tombent sur une absence validée.</Alert></div>
        )}
      </Modal>
    </div>
  );
}

function ShiftChip({shift, positions, onClick}: {shift: Shift; positions: string[]; onClick: () => void}) {
  const warned = shift.warnings.length > 0;
  return (
    <button type="button" onClick={onClick} title={shift.warnings.map(warning => warningLabel[warning]).join(' · ') || undefined}
      className={cx('block w-full rounded-md px-1.5 py-1 text-left text-xs ring-1 transition hover:brightness-95', toneOf(shift.position, positions), warned && 'ring-2 ring-red-400')}>
      <span className="flex items-center gap-1 font-semibold tabular-nums">
        {warned && <AlertTriangle className="size-3 shrink-0 text-red-600" aria-label="Attention" />}
        {shiftTime(shift)}
      </span>
      {(shift.position || shift.breakMinutes > 0) && (
        <span className="block truncate opacity-80">{[shift.position, shift.breakMinutes > 0 ? `pause ${shift.breakMinutes} min` : null].filter(Boolean).join(' · ')}</span>
      )}
    </button>
  );
}

function ShiftDialog({location, week, days, editing, onClose, onDone}: {
  location: LocationSummary;
  week: ScheduleWeek;
  days: string[];
  editing: Editing;
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const existing = editing.shift;
  const [form, setForm] = useState({
    staffMemberId: editing.staffMemberId,
    date: editing.date,
    start: hm(existing?.start ?? '10:00'),
    end: hm(existing?.end ?? '15:00'),
    breakMinutes: String(existing?.breakMinutes ?? 0),
    position: existing?.position ?? week.positions[0] ?? '',
    note: existing?.note ?? '',
  });
  const members = week.members.filter(member => member.isAssigned || member.staffMemberId === editing.staffMemberId);
  const absence = week.absences.find(item => item.staffMemberId === form.staffMemberId && item.firstDay <= form.date && item.lastDay >= form.date);
  const crossesMidnight = form.end <= form.start;

  const input = (): ShiftInput => ({
    staffMemberId: form.staffMemberId,
    date: form.date,
    start: form.start,
    end: form.end,
    breakMinutes: Number(form.breakMinutes) || 0,
    position: form.position.trim() || null,
    note: form.note.trim() || null,
  });

  return (
    <Modal open onClose={onClose} title={existing ? 'Modifier le créneau' : 'Nouveau créneau'} footer={
      <>
        {existing && (
          <Button variant="ghost" className="mr-auto text-red-600" busy={busy === 'remove'} onClick={() => void run('remove', async () => {
            await removeShift(location.id, week.weekStart, existing.id);
            await onDone();
          }, 'Créneau retiré.')}><Trash2 className="size-4" aria-hidden="true" />Retirer</Button>
        )}
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'save'} onClick={() => void run('save', async () => {
          if (existing) await updateShift(location.id, week.weekStart, existing.id, input());
          else await addShift(location.id, week.weekStart, input());
          await onDone();
        }, existing ? 'Créneau modifié.' : 'Créneau ajouté.')}>Enregistrer</Button>
      </>
    }>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Membre" className="sm:col-span-2">
          <Select value={form.staffMemberId} onChange={event => setForm({...form, staffMemberId: event.target.value})}>
            {members.map(member => <option key={member.staffMemberId} value={member.staffMemberId}>{member.name}</option>)}
          </Select>
        </Field>
        <Field label="Jour" className="sm:col-span-2">
          <Select value={form.date} onChange={event => setForm({...form, date: event.target.value})}>
            {days.map(date => <option key={date} value={date}>{dayLabel(date)}</option>)}
          </Select>
        </Field>
        <Field label="Début">
          <Input type="time" step={300} value={form.start} onChange={event => setForm({...form, start: event.target.value})} />
        </Field>
        <Field label="Fin" hint={crossesMidnight ? 'Se termine le lendemain.' : undefined}>
          <Input type="time" step={300} value={form.end} onChange={event => setForm({...form, end: event.target.value})} />
        </Field>
        <Field label="Pause (minutes)" hint="Déduite des heures prévues.">
          <Input type="number" min={0} max={180} step={5} value={form.breakMinutes} onChange={event => setForm({...form, breakMinutes: event.target.value})} />
        </Field>
        <Field label="Poste">
          <Input list="schedule-positions" maxLength={40} value={form.position} onChange={event => setForm({...form, position: event.target.value})} />
          <datalist id="schedule-positions">{week.positions.map(position => <option key={position} value={position} />)}</datalist>
        </Field>
        <Field label="Note (facultatif)" className="sm:col-span-2">
          <Textarea rows={2} maxLength={200} value={form.note} onChange={event => setForm({...form, note: event.target.value})} placeholder="Ouverture, inventaire…" />
        </Field>
      </div>
      {absence && (
        <div className="mt-4">
          <Alert tone="amber">{absence.name} : {timeOffKindLabel[absence.kind].toLowerCase()} {absence.status === 'Approved' ? 'validé' : 'demandé'} ce jour-là.</Alert>
        </div>
      )}
      {existing?.warnings.includes('not_assigned') && <div className="mt-3"><Alert>Ce membre ne travaille plus dans cet établissement.</Alert></div>}
    </Modal>
  );
}

function SettingsDialog({location, onClose, onDone}: {location: LocationSummary; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const data = useResource(signal => getScheduleSettings(location.id, signal), [location.id]);
  const [form, setForm] = useState<{positions: string; limit: string} | null>(null);
  const current = form ?? (data.data ? {positions: data.data.positions.join(', '), limit: String(data.data.weeklyHoursLimit)} : null);
  return (
    <Modal open onClose={onClose} title={`Réglages du planning · ${location.name}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button disabled={!current} busy={busy === 'settings'} onClick={() => current && void run('settings', async () => {
          await setScheduleSettings(location.id, {
            positions: current.positions.split(',').map(item => item.trim()).filter(Boolean),
            weeklyHoursLimit: Number(current.limit),
          });
          await onDone();
        }, 'Réglages enregistrés.')}>Enregistrer</Button>
      </>
    }>
      {data.error ? <Alert>{data.error}</Alert> : !current ? <Spinner /> : (
        <div className="space-y-4">
          <Field label="Postes proposés" hint="Séparés par des virgules (20 au plus). Un créneau peut aussi porter un autre poste.">
            <Input value={current.positions} onChange={event => setForm({...current, positions: event.target.value})} />
          </Field>
          <Field label="Plafond d’heures par semaine" hint="Au-delà, le total du membre passe en rouge. Entre 1 et 80 heures.">
            <Input type="number" min={1} max={80} value={current.limit} onChange={event => setForm({...current, limit: event.target.value})} />
          </Field>
        </div>
      )}
    </Modal>
  );
}
