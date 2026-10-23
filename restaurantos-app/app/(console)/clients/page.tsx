'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {ChevronLeft, ChevronRight, Contact, FileUp, Mail, Phone, Plus, Search, Settings2, X} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  channels, createCustomer, day, initials, languages, loadContactPolicy, requirementLabel, searchCustomers, searchQuery, setContactPolicy,
  type Channel, type ContactRequirement,
} from '@/lib/customers';
import {ImportDialog} from './import';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, PageHeader, Select, Spinner, cx, useAction} from '@/components/ui';

function CreateDialog({open, requirement, onClose}: {open: boolean; requirement: ContactRequirement | undefined; onClose: () => void}) {
  const router = useRouter();
  const {busy, run} = useAction();
  const [form, setForm] = useState({name: '', email: '', phone: '', tags: '', language: 'fr'});
  const [wanted, setWanted] = useState<Channel[]>([]);
  useEffect(() => {
    if (!open) return;
    setForm({name: '', email: '', phone: '', tags: '', language: 'fr'});
    setWanted([]);
  }, [open]);
  const set = (key: keyof typeof form) => (event: {target: {value: string}}) => setForm(current => ({...current, [key]: event.target.value}));
  const missing = requirement === 'Email' ? !form.email.trim()
    : requirement === 'Phone' ? !form.phone.trim()
    : requirement === 'EmailOrPhone' ? !form.email.trim() && !form.phone.trim() : false;

  return (
    <Modal open={open} onClose={onClose} title="Nouveau client" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="customer-form" busy={busy === 'create'} disabled={!form.name.trim() || missing}>Créer la fiche</Button>
      </>
    }>
      <form id="customer-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('create', async () => {
          const {id} = await createCustomer({
            name: form.name.trim(),
            email: form.email.trim() || null,
            phone: form.phone.trim() || null,
            tags: form.tags.split(',').map(tag => tag.trim()).filter(Boolean),
            preferences: {language: form.language, marketingChannels: wanted},
          });
          onClose();
          router.push(`/clients/${id}`);
        }, 'Fiche client créée.');
      }}>
        <Field label="Nom"><Input required maxLength={150} autoComplete="off" value={form.name} onChange={set('name')} placeholder="Awa Houngbédji" /></Field>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Email"><Input type="email" autoComplete="off" value={form.email} onChange={set('email')} placeholder="client@exemple.com" /></Field>
          <Field label="Téléphone" hint="Format international."><Input type="tel" autoComplete="off" value={form.phone} onChange={set('phone')} placeholder="+229 01 97 00 00 00" /></Field>
        </div>
        {requirement && requirement !== 'None' && <p className={cx('text-xs', missing ? 'text-amber-700' : 'text-ink-500')}>Exigé par votre réglage : {requirementLabel[requirement].toLowerCase()}.</p>}
        <Field label="Tags (facultatif)" hint="Séparés par des virgules : VIP, Allergie arachide, Entreprise…">
          <Input value={form.tags} onChange={set('tags')} placeholder="VIP, Habitué" />
        </Field>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Langue">
            <Select value={form.language} onChange={set('language')}>
              {languages.map(language => <option key={language.value} value={language.value}>{language.label}</option>)}
            </Select>
          </Field>
          <fieldset>
            <legend className="mb-1.5 text-sm font-medium">Canaux souhaités</legend>
            <div className="flex flex-wrap gap-x-4 gap-y-1">
              {channels.map(channel => (
                <label key={channel.value} className="flex items-center gap-2 text-sm">
                  <input type="checkbox" className="size-4 accent-brand-600" checked={wanted.includes(channel.value)}
                    onChange={event => setWanted(current => event.target.checked ? [...current, channel.value] : current.filter(item => item !== channel.value))} />
                  {channel.label}
                </label>
              ))}
            </div>
          </fieldset>
        </div>
        <p className="text-xs text-ink-500">Un canal souhaité n’autorise aucun envoi marketing : enregistrez le consentement sur la fiche.</p>
      </form>
    </Modal>
  );
}

function PolicyDialog({open, current, onClose, onDone}: {open: boolean; current: ContactRequirement | undefined; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [value, setValue] = useState<ContactRequirement>('EmailOrPhone');
  useEffect(() => {
    if (open && current) setValue(current);
  }, [open, current]);
  return (
    <Modal open={open} onClose={onClose} title="Coordonnées exigées" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'policy'} disabled={value === current} onClick={() => run('policy', async () => {
          await setContactPolicy(value);
          await onDone();
          onClose();
        }, 'Réglage enregistré.')}>Enregistrer</Button>
      </>
    }>
      <p className="mb-4 text-sm text-ink-600">S’applique aux nouvelles fiches. Une fiche existante garde sa règle jusqu’à ce que vous lui appliquiez la nouvelle.</p>
      <div className="space-y-2" role="radiogroup" aria-label="Coordonnées exigées">
        {(Object.keys(requirementLabel) as ContactRequirement[]).map(item => (
          <label key={item} className={cx('flex cursor-pointer items-center gap-3 rounded-xl border p-3 text-sm', value === item ? 'border-brand-500 bg-brand-50 ring-1 ring-brand-500' : 'border-slate-200 hover:border-slate-300')}>
            <input type="radio" name="requirement" className="size-4 accent-brand-600" checked={value === item} onChange={() => setValue(item)} />
            {requirementLabel[item]}
          </label>
        ))}
      </div>
    </Modal>
  );
}

export default function CustomersPage() {
  const {can} = useConsole();
  const allowed = can('customers.view');
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  const [tag, setTag] = useState('');
  const [anonymized, setAnonymized] = useState(false);
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [importing, setImporting] = useState(false);
  const [configuring, setConfiguring] = useState(false);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(text);
      setPage(1);
    }, 350);
    return () => window.clearTimeout(timer);
  }, [text]);

  const {data, error, loading, reload} = useResource(
    signal => (allowed ? searchCustomers(searchQuery(search, tag, anonymized, page), signal) : Promise.resolve(null)),
    [allowed, search, tag, anonymized, page],
  );
  const policy = useResource(signal => (allowed ? loadContactPolicy(signal) : Promise.resolve(null)), [allowed]);
  const requirement = policy.data?.requirement;
  const filtered = Boolean(search.trim() || tag || anonymized);

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Clients" />
        <Alert tone="blue">Vous n’avez pas accès aux fiches clients (permission « Voir les clients »). Demandez-la à un gérant.</Alert>
      </div>
    );
  }

  const filterTag = (value: string) => {
    setTag(value);
    setPage(1);
  };

  return (
    <div>
      <PageHeader title="Clients" description="Vos clients, leurs coordonnées, leurs consentements et leur historique." actions={
        <>
          {can('customers.configure') && <Button variant="secondary" onClick={() => setConfiguring(true)}><Settings2 className="size-4" aria-hidden="true" />Coordonnées exigées</Button>}
          {can('customers.manage') && <Button variant="secondary" onClick={() => setImporting(true)}><FileUp className="size-4" aria-hidden="true" />Importer</Button>}
          {can('customers.manage') && <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouveau client</Button>}
        </>
      } />

      <Card className="mb-4 flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
          <Input type="search" aria-label="Rechercher un client" className="pl-9" value={text} onChange={event => setText(event.target.value)}
            placeholder="Nom, email exact ou téléphone (+229…)" />
        </div>
        {tag && (
          <button type="button" onClick={() => filterTag('')} className="inline-flex items-center gap-1.5 self-start rounded-full bg-brand-50 px-3 py-1.5 text-sm font-medium text-brand-700 sm:self-auto">
            Tag : {tag}<X className="size-3.5" aria-label="Retirer le filtre" />
          </button>
        )}
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" className="size-4 accent-brand-600" checked={anonymized} onChange={event => {
            setAnonymized(event.target.checked);
            setPage(1);
          }} />
          Inclure les anonymisés
        </label>
      </Card>

      {error ? <Alert>{error}</Alert> : loading || !data ? <Spinner label="Recherche des clients…" /> : data.items.length === 0 ? (
        <Card className="p-6">
          <Empty icon={<Contact className="size-8" />} title={filtered ? 'Aucun client ne correspond' : 'Aucun client pour l’instant'}>
            {filtered ? 'Le nom se cherche par morceau ; email et téléphone doivent être exacts.' : 'Créez une fiche pour suivre un habitué, ses préférences et son accord marketing.'}
          </Empty>
        </Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100 sm:hidden">
            {data.items.map(customer => (
              <li key={customer.id}>
                <Link href={`/clients/${customer.id}`} className="flex items-center gap-3 px-4 py-3 hover:bg-slate-50">
                  <span className={cx('grid size-10 shrink-0 place-items-center rounded-full text-xs font-semibold', customer.isAnonymized ? 'bg-slate-100 text-ink-400' : 'bg-brand-50 text-brand-700')} aria-hidden="true">
                    {customer.isAnonymized ? '—' : initials(customer.name)}
                  </span>
                  <span className="min-w-0 flex-1 text-sm">
                    <span className="block truncate font-medium">{customer.name}</span>
                    <span className="block truncate text-ink-500">{customer.phone ?? customer.email ?? 'Sans coordonnées'}</span>
                  </span>
                  {customer.isAnonymized ? <Badge>Anonymisé</Badge> : customer.tags[0] && <Badge tone="brand">{customer.tags[0]}{customer.tags.length > 1 ? ` +${customer.tags.length - 1}` : ''}</Badge>}
                </Link>
              </li>
            ))}
          </ul>
          <div className="relative hidden overflow-x-auto sm:block">
            <table className="w-full min-w-[40rem] text-sm">
              <thead className="bg-slate-50 text-left text-xs font-semibold tracking-wide text-ink-500 uppercase">
                <tr>
                  <th scope="col" className="px-4 py-3">Client</th>
                  <th scope="col" className="px-4 py-3">Coordonnées</th>
                  <th scope="col" className="px-4 py-3">Tags</th>
                  <th scope="col" className="px-4 py-3">Créé le</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.items.map(customer => (
                  <tr key={customer.id} className="hover:bg-slate-50">
                    <td className="px-4 py-3">
                      <Link href={`/clients/${customer.id}`} className="flex items-center gap-3 font-medium hover:text-brand-700">
                        <span className={cx('grid size-9 shrink-0 place-items-center rounded-full text-xs font-semibold', customer.isAnonymized ? 'bg-slate-100 text-ink-400' : 'bg-brand-50 text-brand-700')} aria-hidden="true">
                          {customer.isAnonymized ? '—' : initials(customer.name)}
                        </span>
                        <span className="min-w-0">
                          <span className="block truncate">{customer.name}</span>
                          {customer.isAnonymized && <Badge>Anonymisé</Badge>}
                        </span>
                      </Link>
                    </td>
                    <td className="px-4 py-3 text-ink-600">
                      {customer.email && <span className="flex items-center gap-1.5"><Mail className="size-3.5 shrink-0" aria-hidden="true" />{customer.email}</span>}
                      {customer.phone && <span className="flex items-center gap-1.5"><Phone className="size-3.5 shrink-0" aria-hidden="true" />{customer.phone}</span>}
                      {!customer.email && !customer.phone && <span className="text-ink-400">—</span>}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-1">
                        {customer.tags.map(item => (
                          <button key={item} type="button" onClick={() => filterTag(item)} title="Filtrer sur ce tag"
                            className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-ink-600 hover:bg-brand-50 hover:text-brand-700">{item}</button>
                        ))}
                      </div>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-ink-500">{day(customer.createdAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="flex items-center justify-between gap-2 border-t border-slate-100 px-4 py-3 text-sm text-ink-500">
            <span>Page {page}</span>
            <div className="flex gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}><ChevronLeft className="size-4" aria-hidden="true" />Précédente</Button>
              <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(page + 1)}>Suivante<ChevronRight className="size-4" aria-hidden="true" /></Button>
            </div>
          </div>
        </Card>
      )}

      <ImportDialog open={importing} onClose={() => setImporting(false)} onDone={reload} />
      <CreateDialog open={creating} requirement={requirement} onClose={() => setCreating(false)} />
      <PolicyDialog open={configuring} current={requirement} onClose={() => setConfiguring(false)} onDone={policy.reload} />
    </div>
  );
}
