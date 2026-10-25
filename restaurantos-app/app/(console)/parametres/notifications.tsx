'use client';
import {useEffect, useState} from 'react';
import {Bell, Building2, Check, Copy, FlaskConical, KeyRound, Lock, Mail, MessageCircle, MessageSquare, Send, Smartphone} from 'lucide-react';
import {
  assignNotificationLocation, channelHint, channelLabel, configureNotificationProvider, removeNotificationLocation, disableNotification, enableNotification, loadNotificationConfigurations, notificationField, notificationWebhookUrl, smtpPresets,
  loadNotificationProviders, modesOf, notificationTestSummary, setNotificationCredentials, setNotificationMode, testableChannels, testNotification,
  type Channel, type NotificationConfiguration, type NotificationMode, type NotificationProvider, type NotificationTest,
} from '@/lib/settings';
import {API_URL} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {useConsole} from '@/components/console';
import {PlanBadge, UpgradePanel, usePlanFeatures} from '@/components/plan';
import {Feature} from '@/lib/plan';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';
import {Switch, TestResult} from './shared';

const channelIcon: Record<Channel, typeof Mail> = {Email: Mail, Sms: MessageSquare, WhatsApp: MessageCircle, Push: Smartphone, InApp: Bell};

const modeLabel: Record<NotificationMode, string> = {PlatformManaged: 'Compte de la plateforme', TenantManaged: 'Votre propre compte'};

type Target = {provider: NotificationProvider; configuration: NotificationConfiguration};

function TestDialog({target, onClose}: {target: Target | null; onClose: () => void}) {
  const {email} = useConsole();
  const {busy, run} = useAction();
  const [address, setAddress] = useState('');
  const [result, setResult] = useState<{test: NotificationTest; address: string} | null>(null);
  const channel = target?.provider.channel ?? 'Email';
  useEffect(() => {
    setResult(null);
    setAddress(target?.provider.channel === 'Email' ? email : '');
  }, [target, email]);
  const summary = result && notificationTestSummary(result.test, channel, result.address);

  return (
    <Modal open={!!target} onClose={onClose} title={`Tester ${target?.provider.name ?? ''}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Fermer</Button>
        <Button type="submit" form="notification-test" busy={busy === 'test'} disabled={!address.trim()}>
          <Send className="size-4" aria-hidden="true" />{result ? 'Renvoyer' : 'Envoyer le test'}
        </Button>
      </>
    }>
      {target && (
        <form id="notification-test" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          const to = address.trim();
          setResult(null);
          void run('test', async () => setResult({test: await testNotification(target.configuration.id, to), address: to}));
        }}>
          <p className="text-sm text-ink-600">
            Un court message de test est envoyé avec le compte enregistré. Il n’apparaît pas dans l’historique et ne compte pas dans votre quota.
          </p>
          <Field label={channel === 'Email' ? 'Adresse email du destinataire' : 'Numéro du destinataire'}
            hint={channel === 'Email' ? undefined : 'Format international, par exemple +2290197000000.'}>
            <Input type={channel === 'Email' ? 'email' : 'tel'} value={address} autoComplete={channel === 'Email' ? 'email' : 'tel'}
              placeholder={channel === 'Email' ? 'vous@exemple.com' : '+229…'} onChange={event => setAddress(event.target.value)} />
          </Field>
          {channel === 'WhatsApp' && (
            <p className="text-xs text-ink-500">Le message part avec votre modèle approuvé par Meta ; le numéro doit avoir un compte WhatsApp.</p>
          )}
          {summary && <TestResult tone={summary.tone} title={summary.title} detail={summary.detail} />}
        </form>
      )}
    </Modal>
  );
}

function CredentialsDialog({target, onClose, onDone, onSaved}: {
  target: Target | null;
  onClose: () => void;
  onDone: () => Promise<void>;
  onSaved?: (target: Target) => void;
}) {
  const {busy, run} = useAction();
  const [values, setValues] = useState<Record<string, string>>({});
  const [copied, setCopied] = useState(false);
  const code = target?.provider.code ?? '';
  const fields = target?.provider.credentialFields ?? [];
  useEffect(() => {
    setCopied(false);
    setValues(Object.fromEntries(fields.flatMap(field => {
      const options = notificationField(code, field).options;
      return options ? [[field, options[0][0]]] : [];
    })));
  }, [target]); // fields et code découlent de target
  const complete = fields.length > 0 && fields.every(field => values[field]?.trim());
  const set = (patch: Record<string, string>) => setValues(current => ({...current, ...patch}));
  // Adresse de webhook à déclarer chez le prestataire : mot de passe Basic dans l'URL, ou signature (clé à part).
  const webhook = !target ? null
    : fields.includes('webhook_password') ? notificationWebhookUrl(API_URL, code, target.configuration.id, values.webhook_password ?? '')
    : fields.includes('webhook_secret') ? notificationWebhookUrl(API_URL, code, target.configuration.id)
    : null;

  return (
    <Modal open={!!target} onClose={onClose} title={`Compte ${target?.provider.name ?? ''}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="notification-credentials" busy={busy === 'save'} disabled={!complete}>Enregistrer et activer</Button>
      </>
    }>
      {target && (
        <form id="notification-credentials" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('save', async () => {
            await setNotificationCredentials(target.configuration.id, Object.fromEntries(fields.map(field => [field, values[field].trim()])));
            if (!target.configuration.isEnabled) await enableNotification(target.configuration.id);
            setValues({});
            await onDone();
            onClose();
            if (testableChannels.has(target.provider.channel)) onSaved?.(target);
          }, `${target.provider.name} activé.`);
        }}>
          <p className="flex items-start gap-2 rounded-xl bg-slate-50 p-3 text-sm text-ink-600">
            <Lock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            Les identifiants de votre compte sont chiffrés au coffre et ne sont jamais réaffichés. Pour les changer, saisissez-les à nouveau.
          </p>
          {target.configuration.hasCredentials && <Alert tone="blue">Des identifiants sont déjà enregistrés ; ceux-ci les remplaceront.</Alert>}
          {fields.length === 0 && <Alert tone="amber">Ce prestataire n’est pas encore branché : impossible d’utiliser votre propre compte pour l’instant.</Alert>}
          {code === 'SMTP' && (
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-ink-600">Pré-remplir pour</span>
              {smtpPresets.map(preset => (
                <Button key={preset.label} type="button" size="sm" variant="secondary" onClick={() => set(preset.values)}>{preset.label}</Button>
              ))}
            </div>
          )}
          {fields.map(field => {
            const meta = notificationField(code, field);
            return (
              <Field key={field} label={meta.label} hint={meta.hint}>
                {meta.options ? (
                  <Select value={values[field] ?? ''} onChange={event => set({[field]: event.target.value})}>
                    {meta.options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                  </Select>
                ) : (
                  <Input type={meta.secret ? 'password' : meta.type ?? 'text'} autoComplete="off" spellCheck={false} value={values[field] ?? ''}
                    onChange={event => set({[field]: event.target.value})} />
                )}
              </Field>
            );
          })}
          {webhook && (
            <div className="space-y-1.5 rounded-xl border border-slate-200 p-3">
              <p className="text-sm font-medium text-ink-800">Suivi des remises : adresse à déclarer chez {target.provider.name}</p>
              <div className="flex items-center gap-2">
                <code className="min-w-0 flex-1 truncate rounded-lg bg-slate-100 px-2.5 py-2 text-xs">{webhook}</code>
                <Button type="button" size="sm" variant="secondary" disabled={fields.includes('webhook_password') && !values.webhook_password}
                  onClick={() => void navigator.clipboard?.writeText(webhook).then(() => setCopied(true))}>
                  {copied ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}{copied ? 'Copiée' : 'Copier'}
                </Button>
              </div>
              <p className="text-xs text-ink-500">
                {code === 'MAILJET'
                  ? 'Mailjet › Paramètres du compte › Notifications d’événements (webhooks) : collez cette adresse pour les événements « Envoyé », « Rebond » et « Bloqué ».'
                  : 'Collez cette adresse dans les rapports de remise (webhook ou URL de rappel) de votre compte.'}{' '}
                L’adresse doit être joignable depuis Internet.
              </p>
            </div>
          )}
        </form>
      )}
    </Modal>
  );
}

/** Établissements où ce fournisseur envoie. Aucun coché : tous les établissements. */
function LocationsDialog({target, onClose, onDone}: {target: {provider: NotificationProvider; configuration: NotificationConfiguration} | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const [picked, setPicked] = useState<Map<string, number>>(new Map());
  useEffect(() => {
    if (target) setPicked(new Map(target.configuration.assignments.filter(item => item.isEnabled).map(item => [item.locationId, item.priority])));
  }, [target]);
  if (!target) return null;
  const toggle = (id: string) => setPicked(current => {
    const next = new Map(current);
    if (next.has(id)) next.delete(id);
    else next.set(id, 0);
    return next;
  });

  return (
    <Modal open onClose={onClose} title={`Établissements · ${target.provider.name}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'save'} onClick={() => run('save', async () => {
          const {configuration} = target;
          for (const [locationId, priority] of picked) {
            const existing = configuration.assignments.find(item => item.locationId === locationId);
            if (!existing || existing.priority !== priority || !existing.isEnabled) await assignNotificationLocation(configuration.id, locationId, priority);
          }
          for (const item of configuration.assignments) {
            if (!picked.has(item.locationId)) await removeNotificationLocation(configuration.id, item.locationId);
          }
          onClose();
          await onDone();
        }, 'Établissements enregistrés.')}>Enregistrer</Button>
      </>
    }>
      <div className="space-y-4">
        <p className="text-sm text-ink-600">Choisissez les établissements qui envoient par {target.provider.name}. <strong>Aucun coché : tous les établissements.</strong></p>
        <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
          {locations.map(location => (
            <li key={location.id} className="flex flex-wrap items-center gap-3 px-4 py-2.5">
              <label className="flex min-w-0 flex-1 items-center gap-2 text-sm">
                <input type="checkbox" className="size-4 accent-brand-600" checked={picked.has(location.id)} onChange={() => toggle(location.id)} />
                <span className="truncate">{location.name}</span>
              </label>
              {picked.has(location.id) && (
                <label className="flex items-center gap-2 text-xs text-ink-500">
                  Rang
                  <Input type="number" min={0} max={100} aria-label={`Rang pour ${location.name}`} className="h-8 w-16! py-0 text-sm" value={picked.get(location.id)}
                    onChange={event => setPicked(current => new Map(current).set(location.id, Math.max(0, Number(event.target.value) || 0)))} />
                </label>
              )}
            </li>
          ))}
        </ul>
        <p className="text-xs text-ink-500">Rang : quand plusieurs fournisseurs du même canal servent un établissement, le plus petit rang est essayé d’abord.</p>
      </div>
    </Modal>
  );
}

function ProviderRow({provider, configuration, editable, onChanged, onCredentials, onLocations, onTest}: {
  provider: NotificationProvider;
  configuration: NotificationConfiguration | undefined;
  editable: boolean;
  onChanged: () => Promise<void>;
  onCredentials: (configuration: NotificationConfiguration) => void;
  onLocations: (configuration: NotificationConfiguration) => void;
  onTest: (configuration: NotificationConfiguration) => void;
}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const modes = modesOf(provider.configurationType);
  const mode = configuration?.mode ?? modes[0];
  const enabled = !!configuration?.isEnabled;
  const awaiting = configuration?.status === 'AwaitingCredentials';
  // Sans le droit, votre propre compte n'est ni configurable ni utilisé à l'envoi.
  const byokLocked = usePlanFeatures().blocked(Feature.NotificationsOwnAccount);
  const ownLocked = byokLocked && mode === 'TenantManaged';

  const activate = (chosen: NotificationMode) => run('toggle', async () => {
    let current = configuration;
    if (!current) {
      await configureNotificationProvider(provider.id, chosen);
      current = (await loadNotificationConfigurations()).find(item => item.providerId === provider.id);
    } else if (current.mode !== chosen) {
      await setNotificationMode(current.id, chosen);
      current = {...current, mode: chosen, status: chosen === 'PlatformManaged' || current.hasCredentials ? 'Configured' : 'AwaitingCredentials'};
    }
    if (current?.status === 'AwaitingCredentials') {
      await onChanged();
      onCredentials(current);
      return;
    }
    if (current && !current.isEnabled) await enableNotification(current.id);
    await onChanged();
  });

  return (
    <li className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2 font-medium">
          {provider.name}
          {enabled && <Badge tone="green">Actif</Badge>}
          {awaiting && !enabled && <Badge tone="amber">Identifiants à saisir</Badge>}
          {!provider.hasIntegration && <Badge>Bientôt disponible</Badge>}
          {ownLocked && enabled && <Badge tone="amber">Non utilisé : hors plan</Badge>}
        </p>
        {modes.length > 1 ? (
          <div className="mt-2 inline-flex rounded-lg bg-slate-100 p-0.5 text-xs" role="radiogroup" aria-label={`Compte utilisé pour ${provider.name}`}>
            {modes.map(item => (
              <button key={item} type="button" role="radio" aria-checked={mode === item} disabled={!editable || !!busy || !provider.hasIntegration || (item === 'TenantManaged' && byokLocked)}
                onClick={() => {
                  if (item !== mode) void activate(item);
                }}
                className={cx('rounded-md px-2.5 py-1 font-medium disabled:cursor-default', mode === item ? 'bg-white shadow-sm' : 'text-ink-500')}>
                {modeLabel[item]}{item === 'TenantManaged' && <PlanBadge feature={Feature.NotificationsOwnAccount} className="ml-1" />}
              </button>
            ))}
          </div>
        ) : (
          <p className="flex flex-wrap items-center gap-1 text-sm text-ink-500">{modeLabel[modes[0]]}{modes[0] === 'PlatformManaged' ? ' : les envois comptent dans le quota de votre abonnement.' : ''}{modes[0] === 'TenantManaged' && <PlanBadge feature={Feature.NotificationsOwnAccount} />}</p>
        )}
      </div>
      <div className="flex shrink-0 items-center gap-2 self-end sm:self-auto">
        {configuration && locations.length > 1 && (
          <Button size="sm" variant="ghost" disabled={!editable} onClick={() => onLocations(configuration)}
            title={configuration.isGlobal ? 'Tous les établissements' : `${configuration.assignments.length} établissement(s)`}>
            <Building2 className="size-4" aria-hidden="true" />{configuration.isGlobal ? 'Tous' : configuration.assignments.filter(item => item.isEnabled).length}
          </Button>
        )}
        {configuration && configuration.status === 'Configured' && editable && provider.hasIntegration && testableChannels.has(provider.channel) && (
          <Button size="sm" variant="ghost" onClick={() => onTest(configuration)} title={`Envoyer un message de test avec ${provider.name}`}>
            <FlaskConical className="size-4" aria-hidden="true" />Tester
          </Button>
        )}
        {configuration && mode === 'TenantManaged' && editable && !byokLocked && (
          <Button size="sm" variant="secondary" onClick={() => onCredentials(configuration)}><KeyRound className="size-4" aria-hidden="true" />Identifiants</Button>
        )}
        <Switch on={enabled} label={`${enabled ? 'Désactiver' : 'Activer'} : ${provider.name}`} disabled={!editable || !!busy || (!provider.hasIntegration && !enabled) || (ownLocked && !enabled)}
          onChange={() => {
            if (enabled && configuration) {
              void run('toggle', async () => {
                await disableNotification(configuration.id);
                await onChanged();
              }, `${provider.name} désactivé.`);
            } else {
              void activate(mode);
            }
          }} />
      </div>
    </li>
  );
}

export function NotificationSettings() {
  const {can} = useConsole();
  const editable = can('notifications.configure');
  const allowed = can('notifications.view') || editable;
  const byokLocked = usePlanFeatures().blocked(Feature.NotificationsOwnAccount);
  const [credentials, setCredentials] = useState<Target | null>(null);
  const [placing, setPlacing] = useState<Target | null>(null);
  const [testing, setTesting] = useState<Target | null>(null);
  const data = useResource(async signal => {
    if (!allowed) return null;
    const [providers, configurations] = await Promise.all([loadNotificationProviders(signal), loadNotificationConfigurations(signal)]);
    return {providers, configurations};
  }, [allowed]);

  if (!allowed) return <Empty icon={<Bell className="size-8" />} title="Notifications non disponibles">Réservé aux membres qui gèrent les notifications.</Empty>;
  if (data.error) return <Alert>{data.error}</Alert>;
  if (!data.data) return <Spinner label="Chargement des notifications…" />;
  const {providers, configurations} = data.data;
  const channels = (['Email', 'Sms', 'WhatsApp', 'Push'] as Channel[])
    .map(channel => ({channel, list: providers.filter(provider => provider.channel === channel && provider.configurationType !== 'Internal')}))
    .filter(group => group.list.length > 0);

  return (
    <div className="space-y-4">
      {byokLocked && <UpgradePanel feature={Feature.NotificationsOwnAccount} title="Envoi par votre propre compte">Envoyez avec vos propres identifiants (SMTP, Twilio…), sans quota.</UpgradePanel>}
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres qui configurent les notifications peuvent modifier ces réglages.</Alert>}
      {channels.length === 0 && <Empty icon={<Bell className="size-8" />} title="Aucun canal proposé">La plateforme n’a encore ouvert aucun prestataire d’envoi.</Empty>}
      {channels.map(({channel, list}) => {
        const Icon = channelIcon[channel];
        const active = configurations.filter(configuration => configuration.channel === channel && configuration.isEnabled).length;
        return (
          <Card key={channel} className="p-5">
            <div className="flex items-start gap-3">
              <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true"><Icon className="size-5" /></span>
              <div className="min-w-0 flex-1">
                <h2 className="flex flex-wrap items-center gap-2 font-semibold">{channelLabel[channel]}{active > 0 && <Badge tone="green">{active} actif{active > 1 ? 's' : ''}</Badge>}</h2>
                <p className="text-sm text-ink-500">{channelHint[channel]}</p>
              </div>
            </div>
            <ul className="mt-2 divide-y divide-slate-100">
              {list.map(provider => (
                <ProviderRow key={provider.id} provider={provider} configuration={configurations.find(configuration => configuration.providerId === provider.id)}
                  editable={editable} onChanged={data.reload} onCredentials={configuration => setCredentials({provider, configuration})} onLocations={configuration => setPlacing({provider, configuration})}
                  onTest={configuration => setTesting({provider, configuration})} />
              ))}
            </ul>
          </Card>
        );
      })}
      <p className="text-xs text-ink-500">Les alertes de l’équipe dans la console sont toujours actives.</p>
      <CredentialsDialog target={credentials} onClose={() => setCredentials(null)} onDone={data.reload} onSaved={setTesting} />
      <TestDialog target={testing} onClose={() => setTesting(null)} />
      <LocationsDialog target={placing} onClose={() => setPlacing(null)} onDone={data.reload} />
    </div>
  );
}
