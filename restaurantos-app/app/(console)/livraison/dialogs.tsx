'use client';
import {useEffect, useMemo, useRef, useState} from 'react';
import {Banknote, MapPin, ShoppingBag, Smartphone, Truck} from 'lucide-react';
import {
  assignCourier, cancelFulfillment, changeAddress, confirmDelivered, courierKindLabel, createDelivery, createPickup, fail, updateContact,
  type CollectionMethod, type Courier, type DeliverySettings, type Fulfillment,
} from '@/lib/delivery';
import {money, orderTypeLabel} from '@/lib/format';
import {normalizePhone} from '@/lib/reservations';
import type {OrderSummary} from '@/lib/types';
import {Alert, Button, Field, Input, Modal, Select, Textarea, cx, useAction} from '@/components/ui';

const OTHER = 'other';

export function phoneOrThrow(value: string, required: boolean): string | null {
  const phone = normalizePhone(value);
  if (phone === undefined) throw new Error('Numéro de téléphone invalide : utilisez le format international (+229…).');
  if (phone === null && required) throw new Error('Le téléphone du client est obligatoire pour une livraison.');
  return phone;
}

/** Heure du jour (HH:MM) en instant ISO : demain si l'heure est déjà passée de plus de 5 minutes. */
function pickupInstant(value: string): string | null {
  if (!value) return null;
  const [hours, minutes] = value.split(':').map(Number);
  const at = new Date();
  at.setHours(hours, minutes, 0, 0);
  if (at.getTime() < Date.now() - 5 * 60_000) at.setDate(at.getDate() + 1);
  return at.toISOString();
}

export function NewFulfillmentDialog({open, orders, settings, initialOrderId, onClose, onDone, onOpenSettings}: {
  open: boolean;
  orders: OrderSummary[];
  settings: DeliverySettings | null;
  initialOrderId: string | null;
  onClose: () => void;
  onDone: () => Promise<void>;
  onOpenSettings: () => void;
}) {
  const {busy, run} = useAction();
  const [orderId, setOrderId] = useState('');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [pickupAt, setPickupAt] = useState('');
  const [zoneId, setZoneId] = useState('');
  const [address, setAddress] = useState('');
  const [landmark, setLandmark] = useState('');
  const [instructions, setInstructions] = useState('');
  const zones = useMemo(() => settings?.zones.filter(zone => zone.isActive) ?? [], [settings]);

  const opened = useRef(false);
  useEffect(() => {
    if (!open) return;
    setOrderId(current => (orders.some(order => order.id === current) ? current : orders[0]?.id ?? ''));
  }, [open, orders]);
  useEffect(() => {
    if (!open) {
      opened.current = false;
      return;
    }
    if (opened.current) return;
    opened.current = true;
    setOrderId(initialOrderId && orders.some(order => order.id === initialOrderId) ? initialOrderId : orders[0]?.id ?? '');
    setName('');
    setPhone('');
    setPickupAt('');
    setAddress('');
    setLandmark('');
    setInstructions('');
  }, [open, initialOrderId, orders]);
  useEffect(() => {
    if (open) setZoneId(current => (zones.some(zone => zone.id === current) ? current : zones[0]?.id ?? ''));
  }, [open, zones]);

  const order = orders.find(item => item.id === orderId);
  const delivery = order?.type === 'Delivery';
  const zone = zones.find(item => item.id === zoneId);
  const serviceOff = order && settings && (delivery ? !settings.deliveryEnabled : !settings.pickupEnabled);
  const belowMinimum = delivery && zone && order && order.total < zone.minimumOrder;

  return (
    <Modal open={open} onClose={onClose} title="Nouvelle fiche de retrait ou de livraison" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="fulfillment-form" busy={busy === 'create'} disabled={!order || !!serviceOff || (delivery && !zone)}>Créer la fiche</Button>
      </>
    }>
      {orders.length === 0 ? (
        <p className="text-sm text-ink-600">Aucune commande à emporter ou en livraison en attente de fiche. Créez d’abord la commande depuis l’écran Commandes.</p>
      ) : (
        <form id="fulfillment-form" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          if (!order) return;
          void run('create', async () => {
            const customerPhone = phoneOrThrow(phone, delivery);
            if (delivery) {
              await createDelivery({
                orderId: order.id, zoneId, customerName: name.trim(), customerPhone: customerPhone!, addressLine: address.trim(),
                landmark: landmark.trim() || null, instructions: instructions.trim() || null,
              });
            } else {
              await createPickup({orderId: order.id, customerName: name.trim(), customerPhone, pickupAt: pickupInstant(pickupAt)});
            }
            await onDone();
            onClose();
          }, delivery ? 'Livraison créée.' : 'Retrait créé.');
        }}>
          <Field label="Commande">
            <Select value={orderId} onChange={event => setOrderId(event.target.value)}>
              {orders.map(item => (
                <option key={item.id} value={item.id}>#{item.number} · {orderTypeLabel[item.type]} · {money(item.total, item.currencyCode)}</option>
              ))}
            </Select>
          </Field>
          {order && (
            <p className={cx('flex items-center gap-2 rounded-xl px-3 py-2 text-sm font-medium', delivery ? 'bg-sky-50 text-sky-800' : 'bg-amber-50 text-amber-800')}>
              {delivery ? <Truck className="size-4" aria-hidden="true" /> : <ShoppingBag className="size-4" aria-hidden="true" />}
              {delivery ? 'Livraison à domicile' : 'Retrait au comptoir'}
            </p>
          )}
          {serviceOff && (
            <Alert tone="amber">
              {delivery ? 'La livraison n’est pas activée pour cet établissement.' : 'Le retrait n’est pas activé pour cet établissement.'}{' '}
              <button type="button" className="font-semibold underline" onClick={onOpenSettings}>Ouvrir les réglages</button>
            </Alert>
          )}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="Nom du client"><Input required maxLength={100} autoComplete="off" value={name} onChange={event => setName(event.target.value)} /></Field>
            <Field label={delivery ? 'Téléphone' : 'Téléphone (facultatif)'}>
              <Input type="tel" required={delivery} autoComplete="off" placeholder="+229 01 90 00 00 00" value={phone} onChange={event => setPhone(event.target.value)} />
            </Field>
          </div>
          {!delivery && (
            <Field label="Heure de retrait souhaitée" hint={`Facultatif. Sans heure : prête dans ${settings?.pickupLeadMinutes ?? 20} min environ.`}>
              <Input type="time" value={pickupAt} onChange={event => setPickupAt(event.target.value)} />
            </Field>
          )}
          {delivery && (
            <>
              <Field label="Zone de livraison">
                <Select value={zoneId} onChange={event => setZoneId(event.target.value)} disabled={zones.length === 0}>
                  {zones.length === 0 && <option value="">Aucune zone active</option>}
                  {zones.map(item => (
                    <option key={item.id} value={item.id}>
                      {item.name} · frais {money(item.fee, settings!.currencyCode)} · min {money(item.minimumOrder, settings!.currencyCode)} · {item.estimatedMinutes} min
                    </option>
                  ))}
                </Select>
              </Field>
              {belowMinimum && <Alert tone="amber">La commande ({money(order!.total, order!.currencyCode)}) n’atteint pas le minimum de cette zone ({money(zone!.minimumOrder, settings!.currencyCode)}).</Alert>}
              <Field label="Adresse"><Input required maxLength={200} value={address} onChange={event => setAddress(event.target.value)} placeholder="Rue, quartier, maison…" /></Field>
              <Field label="Point de repère (facultatif)"><Input maxLength={200} value={landmark} onChange={event => setLandmark(event.target.value)} placeholder="Près de la pharmacie…" /></Field>
              <Field label="Instructions (facultatif)"><Textarea rows={2} maxLength={500} value={instructions} onChange={event => setInstructions(event.target.value)} /></Field>
              {zone && <p className="text-xs text-ink-500">Frais de livraison {money(zone.fee, settings!.currencyCode)}, ajoutés au total de la commande.</p>}
            </>
          )}
        </form>
      )}
    </Modal>
  );
}

export function CourierDialog({fulfillment, couriers, onClose, onDone}: {
  fulfillment: Fulfillment | null;
  couriers: Courier[];
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [choice, setChoice] = useState('');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const active = useMemo(() => couriers.filter(courier => courier.isActive), [couriers]);
  useEffect(() => {
    if (!fulfillment) return;
    const registered = fulfillment.courierId && active.some(courier => courier.id === fulfillment.courierId) ? fulfillment.courierId : null;
    setChoice(registered ?? (fulfillment.courierName ? OTHER : active[0]?.id ?? OTHER));
    setName(registered ? '' : fulfillment.courierName ?? '');
    setPhone(registered ? '' : fulfillment.courierPhone ?? '');
  }, [fulfillment, active]);
  const picked = active.find(courier => courier.id === choice);

  return (
    <Modal open={!!fulfillment} onClose={onClose} title={fulfillment?.courierName ? 'Changer de livreur' : 'Assigner un livreur'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="courier-form" busy={busy === 'courier'}>Assigner</Button>
      </>
    }>
      <form id="courier-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        if (!fulfillment) return;
        void run('courier', async () => {
          await assignCourier(fulfillment.id, picked ? {courierId: picked.id} : {name: name.trim(), phone: phoneOrThrow(phone, false)});
          await onDone();
          onClose();
        }, 'Livreur assigné.');
      }}>
        {active.length > 0 && (
          <Field label="Livreur">
            <Select value={choice} onChange={event => setChoice(event.target.value)}>
              {active.map(courier => (
                <option key={courier.id} value={courier.id}>
                  {courier.name} · {courierKindLabel[courier.kind]}
                  {courier.feePerDelivery ? ` · ${money(courier.feePerDelivery, courier.feeCurrencyCode ?? fulfillment?.feeCurrencyCode ?? 'XOF')} la course` : ''}
                </option>
              ))}
              <option value={OTHER}>Autre livreur (saisi à la main)</option>
            </Select>
          </Field>
        )}
        {picked ? (
          <p className="text-sm text-ink-600">
            {picked.phone ? `Téléphone ${picked.phone}. ` : 'Pas de téléphone enregistré. '}
            {picked.feePerDelivery ? `Son tarif (${money(picked.feePerDelivery, picked.feeCurrencyCode ?? 'XOF')}) est figé sur cette course et réglé à sa remise.` : 'Pas de tarif par course.'}
          </p>
        ) : (
          <>
            <Field label="Nom du livreur"><Input required maxLength={80} autoComplete="off" value={name} onChange={event => setName(event.target.value)} /></Field>
            <Field label="Téléphone du livreur (facultatif)"><Input type="tel" autoComplete="off" value={phone} onChange={event => setPhone(event.target.value)} /></Field>
            {active.length === 0 && <p className="text-xs text-ink-500">Enregistrez vos livreurs dans l’onglet Livreurs pour les choisir ici et suivre leur tarif par course.</p>}
          </>
        )}
      </form>
    </Modal>
  );
}

/** Livraison remise : s'il restait à payer, ce que le client a donné au livreur et comment. */
export function DeliveredDialog({fulfillment, currency, onClose, onDone}: {
  fulfillment: Fulfillment | null;
  currency: string;
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [method, setMethod] = useState<CollectionMethod>('Cash');
  const [amount, setAmount] = useState('');
  useEffect(() => {
    setMethod('Cash');
    setAmount(fulfillment?.amountToCollect !== null && fulfillment?.amountToCollect !== undefined ? String(fulfillment.amountToCollect) : '');
  }, [fulfillment]);
  const due = fulfillment?.amountToCollect ?? 0;
  const value = Number(amount.replace(',', '.'));
  const partial = amount !== '' && value > 0 && value < due;

  return (
    <Modal open={!!fulfillment} onClose={onClose} title="Confirmer la livraison" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="delivered-form" variant="success" busy={busy === 'delivered'}>Livrée et encaissée</Button>
      </>
    }>
      <form id="delivered-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        if (!fulfillment) return;
        void run('delivered', async () => {
          if (!(value > 0) || value > due) throw new Error(`Le montant encaissé doit être compris entre 0 et ${money(due, currency)}.`);
          await confirmDelivered(fulfillment.id, {collected: value, method});
          await onDone();
          onClose();
        }, 'Livraison confirmée, paiement enregistré.');
      }}>
        <p className="rounded-xl bg-sky-50 px-3 py-2 text-sm text-sky-900">
          Le livreur devait encaisser <strong>{money(due, currency)}</strong> auprès de {fulfillment?.customerName}.
        </p>
        <Field label="Le client a payé">
          <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Mode de paiement">
            {(['Cash', 'MobileMoney'] as const).map(item => (
              <button key={item} type="button" role="radio" aria-checked={method === item} onClick={() => setMethod(item)}
                className={cx('flex items-center justify-center gap-2 rounded-xl border px-3 py-2.5 text-sm font-semibold', method === item ? 'border-brand-600 bg-brand-50 text-brand-700' : 'border-slate-200 hover:border-slate-300')}>
                {item === 'Cash' ? <Banknote className="size-4" aria-hidden="true" /> : <Smartphone className="size-4" aria-hidden="true" />}
                {item === 'Cash' ? 'En espèces au livreur' : 'Mobile money au restaurant'}
              </button>
            ))}
          </div>
        </Field>
        <Field label="Montant reçu" hint={method === 'Cash' ? 'Le livreur garde ces espèces jusqu’à sa remise.' : 'Vérifiez la réception sur le téléphone du restaurant.'}>
          <Input inputMode="decimal" required value={amount} onChange={event => setAmount(event.target.value)} />
        </Field>
        {partial && <Alert tone="amber">Paiement partiel : il restera {money(due - value, currency)} à encaisser sur la commande.</Alert>}
      </form>
    </Modal>
  );
}

export function ReasonDialog({fulfillment, kind, onClose, onDone}: {fulfillment: Fulfillment | null; kind: 'fail' | 'cancel'; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [reason, setReason] = useState('');
  useEffect(() => setReason(''), [fulfillment]);
  const failing = kind === 'fail';
  return (
    <Modal open={!!fulfillment} onClose={onClose} title={failing ? 'Signaler un échec de livraison' : 'Annuler la fiche'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Retour</Button>
        <Button type="submit" form="reason-form" variant="danger" busy={busy === 'reason'}>{failing ? 'Signaler l’échec' : 'Annuler la fiche'}</Button>
      </>
    }>
      <form id="reason-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        if (!fulfillment) return;
        void run('reason', async () => {
          if (failing) await fail(fulfillment.id, reason.trim());
          else await cancelFulfillment(fulfillment.id, reason.trim() || null);
          await onDone();
          onClose();
        }, failing ? 'Échec enregistré.' : 'Fiche annulée.');
      }}>
        <p className="text-sm text-ink-600">
          {failing
            ? 'La commande revient au restaurant. Vous pourrez relancer la livraison avec un livreur.'
            : 'La commande elle-même n’est pas annulée : vous pourrez créer une nouvelle fiche.'}
        </p>
        <Field label={failing ? 'Motif' : 'Motif (facultatif)'}>
          <Textarea rows={3} required={failing} maxLength={300} value={reason} onChange={event => setReason(event.target.value)} placeholder={failing ? 'Client absent, adresse introuvable…' : ''} />
        </Field>
      </form>
    </Modal>
  );
}

export function EditDialog({fulfillment, onClose, onDone}: {fulfillment: Fulfillment | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [address, setAddress] = useState('');
  const [landmark, setLandmark] = useState('');
  const [instructions, setInstructions] = useState('');
  useEffect(() => {
    setName(fulfillment?.customerName ?? '');
    setPhone(fulfillment?.customerPhone ?? '');
    setAddress(fulfillment?.addressLine ?? '');
    setLandmark(fulfillment?.landmark ?? '');
    setInstructions(fulfillment?.instructions ?? '');
  }, [fulfillment]);
  const delivery = fulfillment?.mode === 'Delivery';
  const addressEditable = delivery && (fulfillment?.status === 'Pending' || fulfillment?.status === 'Assigned');

  return (
    <Modal open={!!fulfillment} onClose={onClose} title="Client et adresse" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="edit-form" busy={busy === 'edit'}>Enregistrer</Button>
      </>
    }>
      <form id="edit-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        if (!fulfillment) return;
        void run('edit', async () => {
          const customerPhone = phoneOrThrow(phone, delivery);
          if (name.trim() !== fulfillment.customerName || customerPhone !== fulfillment.customerPhone) {
            await updateContact(fulfillment.id, name.trim(), customerPhone);
          }
          if (addressEditable && (address.trim() !== fulfillment.addressLine || (landmark.trim() || null) !== fulfillment.landmark || (instructions.trim() || null) !== fulfillment.instructions)) {
            await changeAddress(fulfillment.id, address.trim(), landmark.trim() || null, instructions.trim() || null);
          }
          await onDone();
          onClose();
        }, 'Fiche mise à jour.');
      }}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Nom du client"><Input required maxLength={100} value={name} onChange={event => setName(event.target.value)} /></Field>
          <Field label={delivery ? 'Téléphone' : 'Téléphone (facultatif)'}><Input type="tel" required={delivery} value={phone} onChange={event => setPhone(event.target.value)} /></Field>
        </div>
        {delivery && (
          addressEditable ? (
            <>
              <Field label="Adresse"><Input required maxLength={200} value={address} onChange={event => setAddress(event.target.value)} /></Field>
              <Field label="Point de repère (facultatif)"><Input maxLength={200} value={landmark} onChange={event => setLandmark(event.target.value)} /></Field>
              <Field label="Instructions (facultatif)"><Textarea rows={2} maxLength={500} value={instructions} onChange={event => setInstructions(event.target.value)} /></Field>
            </>
          ) : (
            <p className="flex items-start gap-2 text-sm text-ink-600"><MapPin className="mt-0.5 size-4 shrink-0" aria-hidden="true" />Le livreur est parti : l’adresse ne peut plus être modifiée.</p>
          )
        )}
      </form>
    </Modal>
  );
}
