'use client';
import {useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {CalendarClock, Megaphone, Pencil, Plus, Rocket, Square, Undo2, Users} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {dateTime} from '@/lib/format';
import {
  campaignAudience, campaignStatusLabel, campaignStatusTone, cancelCampaign, createCampaign, listCampaigns, marketingRules, perRecipientVariables,
  scheduleCampaign, setMarketingRules, unscheduleCampaign, updateCampaign, type Campaign, type CampaignInput,
} from '@/lib/campaigns';
import {
  channelLabel, customerChannels, effectiveTemplate, listTemplates, loadMessageTypes, render, sampleValues, variableLabel, type MessageChannel,
} from '@/lib/messages';
import {useConsole} from '@/components/console';
import {PlanBadge, UpgradePanel, usePlanFeatures} from '@/components/plan';
import {Feature} from '@/lib/plan';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, Tabs, Textarea, cx, useAction} from '@/components/ui';

/** Date et heure locales pour un champ datetime-local. */
function localInput(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function CampaignDialog({campaign, open, onClose, onDone}: {campaign: Campaign | null; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {tenant, location, locations} = useConsole();
  const {busy, run} = useAction();
  const [local, setLocal] = useState(false);
  const types = useResource(signal => (open ? loadMessageTypes(signal) : Promise.resolve(null)), [open]);
  const templates = useResource(signal => (open ? listTemplates(signal) : Promise.resolve(null)), [open]);
  const marketing = (types.data ?? []).filter(type => type.marketing);
  const [name, setName] = useState('');
  const [type, setType] = useState('marketing.promotional_campaign');
  const [channel, setChannel] = useState<MessageChannel>('WhatsApp');
  const [tag, setTag] = useState('');
  const [values, setValues] = useState<Record<string, string>>({});
  const [debouncedTag, setDebouncedTag] = useState('');
  useEffect(() => {
    if (!open) return;
    setName(campaign?.name ?? '');
    setType(campaign?.type ?? 'marketing.promotional_campaign');
    setChannel(campaign?.channel ?? 'WhatsApp');
    setTag(campaign?.tag ?? '');
    setLocal(!!campaign?.onlyLocationCustomers);
    setDebouncedTag(campaign?.tag ?? '');
    setValues(campaign?.variables ?? {'restaurant.name': tenant.branding.displayName || tenant.name});
  }, [open, campaign, tenant]);
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedTag(tag.trim()), 400);
    return () => clearTimeout(timer);
  }, [tag]);
  // Une campagne déjà ciblée garde son établissement ; une nouvelle prend celui affiché.
  const target = local ? (campaign?.onlyLocationCustomers && campaign.locationId ? campaign.locationId : location?.id ?? null) : null;
  const targetName = locations.find(item => item.id === target)?.name ?? location?.name ?? '';
  const audience = useResource(
    signal => (open ? campaignAudience(channel, debouncedTag || null, target, signal) : Promise.resolve(null)),
    [open, channel, debouncedTag, target],
  );
  const definition = marketing.find(item => item.key === type);
  const editable = (definition?.variables ?? []).filter(variable => !perRecipientVariables.includes(variable));
  const template = templates.data ? effectiveTemplate(templates.data, type, channel, 'fr') : null;
  const preview = {...sampleValues, ...Object.fromEntries(Object.entries(values).filter(([, value]) => value.trim()))};
  const missing = template ? template.variables.filter(variable => !perRecipientVariables.includes(variable) && !values[variable]?.trim()) : [];

  const input = (): CampaignInput => ({
    name: name.trim(),
    type,
    channel,
    tag: tag.trim() || null,
    locationId: target,
    onlyLocationCustomers: !!target,
    variables: Object.fromEntries(editable.filter(variable => values[variable]?.trim()).map(variable => [variable, values[variable].trim()])),
  });

  return (
    <Modal wide open={open} onClose={onClose} title={campaign ? campaign.name : 'Nouvelle campagne'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="campaign" busy={busy === 'save'} disabled={!name.trim() || !definition}>Enregistrer le brouillon</Button>
      </>
    }>
      <form id="campaign" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('save', async () => {
          if (campaign) await updateCampaign(campaign.id, input());
          else await createCampaign(input());
          await onDone();
          onClose();
        }, 'Brouillon enregistré.');
      }}>
        <Field label="Nom (interne)"><Input required maxLength={100} value={name} onChange={event => setName(event.target.value)} placeholder="Happy hour d’octobre" /></Field>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <Field label="Message">
            {!types.data ? <Spinner /> : (
              <Select value={type} onChange={event => setType(event.target.value)}>
                {marketing.map(item => <option key={item.key} value={item.key}>{item.label}</option>)}
              </Select>
            )}
          </Field>
          <Field label="Canal">
            <Select value={channel} onChange={event => setChannel(event.target.value as MessageChannel)}>
              {customerChannels.map(item => <option key={item} value={item}>{channelLabel[item]}</option>)}
            </Select>
          </Field>
          <Field label="Clients ayant le tag (facultatif)" hint="Vide : tous les clients qui ont accepté.">
            <Input maxLength={50} value={tag} onChange={event => setTag(event.target.value)} placeholder="vip" />
          </Field>
        </div>
        {locations.length > 1 && location && (
          <Field label="Destinataires">
            <Select value={local ? 'local' : 'all'} onChange={event => setLocal(event.target.value === 'local')}>
              <option value="all">Clients de tout le restaurant</option>
              <option value="local">Clients de {targetName} (au moins une commande)</option>
            </Select>
          </Field>
        )}
        <p className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 text-sm">
          <Users className="size-4 text-ink-500" aria-hidden="true" />
          {audience.data ? (
            <span><strong>{audience.data.count}</strong> client{audience.data.count > 1 ? 's' : ''} joignable{audience.data.count > 1 ? 's' : ''} par {channelLabel[channel]} (accord marketing donné).</span>
          ) : <span className="text-ink-500">Calcul de l’audience…</span>}
        </p>
        {editable.length > 0 && (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            {editable.map(variable => (
              <Field key={variable} label={variableLabel[variable] ?? variable}>
                {variable === 'campaign.title' ? (
                  <Textarea rows={2} maxLength={500} value={values[variable] ?? ''} onChange={event => setValues(current => ({...current, [variable]: event.target.value}))} />
                ) : (
                  <Input maxLength={500} value={values[variable] ?? ''} onChange={event => setValues(current => ({...current, [variable]: event.target.value}))}
                    placeholder={sampleValues[variable]} />
                )}
              </Field>
            ))}
          </div>
        )}
        <section className="rounded-2xl border border-slate-200 p-4">
          <p className="mb-2 text-sm font-semibold">Aperçu</p>
          {!templates.data ? <Spinner /> : template ? (
            <div className="space-y-1 text-sm">
              {template.subject && <p className="font-semibold">{render(template.subject, preview)}</p>}
              <p className="whitespace-pre-line text-ink-700">{render(template.body, preview)}</p>
              {missing.length > 0 && <p className="text-xs text-amber-700">À remplir pour l’envoi : {missing.map(variable => variableLabel[variable] ?? variable).join(', ')}.</p>}
            </div>
          ) : (
            <Alert tone="amber">Aucun modèle publié pour ce message sur ce canal. <Link className="font-semibold underline" href="/messages?onglet=modeles">Créer le modèle</Link></Alert>
          )}
          <p className="mt-2 text-xs text-ink-500">Le prénom et le lien de désinscription sont ajoutés pour chaque client.</p>
        </section>
      </form>
    </Modal>
  );
}

function ScheduleDialog({campaign, onClose, onDone}: {campaign: Campaign | null; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const {blocked} = usePlanFeatures();
  const later = !blocked(Feature.Automations);
  const [mode, setMode] = useState<'now' | 'later'>('now');
  const [at, setAt] = useState('');
  useEffect(() => {
    if (!campaign) return;
    setMode('now');
    setAt(localInput(new Date(Date.now() + 60 * 60_000)));
  }, [campaign]);
  if (!campaign) return null;
  const when = mode === 'later' && at ? new Date(at) : null;

  return (
    <Modal open onClose={onClose} title={`Lancer « ${campaign.name} »`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'go'} disabled={mode === 'later' && (!when || when.getTime() <= Date.now())} onClick={() => void run('go', async () => {
          await scheduleCampaign(campaign.id, when ? when.toISOString() : null);
          await onDone();
          onClose();
        }, when ? 'Campagne programmée.' : 'Envoi lancé.')}><Rocket className="size-4" aria-hidden="true" />{when ? 'Programmer' : 'Envoyer maintenant'}</Button>
      </>
    }>
      <div className="space-y-3 text-sm">
        <label className="flex items-center gap-2"><input type="radio" name="when" className="size-4 accent-brand-600" checked={mode === 'now'} onChange={() => setMode('now')} />Maintenant</label>
        <label className={cx('flex items-center gap-2', !later && 'text-ink-400')}><input type="radio" name="when" className="size-4 accent-brand-600" disabled={!later} checked={mode === 'later'} onChange={() => setMode('later')} />Plus tard<PlanBadge feature={Feature.Automations} /></label>
        {mode === 'later' && <Field label="Date et heure"><Input type="datetime-local" value={at} onChange={event => setAt(event.target.value)} /></Field>}
        <p className="text-ink-500">Chaque client reçoit le message une seule fois. Ceux qui ont retiré leur accord ou reçu un message marketing récemment sont écartés.</p>
      </div>
    </Modal>
  );
}

function Rules() {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const rules = useResource(signal => marketingRules(signal), []);
  const [days, setDays] = useState('');
  useEffect(() => {
    if (rules.data) setDays(String(rules.data.minimumIntervalDays));
  }, [rules.data]);
  if (!rules.data) return null;
  const parsed = Number(days);
  const valid = days.trim() !== '' && Number.isInteger(parsed) && parsed >= 0 && parsed <= 30;

  return (
    <Card className="p-4">
      <form className="flex flex-wrap items-end gap-3" onSubmit={event => {
        event.preventDefault();
        if (valid) void run('rules', async () => {
          await setMarketingRules(parsed);
          await rules.reload();
        }, 'Règle enregistrée.');
      }}>
        <div className="min-w-0 flex-1 basis-64">
          <p className="font-semibold">Fréquence</p>
          <p className="text-sm text-ink-500">Un client reçoit au plus un message marketing par période, toutes campagnes et tous canaux confondus. 0 : pas de limite.</p>
        </div>
        <Field label="Jours entre deux messages" className="w-40">
          <Input inputMode="numeric" value={days} disabled={!can('notifications.configure')} onChange={event => setDays(event.target.value)} />
        </Field>
        {can('notifications.configure') && (
          <Button type="submit" variant="secondary" busy={busy === 'rules'} disabled={!valid || parsed === rules.data.minimumIntervalDays}>Enregistrer</Button>
        )}
      </form>
    </Card>
  );
}

export function Campaigns() {
  const {can, location, locations} = useConsole();
  const [scope, setScope] = useState<'here' | 'all'>('here');
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => listCampaigns(signal), []);
  const [editing, setEditing] = useState<Campaign | null>(null);
  const [open, setOpen] = useState(false);
  const [launching, setLaunching] = useState<Campaign | null>(null);
  const {blocked} = usePlanFeatures();
  const locked = blocked(Feature.MarketingCampaigns);
  // Sans le droit : campagnes visibles, plus de création ni d'envoi.
  const send = can('notifications.send') && !locked;
  const active = useMemo(() => (data ?? []).some(campaign => campaign.status === 'Sending' || campaign.status === 'Scheduled'), [data]);
  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => void reload(), 5000);
    return () => clearInterval(timer);
  }, [active, reload]);

  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  const several = locations.length > 1 && !!location;
  const nameOf = (id: string | null) => locations.find(item => item.id === id)?.name ?? 'un établissement';
  // Ici : les campagnes pour tout le restaurant et celles des clients de cet établissement.
  const shown = several && scope === 'here' ? data.filter(campaign => !campaign.onlyLocationCustomers || campaign.locationId === location!.id) : data;

  return (
    <div className="space-y-4">
      {locked && <UpgradePanel feature={Feature.MarketingCampaigns} title="Campagnes marketing">Envoyez des messages groupés à vos clients par WhatsApp, SMS ou e-mail.{data.length > 0 ? ' Vos campagnes restent consultables ; celles programmées ne partent plus.' : ''}</UpgradePanel>}
      <Rules />
      {several && (
        <Tabs label="Campagnes" value={scope} onChange={setScope} items={[
          {value: 'here', label: `${location!.name} et tout le restaurant`},
          {value: 'all', label: `Tous les établissements (${data.length})`},
        ]} />
      )}
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-ink-500">Messages groupés aux clients qui ont accepté le marketing, par WhatsApp, SMS ou e-mail.</p>
        {send && <Button onClick={() => {
          setEditing(null);
          setOpen(true);
        }}><Plus className="size-4" aria-hidden="true" />Nouvelle campagne</Button>}
      </div>
      {shown.length === 0 ? (
        <Card className="p-6"><Empty icon={<Megaphone className="size-8" />} title="Aucune campagne">Annoncez une nouvelle carte, un événement ou une offre à vos clients fidèles.</Empty></Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {shown.map(campaign => (
              <li key={campaign.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
                <div className="min-w-0 flex-1 basis-60">
                  <p className="flex flex-wrap items-center gap-2 font-medium">{campaign.name}<Badge tone={campaignStatusTone[campaign.status]}>{campaignStatusLabel[campaign.status]}</Badge></p>
                  <p className="truncate text-ink-500">
                    {channelLabel[campaign.channel]}
                    {campaign.onlyLocationCustomers ? ` · clients de ${nameOf(campaign.locationId)}` : several ? ' · tout le restaurant' : ''}
                    {campaign.tag ? ` · tag « ${campaign.tag} »` : ' · tous les clients'}
                    {campaign.status === 'Scheduled' && campaign.scheduledForUtc ? ` · départ ${dateTime(campaign.scheduledForUtc)}` : ''}
                    {campaign.completedAtUtc ? ` · ${dateTime(campaign.completedAtUtc)}` : ''}
                  </p>
                  {campaign.processed > 0 && (
                    <p className={cx('text-xs', campaign.status === 'Sending' ? 'text-amber-700' : 'text-ink-500')}>
                      {campaign.created} message{campaign.created > 1 ? 's' : ''} sur {campaign.processed} client{campaign.processed > 1 ? 's' : ''}
                      {campaign.skipped > 0 ? ` · ${campaign.skipped} écarté${campaign.skipped > 1 ? 's' : ''}` : ''}
                      {campaign.skippedForFrequency > 0 ? ` (dont ${campaign.skippedForFrequency} par la règle de fréquence)` : ''}
                    </p>
                  )}
                </div>
                {send && (
                  <div className="flex flex-wrap gap-1">
                    {campaign.status === 'Draft' && (
                      <>
                        <Button size="sm" variant="secondary" onClick={() => {
                          setEditing(campaign);
                          setOpen(true);
                        }}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>
                        <Button size="sm" onClick={() => setLaunching(campaign)}><Rocket className="size-4" aria-hidden="true" />Lancer</Button>
                      </>
                    )}
                    {campaign.status === 'Scheduled' && (
                      <Button size="sm" variant="secondary" busy={busy === `un-${campaign.id}`} onClick={() => run(`un-${campaign.id}`, async () => {
                        await unscheduleCampaign(campaign.id);
                        await reload();
                      }, 'Campagne repassée en brouillon.')}><Undo2 className="size-4" aria-hidden="true" />Déprogrammer</Button>
                    )}
                    {(campaign.status === 'Scheduled' || campaign.status === 'Sending') && (
                      <Button size="sm" variant="ghost" className="text-red-600" busy={busy === `stop-${campaign.id}`} onClick={() => run(`stop-${campaign.id}`, async () => {
                        await cancelCampaign(campaign.id);
                        await reload();
                      }, 'Campagne arrêtée.')}><Square className="size-4" aria-hidden="true" />Arrêter</Button>
                    )}
                    {campaign.status === 'Scheduled' && <CalendarClock className="size-4 self-center text-sky-600" aria-hidden="true" />}
                  </div>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
      <CampaignDialog campaign={editing} open={open} onClose={() => setOpen(false)} onDone={reload} />
      <ScheduleDialog campaign={launching} onClose={() => setLaunching(null)} onDone={reload} />
    </div>
  );
}
