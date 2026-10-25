'use client';
import {useState} from 'react';
import Link from 'next/link';
import {Ban, Link2, Monitor} from 'lucide-react';
import {listKiosks, pairKiosk, revokeKiosk, type Kiosk, type StorefrontOverview} from '@/lib/storefront';
import {dateTime} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {Badge, Button, Card, Field, Input, Modal, Select, Spinner, useAction} from '@/components/ui';
import {FrozenBadge, FrozenNote} from '@/components/plan';

function PairDialog({open, overview, onClose, onPaired}: {
  open: boolean;
  overview: StorefrontOverview;
  onClose: () => void;
  onPaired: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [code, setCode] = useState('');
  const [locationId, setLocationId] = useState('');
  const [name, setName] = useState('');
  const location = locationId || overview.locations[0]?.locationId || '';
  const valid = /^\d{6}$/.test(code) && !!location && !!name.trim();

  return (
    <Modal open={open} onClose={onClose} title="Appairer une borne" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="pair-kiosk" busy={busy === 'pair'} disabled={!valid}>Appairer</Button>
      </>
    }>
      <form id="pair-kiosk" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('pair', async () => {
          await pairKiosk(code, location, name.trim());
          setCode('');
          setName('');
          await onPaired();
        }, `${name.trim()} appairée : la borne s’ouvre d’elle-même.`);
      }}>
        <p className="text-sm text-ink-600">
          Ouvrez l’adresse de la borne sur la tablette : elle affiche un code à 6 chiffres, valable 10 minutes.
        </p>
        <Field label="Code affiché sur la borne">
          <Input inputMode="numeric" autoComplete="off" maxLength={6} value={code} placeholder="000000"
            className="text-center font-mono text-2xl tracking-[0.4em]"
            onChange={event => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))} />
        </Field>
        <Field label="Établissement" hint="La borne ne verra et ne commandera que pour cet établissement.">
          <Select value={location} onChange={event => setLocationId(event.target.value)}>
            {overview.locations.map(item => <option key={item.locationId} value={item.locationId}>{item.locationName}</option>)}
          </Select>
        </Field>
        <Field label="Nom de la borne" hint="Par exemple « Borne entrée ».">
          <Input value={name} maxLength={60} onChange={event => setName(event.target.value)} />
        </Field>
      </form>
    </Modal>
  );
}

function KioskRow({kiosk, locationName, editable, onChanged}: {kiosk: Kiosk; locationName: string; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const active = kiosk.status === 'Active';
  return (
    <li className="flex flex-col gap-3 py-3 sm:flex-row sm:items-center">
      <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true"><Monitor className="size-5" /></span>
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2 font-medium">
          {kiosk.name}
          <Badge tone={active ? 'green' : 'neutral'}>{active ? 'Appairée' : 'Révoquée'}</Badge>
          {active && kiosk.isFrozen && <FrozenBadge />}
        </p>
        <p className="text-sm text-ink-500">
          {locationName} · {kiosk.appName} · jeton …{kiosk.tokenHint} · {active ? `depuis le ${dateTime(kiosk.pairedAtUtc)}` : `révoquée le ${dateTime(kiosk.revokedAtUtc ?? kiosk.pairedAtUtc)}`}
        </p>
      </div>
      {editable && active && (
        <Button size="sm" variant="secondary" busy={busy === 'revoke'} className="self-end sm:self-auto"
          onClick={() => void run('revoke', async () => {
            await revokeKiosk(kiosk.id);
            await onChanged();
          }, `${kiosk.name} révoquée : elle ne prend plus de commande.`)}>
          <Ban className="size-4" aria-hidden="true" />Révoquer
        </Button>
      )}
    </li>
  );
}

/** Les bornes appairées : appairage par le code affiché sur la tablette, révocation, nombre de bornes de l'option. */
export function KioskDevices({overview, editable}: {overview: StorefrontOverview; editable: boolean}) {
  const [pairing, setPairing] = useState(false);
  const data = useResource(signal => listKiosks(signal), []);
  const hasKioskApp = overview.apps.some(app => app.kind === 'Kiosk' && app.status === 'Active');
  const full = !!data.data && data.data.limit !== null && data.data.active >= data.data.limit;
  const nameOf = (id: string) => overview.locations.find(location => location.locationId === id)?.locationName ?? 'Établissement';

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start gap-3">
        <div className="min-w-0 flex-1">
          <h2 className="flex flex-wrap items-center gap-2 font-semibold">
            Bornes
            {data.data && <Badge tone={full ? 'amber' : 'neutral'}>{data.data.limit === null ? `${data.data.active} appairée(s)` : `${data.data.active} / ${data.data.limit}`}</Badge>}
          </h2>
          <p className="text-sm text-ink-500">Chaque tablette est appairée à un établissement. Révoquée, elle s’arrête aussitôt.</p>
        </div>
        {editable && (
          <Button size="sm" disabled={!hasKioskApp || full} onClick={() => setPairing(true)}>
            <Link2 className="size-4" aria-hidden="true" />Appairer une borne
          </Button>
        )}
      </div>
      {!hasKioskApp && <p className="mt-3 text-sm text-ink-500">Créez d’abord une application de type « Borne » : sa clé est à placer dans l’adresse de la borne.</p>}
      {full && <p className="mt-3 text-sm text-amber-700">Toutes les bornes de votre option sont appairées : révoquez-en une ou <Link href="/abonnement#options" className="font-medium underline">ajoutez des bornes</Link> à votre abonnement.</p>}
      {data.data?.kiosks.some(kiosk => kiosk.status === 'Active' && kiosk.isFrozen) && (
        <div className="mt-3"><FrozenNote what="Bornes gelées" action="Elles restent listées mais ne prennent plus de commande." fix="révoquez-en une" /></div>
      )}
      {data.error && <p className="mt-3 text-sm text-red-700">{data.error}</p>}
      {!data.data && !data.error && <Spinner label="Chargement des bornes…" />}
      {data.data && data.data.kiosks.length > 0 && (
        <ul className="mt-2 divide-y divide-slate-100">
          {data.data.kiosks.map(kiosk => (
            <KioskRow key={kiosk.id} kiosk={kiosk} locationName={nameOf(kiosk.locationId)} editable={editable} onChanged={data.reload} />
          ))}
        </ul>
      )}
      {data.data && data.data.kiosks.length === 0 && hasKioskApp && <p className="mt-3 text-sm text-ink-500">Aucune borne appairée.</p>}
      <PairDialog open={pairing} overview={overview} onClose={() => setPairing(false)} onPaired={async () => {
        setPairing(false);
        await data.reload();
      }} />
    </Card>
  );
}
