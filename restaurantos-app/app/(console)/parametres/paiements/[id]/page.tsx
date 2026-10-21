'use client';
import {use, useEffect, useState, type ReactNode} from 'react';
import Link from 'next/link';
import {ArrowLeft, Building2, FlaskConical, KeyRound, ListChecks, ShieldCheck, Undo2} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {parseAmount} from '@/lib/format';
import {
  assignLocation, disableConfiguration, enableConfiguration, loadConfigurations, loadProviders, methodLabel, paymentTestSummary, paymentTestWarning,
  providerGuide, pushProviders, refundsByApi, setMethod, signedWebhookProviders, testConfiguration, unassignLocation,
  type Assignment, type Configuration, type PaymentMethod, type PaymentTest, type Provider,
} from '@/lib/payments';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Spinner, cx, useAction} from '@/components/ui';
import {Switch, TestResult} from '../../shared';
import {CredentialsDialog, WebhookAddress, methodIcon} from '../../payments';

function Section({icon, title, description, children, action}: {icon: ReactNode; title: string; description?: string; children: ReactNode; action?: ReactNode}) {
  return (
    <Card className="p-5">
      <div className="mb-4 flex flex-wrap items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true">{icon}</span>
        <div className="min-w-0 flex-1">
          <h2 className="font-semibold">{title}</h2>
          {description && <p className="text-sm text-ink-500">{description}</p>}
        </div>
        {action}
      </div>
      {children}
    </Card>
  );
}

function MerchantTestResult({result, configuration}: {result: PaymentTest; configuration: Configuration}) {
  const summary = paymentTestSummary(result, configuration.displayName);
  const tone = summary.tone === 'green' && result.warnings.length > 0 ? 'amber' : summary.tone;
  return (
    <TestResult tone={tone} title={summary.title} detail={summary.detail}>
      {result.outcome === 'verified' && result.isSandbox !== null && (
        <p>{result.isSandbox ? 'Compte de test (sandbox) : aucun argent réel ne sera encaissé.' : 'Compte de production : les paiements seront réels.'}</p>
      )}
      {result.warnings.length > 0 && (
        <ul className="list-disc space-y-1 pl-4">
          {result.warnings.map(code => <li key={code}>{paymentTestWarning(code)}</li>)}
        </ul>
      )}
      {result.outcome === 'verified' && signedWebhookProviders.has(configuration.provider) && (
        <p className="text-xs opacity-80">Le secret du webhook ne peut être vérifié qu’à la réception d’une notification du prestataire.</p>
      )}
      <p className="text-xs opacity-80">Testé à {new Date(result.testedAtUtc).toLocaleTimeString('fr-FR', {hour: '2-digit', minute: '2-digit'})}.</p>
    </TestResult>
  );
}

type Row = {offered: boolean; priority: string; min: string; max: string};

const rowOf = (assignment: Assignment | undefined): Row => ({
  offered: assignment?.isEnabled ?? false,
  priority: String(assignment?.priority ?? 0),
  min: assignment?.minAmount === null || assignment?.minAmount === undefined ? '' : String(assignment.minAmount),
  max: assignment?.maxAmount === null || assignment?.maxAmount === undefined ? '' : String(assignment.maxAmount),
});

function LocationRow({configuration, locationId, name, method, assignment, editable, onChanged}: {
  configuration: Configuration;
  locationId: string;
  name: string;
  method: PaymentMethod;
  assignment: Assignment | undefined;
  editable: boolean;
  onChanged: () => Promise<void>;
}) {
  const {currency} = useConsole();
  const {busy, run} = useAction();
  const [row, setRow] = useState<Row>(() => rowOf(assignment));
  useEffect(() => setRow(rowOf(assignment)), [assignment]);
  const saved = rowOf(assignment);
  const changed = JSON.stringify(row) !== JSON.stringify(saved);
  const min = row.min.trim() ? parseAmount(row.min) : null;
  const max = row.max.trim() ? parseAmount(row.max) : null;
  const priority = Number(row.priority);
  const invalid = (row.min.trim() !== '' && (min === null || min < 0)) || (row.max.trim() !== '' && (max === null || max <= 0))
    || (min !== null && max !== null && min > max) || !Number.isInteger(priority) || priority < 0 || priority > 100;
  const set = (patch: Partial<Row>) => setRow(current => ({...current, ...patch}));

  return (
    <li className="grid grid-cols-3 gap-3 py-3 lg:grid-cols-[minmax(0,1fr)_6rem_9rem_9rem_auto] lg:items-end">
      <label className="col-span-3 flex min-w-0 items-center gap-3 self-center lg:col-span-1">
        <Switch on={row.offered} label={`${row.offered ? 'Ne plus proposer' : 'Proposer'} ${methodLabel[method]} à ${name}`} disabled={!editable || !!busy}
          onChange={() => set({offered: !row.offered})} />
        <span className="min-w-0">
          <span className="block truncate text-sm font-medium">{name}</span>
          {configuration.enabledMethods.length > 1 && <span className="block text-xs text-ink-500">{methodLabel[method]}</span>}
        </span>
      </label>
      <Field label="Rang"><Input type="number" min={0} max={100} value={row.priority} disabled={!editable} onChange={event => set({priority: event.target.value})} /></Field>
      <Field label="Minimum"><Input inputMode="decimal" placeholder="Aucun" value={row.min} disabled={!editable} onChange={event => set({min: event.target.value})} /></Field>
      <Field label="Maximum"><Input inputMode="decimal" placeholder="Aucun" value={row.max} disabled={!editable} onChange={event => set({max: event.target.value})} /></Field>
      <div className="col-span-3 flex justify-end gap-2 lg:col-span-1">
        {editable && assignment && (
          <Button size="sm" variant="ghost" busy={busy === 'remove'} onClick={() => run('remove', async () => {
            await unassignLocation(configuration.id, locationId, method);
            await onChanged();
          }, `${name} retiré.`)}>Retirer</Button>
        )}
        {editable && (
          <Button size="sm" disabled={!changed || invalid} busy={busy === 'save'} onClick={() => run('save', async () => {
            await assignLocation(configuration.id, locationId, {method, priority, minAmount: min, maxAmount: max, currency, isEnabled: row.offered});
            await onChanged();
          }, 'Établissement enregistré.')}>Enregistrer</Button>
        )}
      </div>
      {invalid && <p className="col-span-3 text-xs text-red-600 lg:col-span-5">Rang de 0 à 100 ; le minimum ne peut pas dépasser le maximum.</p>}
    </li>
  );
}

export default function PaymentConfigurationPage({params}: {params: Promise<{id: string}>}) {
  const {id} = use(params);
  const {can, locations, currency} = useConsole();
  const editable = can('payments.configure');
  const {busy, run} = useAction();
  const [credentials, setCredentials] = useState<{provider: Provider; configuration: Configuration} | null>(null);
  const [tested, setTested] = useState<PaymentTest | null>(null);
  const data = useResource(async signal => {
    const [providers, configurations] = await Promise.all([loadProviders(signal), loadConfigurations(signal)]);
    const configuration = configurations.find(item => item.id === id) ?? null;
    return {configuration, provider: configuration ? providers.find(item => item.id === configuration.providerId) ?? null : null};
  }, [id]);

  const back = <Link href="/parametres?onglet=paiements" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Moyens de paiement</Link>;
  if (data.error) return <div className="space-y-4">{back}<Alert>{data.error}</Alert></div>;
  if (!data.data) return <Spinner label="Chargement de la configuration…" />;
  const {configuration, provider} = data.data;
  if (!configuration) return <div className="space-y-4">{back}<Card className="p-6"><Empty title="Configuration introuvable">Ce moyen de paiement n’est pas configuré dans votre restaurant.</Empty></Card></div>;

  const online = configuration.type === 'Online';
  const awaiting = configuration.status === 'AwaitingCredentials';
  const guide = providerGuide[configuration.provider];
  const active = locations.filter(location => location.status !== 'Archived');
  const methods = provider?.capability.methods ?? configuration.enabledMethods;
  const Icon = methodIcon[methods[0] ?? 'Cash'];

  const toggle = () => run('toggle', async () => {
    if (configuration.isEnabled) await disableConfiguration(configuration.id);
    else if (awaiting && provider) {
      setCredentials({provider, configuration});
      return;
    } else await enableConfiguration(configuration.id);
    await data.reload();
  }, configuration.isEnabled ? `${configuration.displayName} n’est plus accepté.` : undefined);

  return (
    <div className="space-y-6">
      {back}
      <div className="flex flex-wrap items-start gap-4">
        <span className={cx('grid size-12 shrink-0 place-items-center rounded-2xl', online ? 'bg-sky-50 text-sky-600' : 'bg-emerald-50 text-emerald-600')} aria-hidden="true"><Icon className="size-6" /></span>
        <div className="min-w-0 flex-1">
          <h1 className="flex flex-wrap items-center gap-2 text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">
            {configuration.displayName}
            {configuration.isEnabled ? <Badge tone="green">Accepté</Badge> : awaiting ? <Badge tone="amber">Identifiants à saisir</Badge> : <Badge>Non accepté</Badge>}
            {!configuration.isAvailable && <Badge tone="red">Retiré par la plateforme</Badge>}
          </h1>
          <p className="mt-1 text-sm text-ink-500">{online ? 'Paiement en ligne : le client paie chez le prestataire, l’argent arrive sur votre compte marchand.' : 'Encaissé au comptoir ou à la livraison.'}</p>
        </div>
        <label className="flex w-full items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-2.5 text-sm font-medium sm:w-auto">
          {configuration.isEnabled ? 'Accepté' : 'Non accepté'}
          <Switch on={configuration.isEnabled} label={configuration.isEnabled ? 'Ne plus accepter' : 'Accepter'} disabled={!editable || !!busy || (!configuration.isAvailable && !configuration.isEnabled)} onChange={() => void toggle()} />
        </label>
      </div>
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres qui règlent les paiements peuvent modifier cette configuration.</Alert>}

      <Section icon={<ListChecks className="size-5" />} title="Moyens proposés" description="Ce que la caisse et le client peuvent choisir avec ce prestataire.">
        <div className="flex flex-wrap gap-2">
          {methods.map(method => {
            const on = configuration.enabledMethods.includes(method);
            const MethodIcon = methodIcon[method];
            return (
              <button key={method} type="button" aria-pressed={on} disabled={!editable || !!busy || methods.length === 1}
                onClick={() => run(method, async () => {
                  await setMethod(configuration.id, method, !on);
                  await data.reload();
                })}
                className={cx('inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-sm font-medium transition disabled:cursor-default',
                  on ? 'border-brand-300 bg-brand-50 text-brand-800' : 'border-slate-200 text-ink-500 hover:border-slate-300')}>
                <MethodIcon className="size-4" aria-hidden="true" />{methodLabel[method]}
              </button>
            );
          })}
        </div>
      </Section>

      {online && (
        <Section icon={<KeyRound className="size-5" />} title="Compte marchand"
          description={configuration.hasCredentials ? 'Identifiants enregistrés et chiffrés. Ils ne sont jamais réaffichés.' : 'Saisissez les identifiants de votre compte chez le prestataire pour accepter ce moyen.'}
          action={editable ? (
            <div className="flex flex-wrap gap-2">
              {configuration.status === 'Configured' && (
                <Button size="sm" variant="secondary" busy={busy === 'test'} onClick={() => void run('test', async () => {
                  setTested(null);
                  setTested(await testConfiguration(configuration.id));
                })}>
                  <FlaskConical className="size-4" aria-hidden="true" />Tester la connexion
                </Button>
              )}
              {provider && (
                <Button size="sm" variant={configuration.hasCredentials ? 'secondary' : 'primary'} onClick={() => setCredentials({provider, configuration})}>
                  <KeyRound className="size-4" aria-hidden="true" />{configuration.hasCredentials ? 'Remplacer les identifiants' : 'Saisir les identifiants'}
                </Button>
              )}
            </div>
          ) : undefined}>
          <div className="space-y-4">
            {tested && <MerchantTestResult result={tested} configuration={configuration} />}
            {guide && <p className="text-sm text-ink-600">{guide.where}</p>}
            {guide?.webhookEvents && configuration.webhookPath ? (
              <div className="space-y-1 rounded-xl border border-slate-200 p-3">
                <WebhookAddress path={configuration.webhookPath} />
                <p className="text-xs text-ink-500">{guide.webhookEvents} L’adresse doit être joignable depuis Internet.</p>
              </div>
            ) : online && <p className="text-sm text-ink-500">Rien à déclarer chez le prestataire : l’adresse de notification est transmise à chaque paiement.</p>}
            {pushProviders.has(configuration.provider) && (
              <Alert tone="blue">La demande de paiement est envoyée sur le téléphone du client : la caisse demande son numéro à chaque encaissement.</Alert>
            )}
          </div>
        </Section>
      )}

      <Section icon={<Building2 className="size-5" />} title="Établissements"
        description={configuration.isGlobal ? 'Proposé dans tous vos établissements, sans limite de montant.' : 'Proposé seulement dans les établissements activés ci-dessous.'}>
        {active.length === 0 ? (
          <p className="text-sm text-ink-500">Aucun établissement.</p>
        ) : (
          <>
            <p className="mb-2 text-sm text-ink-600">
              Dès qu’un établissement est enregistré ici, seuls les établissements activés proposent ce moyen. Rang : le plus petit est présenté
              en premier à la caisse. Bornes : montants d’encaissement acceptés, en {currency}.
            </p>
            <ul className="divide-y divide-slate-100">
              {active.flatMap(location => configuration.enabledMethods.map(method => (
                <LocationRow key={`${location.id}:${method}`} configuration={configuration} locationId={location.id} name={location.name} method={method}
                  assignment={configuration.assignments.find(item => item.locationId === location.id && item.method === method)}
                  editable={editable} onChanged={data.reload} />
              )))}
            </ul>
            {!configuration.isGlobal && editable && (
              <p className="mt-3 text-xs text-ink-500">Retirer tous les établissements rend ce moyen de nouveau disponible partout.</p>
            )}
          </>
        )}
      </Section>

      {online && (
        <Section icon={refundsByApi.has(configuration.provider) ? <Undo2 className="size-5" /> : <ShieldCheck className="size-5" />} title="Remboursements">
          <p className="text-sm text-ink-600">
            {refundsByApi.has(configuration.provider)
              ? 'Un remboursement demandé depuis la commande est transmis au prestataire ; il est confirmé dès sa réponse.'
              : `Ce prestataire ne rembourse pas par API : faites le remboursement depuis votre tableau de bord ${configuration.displayName}.`}
          </p>
        </Section>
      )}

      <CredentialsDialog target={credentials} onClose={() => setCredentials(null)} onDone={async () => {
        setTested(null);
        await data.reload();
      }} />
    </div>
  );
}
