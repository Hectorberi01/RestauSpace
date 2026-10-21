'use client';
import {useState, type FormEvent} from 'react';
import Link from 'next/link';
import {Building2, Plus, Settings2} from 'lucide-react';
import {countries} from '@/lib/locations';
import {post} from '@/lib/api';
import {locationStatusLabel} from '@/lib/format';
import type {LocationSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, PageHeader, Select, useAction} from '@/components/ui';

const days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];


function CreateLocation({open, onClose, onCreated}: {open: boolean; onClose: () => void; onCreated: (id: string) => void}) {
  const [form, setForm] = useState({name: '', street: '', city: '', postalCode: '', countryCode: 'BJ', seats: '40', opens: '11:00', closes: '23:00'});
  const {busy, run} = useAction();
  const set = (key: keyof typeof form) => (event: {target: {value: string}}) => setForm(current => ({...current, [key]: event.target.value}));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const country = countries.find(item => item.code === form.countryCode);
    const endsNextDay = form.closes <= form.opens;
    await run(
      'create',
      async () => {
        const created = await post<{id: string}>('/locations', {
          name: form.name.trim(),
          address: {street: form.street.trim(), city: form.city.trim(), postalCode: form.postalCode.trim() || null, countryCode: form.countryCode},
          seats: Number(form.seats) || 0,
          timeZoneId: country?.timeZone,
          weeklyHours: days.map(day => ({day, start: form.opens, end: form.closes, endsNextDay})),
        });
        onCreated(created.id);
      },
      'Établissement créé.',
    );
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Nouvel établissement"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Annuler</Button>
          <Button type="submit" form="create-location" busy={busy === 'create'}>Créer</Button>
        </>
      }>
      <form id="create-location" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <Field label="Nom" className="sm:col-span-2">
          <Input required maxLength={120} value={form.name} onChange={set('name')} placeholder="Chez Awa · Cadjèhoun" />
        </Field>
        <Field label="Adresse" className="sm:col-span-2">
          <Input required value={form.street} onChange={set('street')} />
        </Field>
        <Field label="Ville">
          <Input required value={form.city} onChange={set('city')} />
        </Field>
        <Field label="Code postal" hint="Facultatif au Bénin.">
          <Input value={form.postalCode} onChange={set('postalCode')} />
        </Field>
        <Field label="Pays">
          <Select value={form.countryCode} onChange={set('countryCode')}>
            {countries.map(country => <option key={country.code} value={country.code}>{country.label}</option>)}
          </Select>
        </Field>
        <Field label="Places assises">
          <Input type="number" min={0} required value={form.seats} onChange={set('seats')} />
        </Field>
        <Field label="Ouverture (tous les jours)">
          <Input type="time" required value={form.opens} onChange={set('opens')} />
        </Field>
        <Field label="Fermeture" hint="Ajustez ensuite les horaires jour par jour dans « Gérer ».">
          <Input type="time" required value={form.closes} onChange={set('closes')} />
        </Field>
      </form>
    </Modal>
  );
}

function LocationCard({location, onChanged}: {location: LocationSummary; onChanged: () => Promise<void>}) {
  const {can, location: selected, setLocationId} = useConsole();
  const {busy, run} = useAction();
  const manage = can('locations.manage');
  const action = (key: string, path: string, success: string, body?: unknown) => run(key, async () => {
    await post(`/locations/${location.id}/${path}`, body);
    await onChanged();
  }, success);

  return (
    <Card className="flex flex-col p-5">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="truncate font-semibold">{location.name}</h2>
          <p className="text-sm text-ink-500">{location.city} · {location.seats} places</p>
        </div>
        <Badge tone={location.status === 'Active' ? 'green' : location.status === 'Draft' ? 'neutral' : 'amber'}>{locationStatusLabel[location.status]}</Badge>
      </div>
      <div className="mt-5 flex flex-wrap gap-2">
        <Link href={`/etablissements/${location.id}`} className="inline-flex h-8 items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 text-sm font-semibold hover:bg-slate-50">
          <Settings2 className="size-4" aria-hidden="true" />{manage ? 'Gérer' : 'Détails'}
        </Link>
        {selected?.id === location.id ? (
          <Badge tone="brand" className="h-8 px-3">Sélectionné</Badge>
        ) : (
          <Button size="sm" variant="secondary" onClick={() => setLocationId(location.id)}>Travailler ici</Button>
        )}
        {manage && location.status === 'Draft' && <Button size="sm" busy={busy === 'activate'} onClick={() => action('activate', 'activate', 'Établissement ouvert.')}>Ouvrir</Button>}
        {manage && location.status === 'Active' && (
          <Button size="sm" variant="ghost" busy={busy === 'close'} onClick={() => action('close', 'temporarily-close', 'Établissement fermé temporairement.', {reason: null})}>Fermer temporairement</Button>
        )}
        {manage && location.status === 'TemporarilyClosed' && <Button size="sm" busy={busy === 'reopen'} onClick={() => action('reopen', 'reopen', 'Établissement rouvert.')}>Rouvrir</Button>}
      </div>
    </Card>
  );
}

export default function LocationsPage() {
  const {locations, can, reloadLocations, setLocationId} = useConsole();
  const [creating, setCreating] = useState(false);
  const manage = can('locations.manage');

  return (
    <>
      <PageHeader
        title="Établissements"
        description="Les adresses de votre restaurant. Chaque écran travaille sur l’établissement choisi en haut de page."
        actions={manage && <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouvel établissement</Button>}
      />
      {!manage && <div className="mb-4"><Alert tone="blue">Seuls les gérants peuvent créer ou fermer un établissement.</Alert></div>}
      {locations.length === 0 ? (
        <Empty icon={<Building2 className="size-8" />} title="Aucun établissement">
          Créez votre premier établissement, puis ouvrez-le pour prendre des commandes.
        </Empty>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {locations.map(location => <LocationCard key={location.id} location={location} onChanged={reloadLocations} />)}
        </div>
      )}
      <CreateLocation
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={async id => {
          setCreating(false);
          await reloadLocations();
          setLocationId(id);
        }}
      />
    </>
  );
}
