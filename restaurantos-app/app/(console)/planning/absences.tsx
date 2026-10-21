'use client';
import {useState} from 'react';
import {CalendarPlus, Check, X} from 'lucide-react';
import {isoDate} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  decideTimeOff, getWeek, listTimeOff, mondayOf, period, recordTimeOff, timeOffKindLabel, timeOffStatusLabel, timeOffStatusTone, type TimeOff, type TimeOffKind,
} from '@/lib/schedule';
import type {LocationSummary} from '@/lib/types';
import {Alert, Badge, Button, Empty, Field, Input, Modal, Select, Spinner, Textarea, useAction} from '@/components/ui';

/** Absences de l'équipe : les demandes à valider d'abord, puis les absences des trois prochains mois. */
export function Absences({location, onChanged}: {location: LocationSummary; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const today = new Date();
  const from = isoDate(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 7));
  const to = isoDate(new Date(today.getFullYear(), today.getMonth(), today.getDate() + 84));
  const data = useResource(signal => listTimeOff(from, to, undefined, signal), [from, to]);
  const [rejecting, setRejecting] = useState<TimeOff | null>(null);
  const [note, setNote] = useState('');
  const [recording, setRecording] = useState(false);

  const reload = async () => {
    await data.reload();
    await onChanged();
  };

  if (data.error) return <Alert>{data.error}</Alert>;
  if (!data.data) return <Spinner label="Chargement des absences…" />;
  const pending = data.data.filter(absence => absence.status === 'Pending');
  const others = data.data.filter(absence => absence.status !== 'Pending').sort((a, b) => a.firstDay.localeCompare(b.firstDay));

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-ink-500">Chacun demande ses absences depuis « Mon planning ». Une absence validée est signalée sur le planning.</p>
        <Button variant="secondary" onClick={() => setRecording(true)}><CalendarPlus className="size-4" aria-hidden="true" />Saisir une absence</Button>
      </div>

      <section>
        <h2 className="mb-2 text-base font-semibold">À valider ({pending.length})</h2>
        {pending.length === 0 ? (
          <p className="rounded-xl bg-slate-50 px-4 py-3 text-sm text-ink-500">Aucune demande en attente.</p>
        ) : (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {pending.map(absence => (
              <li key={absence.id} className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
                <div className="min-w-0 flex-1">
                  <p className="font-semibold">{absence.name} <span className="font-normal text-ink-500">· {timeOffKindLabel[absence.kind]}</span></p>
                  <p className="text-sm text-ink-700 first-letter:uppercase">{period(absence)}</p>
                  {absence.comment && <p className="text-sm text-ink-500">« {absence.comment} »</p>}
                </div>
                <div className="flex gap-2">
                  <Button variant="secondary" size="sm" onClick={() => {
                    setNote('');
                    setRejecting(absence);
                  }}><X className="size-4" aria-hidden="true" />Refuser</Button>
                  <Button variant="success" size="sm" busy={busy === `ok-${absence.id}`} onClick={() => void run(`ok-${absence.id}`, async () => {
                    await decideTimeOff(absence.id, 'approve');
                    await reload();
                  }, 'Absence validée.')}><Check className="size-4" aria-hidden="true" />Valider</Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h2 className="mb-2 text-base font-semibold">Prochaines absences</h2>
        {others.length === 0 ? (
          <Empty title="Aucune absence">Les absences validées des douze prochaines semaines apparaîtront ici.</Empty>
        ) : (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {others.map(absence => (
              <li key={absence.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
                <span className="min-w-0 flex-1">
                  <span className="font-semibold">{absence.name}</span> · {timeOffKindLabel[absence.kind]} · {period(absence)}
                  {absence.decisionNote && <span className="block text-xs text-ink-500">Réponse : {absence.decisionNote}</span>}
                </span>
                <Badge tone={timeOffStatusTone[absence.status]}>{timeOffStatusLabel[absence.status]}</Badge>
                {absence.status === 'Approved' && (
                  <Button variant="ghost" size="sm" busy={busy === `cancel-${absence.id}`} onClick={() => void run(`cancel-${absence.id}`, async () => {
                    await decideTimeOff(absence.id, 'cancel');
                    await reload();
                  }, 'Absence annulée.')}>Annuler</Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <Modal open={!!rejecting} onClose={() => setRejecting(null)} title="Refuser l’absence" footer={
        <>
          <Button variant="secondary" onClick={() => setRejecting(null)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'reject'} onClick={() => rejecting && void run('reject', async () => {
            await decideTimeOff(rejecting.id, 'reject', note);
            setRejecting(null);
            await reload();
          }, 'Absence refusée.')}>Refuser</Button>
        </>
      }>
        {rejecting && <p className="mb-3 text-sm text-ink-700">{rejecting.name} · {period(rejecting)}</p>}
        <Field label="Motif (facultatif)" hint="Le membre le voit dans « Mon planning ».">
          <Textarea rows={2} maxLength={200} value={note} onChange={event => setNote(event.target.value)} placeholder="Trop de monde absent ce week-end…" />
        </Field>
      </Modal>
      {recording && <RecordDialog location={location} onClose={() => setRecording(false)} onDone={async () => {
        setRecording(false);
        await reload();
      }} />}
    </div>
  );
}

function RecordDialog({location, onClose, onDone}: {location: LocationSummary; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const team = useResource(signal => getWeek(location.id, isoDate(mondayOf(new Date())), signal), [location.id]);
  const today = isoDate(new Date());
  const [form, setForm] = useState({staffMemberId: '', firstDay: today, lastDay: today, kind: 'Leave' as TimeOffKind, comment: ''});
  const members = (team.data?.members ?? []).filter(member => member.isAssigned);
  const memberId = form.staffMemberId || members[0]?.staffMemberId || '';
  return (
    <Modal open onClose={onClose} title="Saisir une absence" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button disabled={!memberId} busy={busy === 'record'} onClick={() => void run('record', async () => {
          await recordTimeOff({staffMemberId: memberId, firstDay: form.firstDay, lastDay: form.lastDay < form.firstDay ? form.firstDay : form.lastDay, kind: form.kind, comment: form.comment.trim() || null});
          await onDone();
        }, 'Absence enregistrée.')}>Enregistrer</Button>
      </>
    }>
      {team.error ? <Alert>{team.error}</Alert> : !team.data ? <Spinner /> : (
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Membre" className="sm:col-span-2">
            <Select value={memberId} onChange={event => setForm({...form, staffMemberId: event.target.value})}>
              {members.map(member => <option key={member.staffMemberId} value={member.staffMemberId}>{member.name}</option>)}
            </Select>
          </Field>
          <Field label="Premier jour"><Input type="date" value={form.firstDay} onChange={event => setForm({...form, firstDay: event.target.value})} /></Field>
          <Field label="Dernier jour"><Input type="date" min={form.firstDay} value={form.lastDay} onChange={event => setForm({...form, lastDay: event.target.value})} /></Field>
          <Field label="Type" className="sm:col-span-2">
            <Select value={form.kind} onChange={event => setForm({...form, kind: event.target.value as TimeOffKind})}>
              <option value="Leave">Congé</option>
              <option value="Unavailable">Indisponible</option>
            </Select>
          </Field>
          <Field label="Commentaire (facultatif)" hint="Pas de motif médical : « absent » suffit." className="sm:col-span-2">
            <Textarea rows={2} maxLength={200} value={form.comment} onChange={event => setForm({...form, comment: event.target.value})} />
          </Field>
          <p className="text-xs text-ink-500 sm:col-span-2">Saisie par le gérant, l’absence est validée d’emblée.</p>
        </div>
      )}
    </Modal>
  );
}
