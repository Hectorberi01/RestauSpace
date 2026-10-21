'use client';
import {use, useEffect, useState, type FormEvent, type ReactNode} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {Archive, ArrowLeft, CalendarDays, Clock, LocateFixed, MapPin, Pause, Phone, Play, Users} from 'lucide-react';
import {locationStatusLabel} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {changeAddress, countries, lifecycle, loadLocation, renameLocation, setCapacity, setContact, setTimeZone} from '@/lib/locations';
import {timeZones} from '@/lib/profile';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Field, Input, Modal, Select, Spinner, Textarea, useAction} from '@/components/ui';
import {Exceptions} from './exceptions';
import {HoursEditor} from './hours';

function Section({icon, title, description, children, className}: {icon: ReactNode; title: string; description?: string; children: ReactNode; className?: string}) {
  return (
    <Card className={className ?? 'p-5'}>
      <div className="mb-4 flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true">{icon}</span>
        <div>
          <h2 className="font-semibold">{title}</h2>
          {description && <p className="text-sm text-ink-500">{description}</p>}
        </div>
      </div>
      {children}
    </Card>
  );
}

export default function LocationPage({params}: {params: Promise<{id: string}>}) {
  const {id} = use(params);
  const router = useRouter();
  const {can, reloadLocations, location: selected, setLocationId} = useConsole();
  const manage = can('locations.manage');
  const {busy, run} = useAction();
  const {data: location, error, reload} = useResource(signal => loadLocation(id, signal), [id]);
  const [name, setName] = useState('');
  const [address, setAddressForm] = useState({street: '', city: '', postalCode: '', countryCode: 'BJ', latitude: '', longitude: ''});
  const [contact, setContactForm] = useState({phone: '', whatsApp: '', email: ''});
  const [locating, setLocating] = useState(false);
  const [seats, setSeats] = useState('0');
  const [zone, setZone] = useState('');
  const [closing, setClosing] = useState(false);
  const [closeReason, setCloseReason] = useState('');
  const [archiving, setArchiving] = useState(false);

  useEffect(() => {
    if (!location) return;
    setName(location.name);
    setAddressForm({
      street: location.address.street, city: location.address.city, postalCode: location.address.postalCode ?? '', countryCode: location.address.countryCode,
      latitude: location.address.latitude?.toString() ?? '', longitude: location.address.longitude?.toString() ?? '',
    });
    setContactForm({phone: location.contact?.phone ?? '', whatsApp: location.contact?.whatsApp ?? '', email: location.contact?.email ?? ''});
    setSeats(String(location.seats));
    setZone(location.timeZoneId);
  }, [location]);

  if (error) return <div className="space-y-4"><Link href="/etablissements" className="text-sm font-semibold text-brand-700">Retour aux établissements</Link><Alert>{error}</Alert></div>;
  if (!location) return <Spinner label="Chargement de l’établissement…" />;

  const archived = location.status === 'Archived';
  const editable = manage && !archived;
  const refresh = async () => {
    await reload();
    await reloadLocations();
  };
  const submit = (key: string, action: () => Promise<unknown>, success: string) => (event: FormEvent) => {
    event.preventDefault();
    void run(key, async () => {
      await action();
      await refresh();
    }, success);
  };
  const zones = timeZones.some(item => item.id === location.timeZoneId) ? timeZones : [{id: location.timeZoneId, label: location.timeZoneId}, ...timeZones];
  const addressChanged = address.street !== location.address.street || address.city !== location.address.city
    || address.postalCode !== (location.address.postalCode ?? '') || address.countryCode !== location.address.countryCode
    || address.latitude !== (location.address.latitude?.toString() ?? '') || address.longitude !== (location.address.longitude?.toString() ?? '');
  const coordinate = (value: string) => value.trim() === '' ? null : Number(value.trim().replace(',', '.'));
  const latitude = coordinate(address.latitude);
  const longitude = coordinate(address.longitude);
  const coordinatesValid = (latitude === null) === (longitude === null)
    && (latitude === null || (Number.isFinite(latitude) && Math.abs(latitude) <= 90))
    && (longitude === null || (Number.isFinite(longitude) && Math.abs(longitude) <= 180));
  const contactChanged = contact.phone !== (location.contact?.phone ?? '') || contact.whatsApp !== (location.contact?.whatsApp ?? '')
    || contact.email !== (location.contact?.email ?? '');
  const locate = () => {
    if (!navigator.geolocation) return;
    setLocating(true);
    navigator.geolocation.getCurrentPosition(position => {
      setLocating(false);
      setAddressForm(current => ({...current, latitude: position.coords.latitude.toFixed(6), longitude: position.coords.longitude.toFixed(6)}));
    }, () => setLocating(false), {enableHighAccuracy: true, timeout: 10000});
  };
  const clean = (value: string) => value.trim() || null;
  const tone = location.status === 'Active' ? 'green' : location.status === 'Draft' ? 'neutral' : location.status === 'Archived' ? 'red' : 'amber';

  return (
    <div className="space-y-6">
      <Link href="/etablissements" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Établissements</Link>
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="truncate text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{location.name}</h1>
            <Badge tone={tone}>{locationStatusLabel[location.status]}</Badge>
          </div>
          <p className="mt-1 text-sm text-ink-500">{location.address.street}, {location.address.city} · {location.seats} places · {location.timeZoneId}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {selected?.id !== location.id && !archived && <Button variant="secondary" onClick={() => setLocationId(location.id)}>Travailler ici</Button>}
          {manage && location.status === 'Draft' && (
            <Button busy={busy === 'activate'} onClick={() => run('activate', async () => {
              await lifecycle(location.id, 'activate');
              await refresh();
            }, 'Établissement ouvert.')}><Play className="size-4" aria-hidden="true" />Ouvrir</Button>
          )}
          {manage && location.status === 'Active' && <Button variant="secondary" onClick={() => setClosing(true)}><Pause className="size-4" aria-hidden="true" />Fermer temporairement</Button>}
          {manage && location.status === 'TemporarilyClosed' && (
            <Button busy={busy === 'reopen'} onClick={() => run('reopen', async () => {
              await lifecycle(location.id, 'reopen');
              await refresh();
            }, 'Établissement rouvert.')}><Play className="size-4" aria-hidden="true" />Rouvrir</Button>
          )}
          {manage && !archived && <Button variant="ghost" className="text-red-600" onClick={() => setArchiving(true)}><Archive className="size-4" aria-hidden="true" />Archiver</Button>}
        </div>
      </div>

      {location.status === 'TemporarilyClosed' && <Alert tone="amber">Fermé temporairement{location.temporaryClosureReason ? ` : ${location.temporaryClosureReason}` : ''}. Aucune commande ni réservation n’est acceptée.</Alert>}
      {location.status === 'Draft' && <Alert tone="blue">Brouillon : ouvrez l’établissement pour prendre des commandes et des réservations.</Alert>}
      {archived && <Alert>Établissement archivé : plus aucune modification n’est possible.</Alert>}
      {!manage && !archived && <Alert tone="blue">Lecture seule : seuls les gérants peuvent modifier un établissement.</Alert>}

      <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <Section icon={<Clock className="size-5" />} title="Horaires d’ouverture" description="Les réservations ne sont proposées que pendant ces horaires.">
          <HoursEditor location={location} editable={editable} onSaved={refresh} />
        </Section>

        <div className="space-y-4">
          <Section icon={<MapPin className="size-5" />} title="Nom et adresse">
            <form className="space-y-3" onSubmit={submit('name', () => renameLocation(location.id, name.trim()), 'Nom enregistré.')}>
              <Field label="Nom">
                <div className="flex gap-2">
                  <Input required minLength={2} maxLength={100} disabled={!editable} value={name} onChange={event => setName(event.target.value)} />
                  {editable && <Button type="submit" variant="secondary" busy={busy === 'name'} disabled={name.trim() === location.name || name.trim().length < 2}>OK</Button>}
                </div>
              </Field>
            </form>
            <form className="mt-4 grid grid-cols-1 gap-3 border-t border-slate-100 pt-4 sm:grid-cols-2" onSubmit={submit('address', () => changeAddress(location.id, {
              street: address.street.trim(), city: address.city.trim(), postalCode: address.postalCode.trim() || null, countryCode: address.countryCode,
              latitude, longitude,
            }), 'Adresse enregistrée.')}>
              <Field label="Adresse" className="sm:col-span-2"><Input required disabled={!editable} value={address.street} onChange={event => setAddressForm(current => ({...current, street: event.target.value}))} /></Field>
              <Field label="Ville"><Input required disabled={!editable} value={address.city} onChange={event => setAddressForm(current => ({...current, city: event.target.value}))} /></Field>
              <Field label="Code postal"><Input disabled={!editable} value={address.postalCode} onChange={event => setAddressForm(current => ({...current, postalCode: event.target.value}))} /></Field>
              <Field label="Pays" className="sm:col-span-2">
                <Select disabled={!editable} value={address.countryCode} onChange={event => setAddressForm(current => ({...current, countryCode: event.target.value}))}>
                  {!countries.some(country => country.code === address.countryCode) && <option value={address.countryCode}>{address.countryCode}</option>}
                  {countries.map(country => <option key={country.code} value={country.code}>{country.label}</option>)}
                </Select>
              </Field>
              <Field label="Latitude" hint="Ex. 6.365536"><Input inputMode="decimal" disabled={!editable} value={address.latitude} onChange={event => setAddressForm(current => ({...current, latitude: event.target.value}))} /></Field>
              <Field label="Longitude" hint="Ex. 2.418378"><Input inputMode="decimal" disabled={!editable} value={address.longitude} onChange={event => setAddressForm(current => ({...current, longitude: event.target.value}))} /></Field>
              <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm sm:col-span-2">
                {editable && (
                  <button type="button" onClick={locate} disabled={locating} className="inline-flex items-center gap-1.5 font-semibold text-brand-700 hover:underline disabled:opacity-60">
                    <LocateFixed className="size-4" aria-hidden="true" />{locating ? 'Localisation…' : 'Utiliser ma position actuelle'}
                  </button>
                )}
                {latitude !== null && longitude !== null && coordinatesValid && (
                  <a href={`https://www.openstreetmap.org/?mlat=${latitude}&mlon=${longitude}#map=18/${latitude}/${longitude}`} target="_blank" rel="noreferrer" className="font-semibold text-ink-600 hover:underline">Vérifier sur la carte</a>
                )}
              </div>
              <p className="text-xs text-ink-500 sm:col-span-2">La position permet à votre site et à votre application d’afficher un plan et un itinéraire.</p>
              {!coordinatesValid && <p className="text-sm text-red-600 sm:col-span-2">Renseignez la latitude et la longitude ensemble (latitude entre -90 et 90, longitude entre -180 et 180).</p>}
              {editable && <div className="sm:col-span-2"><Button type="submit" variant="secondary" busy={busy === 'address'} disabled={!addressChanged || !coordinatesValid}>Enregistrer l’adresse</Button></div>}
            </form>
          </Section>

          <Section icon={<Phone className="size-5" />} title="Coordonnées publiques" description="Affichées sur votre site et votre application pour que vos clients vous joignent.">
            <form className="grid grid-cols-1 gap-3 sm:grid-cols-2" onSubmit={submit('contact', () => setContact(location.id, {
              phone: clean(contact.phone), whatsApp: clean(contact.whatsApp), email: clean(contact.email),
            }), 'Coordonnées enregistrées.')}>
              <Field label="Téléphone" hint="Ex. +229 97 00 00 00"><Input type="tel" disabled={!editable} value={contact.phone} onChange={event => setContactForm(current => ({...current, phone: event.target.value}))} /></Field>
              <Field label="WhatsApp" hint="Numéro WhatsApp, même s’il est identique au téléphone."><Input type="tel" disabled={!editable} value={contact.whatsApp} onChange={event => setContactForm(current => ({...current, whatsApp: event.target.value}))} /></Field>
              <Field label="E-mail" className="sm:col-span-2"><Input type="email" maxLength={254} disabled={!editable} value={contact.email} onChange={event => setContactForm(current => ({...current, email: event.target.value}))} /></Field>
              {editable && <div className="sm:col-span-2"><Button type="submit" variant="secondary" busy={busy === 'contact'} disabled={!contactChanged}>Enregistrer les coordonnées</Button></div>}
            </form>
          </Section>

          <Section icon={<Users className="size-5" />} title="Capacité et fuseau">
            <form className="flex items-end gap-2" onSubmit={submit('seats', () => setCapacity(location.id, Number(seats)), 'Capacité enregistrée.')}>
              <Field label="Places assises" hint="Plafond des couverts réservables à un même moment." className="flex-1">
                <Input type="number" min={0} max={10000} required disabled={!editable} value={seats} onChange={event => setSeats(event.target.value)} />
              </Field>
              {editable && <Button type="submit" variant="secondary" busy={busy === 'seats'} disabled={Number(seats) === location.seats || seats === ''}>OK</Button>}
            </form>
            <form className="mt-4 flex items-end gap-2" onSubmit={submit('zone', () => setTimeZone(location.id, zone), 'Fuseau enregistré.')}>
              <Field label="Fuseau horaire" hint="Les horaires ci-contre sont en heure locale de ce fuseau." className="flex-1">
                <Select disabled={!editable} value={zone} onChange={event => setZone(event.target.value)}>
                  {zones.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
                </Select>
              </Field>
              {editable && <Button type="submit" variant="secondary" busy={busy === 'zone'} disabled={zone === location.timeZoneId}>OK</Button>}
            </form>
          </Section>
        </div>
      </div>

      <Section icon={<CalendarDays className="size-5" />} title="Jours exceptionnels" description="Fermetures et horaires spéciaux : ils remplacent l’horaire habituel ce jour-là.">
        <Exceptions location={location} editable={editable} onChanged={refresh} />
      </Section>

      <Modal open={closing} onClose={() => setClosing(false)} title="Fermer temporairement" footer={
        <>
          <Button variant="secondary" onClick={() => setClosing(false)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'close'} onClick={() => run('close', async () => {
            await lifecycle(location.id, 'temporarily-close', {reason: closeReason.trim() || null});
            setClosing(false);
            setCloseReason('');
            await refresh();
          }, 'Établissement fermé temporairement.')}>Fermer</Button>
        </>
      }>
        <p className="mb-3 text-sm text-ink-600">Plus aucune commande ni réservation tant que l’établissement n’est pas rouvert. Pour un seul jour, préférez un jour exceptionnel.</p>
        <Field label="Motif (facultatif)" hint="Affiché à vos clients sur votre site et votre application."><Textarea rows={2} maxLength={200} value={closeReason} onChange={event => setCloseReason(event.target.value)} placeholder="Travaux, congés annuels…" /></Field>
      </Modal>

      <Modal open={archiving} onClose={() => setArchiving(false)} title="Archiver l’établissement" footer={
        <>
          <Button variant="secondary" onClick={() => setArchiving(false)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'archive'} onClick={() => run('archive', async () => {
            await lifecycle(location.id, 'archive');
            setArchiving(false);
            await reloadLocations();
            router.replace('/etablissements');
          }, 'Établissement archivé.')}>Archiver définitivement</Button>
        </>
      }>
        <p className="text-sm text-ink-600">L’établissement disparaît de la console et ne peut plus être modifié ni rouvert. Son historique (commandes, rapports) est conservé.</p>
      </Modal>
    </div>
  );
}
