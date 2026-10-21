'use client';
import {useEffect, useState, type ReactNode} from 'react';
import {MapPinned, Pencil, Plus, ShoppingBag, Trash2, Truck} from 'lucide-react';
import {addZone, configureDelivery, configurePickup, removeZone, setZoneActive, updateZone, type DeliverySettings, type DeliveryZone} from '@/lib/delivery';
import {money, parseAmount} from '@/lib/format';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, cx, useAction} from '@/components/ui';

function Switch({on, label, disabled, onChange}: {on: boolean; label: string; disabled?: boolean; onChange: () => void}) {
  return (
    <button type="button" role="switch" aria-checked={on} aria-label={label} disabled={disabled} onClick={onChange}
      className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', on ? 'bg-brand-600' : 'bg-slate-300')}>
      <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', on ? 'left-[1.375rem]' : 'left-0.5')} />
    </button>
  );
}

function ServiceCard({icon, tone, title, description, enabled, lead, editable, onSave, children}: {
  icon: ReactNode;
  tone: string;
  title: string;
  description: string;
  enabled: boolean;
  lead: number;
  editable: boolean;
  onSave: (enabled: boolean, lead: number) => Promise<boolean>;
  children?: ReactNode;
}) {
  const [value, setValue] = useState(String(lead));
  useEffect(() => setValue(String(lead)), [lead]);
  const minutes = Number(value);
  const valid = value !== '' && Number.isInteger(minutes) && minutes >= 0 && minutes <= 240;
  return (
    <Card className="p-5">
      <div className="flex items-start gap-4">
        <span className={cx('grid size-11 shrink-0 place-items-center rounded-xl', tone)} aria-hidden="true">{icon}</span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center justify-between gap-3">
            <h2 className="font-semibold">{title}</h2>
            <Switch on={enabled} label={`${enabled ? 'Désactiver' : 'Activer'} : ${title}`} disabled={!editable} onChange={() => void onSave(!enabled, lead)} />
          </div>
          <p className="mt-1 text-sm text-ink-500">{description}</p>
          <form className="mt-4 flex flex-wrap items-end gap-2" onSubmit={event => {
            event.preventDefault();
            if (valid) void onSave(enabled, minutes);
          }}>
            <Field label="Délai annoncé (minutes)" className="w-44">
              <Input type="number" min={0} max={240} step={1} inputMode="numeric" disabled={!editable} value={value} onChange={event => setValue(event.target.value)} />
            </Field>
            {editable && <Button type="submit" variant="secondary" disabled={!valid || minutes === lead}>Enregistrer</Button>}
          </form>
          {children}
        </div>
      </div>
    </Card>
  );
}

function ZoneDialog({open, zone, currency, onClose, onSave}: {
  open: boolean;
  zone: DeliveryZone | null;
  currency: string;
  onClose: () => void;
  onSave: (input: {name: string; fee: number; minimumOrder: number; estimatedMinutes: number}) => Promise<boolean>;
}) {
  const [name, setName] = useState('');
  const [fee, setFee] = useState('');
  const [minimum, setMinimum] = useState('');
  const [minutes, setMinutes] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!open) return;
    setName(zone?.name ?? '');
    setFee(zone ? String(zone.fee) : '');
    setMinimum(zone ? String(zone.minimumOrder) : '0');
    setMinutes(zone ? String(zone.estimatedMinutes) : '30');
    setError(null);
  }, [open, zone]);

  return (
    <Modal open={open} onClose={onClose} title={zone ? 'Modifier la zone' : 'Nouvelle zone de livraison'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="zone-form" busy={busy}>{zone ? 'Enregistrer' : 'Ajouter la zone'}</Button>
      </>
    }>
      <form id="zone-form" className="space-y-4" onSubmit={async event => {
        event.preventDefault();
        const feeValue = parseAmount(fee);
        const minimumValue = parseAmount(minimum || '0');
        const minutesValue = Number(minutes);
        if (feeValue === null || minimumValue === null) return setError('Montants invalides.');
        if (!Number.isInteger(minutesValue) || minutesValue < 5 || minutesValue > 240) return setError('La durée doit être comprise entre 5 et 240 minutes.');
        setError(null);
        setBusy(true);
        const ok = await onSave({name: name.trim(), fee: feeValue, minimumOrder: minimumValue, estimatedMinutes: minutesValue});
        setBusy(false);
        if (ok) onClose();
      }}>
        {error && <Alert>{error}</Alert>}
        <Field label="Nom de la zone"><Input required maxLength={80} value={name} onChange={event => setName(event.target.value)} placeholder="Centre-ville, Cadjehoun…" /></Field>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label={`Frais (${currency})`}><Input required inputMode="decimal" value={fee} onChange={event => setFee(event.target.value)} placeholder="1000" /></Field>
          <Field label={`Minimum (${currency})`}><Input inputMode="decimal" value={minimum} onChange={event => setMinimum(event.target.value)} /></Field>
          <Field label="Durée (min)"><Input type="number" min={5} max={240} required value={minutes} onChange={event => setMinutes(event.target.value)} /></Field>
        </div>
        <p className="text-xs text-ink-500">La durée s’ajoute au délai de préparation pour calculer l’heure de livraison annoncée.</p>
      </form>
    </Modal>
  );
}

export function SettingsPanel({settings, editable, onChanged}: {settings: DeliverySettings; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [editing, setEditing] = useState<DeliveryZone | 'new' | null>(null);
  const [removing, setRemoving] = useState<DeliveryZone | null>(null);
  const location = settings.locationId;
  const currency = settings.currencyCode;
  const activeZones = settings.zones.filter(zone => zone.isActive).length;

  return (
    <div className="space-y-6">
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres qui règlent la livraison peuvent modifier ces réglages.</Alert>}
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <ServiceCard
          icon={<ShoppingBag className="size-5" />}
          tone="bg-amber-50 text-amber-600"
          title="Retrait au comptoir"
          description="Le client passe chercher sa commande à emporter. Le délai sert à annoncer l’heure à laquelle elle sera prête."
          enabled={settings.pickupEnabled}
          lead={settings.pickupLeadMinutes}
          editable={editable}
          onSave={(enabled, lead) => run('pickup', async () => {
            await configurePickup(location, enabled, lead);
            await onChanged();
          }, 'Retrait enregistré.')}
        />
        <ServiceCard
          icon={<Truck className="size-5" />}
          tone="bg-sky-50 text-sky-600"
          title="Livraison à domicile"
          description="Délai de préparation avant le départ du livreur ; la durée de la zone s’y ajoute."
          enabled={settings.deliveryEnabled}
          lead={settings.deliveryLeadMinutes}
          editable={editable}
          onSave={(enabled, lead) => run('delivery', async () => {
            await configureDelivery(location, enabled, lead);
            await onChanged();
          }, 'Livraison enregistrée.')}>
          {!settings.deliveryEnabled && activeZones === 0 && <p className="mt-3 text-xs text-ink-500">Ajoutez au moins une zone active pour activer la livraison.</p>}
        </ServiceCard>
      </div>

      <Card className="p-5">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="font-semibold">Zones de livraison</h2>
            <p className="text-sm text-ink-500">Frais, minimum de commande et durée du trajet par quartier.</p>
          </div>
          {editable && <Button onClick={() => setEditing('new')}><Plus className="size-4" aria-hidden="true" />Ajouter une zone</Button>}
        </div>
        {settings.zones.length === 0 ? (
          <Empty icon={<MapPinned className="size-8" />} title="Aucune zone">Créez une zone pour chaque quartier livré.</Empty>
        ) : (
          <div className="relative overflow-x-auto">
            <table className="w-full min-w-[640px] text-left text-sm">
              <thead className="border-b border-slate-200 text-xs text-ink-500">
                <tr>
                  <th scope="col" className="px-3 py-2 font-medium">Zone</th>
                  <th scope="col" className="px-3 py-2 text-right font-medium">Frais</th>
                  <th scope="col" className="px-3 py-2 text-right font-medium">Minimum</th>
                  <th scope="col" className="px-3 py-2 text-right font-medium">Durée</th>
                  <th scope="col" className="px-3 py-2 font-medium">Active</th>
                  <th scope="col" className="px-3 py-2"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {settings.zones.map(zone => (
                  <tr key={zone.id}>
                    <td className="px-3 py-3 font-semibold">{zone.name}{!zone.isActive && <Badge className="ml-2">Inactive</Badge>}</td>
                    <td className="px-3 py-3 text-right">{money(zone.fee, currency)}</td>
                    <td className="px-3 py-3 text-right">{zone.minimumOrder > 0 ? money(zone.minimumOrder, currency) : '—'}</td>
                    <td className="px-3 py-3 text-right">{zone.estimatedMinutes} min</td>
                    <td className="px-3 py-3">
                      <Switch on={zone.isActive} label={`${zone.isActive ? 'Désactiver' : 'Activer'} la zone ${zone.name}`} disabled={!editable || !!busy}
                        onChange={() => void run(`z-${zone.id}`, async () => {
                          await setZoneActive(location, zone.id, !zone.isActive);
                          await onChanged();
                        }, zone.isActive ? 'Zone désactivée.' : 'Zone activée.')} />
                    </td>
                    <td className="px-3 py-3">
                      {editable && (
                        <span className="flex justify-end gap-1">
                          <Button size="sm" variant="ghost" aria-label={`Modifier ${zone.name}`} onClick={() => setEditing(zone)}><Pencil className="size-4" aria-hidden="true" /></Button>
                          <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Supprimer ${zone.name}`} onClick={() => setRemoving(zone)}><Trash2 className="size-4" aria-hidden="true" /></Button>
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <ZoneDialog
        open={editing !== null}
        zone={editing === 'new' ? null : editing}
        currency={currency}
        onClose={() => setEditing(null)}
        onSave={input => run('zone', async () => {
          if (editing === 'new' || editing === null) await addZone(location, input);
          else await updateZone(location, editing.id, input);
          await onChanged();
        }, editing === 'new' ? 'Zone ajoutée.' : 'Zone enregistrée.')}
      />
      <Modal open={!!removing} onClose={() => setRemoving(null)} title="Supprimer la zone" footer={
        <>
          <Button variant="secondary" onClick={() => setRemoving(null)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'remove'} onClick={() => void run('remove', async () => {
            await removeZone(location, removing!.id);
            setRemoving(null);
            await onChanged();
          }, 'Zone supprimée.')}>Supprimer</Button>
        </>
      }>
        <p className="text-sm text-ink-600">La zone « {removing?.name} » ne sera plus proposée. Les livraisons en cours gardent leurs frais.</p>
      </Modal>
    </div>
  );
}
