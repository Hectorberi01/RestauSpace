'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {ArrowLeft, CalendarClock, Download, Eye, Mail, Pencil, Plus} from 'lucide-react';
import {downloadFile} from '@/lib/api';
import {money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  METRICS, changeDefinitionLocation, exportDefinition, listExports, metricLabel, presets, runDefinition, total, createDefinition, definitionKind, definitionLocation, listDefinitions, renameDefinition, scheduleDefinition, unscheduleDefinition,
  type Frequency, type ReportDefinition, type ReportKind, type Weekday,
} from '@/lib/reports';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';
import {MetricPicker} from '../metric-picker';

const kindLabel: Record<ReportKind | 'all', string> = {
  sales: 'Ventes',
  orders: 'Commandes',
  reservations: 'Réservations',
  kitchen: 'Cuisine',
  payments: 'Paiements',
  all: 'Toutes les mesures',
};

const weekdays: [Weekday, string][] = [
  ['Monday', 'lundi'], ['Tuesday', 'mardi'], ['Wednesday', 'mercredi'], ['Thursday', 'jeudi'], ['Friday', 'vendredi'], ['Saturday', 'samedi'], ['Sunday', 'dimanche'],
];
const weekdayLabel = Object.fromEntries(weekdays) as Record<Weekday, string>;

const frequencyLabel: Record<Frequency, string> = {Daily: 'Chaque jour', Weekly: 'Chaque semaine', Monthly: 'Chaque mois'};
const covered: Record<Frequency, string> = {Daily: 'la veille', Weekly: 'les 7 jours précédents', Monthly: 'le mois précédent'};

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const when = (iso: string) => new Intl.DateTimeFormat('fr-FR', {weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

function rhythm(definition: ReportDefinition) {
  const schedule = definition.schedule;
  if (!schedule) return null;
  const day = schedule.frequency === 'Weekly' && schedule.dayOfWeek ? ` le ${weekdayLabel[schedule.dayOfWeek]}` : schedule.frequency === 'Monthly' && schedule.dayOfMonth ? ` le ${schedule.dayOfMonth}` : '';
  return `${frequencyLabel[schedule.frequency]}${day} à ${schedule.localTime}`;
}

function DefinitionDialog({definition, open, onClose, onSaved}: {definition: ReportDefinition | null; open: boolean; onClose: () => void; onSaved: () => Promise<void>}) {
  const {locations, location} = useConsole();
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [kind, setKind] = useState<ReportKind | 'all' | 'custom'>('sales');
  const [picked, setPicked] = useState<string[]>([]);
  const [scope, setScope] = useState('');
  useEffect(() => {
    if (!open) return;
    setName(definition?.name ?? '');
    setKind(definition ? definitionKind(definition) ?? 'sales' : 'sales');
    setPicked([]);
    setScope(definition ? definitionLocation(definition) ?? '' : location?.id ?? '');
  }, [open, definition, location?.id]);

  return (
    <Modal wide={kind === 'custom' && !definition} open={open} onClose={onClose} title={definition ? 'Modifier le rapport' : 'Nouveau rapport'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="definition-form" busy={busy === 'save'} disabled={!name.trim()}>Enregistrer</Button>
      </>
    }>
      <form id="definition-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('save', async () => {
          if (definition) {
            if (name.trim() !== definition.name) await renameDefinition(definition.id, name.trim());
            if ((definitionLocation(definition) ?? '') !== scope) await changeDefinitionLocation(definition.id, scope || null);
          } else {
            if (kind === 'custom' && picked.length === 0) throw new Error('Cochez au moins une mesure.');
            await createDefinition(name.trim(), kind === 'all' ? Object.values(METRICS).flat() : kind === 'custom' ? picked : METRICS[kind], scope || null);
          }
          onClose();
          await onSaved();
        }, definition ? 'Rapport modifié.' : 'Rapport créé.');
      }}>
        <Field label="Nom"><Input required maxLength={100} value={name} onChange={event => setName(event.target.value)} placeholder="Ventes de la semaine" /></Field>
        {!definition && (
          <Field label="Contenu">
            <Select value={kind} onChange={event => setKind(event.target.value as ReportKind | 'all' | 'custom')}>
              {(Object.keys(kindLabel) as (ReportKind | 'all')[]).map(value => <option key={value} value={value}>{kindLabel[value]}</option>)}
              <option value="custom">Mesures au choix…</option>
            </Select>
          </Field>
        )}
        {!definition && kind === 'custom' && <MetricPicker value={picked} onChange={setPicked} />}
        <Field label="Établissement">
          <Select value={scope} onChange={event => setScope(event.target.value)}>
            {locations.length > 1 && <option value="">Tous les établissements</option>}
            {locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        </Field>
      </form>
    </Modal>
  );
}

function ScheduleDialog({definition, onClose, onSaved}: {definition: ReportDefinition | null; onClose: () => void; onSaved: () => Promise<void>}) {
  const {email} = useConsole();
  const {busy, run} = useAction();
  const [frequency, setFrequency] = useState<Frequency>('Weekly');
  const [dayOfWeek, setDayOfWeek] = useState<Weekday>('Monday');
  const [dayOfMonth, setDayOfMonth] = useState('1');
  const [time, setTime] = useState('08:00');
  const [recipients, setRecipients] = useState('');
  useEffect(() => {
    if (!definition) return;
    const schedule = definition.schedule;
    setFrequency(schedule?.frequency ?? 'Weekly');
    setDayOfWeek(schedule?.dayOfWeek ?? 'Monday');
    setDayOfMonth(String(schedule?.dayOfMonth ?? 1));
    setTime(schedule?.localTime ?? '08:00');
    setRecipients(schedule?.recipients.join('\n') ?? email ?? '');
  }, [definition, email]);

  const list = [...new Set(recipients.split(/[\s,;]+/).map(item => item.trim().toLowerCase()).filter(Boolean))];
  const invalid = list.filter(item => !EMAIL.test(item));
  const ready = list.length > 0 && list.length <= 20 && invalid.length === 0 && /^\d{2}:\d{2}$/.test(time);

  return (
    <Modal open={definition !== null} onClose={onClose} title={definition ? `Envoi automatique · ${definition.name}` : ''} footer={
      <>
        {definition?.schedule && (
          <Button variant="ghost" className="mr-auto text-red-600" busy={busy === 'stop'} onClick={() => definition && run('stop', async () => {
            await unscheduleDefinition(definition.id);
            onClose();
            await onSaved();
          }, 'Envoi automatique arrêté.')}>Arrêter les envois</Button>
        )}
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="schedule-form" busy={busy === 'save'} disabled={!ready}>Enregistrer</Button>
      </>
    }>
      <form id="schedule-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        if (!definition || !ready) return;
        void run('save', async () => {
          await scheduleDefinition(definition.id, {
            frequency,
            dayOfWeek: frequency === 'Weekly' ? dayOfWeek : null,
            dayOfMonth: frequency === 'Monthly' ? Number(dayOfMonth) : null,
            localTime: time,
            recipients: list,
          });
          onClose();
          await onSaved();
        }, 'Envoi automatique enregistré.');
      }}>
        <div className="flex rounded-xl bg-slate-100 p-1" role="radiogroup" aria-label="Fréquence">
          {(Object.keys(frequencyLabel) as Frequency[]).map(value => (
            <button key={value} type="button" role="radio" aria-checked={frequency === value} onClick={() => setFrequency(value)}
              className={cx('flex-1 rounded-lg px-3 py-1.5 text-sm font-semibold', frequency === value ? 'bg-white shadow-sm' : 'text-ink-500')}>{frequencyLabel[value]}</button>
          ))}
        </div>
        <div className="grid grid-cols-2 gap-3">
          {frequency === 'Weekly' && (
            <Field label="Jour">
              <Select value={dayOfWeek} onChange={event => setDayOfWeek(event.target.value as Weekday)}>
                {weekdays.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </Select>
            </Field>
          )}
          {frequency === 'Monthly' && (
            <Field label="Jour du mois" hint="Du 1 au 28">
              <Select value={dayOfMonth} onChange={event => setDayOfMonth(event.target.value)}>
                {Array.from({length: 28}, (_, index) => String(index + 1)).map(value => <option key={value} value={value}>{value}</option>)}
              </Select>
            </Field>
          )}
          <Field label="Heure" hint={definition?.schedule ? `Fuseau ${definition.schedule.timeZone}` : 'Heure du restaurant'}>
            <Input type="time" required value={time} onChange={event => setTime(event.target.value)} />
          </Field>
        </div>
        <Field label="Destinataires" hint="Une adresse par ligne, 20 au plus.">
          <textarea rows={3} value={recipients} onChange={event => setRecipients(event.target.value)}
            className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none" />
        </Field>
        {invalid.length > 0 && <Alert>Adresse invalide : {invalid.join(', ')}</Alert>}
        {list.length > 20 && <Alert>20 destinataires au plus.</Alert>}
        <p className="text-sm text-ink-600">Chaque envoi couvre {covered[frequency]}. Les destinataires reçoivent un e-mail avec un lien de téléchargement du fichier CSV.</p>
      </form>
    </Modal>
  );
}

const moneyMetrics = new Set(['sales.placed_amount', 'sales.discount_amount', 'sales.tax_amount', 'sales.revenue', 'sales.cancelled_amount', 'sales.average_ticket', 'payments.captured_amount', 'payments.refunded_amount', 'promotions.discount_amount', 'loyalty.reward_discount_amount']);
const rateMetrics = new Set(['sales.cancellation_rate', 'payments.refund_rate', 'reservations.no_show_rate']);
const ratioMetrics = new Set([...rateMetrics, 'sales.average_ticket', 'kitchen.average_preparation_minutes']);

/** Un rapport enregistré, calculé à l'écran sur une période (un total par mesure). */
function RunDialog({definition, onClose}: {definition: ReportDefinition | null; onClose: () => void}) {
  const {can, currency} = useConsole();
  const {busy, run} = useAction();
  const [preset, setPreset] = useState('7d');
  const period = (presets.find(item => item.key === preset) ?? presets[0]).period();
  const result = useResource(
    signal => (definition ? runDefinition(definition.id, period, signal) : Promise.resolve(null)),
    [definition?.id, preset],
  );
  const value = (metric: string) => {
    const rows = (result.data ?? []).filter(row => row.metric === metric);
    if (rows.length === 0) return 0;
    return ratioMetrics.has(metric) ? (rows.find(row => row.dimension === '') ?? rows[0]).value : total(rows, metric);
  };
  const format = (metric: string, amount: number) =>
    moneyMetrics.has(metric) ? money(amount, currency) : rateMetrics.has(metric) ? `${Math.round(amount * 1000) / 10} %` : new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 1}).format(amount);

  return (
    <Modal wide open={definition !== null} onClose={onClose} title={definition?.name ?? ''} footer={can('reporting.export') && definition ? (
      <Button variant="secondary" busy={busy === 'csv'} onClick={() => run('csv', async () => {
        const id = await exportDefinition(definition.id, period);
        await downloadFile(`/reporting/exports/${id}/file`, `${definition.name}.csv`);
      }, 'Export téléchargé.')}><Download className="size-4" aria-hidden="true" />Télécharger (CSV)</Button>
    ) : undefined}>
      <div className="space-y-4">
        <Field label="Période" className="sm:max-w-xs">
          <Select value={preset} onChange={event => setPreset(event.target.value)}>
            {presets.map(item => <option key={item.key} value={item.key}>{item.label}</option>)}
          </Select>
        </Field>
        {result.error ? <Alert>{result.error}</Alert> : !result.data ? <Spinner /> : (
          <dl className="grid grid-cols-1 divide-y divide-slate-100 rounded-xl border border-slate-200 sm:grid-cols-2 sm:divide-y-0">
            {definition?.metrics.map(metric => (
              <div key={metric} className="flex items-baseline justify-between gap-3 px-4 py-2.5 sm:border-b sm:border-slate-100">
                <dt className="text-sm text-ink-600">{metricLabel[metric] ?? metric}</dt>
                <dd className="font-semibold whitespace-nowrap">{format(metric, value(metric))}</dd>
              </div>
            ))}
          </dl>
        )}
      </div>
    </Modal>
  );
}

const exportStatus: Record<string, {label: string; tone: 'neutral' | 'blue' | 'green' | 'red'}> = {
  Queued: {label: 'En file', tone: 'blue'}, Running: {label: 'En cours', tone: 'blue'}, Completed: {label: 'Prêt', tone: 'green'},
  Failed: {label: 'Échec', tone: 'red'}, Expired: {label: 'Expiré', tone: 'neutral'},
};

/** Les derniers exports CSV (à la demande ou envoyés par e-mail), re-téléchargeables tant qu'ils n'ont pas expiré. */
function Exports() {
  const {busy, run} = useAction();
  const [page, setPage] = useState(1);
  const {data, error} = useResource(signal => listExports(page, signal), [page], 30_000);
  const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short'}).format(new Date(iso));
  const at = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));
  return (
    <section className="space-y-3">
      <h2 className="text-lg font-semibold">Exports récents</h2>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.items.length === 0 ? (
        <p className="text-sm text-ink-500">Aucun export pour l’instant.</p>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {data.items.map(item => (
              <li key={item.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3 text-sm">
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{item.title ?? 'Rapport ponctuel'}</span>
                  <span className="block text-xs text-ink-500">
                    {item.from && item.to ? `${day(`${item.from}T00:00:00`)} – ${day(`${item.to}T00:00:00`)}` : ''}
                    {` · demandé le ${at(item.queuedAtUtc)}`}{item.scheduledDelivery ? ' · envoi automatique' : ''}
                  </span>
                  {item.failureReason && <span className="block text-xs text-red-700">{item.failureReason}</span>}
                </span>
                <Badge tone={exportStatus[item.status]?.tone ?? 'neutral'}>{exportStatus[item.status]?.label ?? item.status}</Badge>
                {item.downloadable && (
                  <Button size="sm" variant="ghost" aria-label="Télécharger" busy={busy === item.id} onClick={() => run(item.id, () => downloadFile(`/reporting/exports/${item.id}/file`, `rapport-${item.from ?? ''}-${item.to ?? ''}.csv`))}>
                    <Download className="size-4" />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
      {data && (page > 1 || data.hasMore) && (
        <div className="flex justify-end gap-2">
          <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récents</Button>
          <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(page + 1)}>Plus anciens</Button>
        </div>
      )}
    </section>
  );
}

export default function ScheduledReportsPage() {
  const {can, locations} = useConsole();
  const {data, error, reload} = useResource(signal => listDefinitions(signal), []);
  const [editing, setEditing] = useState<ReportDefinition | null | 'new'>(null);
  const [scheduling, setScheduling] = useState<ReportDefinition | null>(null);
  const [viewing, setViewing] = useState<ReportDefinition | null>(null);
  const manage = can('reporting.manage');

  if (!can('reporting.view')) {
    return <Empty icon={<CalendarClock className="size-8" />} title="Rapports non disponibles">Réservé aux membres qui ont accès aux rapports.</Empty>;
  }
  const definitions = (data ?? []).slice().sort((a, b) => Number(!!b.schedule) - Number(!!a.schedule) || a.name.localeCompare(b.name, 'fr'));

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-0 flex-1">
          <Link href="/rapports" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Rapports</Link>
          <h1 className="mt-2 text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Rapports planifiés</h1>
          <p className="mt-1 text-sm text-ink-500">Enregistrez un rapport et recevez-le automatiquement par e-mail.</p>
        </div>
        {manage && <Button onClick={() => setEditing('new')}><Plus className="size-4" aria-hidden="true" />Nouveau rapport</Button>}
      </div>

      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : definitions.length === 0 ? (
        <Card className="p-6">
          <Empty icon={<CalendarClock className="size-8" />} title="Aucun rapport enregistré">
            {manage ? 'Créez un rapport puis programmez son envoi : chaque jour, chaque semaine ou chaque mois.' : 'Le gérant n’a pas encore enregistré de rapport.'}
          </Empty>
        </Card>
      ) : (
        <div className="space-y-3">
          {definitions.map(definition => {
            const kind = definitionKind(definition);
            const locationId = definitionLocation(definition);
            const site = locationId ? locations.find(item => item.id === locationId)?.name ?? 'Établissement archivé' : 'Tous les établissements';
            const schedule = definition.schedule;
            return (
              <Card key={definition.id} className="p-4">
                <div className="flex flex-wrap items-start gap-3">
                  <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl', schedule ? 'bg-brand-50 text-brand-600' : 'bg-slate-100 text-ink-500')} aria-hidden="true"><CalendarClock className="size-5" /></span>
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 font-semibold">
                      {definition.name}
                      {schedule ? <Badge tone="green">Envoi automatique</Badge> : <Badge>Manuel</Badge>}
                    </p>
                    <p className="mt-0.5 text-sm text-ink-600">{kind ? kindLabel[kind] : `Personnalisé (${definition.metrics.length} mesures)`} · {site}</p>
                    {schedule && (
                      <div className="mt-2 space-y-0.5 text-xs text-ink-500">
                        <p>{rhythm(definition)} ({schedule.timeZone}) · couvre {covered[schedule.frequency]}</p>
                        <p>
                          {schedule.nextRunAtUtc ? `Prochain envoi ${when(schedule.nextRunAtUtc)}` : 'Prochain envoi en cours de calcul'}
                          {schedule.lastRunAtUtc ? ` · dernier envoi ${when(schedule.lastRunAtUtc)}` : ' · jamais envoyé'}
                        </p>
                        <p className="flex items-start gap-1"><Mail className="mt-0.5 size-3 shrink-0" aria-hidden="true" /><span className="break-all">{schedule.recipients.join(', ')}</span></p>
                      </div>
                    )}
                  </div>
                  <div className="flex w-full justify-end gap-1 sm:w-auto">
                    <Button size="sm" variant="ghost" onClick={() => setViewing(definition)}><Eye className="size-4" aria-hidden="true" />Voir</Button>
                    {manage && (
                      <>
                        <Button size="sm" variant="ghost" aria-label={`Modifier ${definition.name}`} title="Modifier" onClick={() => setEditing(definition)}><Pencil className="size-4" /></Button>
                        <Button size="sm" variant="secondary" onClick={() => setScheduling(definition)}><CalendarClock className="size-4" aria-hidden="true" />{schedule ? 'Programmation' : 'Programmer'}</Button>
                      </>
                    )}
                  </div>
                </div>
              </Card>
            );
          })}
        </div>
      )}

      <DefinitionDialog open={editing !== null} definition={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={reload} />
      <ScheduleDialog definition={scheduling} onClose={() => setScheduling(null)} onSaved={reload} />
      <RunDialog definition={viewing} onClose={() => setViewing(null)} />
      <Exports />
    </div>
  );
}
