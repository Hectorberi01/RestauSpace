'use client';
import {useEffect, useState} from 'react';
import {UserRound} from 'lucide-react';
import type {Customer} from '@/lib/customers';
import {useResource} from '@/lib/hooks';
import {channelLabel, customerChannels, effectiveTemplate, languages, listTemplates, loadMessageTypes, messageGroups, sendMessage, skipLabel, variableLabel, type MessageChannel} from '@/lib/messages';
import {useConsole} from '@/components/console';
import {CustomerPickerDialog} from '@/components/customer-picker';
import {Alert, Button, Field, Input, Modal, Select, useAction, useToast} from '@/components/ui';

/** Remplies par le backend (prénom lu dans la fiche) ou par la console : pas de saisie. */
const automatic = new Set(['customer.firstName', 'unsubscribe.url']);

export function SendDialog({open, initialCustomer, onClose, onSent}: {open: boolean; initialCustomer?: Customer | null; onClose: () => void; onSent: () => Promise<void>}) {
  const {tenant, location, locations} = useConsole();
  const {busy, run} = useAction();
  const toast = useToast();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [picking, setPicking] = useState(false);
  const [typeKey, setTypeKey] = useState('order.ready');
  const [channel, setChannel] = useState<MessageChannel>('Sms');
  const [language, setLanguage] = useState('fr');
  const [locationId, setLocationId] = useState('');
  const [values, setValues] = useState<Record<string, string>>({});
  const [later, setLater] = useState('');
  const [key, setKey] = useState('');
  const [skipped, setSkipped] = useState<string | null>(null);
  const types = useResource(signal => (open ? loadMessageTypes(signal) : Promise.resolve(null)), [open]);
  const type = types.data?.find(item => item.key === typeKey);
  const templates = useResource(signal => (open ? listTemplates(signal) : Promise.resolve(null)), [open]);
  const template = templates.data ? effectiveTemplate(templates.data, typeKey, channel, language) : null;

  useEffect(() => {
    if (!open) return;
    setCustomer(initialCustomer ?? null);
    setLocationId(location?.id ?? '');
    setValues({});
    setLater('');
    setSkipped(null);
    setKey(crypto.randomUUID());
  }, [open, initialCustomer, location?.id]);

  const site = locations.find(item => item.id === locationId);
  const prefilled: Record<string, string> = {'restaurant.name': tenant.branding.displayName || tenant.name, 'location.name': site?.name ?? ''};
  const fields = (template?.variables ?? []).filter(name => !!type?.variables.includes(name) && !automatic.has(name));
  const valueOf = (name: string) => values[name] ?? prefilled[name] ?? '';
  const missing = fields.filter(name => !valueOf(name).trim());
  const when = later ? new Date(later) : null;
  const ready = !!customer && !!template && missing.length === 0 && (!when || when.getTime() > Date.now());

  return (
    <>
      <Modal wide open={open} onClose={onClose} title="Envoyer un message à un client" footer={
        <>
          <Button variant="secondary" onClick={onClose}>Fermer</Button>
          <Button type="submit" form="send-form" busy={busy === 'send'} disabled={!ready}>{when ? 'Programmer' : 'Envoyer'}</Button>
        </>
      }>
        <form id="send-form" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          if (!ready || !customer) return;
          setSkipped(null);
          void run('send', async () => {
            const result = await sendMessage({
              type: typeKey,
              channel,
              customerId: customer.id,
              locationId: locationId || null,
              language,
              variables: Object.fromEntries(fields.map(name => [name, valueOf(name).trim()])),
              scheduledAtUtc: when ? when.toISOString() : null,
              idempotencyKey: key,
            });
            if (result.skipped) {
              setSkipped(skipLabel[result.skipped] ?? result.skipped);
              setKey(crypto.randomUUID());
              return;
            }
            toast('success', when ? 'Message programmé.' : 'Message envoyé : suivez sa remise dans l’historique.');
            onClose();
            await onSent();
          });
        }}>
          <Field label="Client">
            <button type="button" onClick={() => setPicking(true)} className="flex w-full items-center gap-3 rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-left text-sm hover:border-brand-300">
              <UserRound className="size-4 text-ink-500" aria-hidden="true" />
              {customer ? (
                <span className="min-w-0 flex-1 truncate"><strong>{customer.name}</strong> <span className="text-ink-500">{[customer.phone, customer.email].filter(Boolean).join(' · ')}</span></span>
              ) : <span className="text-ink-500">Choisir un client…</span>}
            </button>
          </Field>
          <div className="grid gap-3 sm:grid-cols-3">
            <Field label="Message" className="sm:col-span-3">
              <Select value={typeKey} onChange={event => setTypeKey(event.target.value)}>
                {messageGroups.filter(group => types.data?.some(item => item.group === group)).map(group => (
                  <optgroup key={group} label={group}>
                    {(types.data ?? []).filter(item => item.group === group).map(item => <option key={item.key} value={item.key}>{item.label}</option>)}
                  </optgroup>
                ))}
              </Select>
            </Field>
            <Field label="Canal">
              <Select value={channel} onChange={event => setChannel(event.target.value as MessageChannel)}>
                {customerChannels.map(item => <option key={item} value={item}>{channelLabel[item]}</option>)}
              </Select>
            </Field>
            <Field label="Langue">
              <Select value={language} onChange={event => setLanguage(event.target.value)}>
                {languages.map(([code, label]) => <option key={code} value={code}>{label}</option>)}
              </Select>
            </Field>
            <Field label="Établissement">
              <Select value={locationId} onChange={event => setLocationId(event.target.value)}>
                {locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
              </Select>
            </Field>
          </div>
          {type?.marketing && <Alert tone="blue">Message marketing : il ne part que si le client a accepté ce canal, et seulement dans les limites de fréquence.</Alert>}
          {templates.data && !template && <Alert tone="amber">Aucun modèle publié et actif pour ce message en {channelLabel[channel]} ({language}) : rien ne partirait. Créez-le dans l’onglet Modèles.</Alert>}
          {template && fields.length > 0 && <p className="text-sm font-semibold">Informations du message</p>}
          <div className="grid gap-3 sm:grid-cols-2">
            {fields.map(name => (
              <Field key={name} label={variableLabel[name] ?? name}>
                <Input value={valueOf(name)} maxLength={500} onChange={event => setValues(current => ({...current, [name]: event.target.value}))}
                  type={name.endsWith('.url') ? 'url' : 'text'} />
              </Field>
            ))}
          </div>
          <p className="text-xs text-ink-500">Le prénom du client est repris de sa fiche. Le texte est celui du modèle en service ({template ? (template.isPlatform ? 'standard' : `votre version ${template.version}`) : 'aucun'}) ; seules les informations qu’il utilise sont demandées.</p>
          <Field label="Programmer (facultatif)" hint="Laissez vide pour envoyer tout de suite." className="sm:max-w-xs">
            <Input type="datetime-local" value={later} onChange={event => setLater(event.target.value)} />
          </Field>
          {when && when.getTime() <= Date.now() && <Alert>Choisissez une date future.</Alert>}
          {skipped && <Alert tone="amber">Message non envoyé : {skipped}</Alert>}
        </form>
      </Modal>
      <CustomerPickerDialog open={open && picking} onClose={() => setPicking(false)} onPick={picked => {
        setCustomer(picked);
        setPicking(false);
      }} />
    </>
  );
}
