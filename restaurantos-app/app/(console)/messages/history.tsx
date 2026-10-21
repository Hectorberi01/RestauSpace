'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {Inbox, Send} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  PAGE, cancelMessage, channelLabel, customerChannels, listMessages, loadMessage, loadMessageTypes, statusLabel, statusTone, typeLabel,
  type HistoryQuery, type MessageChannel, type MessageStatus, type SentMessage,
} from '@/lib/messages';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, useAction} from '@/components/ui';
import {SendDialog} from './send';

const stamp = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));
const cancellable = (message: SentMessage) => message.status === 'Pending' || message.status === 'Scheduled';

const deliveryLabel: Record<SentMessage['deliveries'][number]['status'], string> = {
  InProgress: 'En cours', Sent: 'Remis au fournisseur', PartiallyDelivered: 'Remis en partie', Delivered: 'Reçu', Failed: 'Échec',
};

function Detail({id, onClose, onChanged}: {id: string; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => loadMessage(id, signal), [id]);
  return (
    <Modal open onClose={onClose} title="Détail du message" footer={data && cancellable(data) && can('notifications.send') ? (
      <Button variant="danger" busy={busy === 'cancel'} onClick={() => run('cancel', async () => {
        await cancelMessage(id);
        await reload();
        await onChanged();
      }, 'Envoi annulé.')}>Annuler l’envoi</Button>
    ) : undefined}>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : (
        <div className="space-y-4 text-sm">
          <dl className="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1.5">
            <dt className="text-ink-500">Message</dt><dd className="font-medium">{typeLabel(data.type)}{data.purpose === 'Marketing' && <Badge className="ml-2">Marketing</Badge>}</dd>
            <dt className="text-ink-500">Canal</dt><dd>{channelLabel[data.channel]} · {data.provider}</dd>
            <dt className="text-ink-500">Destinataire</dt>
            <dd>{data.isRedacted ? 'Effacé (données personnelles supprimées)' : data.recipient}{data.customerId && <> · <Link className="font-semibold text-brand-700" href={`/clients/${data.customerId}`}>fiche client</Link></>}</dd>
            <dt className="text-ink-500">Statut</dt><dd><Badge tone={statusTone[data.status]}>{statusLabel[data.status]}</Badge>{data.isDeliveryAbandoned && <span className="ml-2 text-xs text-red-700">plus de nouvelle tentative</span>}</dd>
            <dt className="text-ink-500">Créé</dt><dd>{stamp(data.createdAtUtc)}</dd>
            {data.scheduledAtUtc && <><dt className="text-ink-500">Programmé</dt><dd>{stamp(data.scheduledAtUtc)}</dd></>}
            {data.sentAtUtc && <><dt className="text-ink-500">Envoyé</dt><dd>{stamp(data.sentAtUtc)}</dd></>}
            {data.deliveredAtUtc && <><dt className="text-ink-500">Reçu</dt><dd>{stamp(data.deliveredAtUtc)}</dd></>}
            {data.cancelledAtUtc && <><dt className="text-ink-500">Annulé</dt><dd>{stamp(data.cancelledAtUtc)}</dd></>}
          </dl>
          <div>
            <h3 className="mb-2 font-semibold">Tentatives</h3>
            {data.deliveries.length === 0 ? <p className="text-ink-500">Aucune tentative pour l’instant.</p> : (
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
                {data.deliveries.map(delivery => (
                  <li key={delivery.id} className="px-3 py-2">
                    <p className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">n°{delivery.attemptNumber}</span>
                      <span>{deliveryLabel[delivery.status]}</span>
                      <span className="text-xs text-ink-500">{stamp(delivery.startedAtUtc)}</span>
                    </p>
                    {delivery.failureCode && (
                      <p className="text-xs text-red-700">
                        {delivery.failureKind === 'Permanent' ? 'Échec définitif' : 'Échec temporaire'} · <code>{delivery.failureCode}</code>
                        {delivery.nextRetryAtUtc && ` · nouvel essai ${stamp(delivery.nextRetryAtUtc)}`}
                      </p>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}

export function History({customerId}: {customerId?: string}) {
  const {can} = useConsole();
  const [query, setQuery] = useState<HistoryQuery>({customerId});
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  useEffect(() => setPage(1), [query]);
  const {data, error, reload} = useResource(signal => listMessages(query, page, signal), [query, page], 30_000);
  const types = useResource(signal => loadMessageTypes(signal), []);
  const items = (data ?? []).slice(0, PAGE);
  const more = (data?.length ?? 0) > PAGE;
  const set = (patch: Partial<HistoryQuery>) => setQuery(current => ({...current, ...patch}));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-2">
        <Field label="Statut">
          <Select className="w-full! sm:w-auto!" value={query.status ?? ''} onChange={event => set({status: event.target.value as MessageStatus | ''})}>
            <option value="">Tous</option>
            {(Object.keys(statusLabel) as MessageStatus[]).map(status => <option key={status} value={status}>{statusLabel[status]}</option>)}
          </Select>
        </Field>
        <Field label="Canal">
          <Select className="w-full! sm:w-auto!" value={query.channel ?? ''} onChange={event => set({channel: event.target.value as MessageChannel | ''})}>
            <option value="">Tous</option>
            {customerChannels.map(channel => <option key={channel} value={channel}>{channelLabel[channel]}</option>)}
          </Select>
        </Field>
        <Field label="Message">
          <Select className="w-full! sm:w-auto!" value={query.type ?? ''} onChange={event => set({type: event.target.value})}>
            <option value="">Tous</option>
            {(types.data ?? []).map(type => <option key={type.key} value={type.key}>{type.label}</option>)}
          </Select>
        </Field>
        <Field label="Du"><Input type="date" value={query.from?.slice(0, 10) ?? ''} onChange={event => set({from: event.target.value ? new Date(`${event.target.value}T00:00:00`).toISOString() : ''})} /></Field>
        <Field label="Au"><Input type="date" value={query.to ? new Date(new Date(query.to).getTime() - 1).toISOString().slice(0, 10) : ''} onChange={event => {
          const value = event.target.value;
          set({to: value ? new Date(new Date(`${value}T00:00:00`).getTime() + 86_400_000).toISOString() : ''});
        }} /></Field>
        {customerId && <Badge tone="blue">Un seul client</Badge>}
        {can('notifications.send') && <Button className="ml-auto" onClick={() => setSending(true)}><Send className="size-4" aria-hidden="true" />Envoyer un message</Button>}
      </div>

      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : items.length === 0 ? (
        <Card className="p-6"><Empty icon={<Inbox className="size-8" />} title="Aucun message">Les confirmations, rappels et campagnes envoyés à vos clients apparaîtront ici.</Empty></Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {items.map(message => (
              <li key={message.id}>
                <button type="button" onClick={() => setOpen(message.id)} className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3 text-left hover:bg-slate-50">
                  <span className="hidden w-28 shrink-0 text-xs text-ink-500 sm:block">{stamp(message.scheduledAtUtc && message.status === 'Scheduled' ? message.scheduledAtUtc : message.createdAtUtc)}</span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium">{typeLabel(message.type)}</span>
                    <span className="block truncate text-xs text-ink-500">
                      <span className="sm:hidden">{stamp(message.scheduledAtUtc && message.status === 'Scheduled' ? message.scheduledAtUtc : message.createdAtUtc)} · </span>
                      {channelLabel[message.channel]} · {message.isRedacted ? 'destinataire effacé' : message.recipient}
                    </span>
                  </span>
                  {message.lastFailureCode && message.status === 'Failed' && <code className="hidden text-xs text-red-700 sm:inline">{message.lastFailureCode}</code>}
                  <Badge tone={statusTone[message.status]}>{statusLabel[message.status]}</Badge>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      )}
      {(page > 1 || more) && (
        <div className="flex justify-end gap-2">
          <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récents</Button>
          <Button size="sm" variant="secondary" disabled={!more} onClick={() => setPage(page + 1)}>Plus anciens</Button>
        </div>
      )}
      {open && <Detail id={open} onClose={() => setOpen(null)} onChanged={reload} />}
      <SendDialog open={sending} onClose={() => setSending(false)} onSent={reload} />
    </div>
  );
}
