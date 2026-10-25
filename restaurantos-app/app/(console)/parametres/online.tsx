'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {Building2, Check, Copy, FileCode2, Globe, KeyRound, Lock, Monitor, Pencil, Plus, Smartphone} from 'lucide-react';
import {
  accessOf, createSalesApp, disableSalesApp, enableSalesApp, kindHint, kindLabel, lines, loadOnlineSales, modeLabel, pauseOnlineOrdering,
  developerDocsUrl, rotateSalesAppKey, saveOnlineOrdering, storefrontDocsUrl, updateSalesApp,
  type ChannelPolicy, type OnlineOrderingSettings, type SalesApp, type SalesAppKind, type ServiceMode, type StorefrontOverview,
} from '@/lib/storefront';
import {parseAmount} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {useConsole} from '@/components/console';
import {PlanBadge} from '@/components/plan';
import {Feature} from '@/lib/plan';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, Textarea, cx, useAction} from '@/components/ui';
import {KioskDevices} from './kiosks';
import {Switch} from './shared';

const kinds: SalesAppKind[] = ['Web', 'MobileApp', 'Kiosk'];
const kindIcon: Record<SalesAppKind, typeof Globe> = {Web: Globe, MobileApp: Smartphone, Kiosk: Monitor};
const kindFeature: Record<SalesAppKind, string> = {Web: Feature.StorefrontWeb, MobileApp: Feature.StorefrontMobileApp, Kiosk: Feature.StorefrontKiosk};
const modes: ServiceMode[] = ['DineIn', 'Takeaway', 'Delivery'];

function CopyValue({value, label}: {value: string; label: string}) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex items-center gap-2">
      <code className="min-w-0 flex-1 truncate rounded-lg bg-slate-100 px-2.5 py-2 text-xs" aria-label={label}>{value}</code>
      <Button size="sm" variant="secondary" onClick={() => void navigator.clipboard?.writeText(value).then(() => setCopied(true))}>
        {copied ? <Check className="size-4" aria-hidden="true" /> : <Copy className="size-4" aria-hidden="true" />}{copied ? 'Copiée' : 'Copier'}
      </Button>
    </div>
  );
}

/** Adresse de la borne : la clé est dans le fragment (#), jamais envoyée au serveur du site ni journalisée. */
const kioskUrl = (key: string) => `${(process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3002').replace(/\/+$/, '')}/borne#cle=${key}`;

function KeyDialog({issued, onClose}: {issued: {name: string; key: string; kind?: SalesAppKind} | null; onClose: () => void}) {
  return (
    <Modal open={!!issued} onClose={onClose} title={`Clé de ${issued?.name ?? ''}`} footer={<Button onClick={onClose}>J’ai copié la clé</Button>}>
      {issued && (
        <div className="space-y-4">
          <p className="flex items-start gap-2 rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
            <Lock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            Copiez cette clé maintenant : elle ne sera plus jamais affichée. Si vous la perdez, générez-en une nouvelle.
          </p>
          <CopyValue value={issued.key} label="Clé publique" />
          {issued.kind === 'Kiosk' ? (
            <div className="space-y-2 text-sm text-ink-600">
              <p>Ouvrez cette adresse sur chaque tablette, en plein écran : elle affiche un code à saisir ci-dessous, dans « Bornes ».</p>
              <CopyValue value={kioskUrl(issued.key)} label="Adresse de la borne" />
            </div>
          ) : (
            <p className="text-sm text-ink-600">
              À transmettre à votre agence ou à placer dans votre site. Chaque requête l’envoie dans l’en-tête <code className="text-xs">X-Storefront-Key</code>.
            </p>
          )}
        </div>
      )}
    </Modal>
  );
}

function AppDialog({target, overview, onClose, onSaved}: {
  target: {app?: SalesApp; kind: SalesAppKind} | null;
  overview: StorefrontOverview;
  onClose: () => void;
  onSaved: (issued?: {name: string; key: string; kind?: SalesAppKind}) => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [kind, setKind] = useState<SalesAppKind>('Web');
  const [name, setName] = useState('');
  const [origins, setOrigins] = useState('');
  const [returns, setReturns] = useState('');
  const [all, setAll] = useState(true);
  const [picked, setPicked] = useState<string[]>([]);
  const [human, setHuman] = useState(false);
  useEffect(() => {
    if (!target) return;
    const app = target.app;
    setKind(app?.kind ?? target.kind);
    setName(app?.name ?? '');
    setOrigins((app?.allowedOrigins ?? []).join('\n'));
    setReturns((app?.returnUrls ?? []).join('\n'));
    setAll(!app || app.locationIds.length === 0);
    setPicked(app?.locationIds ?? []);
    setHuman(app?.requiresHumanCheck ?? false);
  }, [target]);
  const editing = !!target?.app;
  const available = kinds.filter(item => accessOf(overview, item));

  return (
    <Modal open={!!target} onClose={onClose} title={editing ? `Modifier ${target?.app?.name ?? ''}` : 'Nouvelle application de vente'} wide footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="sales-app" busy={busy === 'save'} disabled={!name.trim() || (!all && picked.length === 0)}>
          {editing ? 'Enregistrer' : 'Créer et afficher la clé'}
        </Button>
      </>
    }>
      {target && (
        <form id="sales-app" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          const input = {
            name: name.trim(),
            allowedOrigins: lines(origins),
            locationIds: all ? [] : picked,
            returnUrls: lines(returns),
            requireHumanCheck: kind !== 'Kiosk' && human,
          };
          void run('save', async () => {
            if (target.app) {
              await updateSalesApp(target.app.id, input);
              await onSaved();
            } else {
              const issued = await createSalesApp(kind, input);
              await onSaved({name: input.name, key: issued.key, kind});
            }
          }, editing ? 'Application enregistrée.' : undefined);
        }}>
          {!editing && (
            <Field label="Type" hint={kindHint[kind]}>
              <Select value={kind} onChange={event => setKind(event.target.value as SalesAppKind)}>
                {available.map(item => <option key={item} value={item}>{kindLabel[item]}</option>)}
              </Select>
            </Field>
          )}
          <Field label="Nom" hint="Pour la reconnaître dans la console, par exemple « Site chezawa.bj ».">
            <Input value={name} maxLength={80} onChange={event => setName(event.target.value)} />
          </Field>
          {kind === 'Web' ? (
            <Field label="Adresses du site" hint="Une par ligne, par exemple https://chezawa.bj. La clé est refusée depuis tout autre site.">
              <Textarea rows={3} value={origins} placeholder="https://chezawa.bj" onChange={event => setOrigins(event.target.value)} />
            </Field>
          ) : (
            <Field label="Adresses autorisées (facultatif)" hint={kind === 'MobileApp'
              ? 'Seulement si l’application tourne dans un navigateur ou une WebView (Capacitor, Ionic, Flutter web) : une par ligne, par exemple capacitor://localhost. Une application native n’en a pas besoin.'
              : 'Seulement si la borne est une page web ouverte dans un navigateur : son adresse, par exemple https://borne.chezawa.bj.'}>
              <Textarea rows={2} value={origins} placeholder={kind === 'MobileApp' ? 'capacitor://localhost' : 'https://borne.chezawa.bj'} onChange={event => setOrigins(event.target.value)} />
            </Field>
          )}
          <Field label="Pages de retour après paiement en ligne" hint="Une par ligne, en https. Facultatif tant que le paiement en ligne n’est pas branché.">
            <Textarea rows={2} value={returns} placeholder="https://chezawa.bj/merci" onChange={event => setReturns(event.target.value)} />
          </Field>
          {kind !== 'Kiosk' && (
            <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3 text-sm">
              <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={human} onChange={event => setHuman(event.target.checked)} />
              <span>
                <span className="block font-medium">Contrôle anti-robot</span>
                <span className="block text-ink-500">Une vérification invisible (Cloudflare Turnstile) avant une commande, une réservation ou l’envoi d’un code de connexion. Contre les commandes et les SMS envoyés par des robots.</span>
              </span>
            </label>
          )}
          <fieldset className="space-y-2">
            <legend className="mb-1.5 text-sm font-medium text-ink-800">Établissements servis</legend>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" className="size-4 accent-brand-600" checked={all} onChange={event => setAll(event.target.checked)} />
              Tous les établissements, y compris les prochains
            </label>
            {!all && (
              <ul className="grid gap-1.5 sm:grid-cols-2">
                {overview.locations.map(location => (
                  <li key={location.locationId}>
                    <label className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm">
                      <input type="checkbox" className="size-4 accent-brand-600" checked={picked.includes(location.locationId)}
                        onChange={event => setPicked(current => event.target.checked ? [...current, location.locationId] : current.filter(id => id !== location.locationId))} />
                      {location.locationName}
                    </label>
                  </li>
                ))}
              </ul>
            )}
          </fieldset>
        </form>
      )}
    </Modal>
  );
}

function AppRow({app, overview, editable, onEdit, onIssued, onChanged}: {
  app: SalesApp;
  overview: StorefrontOverview;
  editable: boolean;
  onEdit: () => void;
  onIssued: (issued: {name: string; key: string; kind?: SalesAppKind}) => void;
  onChanged: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const Icon = kindIcon[app.kind];
  const active = app.status === 'Active';
  // Canal perdu (plan ou option) : application visible, clé refusée par l'API.
  const usable = accessOf(overview, app.kind);
  const names = app.locationIds.map(id => overview.locations.find(location => location.locationId === id)?.locationName).filter(Boolean);
  return (
    <li className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center">
      <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true"><Icon className="size-5" /></span>
      <div className="min-w-0 flex-1 space-y-1">
        <p className="flex flex-wrap items-center gap-2 font-medium">
          {app.name}
          <Badge tone={active ? 'green' : 'neutral'}>{active ? 'Active' : 'Désactivée'}</Badge>
          <Badge>{kindLabel[app.kind]}</Badge>
          {!usable && <Badge tone="amber">Hors abonnement</Badge>}
        </p>
        <p className="text-sm text-ink-500">
          Clé <code className="text-xs">{app.keyPrefix}…{app.keyHint}</code>
          {' · '}{names.length > 0 ? names.join(', ') : 'Tous les établissements'}
        </p>
        {app.allowedOrigins.length > 0 && <p className="truncate text-xs text-ink-500">{app.allowedOrigins.join(' · ')}</p>}
        {app.kind === 'Web' && app.allowedOrigins.length === 0 && (
          <p className="text-xs text-amber-700">Aucune adresse de site : la clé ne fonctionnera que depuis un serveur.</p>
        )}
      </div>
      {editable && (
        <div className="flex shrink-0 flex-wrap items-center gap-2 self-end sm:self-auto">
          <Button size="sm" variant="ghost" onClick={onEdit}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>
          {usable && <Button size="sm" variant="secondary" busy={busy === 'key'} onClick={() => void run('key', async () => {
            const issued = await rotateSalesAppKey(app.id);
            await onChanged();
            onIssued({name: app.name, key: issued.key, kind: app.kind});
          })}><KeyRound className="size-4" aria-hidden="true" />Nouvelle clé</Button>}
          <Switch on={active} label={`${active ? 'Désactiver' : 'Activer'} : ${app.name}`} disabled={!!busy || (!active && !usable)}
            onChange={() => void run('toggle', async () => {
              await (active ? disableSalesApp(app.id) : enableSalesApp(app.id));
              await onChanged();
            }, active ? `${app.name} désactivée.` : `${app.name} activée.`)} />
        </div>
      )}
    </li>
  );
}

function amountText(value: number | null) {
  return value === null ? '' : String(value);
}

function LocationSettings({initial, editable, currency, onSaved}: {
  initial: OnlineOrderingSettings;
  editable: boolean;
  currency: string;
  onSaved: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const [settings, setSettings] = useState(initial);
  const [minimum, setMinimum] = useState(amountText(initial.minimumOrderAmount));
  const [slot, setSlot] = useState(initial.slotCapacity == null ? '' : String(initial.slotCapacity));
  const [limits, setLimits] = useState<Record<string, string>>(() => Object.fromEntries(initial.policies.map(policy => [policy.channel, amountText(policy.counterLimit)])));
  useEffect(() => {
    setSettings(initial);
    setMinimum(amountText(initial.minimumOrderAmount));
    setSlot(initial.slotCapacity == null ? '' : String(initial.slotCapacity));
    setLimits(Object.fromEntries(initial.policies.map(policy => [policy.channel, amountText(policy.counterLimit)])));
  }, [initial]);
  const set = (patch: Partial<OnlineOrderingSettings>) => setSettings(current => ({...current, ...patch}));
  const setPolicy = (channel: SalesAppKind, patch: Partial<ChannelPolicy>) =>
    set({policies: settings.policies.map(policy => policy.channel === channel ? {...policy, ...patch} : policy)});
  const minimumValue = minimum.trim() ? parseAmount(minimum) : null;
  const slotValue = slot.trim() ? Number(slot) : null;
  const invalid = (minimum.trim() !== '' && (minimumValue === null || minimumValue < 0))
    || settings.serviceModes.length === 0
    || (slotValue !== null && (!Number.isInteger(slotValue) || slotValue < 1 || slotValue > 500))
    || settings.policies.some(policy => (!policy.payOnline && !policy.payAtCounter)
      || (limits[policy.channel]?.trim() && (parseAmount(limits[policy.channel]) ?? 0) <= 0));
  const accepting = !settings.isPaused;

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true"><Building2 className="size-5" /></span>
        <div className="min-w-0 flex-1">
          <h3 className="flex flex-wrap items-center gap-2 font-semibold">
            {settings.locationName}
            {!settings.isOperational && <Badge tone="amber">Fermé</Badge>}
            {settings.isOperational && <Badge tone={accepting ? 'green' : 'amber'}>{accepting ? 'Prend les commandes' : 'En pause'}</Badge>}
          </h3>
          <p className="text-sm text-ink-500">La pause arrête aussitôt les commandes en ligne de cet établissement, sur tous les canaux.</p>
        </div>
        <Switch on={accepting} label={`${accepting ? 'Mettre en pause' : 'Reprendre'} : ${settings.locationName}`} disabled={!editable || !!busy}
          onChange={() => void run('pause', async () => {
            await pauseOnlineOrdering(settings.locationId, accepting);
            await onSaved();
          }, accepting ? 'Commandes en ligne en pause.' : 'Commandes en ligne reprises.')} />
      </div>

      <form className="mt-5 space-y-5" onSubmit={event => {
        event.preventDefault();
        void run('save', async () => {
          await saveOnlineOrdering({
            ...settings,
            minimumOrderAmount: minimumValue && minimumValue > 0 ? minimumValue : null,
            slotCapacity: slotValue,
            policies: settings.policies.map(policy => ({
              ...policy,
              counterLimit: policy.payAtCounter && limits[policy.channel]?.trim() ? parseAmount(limits[policy.channel]) : null,
            })),
          });
          await onSaved();
        }, `${settings.locationName} : réglages enregistrés.`);
      }}>
        <fieldset disabled={!editable} className="space-y-5">
          <div>
            <p className="mb-1.5 text-sm font-medium text-ink-800">Modes proposés</p>
            <div className="flex flex-wrap gap-2">
              {modes.map(mode => {
                const on = settings.serviceModes.includes(mode);
                return (
                  <label key={mode} className={cx('flex items-center gap-2 rounded-lg border px-3 py-2 text-sm', on ? 'border-brand-300 bg-brand-50' : 'border-slate-200')}>
                    <input type="checkbox" className="size-4 accent-brand-600" checked={on}
                      onChange={event => set({serviceModes: event.target.checked ? modes.filter(item => item === mode || settings.serviceModes.includes(item)) : settings.serviceModes.filter(item => item !== mode)})} />
                    {modeLabel[mode]}
                  </label>
                );
              })}
            </div>
          </div>
          {settings.serviceModes.includes('Delivery') && (
            <p className="-mt-3 text-xs text-ink-500">La livraison en ligne utilise les zones, frais et minimums de <a href="/livraison" className="font-medium text-brand-700 underline">Livraison & retrait</a> ; sans zone active, elle n’est pas proposée.</p>
          )}
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
            <Field label="Préparation (minutes)">
              <Input type="number" min={5} max={240} value={settings.preparationMinutes} onChange={event => set({preparationMinutes: Number(event.target.value)})} />
            </Field>
            <Field label={`Minimum de commande (${currency})`}>
              <Input inputMode="decimal" value={minimum} placeholder="Aucun" onChange={event => setMinimum(event.target.value)} />
            </Field>
            <Field label="Acceptation manuelle (minutes)" hint="Au-delà, la commande est refusée.">
              <Input type="number" min={2} max={60} value={settings.manualAcceptanceMinutes} onChange={event => set({manualAcceptanceMinutes: Number(event.target.value)})} />
            </Field>
            <Field label="Paiement en ligne (minutes)" hint="Au-delà, la commande impayée est annulée.">
              <Input type="number" min={5} max={120} value={settings.unpaidExpiryMinutes} onChange={event => set({unpaidExpiryMinutes: Number(event.target.value)})} />
            </Field>
            <Field label="Commandes par créneau" hint="Par tranche de 15 minutes. Vide : sans limite.">
              <Input type="number" min={1} max={500} value={slot} placeholder="Sans limite" onChange={event => setSlot(event.target.value)} />
            </Field>
          </div>
          <div>
            <p className="mb-1.5 text-sm font-medium text-ink-800">Par canal</p>
            <div className="hidden gap-3 px-3 pb-1 text-xs font-medium text-ink-500 md:grid md:grid-cols-[9rem_1fr_8rem_12rem]">
              <span>Canal</span><span>Paiements acceptés</span><span>Plafond comptoir</span><span>Acceptation des commandes</span>
            </div>
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {settings.policies.map(policy => {
                const Icon = kindIcon[policy.channel];
                const none = !policy.payOnline && !policy.payAtCounter;
                return (
                  <li key={policy.channel} className="grid gap-3 p-3 md:grid-cols-[9rem_1fr_8rem_12rem] md:items-center">
                    <p className="flex items-center gap-2 text-sm font-medium"><Icon className="size-4 text-ink-500" aria-hidden="true" />{kindLabel[policy.channel]}</p>
                    <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
                      <label className="flex items-center gap-2">
                        <input type="checkbox" className="size-4 accent-brand-600" checked={policy.payOnline} onChange={event => setPolicy(policy.channel, {payOnline: event.target.checked})} />
                        Payer en ligne
                      </label>
                      <label className="flex items-center gap-2">
                        <input type="checkbox" className="size-4 accent-brand-600" checked={policy.payAtCounter} onChange={event => setPolicy(policy.channel, {payAtCounter: event.target.checked})} />
                        Payer au comptoir
                      </label>
                      {none && <span className="w-full text-xs text-red-700">Choisissez au moins un moyen.</span>}
                    </div>
                    <Input aria-label={`Plafond au comptoir, ${kindLabel[policy.channel]}`} inputMode="decimal" placeholder="Sans plafond" disabled={!policy.payAtCounter}
                      value={limits[policy.channel] ?? ''} onChange={event => setLimits(current => ({...current, [policy.channel]: event.target.value}))} />
                    <Select aria-label={`Acceptation, ${kindLabel[policy.channel]}`} value={policy.acceptance}
                      onChange={event => setPolicy(policy.channel, {acceptance: event.target.value as ChannelPolicy['acceptance']})}>
                      <option value="Automatic">Automatique</option>
                      <option value="Manual">Par l’équipe</option>
                    </Select>
                  </li>
                );
              })}
            </ul>
            <p className="mt-1.5 text-xs text-ink-500">Plafond au comptoir : au-delà, seul le paiement en ligne est proposé. Montants en {currency}.</p>
          </div>
        </fieldset>
        {editable && (
          <div className="flex justify-end">
            <Button type="submit" busy={busy === 'save'} disabled={invalid}>Enregistrer</Button>
          </div>
        )}
      </form>
    </Card>
  );
}

export function OnlineSales() {
  const {can, currency} = useConsole();
  const editable = can('storefront.manage');
  const allowed = can('storefront.view') || editable;
  const [dialog, setDialog] = useState<{app?: SalesApp; kind: SalesAppKind} | null>(null);
  const [issued, setIssued] = useState<{name: string; key: string; kind?: SalesAppKind} | null>(null);
  const data = useResource(signal => allowed ? loadOnlineSales(signal) : Promise.resolve(null), [allowed]);

  if (!allowed) return <Empty icon={<Globe className="size-8" />} title="Vente en ligne non disponible">Réservé aux membres qui gèrent la vente en ligne.</Empty>;
  if (data.error) return <Alert>{data.error}</Alert>;
  if (!data.data) return <Spinner label="Chargement de la vente en ligne…" />;
  const overview = data.data;
  const anyOption = kinds.some(kind => accessOf(overview, kind));

  return (
    <div className="space-y-6">
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres qui gèrent la vente en ligne peuvent modifier ces réglages.</Alert>}

      <section className="grid gap-3 md:grid-cols-3" aria-label="Options de vente en ligne">
        {kinds.map(kind => {
          const Icon = kindIcon[kind];
          const on = accessOf(overview, kind);
          return (
            <Card key={kind} className="flex gap-3 p-4">
              <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl', on ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-ink-500')} aria-hidden="true"><Icon className="size-5" /></span>
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 font-semibold">{kindLabel[kind]}{on ? <Badge tone="green">Disponible</Badge> : <PlanBadge force feature={kindFeature[kind]} />}</p>
                <p className="text-sm text-ink-500">{on ? kindHint[kind] : `Disponible avec l’option ${kindLabel[kind]}, sur tous les plans.`}</p>
                {!on && <Link href={kind === 'Web' ? '/abonnement' : '/abonnement#options'} className="mt-1 inline-block text-sm font-medium text-brand-700 hover:underline">{kind === 'Web' ? 'Voir les plans' : 'Ajouter cette option'}</Link>}
              </div>
            </Card>
          );
        })}
      </section>

      <Card className="p-5">
        <div className="flex flex-wrap items-start gap-3">
          <div className="min-w-0 flex-1">
            <h2 className="font-semibold">Applications de vente</h2>
            <p className="text-sm text-ink-500">Chaque site, application ou ensemble de bornes a sa clé publique, à remettre à votre agence.</p>
          </div>
          {editable && anyOption && (
            <Button size="sm" onClick={() => setDialog({kind: kinds.find(kind => accessOf(overview, kind)) ?? 'Web'})}>
              <Plus className="size-4" aria-hidden="true" />Nouvelle application
            </Button>
          )}
        </div>
        {overview.apps.length === 0 ? (
          <p className="mt-4 text-sm text-ink-500">{anyOption ? 'Aucune application pour le moment.' : <>Souscrivez une option depuis la page <Link href="/abonnement#options" className="font-medium text-brand-700 hover:underline">Abonnement</Link> pour créer une application de vente.</>}</p>
        ) : (
          <ul className="mt-2 divide-y divide-slate-100">
            {overview.apps.map(app => (
              <AppRow key={app.id} app={app} overview={overview} editable={editable} onEdit={() => setDialog({app, kind: app.kind})}
                onIssued={setIssued} onChanged={data.reload} />
            ))}
          </ul>
        )}
        <div className="mt-4 space-y-1.5 rounded-xl bg-slate-50 p-3">
          <p className="flex items-center gap-2 text-sm font-medium"><FileCode2 className="size-4" aria-hidden="true" />Documentation de l’API pour votre agence</p>
          <p className="text-sm text-ink-500">
            Guides, exemples (TypeScript, Flutter, Swift, Kotlin) et codes d’erreur :{' '}
            <a href={developerDocsUrl} target="_blank" rel="noopener" className="font-medium text-brand-700 hover:underline">documentation développeurs</a>.
          </p>
          <CopyValue value={developerDocsUrl} label="Adresse de la documentation" />
          <CopyValue value={storefrontDocsUrl} label="Description OpenAPI" />
        </div>
      </Card>

      {accessOf(overview, 'Kiosk') && <KioskDevices overview={overview} editable={editable} />}

      <section className="space-y-3" aria-label="Réglages par établissement">
        <div>
          <h2 className="font-semibold">Réglages par établissement</h2>
          <p className="text-sm text-ink-500">Ce que chaque établissement propose en ligne : modes, délais, paiements et acceptation des commandes.</p>
        </div>
        {overview.locations.length === 0 && <Empty icon={<Building2 className="size-8" />} title="Aucun établissement">Créez d’abord un établissement.</Empty>}
        {overview.locations.map(location => (
          <LocationSettings key={location.locationId} initial={location} editable={editable} currency={currency} onSaved={data.reload} />
        ))}
      </section>

      <AppDialog target={dialog} overview={overview} onClose={() => setDialog(null)} onSaved={async created => {
        setDialog(null);
        await data.reload();
        if (created) setIssued(created);
      }} />
      <KeyDialog issued={issued} onClose={() => setIssued(null)} />
    </div>
  );
}
