'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {Banknote, Check, Copy, Bike, CreditCard, Gift, Globe, KeyRound, Landmark, Lock, Settings2, Smartphone, Ticket, Wallet} from 'lucide-react';
import {
  createConfiguration, credentialField, disableConfiguration, enableConfiguration, loadConfigurations, loadProviders, methodLabel, providerGuide, setCredentials, setMethod,
  type Configuration, type PaymentMethod, type Provider,
} from '@/lib/payments';
import {API_URL} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';
import {Switch} from './shared';

export const methodIcon: Record<PaymentMethod, typeof Banknote> = {
  Cash: Banknote,
  CardOnSite: CreditCard,
  MobileMoneyOnSite: Smartphone,
  MealVoucher: Ticket,
  GiftCard: Gift,
  DeliveryPlatform: Bike,
  CashOnDelivery: Bike,
  BankTransfer: Landmark,
  Card: CreditCard,
  ApplePay: Wallet,
  GooglePay: Wallet,
  PayPal: Globe,
  MobileMoney: Smartphone,
};

export function WebhookAddress({path}: {path: string}) {
  const [copied, setCopied] = useState(false);
  const url = `${API_URL}/v1${path}`;
  return (
    <div className="space-y-1.5">
      <p className="text-sm font-medium text-ink-800">Adresse du webhook à déclarer chez le prestataire</p>
      <div className="flex items-center gap-2">
        <code className="min-w-0 flex-1 truncate rounded-lg bg-slate-100 px-2.5 py-2 text-xs">{url}</code>
        <Button size="sm" variant="secondary" onClick={() => void navigator.clipboard?.writeText(url).then(() => setCopied(true))}>
          {copied ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}{copied ? 'Copiée' : 'Copier'}
        </Button>
      </div>
    </div>
  );
}

export function CredentialsDialog({target, onClose, onDone}: {target: {provider: Provider; configuration: Configuration} | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [values, setValues] = useState<Record<string, string>>({});
  const code = target?.provider.code ?? '';
  const fields = target ? (target.configuration.requiredCredentialFields.length ? target.configuration.requiredCredentialFields : target.provider.credentialFields) : [];
  useEffect(() => {
    if (!target) return;
    setValues(Object.fromEntries(fields.flatMap(field => {
      const options = credentialField(code, field).options;
      return options ? [[field, options[0][0]]] : [];
    })));
  }, [target]); // fields et code découlent de target
  const complete = fields.every(field => values[field]?.trim());
  const guide = providerGuide[code];

  return (
    <Modal open={!!target} onClose={onClose} title={`Identifiants ${target?.provider.displayName ?? ''}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="credentials-form" busy={busy === 'credentials'} disabled={!complete}>Enregistrer et activer</Button>
      </>
    }>
      {target && (
        <form id="credentials-form" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('credentials', async () => {
            await setCredentials(target.configuration.id, Object.fromEntries(fields.map(field => [field, values[field].trim()])));
            if (!target.configuration.isEnabled) await enableConfiguration(target.configuration.id);
            setValues({});
            await onDone();
            onClose();
          }, `${target.provider.displayName} activé.`);
        }}>
          <p className="flex items-start gap-2 rounded-xl bg-slate-50 p-3 text-sm text-ink-600">
            <Lock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            Les identifiants de votre compte marchand sont chiffrés au coffre. Ils ne sont jamais réaffichés : pour les changer, saisissez-les à nouveau.
          </p>
          {guide && <p className="text-sm text-ink-600">{guide.where}</p>}
          {target.configuration.hasCredentials && <Alert tone="blue">Des identifiants sont déjà enregistrés ; ceux-ci les remplaceront.</Alert>}
          {fields.map(field => {
            const meta = credentialField(code, field);
            return (
              <Field key={field} label={meta.label} hint={meta.hint}>
                {meta.options ? (
                  <Select value={values[field] ?? ''} onChange={event => setValues(current => ({...current, [field]: event.target.value}))}>
                    {meta.options.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                  </Select>
                ) : (
                  <Input type={meta.secret ? 'password' : 'text'} autoComplete="off" spellCheck={false} value={values[field] ?? ''}
                    onChange={event => setValues(current => ({...current, [field]: event.target.value}))} />
                )}
              </Field>
            );
          })}
          {guide?.webhookEvents && target.configuration.webhookPath && (
            <div className="space-y-1 rounded-xl border border-slate-200 p-3">
              <WebhookAddress path={target.configuration.webhookPath} />
              <p className="text-xs text-ink-500">{guide.webhookEvents} L’adresse doit être joignable depuis Internet.</p>
            </div>
          )}
        </form>
      )}
    </Modal>
  );
}

function ProviderRow({provider, configuration, editable, onChanged, onCredentials}: {
  provider: Provider;
  configuration: Configuration | undefined;
  editable: boolean;
  onChanged: () => Promise<void>;
  onCredentials: (configuration: Configuration) => void;
}) {
  const {busy, run} = useAction();
  const online = provider.type === 'Online';
  const accepted = !!configuration?.isEnabled;
  const awaiting = configuration?.status === 'AwaitingCredentials';
  const unavailable = online && !provider.hasIntegration;
  const Icon = methodIcon[provider.capability.methods[0] ?? 'Cash'];

  const toggle = () => run('toggle', async () => {
    if (accepted && configuration) {
      await disableConfiguration(configuration.id);
      await onChanged();
      return;
    }
    let current = configuration;
    if (!current) {
      await createConfiguration(provider.code, null);
      current = (await loadConfigurations()).find(item => item.provider === provider.code);
    }
    if (current && current.status === 'AwaitingCredentials') {
      await onChanged();
      onCredentials(current);
      return;
    }
    if (current && !current.isEnabled) await enableConfiguration(current.id);
    await onChanged();
  }, accepted ? `${provider.displayName} n’est plus accepté.` : undefined);

  return (
    <li className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center">
      <div className="flex min-w-0 flex-1 items-start gap-3">
        <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl', online ? 'bg-sky-50 text-sky-600' : 'bg-emerald-50 text-emerald-600')} aria-hidden="true"><Icon className="size-5" /></span>
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-2 font-medium">
            {provider.displayName}
            {accepted && <Badge tone="green">Accepté</Badge>}
            {awaiting && !accepted && <Badge tone="amber">Identifiants à saisir</Badge>}
            {configuration && !configuration.isAvailable && <Badge tone="red">Retiré par la plateforme</Badge>}
            {unavailable && <Badge>Bientôt disponible</Badge>}
          </p>
          {provider.capability.methods.length > 1 ? (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {provider.capability.methods.map(method => {
                const active = configuration?.enabledMethods.includes(method) ?? true;
                return (
                  <button key={method} type="button" disabled={!editable || !configuration || !!busy} aria-pressed={active}
                    onClick={() => configuration && run(method, async () => {
                      await setMethod(configuration.id, method, !active);
                      await onChanged();
                    })}
                    className={cx('rounded-full border px-2.5 py-0.5 text-xs font-medium disabled:cursor-default', active ? 'border-brand-200 bg-brand-50 text-brand-800' : 'border-slate-200 text-ink-500')}>
                    {methodLabel[method]}
                  </button>
                );
              })}
            </div>
          ) : (
            <p className="text-sm text-ink-500">{online ? 'Paiement à distance, confirmé par le prestataire.' : 'Encaissé au comptoir ou à la livraison.'}</p>
          )}
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-2 self-end sm:self-auto">
        {online && configuration && editable && (
          <Button size="sm" variant="secondary" onClick={() => onCredentials(configuration)}><KeyRound className="size-4" aria-hidden="true" />Identifiants</Button>
        )}
        {configuration && (
          <Link href={`/parametres/paiements/${configuration.id}`} className="inline-flex h-8 items-center gap-1.5 rounded-lg px-3 text-sm font-semibold text-brand-700 hover:bg-brand-50">
            <Settings2 className="size-4" aria-hidden="true" />Configurer
          </Link>
        )}
        <Switch on={accepted} label={`${accepted ? 'Ne plus accepter' : 'Accepter'} : ${provider.displayName}`} disabled={!editable || !!busy || (unavailable && !accepted)} onChange={() => void toggle()} />
      </div>
    </li>
  );
}

export function PaymentMethods() {
  const {can, currency} = useConsole();
  const editable = can('payments.configure');
  const [credentials, setCredentialsTarget] = useState<{provider: Provider; configuration: Configuration} | null>(null);
  const data = useResource(async signal => {
    const [providers, configurations] = await Promise.all([loadProviders(signal), loadConfigurations(signal)]);
    return {providers, configurations};
  }, []);

  if (data.error) return <Alert>{data.error}</Alert>;
  if (!data.data) return <Spinner label="Chargement des moyens de paiement…" />;
  const {providers, configurations} = data.data;
  const usable = providers.filter(provider => provider.isEnabled && (provider.currencies.length === 0 || provider.currencies.includes(currency)));
  const groups: {title: string; hint: string; list: Provider[]}[] = [
    {title: 'Sur place', hint: 'L’argent est déjà reçu quand le caissier l’enregistre.', list: usable.filter(provider => provider.type === 'OnSite')},
    {title: 'En ligne', hint: 'Le client paie chez le prestataire ; votre compte marchand reçoit l’argent.', list: usable.filter(provider => provider.type === 'Online')},
  ];
  const acceptedCount = configurations.filter(configuration => configuration.isEnabled).length;

  return (
    <Card className="p-5">
      <div className="mb-2 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">Moyens de paiement</h2>
          <p className="text-sm text-ink-500">Ce que la caisse propose à l’encaissement des commandes.</p>
        </div>
        <Badge tone={acceptedCount ? 'green' : 'amber'}>{acceptedCount ? `${acceptedCount} accepté${acceptedCount > 1 ? 's' : ''}` : 'Aucun moyen accepté'}</Badge>
      </div>
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres qui règlent les paiements peuvent modifier cette liste.</Alert>}
      {groups.map(group => group.list.length > 0 && (
        <section key={group.title} className="mt-4">
          <h3 className="text-sm font-semibold">{group.title}</h3>
          <p className="text-xs text-ink-500">{group.hint}</p>
          <ul className="divide-y divide-slate-100">
            {group.list.map(provider => (
              <ProviderRow key={provider.id} provider={provider} configuration={configurations.find(configuration => configuration.provider === provider.code)}
                editable={editable} onChanged={data.reload} onCredentials={configuration => setCredentialsTarget({provider, configuration})} />
            ))}
          </ul>
        </section>
      ))}
      <CredentialsDialog target={credentials} onClose={() => setCredentialsTarget(null)} onDone={data.reload} />
    </Card>
  );
}

