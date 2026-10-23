'use client';
import {useEffect, useMemo, useState} from 'react';
import {useRouter} from 'next/navigation';
import {Bike, Check, MessageCircle, Phone, UserPlus} from 'lucide-react';
import {post} from '@/lib/api';
import {modifierGroups, productDetail, sellableCatalog} from '@/lib/catalog';
import {createCustomer, searchCustomers, searchQuery, type Customer} from '@/lib/customers';
import {useResource} from '@/lib/hooks';
import {parseMessageOrder} from '@/lib/message-order';
import {createOrder, deliveryPlatforms} from '@/lib/orders';
import type {LocationSummary, OrderType} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction, useToast} from '@/components/ui';

const phoneOf = (value: string) => value.replace(/[\s.\-()]/g, '');

/**
 * Commande reçue par WhatsApp ou au téléphone : le numéro retrouve la fiche du client (ou en crée une), le message collé
 * est lu pour préremplir les articles, puis la commande s'ouvre pour être complétée.
 */
export function RemoteOrderDialog({location, open, onClose}: {location: LocationSummary; open: boolean; onClose: () => void}) {
  const router = useRouter();
  const toast = useToast();
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [channel, setChannel] = useState<'WhatsApp' | 'Phone' | 'DeliveryPlatform'>('WhatsApp');
  const [platform, setPlatform] = useState(deliveryPlatforms[0].code);
  const [external, setExternal] = useState('');
  const [type, setType] = useState<OrderType>('Takeaway');
  const [phone, setPhone] = useState('');
  const [name, setName] = useState('');
  const [message, setMessage] = useState('');
  const [lookup, setLookup] = useState('');
  useEffect(() => {
    if (!open) return;
    setPhone('+229 ');
    setName('');
    setMessage('');
    setExternal('');
  }, [open]);
  useEffect(() => {
    const timer = setTimeout(() => setLookup(phoneOf(phone)), 400);
    return () => clearTimeout(timer);
  }, [phone]);
  const validPhone = /^\+\d{8,15}$/.test(lookup);
  const found = useResource(
    signal => (open && validPhone && can('customers.view') ? searchCustomers(searchQuery(lookup, '', false, 1), signal) : Promise.resolve(null)),
    [open, lookup],
  );
  const customer: Customer | undefined = found.data?.items[0];
  const catalog = useResource(signal => (open ? sellableCatalog(location.id, signal) : Promise.resolve(null)), [open, location.id]);
  const products = useMemo(() => {
    const unique = new Map((catalog.data?.sections ?? []).flatMap(section => section.products).map(product => [product.id, product]));
    return [...unique.values()];
  }, [catalog.data]);
  const lines = useMemo(() => (message.trim() ? parseMessageOrder(message, products) : []), [message, products]);
  const recognized = lines.filter(line => line.productId);
  const productName = (id: string) => products.find(product => product.id === id)?.name ?? '';

  const submit = () => run('create', async () => {
    let customerId = customer?.id ?? null;
    if (!customerId && validPhone && name.trim() && can('customers.manage')) {
      customerId = (await createCustomer({name: name.trim(), email: null, phone: lookup, tags: [], preferences: null})).id;
    }

    const id = await createOrder(location.id, type, undefined, channel === 'DeliveryPlatform'
      ? {channel, customerId, platform, externalReference: external}
      : {channel, customerId});
    const later: string[] = [];
    const required = new Set((recognized.length ? await modifierGroups() : []).filter(group => group.minSelections > 0).map(group => group.id));
    for (const line of recognized) {
      const product = await productDetail(line.productId!);
      // Une taille ou une option obligatoire se choisit sur la commande ; les options facultatives restent vides.
      if (product.variants.some(variant => variant.isAvailable) || product.modifierGroupIds.some(group => required.has(group))) {
        later.push(product.name);
        continue;
      }
      await post(`/orders/${id}/items`, {productId: product.id, quantity: line.quantity, variantId: null, modifierIds: [], note: null});
    }

    if (later.length) toast('success', `Commande créée. À compléter (taille, options) : ${later.join(', ')}.`);
    onClose();
    router.push(`/commandes/${id}`);
  });

  return (
    <Modal wide open={open} onClose={onClose} title="Commande reçue à distance" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'create'} onClick={() => void submit()}>Créer la commande{recognized.length ? ` (${recognized.length} article${recognized.length > 1 ? 's' : ''})` : ''}</Button>
      </>
    }>
      <div className="space-y-4 text-sm">
        <div className="grid grid-cols-3 gap-2" role="radiogroup" aria-label="Canal">
          {([['WhatsApp', 'WhatsApp', MessageCircle], ['Phone', 'Téléphone', Phone], ['DeliveryPlatform', 'Plateforme', Bike]] as const).map(([value, label, Icon]) => (
            <button key={value} type="button" role="radio" aria-checked={channel === value} onClick={() => {
              setChannel(value);
              if (value === 'DeliveryPlatform') setType('Delivery');
            }}
              className={cx('flex items-center justify-center gap-2 rounded-xl border px-3 py-2.5 font-semibold', channel === value ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 text-ink-600')}>
              <Icon className="size-4" aria-hidden="true" />{label}
            </button>
          ))}
        </div>
        {channel === 'DeliveryPlatform' && (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field label="Plateforme">
              <Select value={platform} onChange={event => setPlatform(event.target.value)}>
                {deliveryPlatforms.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}
              </Select>
            </Field>
            <Field label="N° de commande sur la plateforme" hint="Évite de saisir deux fois la même commande.">
              <Input maxLength={64} value={external} onChange={event => setExternal(event.target.value)} placeholder="Ex. GL-48210" />
            </Field>
          </div>
        )}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label={channel === 'DeliveryPlatform' ? 'Numéro du client (facultatif)' : 'Numéro du client'}>
            <Input type="tel" value={phone} onChange={event => setPhone(event.target.value)} placeholder="+229 01 97 00 00 00" />
          </Field>
          <Field label="Mode">
            <Select value={type} onChange={event => setType(event.target.value as OrderType)}>
              <option value="Takeaway">À emporter</option>
              <option value="Delivery">Livraison</option>
            </Select>
          </Field>
        </div>
        {validPhone && can('customers.view') && (
          found.loading && !found.data ? <Spinner label="Recherche du client…" /> : customer ? (
            <p className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded-xl bg-emerald-50 px-3 py-2 text-emerald-800">
              <Check className="size-4" aria-hidden="true" /><span>Client reconnu : <strong>{customer.name}</strong></span>{customer.tags.length > 0 && customer.tags.slice(0, 2).map(tag => <Badge key={tag} tone="brand">{tag}</Badge>)}
            </p>
          ) : can('customers.manage') ? (
            <Field label="Nouveau client : son nom (facultatif)" hint="Une fiche est créée avec ce numéro pour retrouver ses commandes.">
              <div className="relative">
                <UserPlus className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
                <Input className="pl-9" maxLength={150} value={name} onChange={event => setName(event.target.value)} placeholder="Nom du client" />
              </div>
            </Field>
          ) : <p className="text-ink-500">Numéro inconnu : la commande sera créée sans fiche client.</p>
        )}
        <Field label="Message du client (facultatif)" hint="Collez le message : les quantités et les plats reconnus sont ajoutés à la commande.">
          <Textarea rows={4} value={message} onChange={event => setMessage(event.target.value)} placeholder={'Bonjour, 2 poulets braisés et 1 bissap svp'} />
        </Field>
        {catalog.error && <Alert>{catalog.error}</Alert>}
        {lines.length > 0 && (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {lines.map((line, index) => (
              <li key={index} className="flex items-center gap-3 px-3 py-2">
                <span className="min-w-0 flex-1 truncate text-ink-500">« {line.text} »</span>
                {line.productId ? <span className="font-medium">{line.quantity} × {productName(line.productId)}</span> : <Badge>Non reconnu</Badge>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  );
}
