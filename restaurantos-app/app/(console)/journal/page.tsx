'use client';
import {useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import {
  AlertTriangle, Bot, CalendarDays, ChevronLeft, ChevronRight, CreditCard, Download, KeyRound, MoreVertical, ScrollText, Search, ShieldCheck, Users,
} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  actionLabel, actionsOf, actorLabel, collectAudit, importanceLabel, importanceTone, metadataLabel, moduleOf, moduleTone, modules,
  searchAudit, summarizeAudit, targetLabel, verbOf, verbTone, type ActorType, type AuditEntry, type AuditFilter, type AuditSummary, type Importance,
} from '@/lib/audit';
import {listKeys} from '@/lib/integrations';
import {listMembers, listRoles, permissionLabel} from '@/lib/staff';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Delta, Empty, Field, Input, Modal, PageHeader, Select, Spinner, StatCard, cx, useAction, useToast} from '@/components/ui';

type Preset = '1' | '7' | '30' | '90' | 'custom';

const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit'}).format(new Date(iso));
const rowStamp = (iso: string) => {
  const date = new Date(iso);
  const sameYear = date.getFullYear() === new Date().getFullYear();
  return new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', ...(sameYear ? {} : {year: 'numeric'}), hour: '2-digit', minute: '2-digit', second: '2-digit'}).format(date);
};
const shortDay = (date: Date) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(date);
const isoDay = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

/** La période choisie, jours locaux : début inclus, fin exclue (lendemain du dernier jour). */
function periodOf(preset: Preset, custom: {from: string; to: string}): {from: Date; to: Date} {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const tomorrow = new Date(today);
  tomorrow.setDate(tomorrow.getDate() + 1);
  if (preset === 'custom' && custom.from && custom.to) {
    const from = new Date(`${custom.from}T00:00:00`);
    const to = new Date(`${custom.to}T00:00:00`);
    to.setDate(to.getDate() + 1);
    return to > from ? {from, to} : {from, to: new Date(from.getTime() + 86_400_000)};
  }
  const from = new Date(today);
  from.setDate(from.getDate() - (Number(preset === 'custom' ? '30' : preset) - 1));
  return {from, to: tomorrow};
}

/** « julien.dupont@… » devient « Julien Dupont » : le journal ne connaît que l'adresse des membres. */
const nameFromEmail = (email: string) =>
  email.split('@')[0].split(/[._-]+/).filter(Boolean).map(part => part.charAt(0).toUpperCase() + part.slice(1)).join(' ') || email;

const roleName: Record<string, string> = {Owner: 'Propriétaire', Manager: 'Gérant'};

interface Person {
  name: string;
  role: string | null;
}

const avatarTones = ['bg-violet-100 text-violet-700', 'bg-sky-100 text-sky-700', 'bg-amber-100 text-amber-700', 'bg-emerald-100 text-emerald-700', 'bg-rose-100 text-rose-700'];
const toneOf = (key: string) => avatarTones[[...key].reduce((sum, char) => sum + char.charCodeAt(0), 0) % avatarTones.length];

function Author({entry, people, keys}: {entry: AuditEntry; people: Map<string, Person>; keys: Map<string, string>}) {
  if (!entry.actorType) return <span className="text-ink-400">—</span>;
  if (entry.actorType !== 'User') {
    const Icon = entry.actorType === 'ApiCredential' ? KeyRound : Bot;
    const name = entry.actorType === 'ApiCredential' && entry.actorId ? keys.get(entry.actorId) : null;
    return (
      <span className="flex min-w-0 items-center gap-2">
        <span className="grid size-8 shrink-0 place-items-center rounded-full bg-slate-100 text-ink-500" aria-hidden="true"><Icon className="size-4" /></span>
        <span className="min-w-0"><span className="block truncate font-medium">{name ?? actorLabel[entry.actorType]}</span>{name && <span className="block text-xs text-ink-500">Clé d’API</span>}</span>
      </span>
    );
  }
  const person = entry.actorId ? people.get(entry.actorId) : undefined;
  const name = person?.name ?? 'Membre de l’équipe';
  const initials = name.split(' ').map(part => part[0]).join('').slice(0, 2).toUpperCase();
  return (
    <span className="flex min-w-0 items-center gap-2">
      <span className={cx('grid size-8 shrink-0 place-items-center rounded-full text-xs font-semibold', toneOf(entry.actorId ?? name))} aria-hidden="true">{initials}</span>
      <span className="min-w-0">
        <span className="block truncate font-medium">{name}</span>
        {person?.role && <span className="block truncate text-xs text-ink-500">{person.role}</span>}
      </span>
    </span>
  );
}

function Sparkline({values, color}: {values: number[]; color: string}) {
  if (values.length < 2) return null;
  const max = Math.max(1, ...values);
  const points = values.map((value, index) => `${(index / (values.length - 1)) * 100},${28 - (value / max) * 26}`).join(' ');
  return (
    <svg viewBox="0 0 100 30" preserveAspectRatio="none" className="h-8 w-20 shrink-0" aria-hidden="true">
      <polyline points={points} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" strokeLinejoin="round" />
    </svg>
  );
}

function Kpi({icon, color, label, value, previous, series, active, onClick}: {
  icon: ReactNode; tone?: string; color: string; label: string; value: number; previous: number | null; series: number[]; active: boolean; onClick?: () => void;
}) {
  return (
    <StatCard icon={icon} label={label} value={new Intl.NumberFormat('fr-FR').format(value)} active={active} onClick={onClick}
      delta={previous ? <Delta value={(value - previous) / previous} better="neutral" /> : <span title="Rien sur la période précédente">Nouveau</span>}
      chart={<Sparkline values={series} color={color} />} />
  );
}

function RowMenu({entry, onFilterActor, onFilterTarget}: {entry: AuditEntry; onFilterActor: () => void; onFilterTarget: () => void}) {
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const item = 'block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50';
  return (
    <div ref={ref} className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label="Actions sur l’écriture" onClick={() => setOpen(value => !value)}
        className="grid size-8 place-items-center rounded-lg border border-slate-200 text-ink-600 hover:bg-slate-50"><MoreVertical className="size-4" /></button>
      {open && (
        <div role="menu" className="absolute top-9 right-0 z-20 w-60 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          {entry.actorType && entry.actorId && (
            <button type="button" role="menuitem" className={item} onClick={() => {
              setOpen(false);
              onFilterActor();
            }}>Toute l’activité de cet auteur</button>
          )}
          <button type="button" role="menuitem" className={item} onClick={() => {
            setOpen(false);
            onFilterTarget();
          }}>Tout l’historique de cet élément</button>
          <button type="button" role="menuitem" className={item} onClick={async () => {
            setOpen(false);
            await navigator.clipboard.writeText(entry.correlationId).catch(() => undefined);
            toast('success', 'Référence copiée.');
          }}>Copier la référence</button>
        </div>
      )}
    </div>
  );
}

const csvCell = (value: string) => (/[";\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value);

export default function JournalPage() {
  const {can, locations} = useConsole();
  const allowed = can('audit.view');
  const {busy, run} = useAction();
  const [preset, setPreset] = useState<Preset>('30');
  const [custom, setCustom] = useState({from: '', to: ''});
  const [picking, setPicking] = useState(false);
  const [module, setModule] = useState('');
  const [action, setAction] = useState('');
  const [author, setAuthor] = useState('');
  const [importance, setImportance] = useState<Importance | ''>('');
  const [target, setTarget] = useState<{type: string; id: string} | null>(null);
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [expanded, setExpanded] = useState<string | null>(null);

  const staff = useResource(
    async () => (allowed && can('staff.manage') ? Promise.all([listMembers(), listRoles()]) : null),
    [allowed],
  );
  const keysList = useResource(() => (allowed && can('integrations.view') ? listKeys() : Promise.resolve(null)), [allowed]);
  const people = useMemo(() => {
    const map = new Map<string, Person>();
    if (!staff.data) return map;
    const [members, roles] = staff.data;
    const roleOf = (id: string | null) => {
      const role = roles.find(item => item.id === id);
      return role ? roleName[role.name] ?? role.name : null;
    };
    for (const member of members) {
      if (!member.email) continue;
      map.set(member.userId, {name: nameFromEmail(member.email), role: roleOf(member.tenantWideRoleId) ?? roleOf(member.assignments[0]?.roleId ?? null)});
    }
    return map;
  }, [staff.data]);
  const keys = useMemo(() => new Map((keysList.data ?? []).map(key => [key.id, key.name])), [keysList.data]);

  const period = useMemo(() => periodOf(preset, custom), [preset, custom]);
  const previous = useMemo(() => ({from: new Date(period.from.getTime() - (period.to.getTime() - period.from.getTime())), to: period.from}), [period]);
  const filter: AuditFilter = useMemo(() => {
    const actor: Pick<AuditFilter, 'actorType' | 'actorId'> = !author ? {} : author.includes(':') ? {actorType: author.split(':')[0] as ActorType, actorId: author.slice(author.indexOf(':') + 1)} : {};
    return {
      module: module || undefined,
      action: action || undefined,
      importance: importance || undefined,
      targetType: target?.type,
      targetId: target?.id,
      ...actor,
      from: period.from.toISOString(),
      to: period.to.toISOString(),
    };
  }, [module, action, importance, target, author, period]);
  const key = JSON.stringify(filter);
  useEffect(() => setPage(1), [key, pageSize]);

  const list = useResource(signal => (allowed ? searchAudit(filter, page, pageSize, signal) : Promise.resolve(null)), [allowed, key, page, pageSize]);
  const base = useMemo(() => ({from: period.from.toISOString(), to: period.to.toISOString()}), [period]);
  const kpis = useResource(
    async signal => {
      if (!allowed) return null;
      const [current, before, filtered] = await Promise.all([
        summarizeAudit(base, signal),
        summarizeAudit({from: previous.from.toISOString(), to: previous.to.toISOString()}, signal).catch(() => null),
        summarizeAudit(filter, signal),
      ]);
      return {current, before, filtered};
    },
    [allowed, key],
    60_000,
  );

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Journal d’activité" />
        <Alert tone="blue">Vous n’avez pas accès au journal (permission « Voir le journal »). Demandez-la au propriétaire.</Alert>
      </div>
    );
  }

  const readable = (name: string, value: string) => {
    if (name === 'location_id') return locations.find(location => location.id === value)?.name ?? value;
    if (name === 'user_id') return people.get(value)?.name ?? value;
    if (name === 'role_id') return staff.data?.[1].find(role => role.id === value)?.name ?? value;
    if (name === 'permission') return permissionLabel[value] ?? value;
    if (name === 'scopes' || name === 'permissions') return value.split(',').map(item => permissionLabel[item.trim()] ?? item.trim()).join(', ');
    if (value === 'true') return 'Oui';
    if (value === 'false') return 'Non';
    return value;
  };
  const detailOf = (entry: AuditEntry) =>
    Object.entries(entry.metadata)
      .filter(([name]) => !/_at_utc$|^previous_/.test(name))
      .slice(0, 2)
      .map(([name, value]) => `${metadataLabel(name)} : ${readable(name, value)}`)
      .join(' · ');

  const needle = query.trim().toLocaleLowerCase('fr');
  const items = (list.data?.items ?? []).filter(entry => {
    if (!needle) return true;
    const person = entry.actorId ? people.get(entry.actorId)?.name ?? '' : '';
    return `${actionLabel(entry.action)} ${detailOf(entry)} ${person} ${modules[moduleOf(entry.action)] ?? ''}`.toLocaleLowerCase('fr').includes(needle);
  });
  const total = kpis.data?.filtered.total ?? null;
  const pages = total !== null ? Math.max(1, Math.ceil(total / pageSize)) : null;
  const series = (summary: AuditSummary | undefined, pick: (day: AuditSummary['days'][number]) => number) => (summary?.days ?? []).map(pick);
  const current = kpis.data?.current;
  const before = kpis.data?.before ?? null;
  const moduleCount = (summary: AuditSummary | null | undefined, name: string) => summary?.modules[name] ?? 0;

  const pageButtons = (() => {
    if (!pages) return [];
    const wanted = new Set([1, pages, page - 1, page, page + 1].filter(value => value >= 1 && value <= pages));
    const sorted = [...wanted].sort((a, b) => a - b);
    const result: (number | 'gap')[] = [];
    sorted.forEach((value, index) => {
      if (index > 0 && value - sorted[index - 1] > 1) result.push('gap');
      result.push(value);
    });
    return result;
  })();

  const exportCsv = () => run('export', async () => {
    const rows = await collectAudit(filter);
    const header = ['Date', 'Domaine', 'Type', 'Action', 'Détail', 'Auteur', 'Élément', 'Priorité', 'Référence'];
    const lines = rows.map(entry => [
      new Date(entry.occurredAtUtc).toLocaleString('fr-FR'),
      modules[moduleOf(entry.action)] ?? moduleOf(entry.action),
      verbOf(entry.action),
      actionLabel(entry.action),
      Object.entries(entry.metadata).map(([name, value]) => `${metadataLabel(name)} : ${readable(name, value)}`).join(' | '),
      entry.actorType === 'User' && entry.actorId ? people.get(entry.actorId)?.name ?? 'Membre de l’équipe' : entry.actorType ? actorLabel[entry.actorType] : '',
      `${targetLabel[entry.targetType] ?? entry.targetType} ${entry.targetId}`,
      importanceLabel[entry.importance],
      entry.correlationId,
    ].map(csvCell).join(';'));
    const blob = new Blob([`﻿${[header.join(';'), ...lines].join('\n')}`], {type: 'text/csv;charset=utf-8'});
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `journal-${isoDay(period.from)}-${isoDay(new Date(period.to.getTime() - 1))}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  }, 'Journal exporté.');

  const periodText = `${shortDay(period.from)} – ${shortDay(new Date(period.to.getTime() - 1))}`;
  const resetFilters = () => {
    setModule('');
    setAction('');
    setAuthor('');
    setImportance('');
    setTarget(null);
    setQuery('');
  };
  const filtered = !!(module || action || author || importance || target || query);

  return (
    <div className="space-y-5">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Journal d’activité</h1>
          <p className="mt-1 text-sm text-ink-500">Qui a fait quoi, et quand : les actions sensibles du restaurant, enregistrées sans modification possible.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <div className="relative">
            <CalendarDays className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
            <Select aria-label="Période" className="w-full! pl-9 sm:w-auto!" value={preset} onChange={event => {
              const value = event.target.value as Preset;
              if (value === 'custom') {
                setCustom({from: isoDay(period.from), to: isoDay(new Date(period.to.getTime() - 1))});
                setPicking(true);
              }
              setPreset(value);
            }}>
              <option value="1">Aujourd’hui</option>
              <option value="7">7 derniers jours</option>
              <option value="30">30 derniers jours</option>
              <option value="90">3 derniers mois</option>
              <option value="custom">{preset === 'custom' ? periodText : 'Personnalisée…'}</option>
            </Select>
          </div>
          <Button variant="secondary" busy={busy === 'export'} onClick={exportCsv}><Download className="size-4" aria-hidden="true" />Exporter</Button>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
        <Kpi icon={<ScrollText className="size-5" />} tone="bg-brand-50 text-brand-600" color="#16a34a" label="Activités" value={current?.total ?? 0} previous={before?.total ?? null}
          series={series(current, day => day.total)} active={!filtered} onClick={resetFilters} />
        <Kpi icon={<Users className="size-5" />} tone="bg-orange-50 text-orange-600" color="#ea580c" label="Équipe et rôles" value={moduleCount(current, 'staff')} previous={before ? moduleCount(before, 'staff') : null}
          series={series(current, day => day.modules.staff ?? 0)} active={module === 'staff'} onClick={() => setModule(module === 'staff' ? '' : 'staff')} />
        <Kpi icon={<ShieldCheck className="size-5" />} tone="bg-sky-50 text-sky-600" color="#0284c7" label="Comptes et connexions" value={moduleCount(current, 'identity')} previous={before ? moduleCount(before, 'identity') : null}
          series={series(current, day => day.modules.identity ?? 0)} active={module === 'identity'} onClick={() => setModule(module === 'identity' ? '' : 'identity')} />
        <Kpi icon={<CreditCard className="size-5" />} tone="bg-emerald-50 text-emerald-600" color="#059669" label="Paiements" value={moduleCount(current, 'payments')} previous={before ? moduleCount(before, 'payments') : null}
          series={series(current, day => day.modules.payments ?? 0)} active={module === 'payments'} onClick={() => setModule(module === 'payments' ? '' : 'payments')} />
        <Kpi icon={<AlertTriangle className="size-5" />} tone="bg-red-50 text-red-600" color="#dc2626" label="Événements importants" value={current?.important ?? 0} previous={before?.important ?? null}
          series={series(current, day => day.important)} active={importance === 'High'} onClick={() => setImportance(importance === 'High' ? '' : 'High')} />
      </div>

      <Card className="p-3">
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-[repeat(4,minmax(0,1fr))_minmax(0,1.4fr)]">
          <Select aria-label="Domaine" value={module} onChange={event => {
            setModule(event.target.value);
            setAction('');
          }}>
            <option value="">Tous les domaines</option>
            {Object.entries(modules).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </Select>
          <Select aria-label="Auteur" value={author} onChange={event => setAuthor(event.target.value)}>
            <option value="">Tous les auteurs</option>
            {people.size > 0 && (
              <optgroup label="Équipe">
                {[...people.entries()].sort((a, b) => a[1].name.localeCompare(b[1].name, 'fr')).map(([id, person]) => <option key={id} value={`User:${id}`}>{person.name}</option>)}
              </optgroup>
            )}
            {keys.size > 0 && (
              <optgroup label="Clés d’API">
                {[...keys.entries()].map(([id, name]) => <option key={id} value={`ApiCredential:${id}`}>{name}</option>)}
              </optgroup>
            )}
          </Select>
          <Select aria-label="Action" value={action} onChange={event => setAction(event.target.value)}>
            <option value="">Toutes les actions</option>
            {actionsOf(module).map(([code, label]) => <option key={code} value={code}>{label}</option>)}
          </Select>
          <Select aria-label="Priorité" value={importance} onChange={event => setImportance(event.target.value as Importance | '')}>
            <option value="">Toutes les priorités</option>
            {(Object.keys(importanceLabel) as Importance[]).map(value => <option key={value} value={value}>{importanceLabel[value]}</option>)}
          </Select>
          <label className="relative block">
            <span className="sr-only">Rechercher dans cette page</span>
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
            <Input value={query} onChange={event => setQuery(event.target.value)} placeholder="Rechercher dans cette page…" className="pl-9" />
          </label>
        </div>
        {(target || filtered) && (
          <div className="mt-2 flex flex-wrap items-center gap-2 text-sm">
            {target && <Badge tone="blue">Élément : {targetLabel[target.type] ?? target.type} {target.id.slice(0, 8)}</Badge>}
            <button type="button" onClick={resetFilters} className="font-semibold text-brand-700 hover:underline">Effacer les filtres</button>
          </div>
        )}
      </Card>

      {list.error ? <Alert>{list.error}</Alert> : !list.data ? <Spinner /> : items.length === 0 ? (
        <Card className="p-6"><Empty icon={<ScrollText className="size-8" />} title="Aucune activité">Rien n’a été enregistré avec ces filtres sur la période.</Empty></Card>
      ) : (
        <Card className="overflow-hidden">
          <div className="hidden grid-cols-[2rem_8.5rem_7.5rem_7.5rem_minmax(0,1fr)_11rem_5.5rem_2.5rem] items-center gap-3 border-b border-slate-100 px-4 py-2.5 text-xs font-medium text-ink-500 xl:grid">
            <span /><span>Date et heure</span><span>Domaine</span><span>Type</span><span>Description</span><span>Auteur</span><span>Priorité</span><span />
          </div>
          <ul className="divide-y divide-slate-100">
            {items.map(entry => {
              const module = moduleOf(entry.action);
              const verb = verbOf(entry.action);
              const open = expanded === entry.id;
              const detail = detailOf(entry);
              return (
                <li key={entry.id} className={cx(open && 'bg-slate-50/60')}>
                  <div className="grid grid-cols-[2rem_minmax(0,1fr)_2.5rem] items-start gap-x-3 gap-y-1 px-4 py-3 text-sm xl:grid-cols-[2rem_8.5rem_7.5rem_7.5rem_minmax(0,1fr)_11rem_5.5rem_2.5rem] xl:items-center">
                    <button type="button" aria-expanded={open} aria-label={open ? 'Masquer le détail' : 'Voir le détail'} onClick={() => setExpanded(open ? null : entry.id)}
                      className="grid size-7 place-items-center rounded-lg text-ink-500 hover:bg-slate-100">
                      <ChevronRight className={cx('size-4 transition', open && 'rotate-90')} />
                    </button>
                    <div className="min-w-0 space-y-1.5 xl:contents">
                      <span className="block text-xs text-ink-500 xl:text-sm xl:text-ink-700" title={stamp(entry.occurredAtUtc)}>{rowStamp(entry.occurredAtUtc)}</span>
                      <span className="flex flex-wrap items-center gap-1.5 xl:contents">
                        <span><span className={cx('inline-flex rounded-lg px-2 py-0.5 text-xs font-semibold', moduleTone[module] ?? 'bg-slate-100 text-ink-600')}>{modules[module] ?? module}</span></span>
                        <span><Badge tone={verbTone(verb)}>{verb}</Badge></span>
                      </span>
                      <span className="block min-w-0">
                        <span className="block font-medium">{actionLabel(entry.action)}</span>
                        {detail && <span className="block truncate text-xs text-ink-500">{detail}</span>}
                      </span>
                      <span className="block min-w-0"><Author entry={entry} people={people} keys={keys} /></span>
                      <span><Badge tone={importanceTone[entry.importance]}>{importanceLabel[entry.importance]}</Badge></span>
                    </div>
                    <RowMenu entry={entry}
                      onFilterActor={() => setAuthor(`${entry.actorType}:${entry.actorId}`)}
                      onFilterTarget={() => setTarget({type: entry.targetType, id: entry.targetId})} />
                  </div>
                  {open && (
                    <dl className="mx-4 mb-3 grid grid-cols-[9rem_minmax(0,1fr)] gap-x-4 gap-y-1.5 rounded-xl border border-slate-200 bg-white p-4 text-sm xl:ml-14">
                      <dt className="text-ink-500">Élément</dt><dd className="break-all">{targetLabel[entry.targetType] ?? entry.targetType} · <code className="text-xs">{entry.targetId}</code></dd>
                      {Object.entries(entry.metadata).map(([name, value]) => (
                        <div key={name} className="contents"><dt className="text-ink-500">{metadataLabel(name)}</dt><dd className="break-all">{readable(name, value)}</dd></div>
                      ))}
                      <dt className="text-ink-500">Enregistré</dt><dd>{stamp(entry.recordedAtUtc)}</dd>
                      <dt className="text-ink-500">Référence</dt><dd className="break-all"><code className="text-xs">{entry.correlationId}</code></dd>
                    </dl>
                  )}
                </li>
              );
            })}
          </ul>
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-3 text-sm text-ink-500">
            <div className="flex flex-wrap items-center gap-1">
              <Button size="sm" variant="secondary" aria-label="Page précédente" disabled={page === 1} onClick={() => setPage(page - 1)}><ChevronLeft className="size-4" /></Button>
              {pages ? pageButtons.map((value, index) => value === 'gap'
                ? <span key={`gap-${index}`} className="px-1">…</span>
                : <button key={value} type="button" aria-current={value === page ? 'page' : undefined} onClick={() => setPage(value)}
                  className={cx('min-w-8 rounded-lg px-2 py-1 font-semibold', value === page ? 'bg-brand-600 text-white' : 'text-ink-700 hover:bg-slate-100')}>{value}</button>)
                : <span className="px-2">Page {page}</span>}
              <Button size="sm" variant="secondary" aria-label="Page suivante" disabled={!list.data.hasMore} onClick={() => setPage(page + 1)}><ChevronRight className="size-4" /></Button>
              {total !== null && <span className="ml-2">{new Intl.NumberFormat('fr-FR').format(total)} écriture{total > 1 ? 's' : ''}</span>}
            </div>
            <label className="flex items-center gap-2">
              <span>Par page</span>
              <Select aria-label="Écritures par page" className="h-9 w-20! py-0" value={pageSize} onChange={event => setPageSize(Number(event.target.value))}>
                {[12, 25, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}
              </Select>
            </label>
          </div>
          {needle && <p className="border-t border-slate-100 px-4 py-2 text-xs text-ink-500">La recherche porte sur la page affichée : utilisez les filtres pour chercher sur toute la période.</p>}
        </Card>
      )}
      {kpis.data?.current.partial && <p className="text-xs text-ink-500">Période très chargée : les courbes portent sur les 50 000 écritures les plus récentes, les totaux restent exacts.</p>}

      <Modal open={picking} onClose={() => {
        setPicking(false);
        if (!custom.from || !custom.to) setPreset('30');
      }} title="Période personnalisée" footer={<Button onClick={() => setPicking(false)} disabled={!custom.from || !custom.to || custom.to < custom.from}>Appliquer</Button>}>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Du"><Input type="date" value={custom.from} max={custom.to || undefined} onChange={event => setCustom(current => ({...current, from: event.target.value}))} /></Field>
          <Field label="Au"><Input type="date" value={custom.to} min={custom.from || undefined} onChange={event => setCustom(current => ({...current, to: event.target.value}))} /></Field>
        </div>
        <p className="mt-3 text-xs text-ink-500">366 jours au plus.</p>
      </Modal>
    </div>
  );
}
