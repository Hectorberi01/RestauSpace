'use client';
import {useState} from 'react';
import {Armchair, Bell, CalendarCheck, Clock, MessageCircle, Plus, RefreshCw, UserPlus, Users, X} from 'lucide-react';
import {describe} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {hhmm, localDateTime, normalizePhone, type FloorState} from '@/lib/reservations';
import {
  addWaitingParty, bookWaitlistEntry, cancelWaitlistEntry, joinWaitlist, listWaitingParties, listWaitlist, notifyWaitingParty, offerFreedTables,
  removeWaitingParty, seatWaitingParty, waitedMinutes, waitlistLink, waitlistStatusLabel, waitlistStatusTone, type WaitingParty, type WaitlistEntry,
} from '@/lib/waitlist';
import {isoDate} from '@/lib/format';
import type {LocationSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Spinner, Textarea, cx, useAction} from '@/components/ui';

const flexLabel = (minutes: number) => (minutes === 0 ? 'heure exacte' : `± ${minutes} min`);

/** Lien wa.me vers le client, avec un texte prérempli (envoyé depuis le téléphone de l'équipier). */
const whatsApp = (phone: string, text: string) => `https://wa.me/${phone.replace(/[^0-9]/g, '')}?text=${encodeURIComponent(text)}`;

/**
 * Les deux listes d'attente d'un établissement : les clients inscrits sur un créneau complet (une table libérée leur
 * est proposée) et, le jour même, la file à la porte (groupes sans réservation, prévenus quand leur table est prête).
 */
export function WaitlistView({location, day, isToday, now, floor, onChanged}: {
  location: LocationSummary;
  day: Date;
  isToday: boolean;
  now: number;
  floor: FloorState | null | undefined;
  onChanged: () => Promise<void>;
}) {
  return (
    <div className="grid gap-5 xl:grid-cols-2">
      <SlotWaitlist location={location} day={day} now={now} onChanged={onChanged} />
      {isToday ? <DoorQueue location={location} now={now} floor={floor} onChanged={onChanged} /> : (
        <Card className="p-5">
          <h2 className="text-base font-semibold">File à la porte</h2>
          <p className="mt-2 text-sm text-ink-500">La file à la porte se tient le jour même : revenez sur aujourd’hui pour l’ouvrir.</p>
        </Card>
      )}
    </div>
  );
}

function SlotWaitlist({location, day, now, onChanged}: {location: LocationSummary; day: Date; now: number; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const manage = can('reservations.manage');
  const {busy, run} = useAction();
  const [adding, setAdding] = useState(false);
  const next = new Date(day.getTime() + 86_400_000);
  const entries = useResource(signal => listWaitlist(location.id, day, next, signal), [location.id, day.getTime()], 30_000);
  const open = (entries.data ?? []).filter(entry => entry.status === 'Waiting' || entry.status === 'Offered');
  const closed = (entries.data ?? []).filter(entry => entry.status !== 'Waiting' && entry.status !== 'Offered');

  const reload = async () => {
    await Promise.all([entries.reload(), onChanged()]);
  };

  const sendLink = (entry: WaitlistEntry) => run(`link-${entry.id}`, async () => {
    const link = await waitlistLink(entry.id).catch(() => ({url: null}));
    const at = hhmm(new Date(entry.offeredStartUtc ?? entry.desiredStartUtc));
    const text = [
      `Bonjour ${entry.guestName},`,
      `une table pour ${entry.partySize} vient de se libérer à ${at}.`,
      link.url ? `Confirmez ici${entry.offerExpiresAtUtc ? ` avant ${hhmm(new Date(entry.offerExpiresAtUtc))}` : ''} : ${link.url}` : 'Répondez à ce message pour la réserver.',
    ].join('\n');
    if (entry.guestPhone) window.open(whatsApp(entry.guestPhone, text), '_blank', 'noopener');
  });

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-base font-semibold">Créneaux complets</h2>
          <p className="text-sm text-ink-500">Quand une table se libère, elle est proposée au premier inscrit, qui a un temps limité pour l’accepter.</p>
        </div>
        {manage && (
          <div className="flex gap-2">
            <Button size="sm" variant="secondary" busy={busy === 'offer'} onClick={() => void run('offer', async () => {
              const result = await offerFreedTables(location.id);
              await reload();
              return result;
            }, 'Tables libres proposées aux inscrits.')}><RefreshCw className="size-4" aria-hidden="true" />Proposer</Button>
            <Button size="sm" onClick={() => setAdding(true)}><UserPlus className="size-4" aria-hidden="true" />Inscrire</Button>
          </div>
        )}
      </div>
      {entries.error && <div className="mt-3"><Alert>{entries.error}</Alert></div>}
      {!entries.data ? <Spinner /> : open.length === 0 ? (
        <Empty title="Personne en attente">Inscrivez un client quand un créneau est complet ; il sera prévenu si une table se libère.</Empty>
      ) : (
        <ul className="mt-4 divide-y divide-slate-100">
          {open.map(entry => {
            const offered = entry.status === 'Offered' && entry.offeredStartUtc;
            const left = entry.offerExpiresAtUtc ? Math.max(0, Math.round((new Date(entry.offerExpiresAtUtc).getTime() - now) / 60_000)) : null;
            return (
              <li key={entry.id} className="py-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold">{entry.guestName}</span>
                  <Badge tone={waitlistStatusTone[entry.status]}>{waitlistStatusLabel[entry.status]}</Badge>
                  {entry.source === 'Online' && <Badge>En ligne</Badge>}
                  <span className="ml-auto text-sm text-ink-600"><Users className="mr-1 inline size-4" aria-hidden="true" />{entry.partySize}</span>
                </div>
                <p className="mt-1 text-sm text-ink-600">
                  <Clock className="mr-1 inline size-4 text-ink-500" aria-hidden="true" />
                  {hhmm(new Date(entry.desiredStartUtc))} ({flexLabel(entry.flexibilityMinutes)})
                  {entry.guestPhone && <> · <a href={`tel:${entry.guestPhone}`} className="hover:text-brand-700">{entry.guestPhone}</a></>}
                </p>
                {offered && (
                  <p className="mt-1 text-sm text-sky-800">Table proposée à {hhmm(new Date(entry.offeredStartUtc!))}{left !== null ? ` · réponse attendue dans ${left} min` : ''}</p>
                )}
                {entry.missedOffers > 0 && <p className="mt-1 text-xs text-ink-500">{entry.missedOffers} offre{entry.missedOffers > 1 ? 's' : ''} sans réponse</p>}
                {entry.notes && <p className="mt-1 text-xs text-ink-500">{entry.notes}</p>}
                {manage && (
                  <div className="mt-2 flex flex-wrap gap-2">
                    <Button size="sm" variant="secondary" busy={busy === `book-${entry.id}`} onClick={() => void run(`book-${entry.id}`, async () => {
                      await bookWaitlistEntry(entry.id, new Date(entry.offeredStartUtc ?? entry.desiredStartUtc));
                      await reload();
                    }, 'Réservation créée : confirmez-la avec le client.')}><CalendarCheck className="size-4" aria-hidden="true" />Réserver à {hhmm(new Date(entry.offeredStartUtc ?? entry.desiredStartUtc))}</Button>
                    {entry.guestPhone && (
                      <Button size="sm" variant="ghost" busy={busy === `link-${entry.id}`} onClick={() => void sendLink(entry)}><MessageCircle className="size-4" aria-hidden="true" />WhatsApp</Button>
                    )}
                    <Button size="sm" variant="ghost" className="text-red-600" busy={busy === `cancel-${entry.id}`} onClick={() => void run(`cancel-${entry.id}`, async () => {
                      await cancelWaitlistEntry(entry.id);
                      await reload();
                    })}><X className="size-4" aria-hidden="true" />Retirer</Button>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {closed.length > 0 && (
        <details className="mt-3 text-sm">
          <summary className="cursor-pointer font-medium text-ink-600">Traitées ({closed.length})</summary>
          <ul className="mt-2 space-y-1">
            {closed.map(entry => (
              <li key={entry.id} className="flex items-center gap-2 text-ink-600">
                <span className="flex-1 truncate">{entry.guestName} · {entry.partySize} pers. · {hhmm(new Date(entry.desiredStartUtc))}</span>
                <Badge tone={waitlistStatusTone[entry.status]}>{waitlistStatusLabel[entry.status]}</Badge>
              </li>
            ))}
          </ul>
        </details>
      )}
      {adding && <JoinDialog location={location} day={day} onClose={() => setAdding(false)} onDone={async () => {
        setAdding(false);
        await reload();
      }} />}
    </Card>
  );
}

function JoinDialog({location, day, onClose, onDone}: {location: LocationSummary; day: Date; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [date, setDate] = useState(isoDate(day));
  const [start, setStart] = useState('20:00');
  const [party, setParty] = useState('2');
  const [flex, setFlex] = useState('30');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    setError(null);
    const at = localDateTime(date, start);
    const normalized = normalizePhone(phone);
    if (!at) return setError('Indiquez une date et une heure valides.');
    if (!name.trim()) return setError('Indiquez le nom du client.');
    if (!normalized) return setError('Un téléphone au format international est nécessaire pour prévenir le client.');
    await run('join', async () => {
      await joinWaitlist({
        locationId: location.id, desiredStart: at, partySize: Number(party) || 1, flexibilityMinutes: Number(flex) || 0, guestName: name.trim(),
        guestPhone: normalized, notes: notes.trim() || null,
      });
      await onDone();
    }, 'Client inscrit en liste d’attente.');
  }

  return (
    <Modal open onClose={onClose} title="Inscrire en liste d’attente" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'join'} onClick={() => void submit()}>Inscrire</Button>
      </>
    }>
      <div className="space-y-4">
        {error && <Alert>{error}</Alert>}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Field label="Date" className="col-span-2"><Input type="date" value={date} min={isoDate(new Date())} onChange={event => setDate(event.target.value)} /></Field>
          <Field label="Heure"><Input type="time" step={900} value={start} onChange={event => setStart(event.target.value)} /></Field>
          <Field label="Couverts"><Input type="number" min={1} value={party} onChange={event => setParty(event.target.value)} /></Field>
        </div>
        <Field label="Souplesse" hint="Le client accepte une table un peu avant ou après.">
          <select value={flex} onChange={event => setFlex(event.target.value)} className="h-10 w-full rounded-xl border border-slate-300 bg-white px-3 text-sm">
            <option value="0">Heure exacte</option>
            <option value="15">± 15 min</option>
            <option value="30">± 30 min</option>
            <option value="60">± 1 h</option>
            <option value="120">± 2 h</option>
          </select>
        </Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Nom"><Input value={name} maxLength={150} onChange={event => setName(event.target.value)} autoComplete="off" /></Field>
          <Field label="Téléphone"><Input type="tel" value={phone} placeholder="+229 01 97 00 00 00" onChange={event => setPhone(event.target.value)} autoComplete="off" /></Field>
        </div>
        <Field label="Note (facultatif)"><Textarea rows={2} maxLength={500} value={notes} onChange={event => setNotes(event.target.value)} /></Field>
      </div>
    </Modal>
  );
}

function DoorQueue({location, now, floor, onChanged}: {location: LocationSummary; now: number; floor: FloorState | null | undefined; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const serve = can('floor.serve');
  const {busy, run} = useAction();
  const parties = useResource(signal => listWaitingParties(location.id, signal), [location.id], 20_000);
  const [name, setName] = useState('');
  const [size, setSize] = useState('2');
  const [phone, setPhone] = useState('');
  const [quoted, setQuoted] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [seating, setSeating] = useState<WaitingParty | null>(null);
  const open = (parties.data ?? []).filter(party => party.status === 'Waiting' || party.status === 'Notified');
  const closed = (parties.data ?? []).filter(party => party.status === 'Seated' || party.status === 'Left');
  const suggested = (open.length + 1) * 10;

  const reload = async () => {
    await Promise.all([parties.reload(), onChanged()]);
  };

  async function add() {
    setError(null);
    const normalized = phone.trim() ? normalizePhone(phone) : null;
    if (!name.trim()) return setError('Indiquez un nom pour appeler le groupe.');
    if (normalized === undefined) return setError('Téléphone au format international (+229…), ou laissez vide.');
    const ok = await run('add', async () => {
      await addWaitingParty({
        locationId: location.id, name: name.trim(), partySize: Number(size) || 1, phone: normalized, note: null,
        quotedMinutes: quoted.trim() ? Number(quoted) : suggested,
      });
      await reload();
    });
    if (ok) {
      setName('');
      setPhone('');
      setQuoted('');
      setSize('2');
    }
  }

  return (
    <Card className="p-5">
      <h2 className="text-base font-semibold">File à la porte</h2>
      <p className="text-sm text-ink-500">Les groupes arrivés sans réservation, prévenus par message quand leur table est prête. Le numéro est effacé dès qu’ils sont installés ou partis.</p>
      {serve && (
        <form className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-[minmax(0,1.2fr)_4.5rem_minmax(0,1.4fr)_5.5rem_auto]" onSubmit={event => {
          event.preventDefault();
          void add();
        }}>
          <Input aria-label="Nom" placeholder="Nom" value={name} maxLength={80} onChange={event => setName(event.target.value)} className="col-span-2 sm:col-span-1" />
          <Input aria-label="Couverts" type="number" min={1} max={100} value={size} onChange={event => setSize(event.target.value)} />
          <Input aria-label="Téléphone (facultatif)" type="tel" placeholder="Téléphone" value={phone} onChange={event => setPhone(event.target.value)} />
          <Input aria-label="Attente annoncée (minutes)" type="number" min={0} max={240} placeholder={`${suggested} min`} value={quoted} onChange={event => setQuoted(event.target.value)} />
          <Button type="submit" busy={busy === 'add'} className="col-span-2 sm:col-span-1"><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
        </form>
      )}
      {error && <div className="mt-3"><Alert>{error}</Alert></div>}
      {parties.error && <div className="mt-3"><Alert>{parties.error}</Alert></div>}
      {!parties.data ? <Spinner /> : open.length === 0 ? (
        <Empty title="Personne à la porte">Ajoutez un groupe qui attend une table.</Empty>
      ) : (
        <ol className="mt-4 divide-y divide-slate-100">
          {open.map(party => {
            const waited = waitedMinutes(party, now);
            const late = party.quotedMinutes !== null && waited > party.quotedMinutes;
            return (
              <li key={party.id} className="flex gap-3 py-3">
                <span className="grid size-9 shrink-0 place-items-center rounded-full bg-brand-50 text-sm font-bold text-brand-700">{party.position}</span>
                <div className="min-w-0 flex-1">
                  <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <span className="font-semibold">{party.name} <span className="font-normal text-ink-500">· {party.partySize} pers.</span></span>
                    {party.status === 'Notified' && <Badge tone="blue">Prévenu</Badge>}
                  </span>
                  <span className={cx('block text-xs', late ? 'text-red-700' : 'text-ink-500')}>
                    Attend depuis {waited} min{party.quotedMinutes !== null ? ` (annoncé : ${party.quotedMinutes} min)` : ''}
                    {party.status === 'Notified' && party.notifiedAtUtc ? ` · prévenu à ${hhmm(new Date(party.notifiedAtUtc))}` : ''}
                  </span>
                {serve && (
                  <span className="mt-2 flex flex-wrap gap-1.5">
                    {party.hasPhone && (
                      <Button size="sm" variant="secondary" busy={busy === `notify-${party.id}`} disabled={party.notificationCount >= 5} onClick={() => void run(`notify-${party.id}`, async () => {
                        await notifyWaitingParty(party.id);
                        await reload();
                      }, 'Message « votre table est prête » envoyé.')}><Bell className="size-4" aria-hidden="true" />Prévenir</Button>
                    )}
                    {party.phone && (
                      <a href={whatsApp(party.phone, `Bonjour ${party.name}, votre table est prête ! Présentez-vous à l’accueil.`)} target="_blank" rel="noreferrer"
                        className="inline-flex h-9 items-center gap-1.5 rounded-lg px-2.5 text-sm font-semibold text-emerald-700 hover:bg-emerald-50" aria-label={`WhatsApp à ${party.name}`}>
                        <MessageCircle className="size-4" aria-hidden="true" />
                      </a>
                    )}
                    <Button size="sm" onClick={() => setSeating(party)}><Armchair className="size-4" aria-hidden="true" />Installer</Button>
                    <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Retirer ${party.name}`} busy={busy === `remove-${party.id}`} onClick={() => void run(`remove-${party.id}`, async () => {
                      await removeWaitingParty(party.id);
                      await reload();
                    })}><X className="size-4" /></Button>
                  </span>
                )}
                </div>
              </li>
            );
          })}
        </ol>
      )}
      {closed.length > 0 && (
        <details className="mt-3 text-sm">
          <summary className="cursor-pointer font-medium text-ink-600">Sortis de la file ({closed.length})</summary>
          <ul className="mt-2 space-y-1 text-ink-600">
            {closed.map(party => (
              <li key={party.id}>{party.name} · {party.partySize} pers. · {party.status === 'Seated' ? 'installé' : 'parti'}{party.closedAtUtc ? ` à ${hhmm(new Date(party.closedAtUtc))}` : ''}</li>
            ))}
          </ul>
        </details>
      )}
      {seating && <SeatPartyDialog party={seating} floor={floor} onClose={() => setSeating(null)} onDone={async () => {
        setSeating(null);
        await reload();
      }} />}
    </Card>
  );
}

function SeatPartyDialog({party, floor, onClose, onDone}: {party: WaitingParty; floor: FloorState | null | undefined; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [tableId, setTableId] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const areas = (floor?.areas ?? [])
    .map(area => ({...area, free: area.tables.filter(table => table.isInService && table.status === 'Available' && !table.openSessionId).sort((a, b) => a.capacity - b.capacity)}))
    .filter(area => area.free.length > 0);
  return (
    <Modal open onClose={onClose} title={`Installer ${party.name} · ${party.partySize} pers.`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button disabled={!tableId} busy={busy === 'seat'} onClick={() => void run('seat', async () => {
          try {
            await seatWaitingParty(party.id, tableId!);
            await onDone();
          } catch (err) {
            setFailure(describe(err));
          }
        }, 'Groupe installé.')}>Installer à cette table</Button>
      </>
    }>
      {failure && <div className="mb-3"><Alert>{failure}</Alert></div>}
      {areas.length === 0 ? <Alert tone="amber">Aucune table libre en ce moment.</Alert> : areas.map(area => (
        <section key={area.id} className="mb-3">
          <h3 className="mb-2 text-sm font-semibold">{area.name}</h3>
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
            {area.free.map(table => (
              <button key={table.id} type="button" onClick={() => setTableId(table.id)} aria-pressed={tableId === table.id}
                className={cx('rounded-xl border px-3 py-2 text-left text-sm', tableId === table.id ? 'border-brand-500 bg-brand-50' : 'border-slate-200 hover:border-slate-300', table.capacity < party.partySize && 'opacity-70')}>
                <span className="block font-semibold">{table.label}</span>
                <span className="text-xs text-ink-500">{table.capacity} places</span>
              </button>
            ))}
          </div>
        </section>
      ))}
    </Modal>
  );
}
