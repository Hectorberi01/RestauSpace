'use client';
import {useEffect, useState} from 'react';
import {Bell, ChevronDown, Mail, MessageCircle, MessageSquare, ShieldCheck, ShieldOff} from 'lucide-react';
import {
  channels, consentSources, grantConsent, stamp, sourceLabel, withdrawConsent, withdrawalLabel, withdrawalReasons,
  type Channel, type Consent, type Customer, type WithdrawalReason,
} from '@/lib/customers';
import {Badge, Button, Field, Modal, Select, cx, useAction} from '@/components/ui';

const icons: Record<Channel, typeof Mail> = {Email: Mail, Sms: MessageSquare, WhatsApp: MessageCircle, Push: Bell};

function reachable(customer: Customer, channel: Channel) {
  if (channel === 'Email') return Boolean(customer.email);
  if (channel === 'Sms' || channel === 'WhatsApp') return Boolean(customer.phone);
  return true;
}

function ConsentDialog({customer, target, onClose, onDone}: {
  customer: Customer;
  target: {channel: Channel; grant: boolean} | null;
  onClose: () => void;
  onDone: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [source, setSource] = useState('back-office');
  const [reason, setReason] = useState<WithdrawalReason>('CustomerRequest');
  useEffect(() => {
    if (!target) return;
    setSource('back-office');
    setReason('CustomerRequest');
  }, [target]);
  const label = target ? channels.find(item => item.value === target.channel)?.label ?? target.channel : '';

  return (
    <Modal open={target !== null} onClose={onClose} title={target?.grant ? `Accord marketing · ${label}` : `Retrait de l’accord · ${label}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button variant={target?.grant ? 'primary' : 'danger'} busy={busy === 'consent'} onClick={() => target && run('consent', async () => {
          if (target.grant) await grantConsent(customer.id, target.channel, source);
          else await withdrawConsent(customer.id, target.channel, reason, source);
          await onDone();
          onClose();
        }, target.grant ? 'Consentement enregistré.' : 'Consentement retiré.')}>{target?.grant ? 'Enregistrer l’accord' : 'Retirer l’accord'}</Button>
      </>
    }>
      <div className="space-y-4">
        <p className="text-sm text-ink-600">
          {target?.grant
            ? `N’enregistrez l’accord que si ${customer.name} l’a donné explicitement. La date et l’origine sont conservées comme preuve.`
            : 'Le client ne recevra plus de message marketing sur ce canal. L’historique reste conservé comme preuve.'}
        </p>
        <Field label="Où l’avez-vous recueilli ?">
          <Select value={source} onChange={event => setSource(event.target.value)}>
            {consentSources.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
          </Select>
        </Field>
        {target && !target.grant && (
          <Field label="Motif">
            <Select value={reason} onChange={event => setReason(event.target.value as WithdrawalReason)}>
              {withdrawalReasons.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
            </Select>
          </Field>
        )}
      </div>
    </Modal>
  );
}

export function Consents({customer, consents, editable, onChanged}: {customer: Customer; consents: Consent[]; editable: boolean; onChanged: () => Promise<void>}) {
  const [target, setTarget] = useState<{channel: Channel; grant: boolean} | null>(null);
  const [open, setOpen] = useState<Channel | null>(null);

  return (
    <div>
      <ul className="divide-y divide-slate-100">
        {channels.map(({value, label}) => {
          const consent = consents.find(item => item.channel === value);
          const granted = consent?.isGranted ?? false;
          const wanted = customer.preferences?.marketingChannels.includes(value) ?? false;
          const Icon = icons[value];
          return (
            <li key={value} className="py-3">
              <div className="flex flex-wrap items-center gap-3">
                <span className={cx('grid size-9 shrink-0 place-items-center rounded-xl', granted ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-ink-500')} aria-hidden="true">
                  <Icon className="size-4" />
                </span>
                <div className="min-w-0 flex-1 text-sm">
                  <p className="flex flex-wrap items-center gap-2 font-medium">
                    {label}
                    {granted ? <Badge tone="green">Accord donné</Badge> : consent ? <Badge tone="red">Retiré</Badge> : <Badge>Aucun accord</Badge>}
                    {wanted && !granted && <Badge tone="amber">Souhaité</Badge>}
                  </p>
                  <p className="text-xs text-ink-500">
                    {granted && consent && `Depuis le ${stamp(consent.grantedAtUtc)} · ${sourceLabel(consent.source)}`}
                    {!granted && consent?.withdrawnAtUtc && `Le ${stamp(consent.withdrawnAtUtc)} · ${withdrawalLabel(consent.withdrawalReason)}`}
                    {!consent && !reachable(customer, value) && (value === 'Email' ? 'Pas d’email sur la fiche.' : 'Pas de téléphone sur la fiche.')}
                  </p>
                </div>
                <div className="flex gap-1">
                  {consent && consent.history.length > 0 && (
                    <Button size="sm" variant="ghost" aria-expanded={open === value} onClick={() => setOpen(open === value ? null : value)}>
                      Historique<ChevronDown className={cx('size-4 transition', open === value && 'rotate-180')} aria-hidden="true" />
                    </Button>
                  )}
                  {editable && (granted
                    ? <Button size="sm" variant="secondary" onClick={() => setTarget({channel: value, grant: false})}><ShieldOff className="size-4" aria-hidden="true" />Retirer</Button>
                    : <Button size="sm" variant="secondary" onClick={() => setTarget({channel: value, grant: true})}><ShieldCheck className="size-4" aria-hidden="true" />Accord</Button>)}
                </div>
              </div>
              {open === value && consent && (
                <ol className="mt-3 ml-12 space-y-1.5 border-l border-slate-200 pl-4 text-xs text-ink-600">
                  {[...consent.history].reverse().map((change, index) => (
                    <li key={index}>
                      <span className={change.status === 'Granted' ? 'font-medium text-emerald-700' : 'font-medium text-red-700'}>{change.status === 'Granted' ? 'Accordé' : 'Retiré'}</span>
                      {' '}le {stamp(change.occurredAtUtc)} · {sourceLabel(change.source)}{change.reason && withdrawalLabel(change.reason) !== sourceLabel(change.source) ? ` · ${withdrawalLabel(change.reason)}` : ''}
                    </li>
                  ))}
                </ol>
              )}
            </li>
          );
        })}
      </ul>
      <p className="mt-2 text-xs text-ink-500">Seul un accord donné autorise un envoi marketing. Les messages liés aux commandes et réservations n’en ont pas besoin.</p>
      <ConsentDialog customer={customer} target={target} onClose={() => setTarget(null)} onDone={onChanged} />
    </div>
  );
}
