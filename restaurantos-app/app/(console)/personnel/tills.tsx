'use client';
import {useMemo, useState} from 'react';
import Link from 'next/link';
import {Clock3, Download, MonitorSmartphone, Tablet, Trash2} from 'lucide-react';
import {downloadCsv} from '@/lib/csv';
import {dateTime, isoDate} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {changeTill, hours, listTills, listTimeEntries, readTill, registerTill, revokeTill, writeTill, type Till} from '@/lib/till';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, useAction} from '@/components/ui';

/**
 * Caisses partagées : un appareil enregistré s'ouvre sur l'écran des PIN. La clé reste sur l'appareil qui l'enregistre ;
 * retirer une caisse coupe aussitôt son accès.
 */
export function Tills() {
  const {locations, location, tenant} = useConsole();
  const {busy, run} = useAction();
  const data = useResource(() => listTills(), []);
  const [registering, setRegistering] = useState(false);
  const [editing, setEditing] = useState<Till | null>(null);
  const [revoking, setRevoking] = useState<Till | null>(null);
  const [form, setForm] = useState({locationId: location?.id ?? '', name: 'Comptoir', autoLock: '5'});
  const [here] = useState(() => (typeof window === 'undefined' ? null : readTill()));
  const nameOf = (id: string) => locations.find(item => item.id === id)?.name ?? 'Établissement';

  const register = () =>
    run('register', async () => {
      const autoLockMinutes = Number(form.autoLock);
      const registered = await registerTill(form.locationId, form.name.trim(), autoLockMinutes);
      writeTill({
        key: registered.key, tillId: registered.id, name: form.name.trim(), tenantName: tenant.name,
        locationId: form.locationId, locationName: nameOf(form.locationId), autoLockMinutes,
      });
      setRegistering(false);
      await data.reload();
    }, 'Cet appareil est maintenant une caisse partagée.');

  return (
    <div className="space-y-4">
      <Alert tone="blue">
        Sur une tablette ou un ordinateur de caisse, enregistrez l’appareil puis ouvrez la <Link href="/tactile" className="font-semibold underline">caisse tactile</Link> :
        chacun s’y identifie par son PIN (choisi dans Mon profil), et la caisse se verrouille seule après quelques minutes sans action.
      </Alert>
      {here && <p className="flex items-center gap-2 text-sm text-ink-600"><MonitorSmartphone className="size-4" aria-hidden="true" />Cet appareil est la caisse « {here.name} ».</p>}
      <div className="flex justify-end"><Button onClick={() => setRegistering(true)}><Tablet className="size-4" aria-hidden="true" />Enregistrer cet appareil</Button></div>
      {data.error && <Alert>{data.error}</Alert>}
      {!data.data ? <Spinner /> : data.data.length === 0 ? <Empty icon={<Tablet className="size-8" />} title="Aucune caisse partagée" /> : (
        <ul className="divide-y divide-slate-100">
          {data.data.map(till => (
            <li key={till.id} className="flex flex-wrap items-center gap-3 py-3">
              <span className="min-w-0 flex-1">
                <span className="block font-semibold">{till.name} {here?.tillId === till.id && <Badge tone="brand">cet appareil</Badge>}</span>
                <span className="text-sm text-ink-500">
                  {nameOf(till.locationId)} · verrouillage après {till.autoLockMinutes} min · {till.lastUsedAtUtc ? `utilisée le ${dateTime(till.lastUsedAtUtc)}` : 'jamais utilisée'}
                </span>
              </span>
              {till.isActive ? (
                <span className="flex gap-2">
                  <Button size="sm" variant="secondary" onClick={() => setEditing(till)}>Modifier</Button>
                  <Button size="sm" variant="ghost" className="text-red-600" onClick={() => setRevoking(till)}><Trash2 className="size-4" aria-hidden="true" />Retirer</Button>
                </span>
              ) : <Badge>Retirée</Badge>}
            </li>
          ))}
        </ul>
      )}

      <Modal open={registering} onClose={() => setRegistering(false)} title="Enregistrer cet appareil comme caisse" footer={
        <>
          <Button variant="secondary" onClick={() => setRegistering(false)}>Annuler</Button>
          <Button busy={busy === 'register'} disabled={!form.locationId || !form.name.trim()} onClick={() => void register()}>Enregistrer</Button>
        </>
      }>
        <div className="space-y-4">
          <Field label="Établissement">
            <Select value={form.locationId} onChange={event => setForm(current => ({...current, locationId: event.target.value}))}>
              <option value="">Choisir…</option>
              {locations.filter(item => item.status === 'Active').map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </Select>
          </Field>
          <Field label="Nom de la caisse"><Input maxLength={60} value={form.name} onChange={event => setForm(current => ({...current, name: event.target.value}))} /></Field>
          <Field label="Verrouillage après (minutes sans action)">
            <Input type="number" min={1} max={60} value={form.autoLock} onChange={event => setForm(current => ({...current, autoLock: event.target.value}))} />
          </Field>
          {here && <Alert tone="amber">Cet appareil est déjà la caisse « {here.name} » : il sera rattaché à la nouvelle.</Alert>}
        </div>
      </Modal>

      {editing && <TillDialog till={editing} onClose={() => setEditing(null)} onDone={data.reload} />}

      <Modal open={!!revoking} onClose={() => setRevoking(null)} title="Retirer la caisse" footer={
        <>
          <Button variant="secondary" onClick={() => setRevoking(null)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'revoke'} onClick={() => revoking && void run('revoke', async () => {
            await revokeTill(revoking.id);
            if (here?.tillId === revoking.id) writeTill(null);
            setRevoking(null);
            await data.reload();
          }, 'Caisse retirée.')}>Retirer</Button>
        </>
      }>
        <p className="text-sm text-ink-600">L’appareil « {revoking?.name} » ne pourra plus ouvrir la caisse par PIN. Les sessions déjà ouvertes dessus se terminent à leur expiration.</p>
      </Modal>
    </div>
  );
}

function TillDialog({till, onClose, onDone}: {till: Till; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [name, setName] = useState(till.name);
  const [autoLock, setAutoLock] = useState(String(till.autoLockMinutes));
  return (
    <Modal open onClose={onClose} title={`Caisse « ${till.name} »`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'save'} onClick={() => void run('save', async () => {
          await changeTill(till.id, name.trim(), Number(autoLock));
          const here = readTill();
          if (here?.tillId === till.id) writeTill({...here, name: name.trim(), autoLockMinutes: Number(autoLock)});
          onClose();
          await onDone();
        }, 'Caisse modifiée.')}>Enregistrer</Button>
      </>
    }>
      <div className="space-y-4">
        <Field label="Nom de la caisse"><Input maxLength={60} value={name} onChange={event => setName(event.target.value)} /></Field>
        <Field label="Verrouillage après (minutes sans action)"><Input type="number" min={1} max={60} value={autoLock} onChange={event => setAutoLock(event.target.value)} /></Field>
      </div>
    </Modal>
  );
}

/** Pointages de la période, par membre, avec l'export des heures. */
export function TimeSheet() {
  const {locations} = useConsole();
  const today = isoDate(new Date());
  const monthStart = `${today.slice(0, 8)}01`;
  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const [locationId, setLocationId] = useState('');
  const start = new Date(`${from}T00:00:00`);
  const end = new Date(`${to < from ? from : to}T00:00:00`);
  end.setDate(end.getDate() + 1);
  const data = useResource(() => listTimeEntries(start.toISOString(), end.toISOString(), locationId || undefined), [from, to, locationId]);
  const nameOf = (id: string) => locations.find(item => item.id === id)?.name ?? '';

  const totals = useMemo(() => {
    const byMember = new Map<string, {name: string; minutes: number; shifts: number; flagged: number}>();
    (data.data ?? []).forEach(entry => {
      const current = byMember.get(entry.staffMemberId) ?? {name: entry.name ?? 'Membre retiré', minutes: 0, shifts: 0, flagged: 0};
      current.minutes += entry.workedMinutes;
      current.shifts += 1;
      if (entry.closedAutomatically) current.flagged += 1;
      byMember.set(entry.staffMemberId, current);
    });
    return [...byMember.values()].sort((a, b) => a.name.localeCompare(b.name, 'fr'));
  }, [data.data]);

  const exportCsv = () =>
    downloadCsv(`pointages-${from}-au-${to}.csv`, [
      ['Membre', 'Établissement', 'Arrivée', 'Départ', 'Durée (h)', 'Départ oublié'],
      ...(data.data ?? []).map(entry => [
        entry.name ?? '', nameOf(entry.locationId), new Date(entry.clockedInAtUtc).toLocaleString('fr-FR'),
        entry.clockedOutAtUtc ? new Date(entry.clockedOutAtUtc).toLocaleString('fr-FR') : '', (entry.workedMinutes / 60).toFixed(2).replace('.', ','),
        entry.closedAutomatically ? 'oui' : '',
      ]),
    ]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <Field label="Du"><Input type="date" value={from} max={today} onChange={event => setFrom(event.target.value)} /></Field>
        <Field label="Au"><Input type="date" value={to} min={from} max={today} onChange={event => setTo(event.target.value)} /></Field>
        <Field label="Établissement">
          <Select value={locationId} onChange={event => setLocationId(event.target.value)}>
            <option value="">Tous</option>
            {locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        </Field>
        <Button variant="secondary" className="ml-auto" disabled={!data.data?.length} onClick={exportCsv}><Download className="size-4" aria-hidden="true" />Exporter (CSV)</Button>
      </div>
      {data.error && <Alert>{data.error}</Alert>}
      {!data.data ? <Spinner /> : data.data.length === 0 ? (
        <Empty icon={<Clock3 className="size-8" />} title="Aucun pointage">Le personnel pointe son arrivée et son départ par PIN sur une caisse partagée.</Empty>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {totals.map(total => (
              <Card key={total.name} className="p-4">
                <p className="font-semibold">{total.name}</p>
                <p className="text-2xl font-bold tabular-nums">{hours(total.minutes)}</p>
                <p className="text-xs text-ink-500">{total.shifts} présence{total.shifts > 1 ? 's' : ''}{total.flagged > 0 ? ` · ${total.flagged} départ${total.flagged > 1 ? 's' : ''} oublié${total.flagged > 1 ? 's' : ''}` : ''}</p>
              </Card>
            ))}
          </div>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-sm">
              <thead className="border-b border-slate-200 text-left text-xs text-ink-500">
                <tr><th className="px-3 py-2 font-medium">Membre</th><th className="px-3 py-2 font-medium">Établissement</th><th className="px-3 py-2 font-medium">Arrivée</th><th className="px-3 py-2 font-medium">Départ</th><th className="px-3 py-2 text-right font-medium">Durée</th></tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.data.map(entry => (
                  <tr key={entry.id}>
                    <td className="px-3 py-2 font-medium">{entry.name ?? 'Membre retiré'}</td>
                    <td className="px-3 py-2">{nameOf(entry.locationId)}</td>
                    <td className="px-3 py-2">{dateTime(entry.clockedInAtUtc)}</td>
                    <td className="px-3 py-2">{entry.clockedOutAtUtc ? dateTime(entry.clockedOutAtUtc) : <Badge tone="green">Présent</Badge>}{entry.closedAutomatically && <Badge tone="amber" className="ml-2">Départ oublié</Badge>}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{hours(entry.workedMinutes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
