'use client';
import {useEffect, useState} from 'react';
import {Pause, Play, Plus, RefreshCw, RotateCcw, Send, Webhook} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  createEndpoint, deliveryLabel, deliveryTone, eventLabel, listDeliveries, listEndpoints, loadDelivery, retryDelivery, rotateSecret,
  setEndpointEnabled, subscribeEvent, testEndpoint, unsubscribeEvent, type Catalog, type DeliveryStatus, type WebhookEndpoint,
} from '@/lib/integrations';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction, useToast} from '@/components/ui';
import {SecretOnce, day, stamp} from './shared';

const events = (catalog: Catalog) => catalog.eventTypes.filter(type => type !== 'integrations.ping');

function CreateDialog({open, catalog, onClose, onCreated}: {open: boolean; catalog: Catalog; onClose: () => void; onCreated: (secret: string, id: string) => Promise<void>}) {
  const {busy, run} = useAction();
  const [url, setUrl] = useState('');
  const [chosen, setChosen] = useState<string[]>([]);
  useEffect(() => {
    if (!open) return;
    setUrl('');
    setChosen([]);
  }, [open]);
  const toggle = (type: string) => setChosen(current => (current.includes(type) ? current.filter(item => item !== type) : [...current, type]));
  return (
    <Modal open={open} onClose={onClose} title="Nouveau webhook" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="webhook-form" busy={busy === 'create'} disabled={!url.trim().startsWith('https://') || chosen.length === 0}>Créer</Button>
      </>
    }>
      <form id="webhook-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('create', async () => {
          const issued = await createEndpoint(url.trim(), chosen);
          onClose();
          await onCreated(issued.secret, issued.endpointId);
        });
      }}>
        <Field label="Adresse de réception" hint="HTTPS obligatoire, sur un serveur public.">
          <Input type="url" required maxLength={2048} value={url} onChange={event => setUrl(event.target.value)} placeholder="https://exemple.com/webhooks/restauspace" />
        </Field>
        <fieldset>
          <legend className="mb-1.5 text-sm font-semibold">Événements envoyés</legend>
          <div className="grid grid-cols-1 gap-1 sm:grid-cols-2">
            {events(catalog).map(type => (
              <label key={type} className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="size-4 accent-brand-600" checked={chosen.includes(type)} onChange={() => toggle(type)} />{eventLabel[type] ?? type}
              </label>
            ))}
          </div>
        </fieldset>
      </form>
    </Modal>
  );
}

function DeliveryDialog({id, onClose, onRetried}: {id: string | null; onClose: () => void; onRetried: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data, error} = useResource(signal => (id ? loadDelivery(id, signal) : Promise.resolve(null)), [id]);
  let body = data?.body ?? '';
  try {
    body = JSON.stringify(JSON.parse(body), null, 2);
  } catch {
    // Corps non JSON : affiché tel quel.
  }
  return (
    <Modal wide open={id !== null} onClose={onClose} title="Livraison" footer={data?.delivery.status === 'DeadLetter' && can('integrations.manage') ? (
      <Button busy={busy === 'retry'} onClick={() => run('retry', async () => {
        await retryDelivery(data.delivery.id);
        onClose();
        await onRetried();
      }, 'Nouvelle livraison programmée.')}><RotateCcw className="size-4" aria-hidden="true" />Renvoyer</Button>
    ) : undefined}>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : (
        <div className="space-y-3 text-sm">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={deliveryTone[data.delivery.status]}>{deliveryLabel[data.delivery.status]}</Badge>
            <span className="font-medium">{eventLabel[data.delivery.eventType] ?? data.delivery.eventType}</span>
            <span className="text-ink-500">{data.delivery.attemptCount} / {data.delivery.maxAttempts} essais</span>
          </div>
          {data.delivery.lastFailureReason && <Alert tone="amber">Dernier échec : {data.delivery.lastFailureReason}</Alert>}
          {data.delivery.nextAttemptAtUtc && data.delivery.status !== 'Succeeded' && <p className="text-ink-500">Prochain essai : {stamp(data.delivery.nextAttemptAtUtc)}</p>}
          <pre className="max-h-80 overflow-auto rounded-xl bg-night-900 p-3 text-xs text-slate-100">{body}</pre>
        </div>
      )}
    </Modal>
  );
}

function EndpointDialog({endpoint, catalog, onClose, onChanged, onSecret}: {
  endpoint: WebhookEndpoint | null; catalog: Catalog; onClose: () => void; onChanged: () => Promise<void>; onSecret: (secret: string) => void;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const toast = useToast();
  const [status, setStatus] = useState<DeliveryStatus | ''>('');
  const [page, setPage] = useState(1);
  const [delivery, setDelivery] = useState<string | null>(null);
  const [rotating, setRotating] = useState(false);
  const [immediate, setImmediate] = useState(false);
  useEffect(() => {
    setStatus('');
    setPage(1);
    setRotating(false);
  }, [endpoint?.id]);
  const deliveries = useResource(signal => (endpoint ? listDeliveries(endpoint.id, status, page, signal) : Promise.resolve(null)), [endpoint?.id, status, page]);
  if (!endpoint) return null;
  const manage = can('integrations.manage');
  const active = endpoint.status === 'Active';

  return (
    <Modal wide open onClose={onClose} title="Webhook" footer={manage ? (
      <>
        <Button variant="ghost" className="mr-auto" onClick={() => setRotating(true)}><RefreshCw className="size-4" aria-hidden="true" />Nouveau secret</Button>
        <Button variant="secondary" busy={busy === 'test'} onClick={() => run('test', async () => {
          const result = await testEndpoint(endpoint.id);
          toast(result.succeeded ? 'success' : 'error', result.succeeded ? 'Test reçu par votre serveur.' : `Test refusé : ${result.failureReason ?? 'erreur inconnue'}`);
        })}><Send className="size-4" aria-hidden="true" />Tester</Button>
        <Button variant={active ? 'secondary' : 'primary'} busy={busy === 'toggle'} onClick={() => run('toggle', async () => {
          await setEndpointEnabled(endpoint.id, !active);
          await onChanged();
        }, active ? 'Webhook désactivé.' : 'Webhook activé.')}>{active ? <><Pause className="size-4" aria-hidden="true" />Désactiver</> : <><Play className="size-4" aria-hidden="true" />Activer</>}</Button>
      </>
    ) : undefined}>
      <div className="space-y-5">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <code className="min-w-0 text-sm break-all">{endpoint.url}</code>
            <Badge tone={active ? 'green' : 'neutral'}>{active ? 'Actif' : 'Désactivé'}</Badge>
          </div>
          <p className="mt-1 text-xs text-ink-500">
            Créé le {day(endpoint.createdAtUtc)}
            {endpoint.secretRotatedAtUtc && ` · secret renouvelé le ${day(endpoint.secretRotatedAtUtc)}`}
            {endpoint.previousSecretValidUntilUtc && new Date(endpoint.previousSecretValidUntilUtc) > new Date() && ` · ancien secret valable jusqu’au ${stamp(endpoint.previousSecretValidUntilUtc)}`}
          </p>
        </div>
        {endpoint.autoDisabledAtUtc && !active && (
          <Alert tone="amber">Désactivé automatiquement le {day(endpoint.autoDisabledAtUtc)} après {endpoint.consecutiveDeadLetters} livraisons abandonnées. Corrigez votre serveur, testez, puis réactivez.</Alert>
        )}
        {rotating && (
          <Card className="space-y-3 p-4">
            <p className="text-sm text-ink-700">Un nouveau secret de signature est créé et affiché une seule fois.</p>
            <label className="flex items-start gap-2 text-sm">
              <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={immediate} onChange={event => setImmediate(event.target.checked)} />
              <span>Invalider l’ancien secret immédiatement<span className="block text-xs text-ink-500">Sinon, il reste accepté quelque temps pour vous laisser déployer le nouveau.</span></span>
            </label>
            <div className="flex gap-2">
              <Button busy={busy === 'rotate'} onClick={() => run('rotate', async () => {
                const issued = await rotateSecret(endpoint.id, immediate);
                setRotating(false);
                onSecret(issued.secret);
                await onChanged();
              })}>Générer</Button>
              <Button variant="ghost" onClick={() => setRotating(false)}>Annuler</Button>
            </div>
          </Card>
        )}

        <section>
          <h3 className="mb-2 font-semibold">Événements</h3>
          <div className="grid grid-cols-1 gap-1 sm:grid-cols-2">
            {events(catalog).map(type => {
              const on = endpoint.events.includes(type);
              return (
                <label key={type} className="flex items-center gap-2 text-sm">
                  <input type="checkbox" className="size-4 accent-brand-600" checked={on} disabled={!manage || busy === type || (on && endpoint.events.length === 1 && active)}
                    onChange={() => run(type, async () => {
                      if (on) await unsubscribeEvent(endpoint.id, type);
                      else await subscribeEvent(endpoint.id, type);
                      await onChanged();
                    })} />
                  {eventLabel[type] ?? type}
                </label>
              );
            })}
          </div>
        </section>

        <section>
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <h3 className="flex-1 font-semibold">Livraisons</h3>
            <Select aria-label="Statut des livraisons" className="w-full! sm:w-auto!" value={status} onChange={event => {
              setStatus(event.target.value as DeliveryStatus | '');
              setPage(1);
            }}>
              <option value="">Toutes</option>
              {(Object.keys(deliveryLabel) as DeliveryStatus[]).map(item => <option key={item} value={item}>{deliveryLabel[item]}</option>)}
            </Select>
          </div>
          {deliveries.error ? <Alert>{deliveries.error}</Alert> : !deliveries.data ? <Spinner /> : deliveries.data.items.length === 0 ? (
            <p className="text-sm text-ink-500">Aucune livraison.</p>
          ) : (
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {deliveries.data.items.map(item => (
                <li key={item.id}>
                  <button type="button" onClick={() => setDelivery(item.id)} className="flex w-full flex-wrap items-center gap-2 px-3 py-2 text-left text-sm hover:bg-slate-50">
                    <span className="min-w-0 flex-1 truncate font-medium">{eventLabel[item.eventType] ?? item.eventType}</span>
                    <span className="text-xs text-ink-500">{stamp(item.createdAtUtc)}</span>
                    <Badge tone={deliveryTone[item.status]}>{deliveryLabel[item.status]}</Badge>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {deliveries.data && (page > 1 || deliveries.data.hasMore) && (
            <div className="mt-2 flex justify-end gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récentes</Button>
              <Button size="sm" variant="secondary" disabled={!deliveries.data.hasMore} onClick={() => setPage(page + 1)}>Plus anciennes</Button>
            </div>
          )}
        </section>
      </div>
      <DeliveryDialog id={delivery} onClose={() => setDelivery(null)} onRetried={deliveries.reload} />
    </Modal>
  );
}

export function Webhooks({catalog}: {catalog: Catalog}) {
  const {can} = useConsole();
  const {data, error, reload} = useResource(signal => listEndpoints(signal), []);
  const [creating, setCreating] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const opened = data?.find(item => item.id === openId) ?? null;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <p className="flex-1 text-sm text-ink-600">Un webhook prévient votre serveur à chaque événement (commande passée, paiement, réservation…). Chaque envoi est signé avec un secret propre au webhook.</p>
        {can('integrations.manage') && <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouveau webhook</Button>}
      </div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.length === 0 ? (
        <Card className="p-6"><Empty icon={<Webhook className="size-8" />} title="Aucun webhook">Ajoutez l’adresse de votre serveur pour recevoir les événements en temps réel.</Empty></Card>
      ) : (
        <div className="space-y-3">
          {data.map(endpoint => (
            <button key={endpoint.id} type="button" className="block w-full text-left" onClick={() => setOpenId(endpoint.id)}>
              <Card className={cx('p-4 transition hover:border-brand-300', endpoint.status !== 'Active' && 'opacity-70')}>
                <div className="flex flex-wrap items-center gap-2">
                  <code className="min-w-0 flex-1 truncate text-sm">{endpoint.url}</code>
                  {endpoint.consecutiveDeadLetters > 0 && endpoint.status === 'Active' && <Badge tone="amber">{endpoint.consecutiveDeadLetters} échec{endpoint.consecutiveDeadLetters > 1 ? 's' : ''}</Badge>}
                  <Badge tone={endpoint.status === 'Active' ? 'green' : endpoint.autoDisabledAtUtc ? 'red' : 'neutral'}>{endpoint.status === 'Active' ? 'Actif' : endpoint.autoDisabledAtUtc ? 'Désactivé (échecs)' : 'Désactivé'}</Badge>
                </div>
                <p className="mt-1 text-xs text-ink-500">{endpoint.events.map(type => eventLabel[type] ?? type).join(' · ')}</p>
              </Card>
            </button>
          ))}
        </div>
      )}
      <CreateDialog open={creating} catalog={catalog} onClose={() => setCreating(false)} onCreated={async (value, id) => {
        setSecret(value);
        await reload();
        setOpenId(id);
      }} />
      <EndpointDialog endpoint={secret ? null : opened} catalog={catalog} onClose={() => setOpenId(null)} onChanged={reload} onSecret={setSecret} />
      <SecretOnce title="Secret de signature" label="Secret" value={secret} onClose={() => setSecret(null)}>
        <p className="text-sm text-ink-600">Votre serveur l’utilise pour vérifier la signature de chaque envoi (HMAC SHA-256) et rejeter les faux appels.</p>
      </SecretOnce>
    </div>
  );
}
