'use client';
import {use, useEffect, useState, type ReactNode} from 'react';
import Link from 'next/link';
import {ArrowLeft, Contact, History as HistoryIcon, Languages, Merge, NotebookPen, Plus, ShieldCheck, Star, Tag, UserX, X, Mails} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  addTag, anonymize, applyContactPolicy, changeContact, channels, day, initials, languageLabel, languages, loadConsents, loadContactPolicy,
  loadCustomer, removeTag, renameCustomer, requirementLabel, setPreferences, type Channel,
} from '@/lib/customers';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';
import {ServiceMessages} from './service-messages';
import {Consents} from './consents';
import {History} from './history';
import {CustomerLoyalty} from './loyalty';
import {CustomerNotes} from './notes';
import {ExportButton, MergeDialog} from './crm-actions';

function Section({icon, title, description, children}: {icon: ReactNode; title: string; description?: string; children: ReactNode}) {
  return (
    <Card className="p-5">
      <div className="mb-4 flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true">{icon}</span>
        <div>
          <h2 className="font-semibold">{title}</h2>
          {description && <p className="text-sm text-ink-500">{description}</p>}
        </div>
      </div>
      {children}
    </Card>
  );
}

export default function CustomerPage({params}: {params: Promise<{id: string}>}) {
  const {id} = use(params);
  const {can} = useConsole();
  const allowed = can('customers.view');
  const {busy, run} = useAction();
  const {data: customer, error, reload} = useResource(signal => (allowed ? loadCustomer(id, signal) : Promise.resolve(null)), [id, allowed]);
  const consents = useResource(signal => (allowed ? loadConsents(id, signal) : Promise.resolve(null)), [id, allowed]);
  const policy = useResource(signal => (allowed ? loadContactPolicy(signal) : Promise.resolve(null)), [allowed]);
  const [form, setForm] = useState({name: '', email: '', phone: ''});
  const [tag, setTag] = useState('');
  const [language, setLanguage] = useState('fr');
  const [wanted, setWanted] = useState<Channel[]>([]);
  const [erasing, setErasing] = useState(false);
  const [merging, setMerging] = useState(false);
  const [version, setVersion] = useState(0);
  const [confirmName, setConfirmName] = useState('');

  useEffect(() => {
    if (!customer) return;
    setForm({name: customer.name, email: customer.email ?? '', phone: customer.phone ?? ''});
    setLanguage(customer.preferences?.language ?? 'fr');
    setWanted(customer.preferences?.marketingChannels ?? []);
  }, [customer]);

  if (!allowed) return <Alert tone="blue">Vous n’avez pas accès aux fiches clients (permission « Voir les clients »).</Alert>;
  if (error) return <div className="space-y-4"><Link href="/clients" className="text-sm font-semibold text-brand-700">Retour aux clients</Link><Alert>{error}</Alert></div>;
  if (!customer) return <Spinner label="Chargement de la fiche…" />;

  const merged = customer.status === 'Merged';
  const editable = can('customers.manage') && !customer.isAnonymized && !merged;
  const refresh = async () => {
    await reload();
    await consents.reload();
  };
  const nameChanged = form.name.trim() !== customer.name;
  const contactChanged = form.email.trim() !== (customer.email ?? '') || form.phone.trim() !== (customer.phone ?? '');
  const preferencesChanged = language !== (customer.preferences?.language ?? 'fr')
    || [...wanted].sort().join() !== [...(customer.preferences?.marketingChannels ?? [])].sort().join();
  const current = policy.data?.requirement;
  const outdatedPolicy = editable && current !== undefined && current !== customer.contactRequirement;

  return (
    <div className="space-y-6">
      <Link href="/clients" className="inline-flex items-center gap-1 text-sm font-semibold text-brand-700"><ArrowLeft className="size-4" aria-hidden="true" />Clients</Link>
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-center gap-4">
          <span className={cx('grid size-14 shrink-0 place-items-center rounded-full text-lg font-semibold', customer.isAnonymized ? 'bg-slate-100 text-ink-400' : 'bg-brand-50 text-brand-700')} aria-hidden="true">
            {customer.isAnonymized ? '—' : initials(customer.name)}
          </span>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="truncate text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{customer.name}</h1>
              {customer.isAnonymized && <Badge tone="red">Anonymisé</Badge>}
              {merged && <Badge>Fusionnée</Badge>}
              {customer.tags.slice(0, 3).map(item => <Badge key={item} tone="brand">{item}</Badge>)}
            </div>
            <p className="mt-1 text-sm text-ink-500">
              Client depuis le {day(customer.createdAtUtc)}
              {customer.preferences && ` · ${languageLabel(customer.preferences.language)}`}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2 self-start sm:self-auto">
          {can('notifications.view') && (
            <Link href={`/messages?onglet=historique&client=${customer.id}`} className="inline-flex h-10 items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 text-sm font-semibold text-ink-800 hover:bg-slate-50">
              <Mails className="size-4" aria-hidden="true" />Messages envoyés
            </Link>
          )}
          {can('customers.manage') && !customer.isAnonymized && !merged && <ExportButton customer={customer} />}
          {editable && <Button variant="secondary" onClick={() => setMerging(true)}><Merge className="size-4" aria-hidden="true" />Fusionner un doublon</Button>}
          {can('customers.anonymize') && !customer.isAnonymized && !merged && (
            <Button variant="ghost" className="text-red-600" onClick={() => {
              setConfirmName('');
              setErasing(true);
            }}><UserX className="size-4" aria-hidden="true" />Anonymiser</Button>
          )}
        </div>
      </div>

      {customer.isAnonymized && (
        <Alert>
          Données personnelles effacées{customer.anonymizedAtUtc ? ` le ${day(customer.anonymizedAtUtc)}` : ''} : la fiche ne peut plus être modifiée.
          Les commandes et réservations restent dans vos statistiques.
        </Alert>
      )}
      {merged && (
        <Alert tone="blue">
          Ce doublon a été fusionné : son historique est rattaché à la fiche gardée.{' '}
          {customer.mergedIntoId && <Link className="font-semibold underline" href={`/clients/${customer.mergedIntoId}`}>Ouvrir la fiche gardée</Link>}
        </Alert>
      )}
      {!customer.isAnonymized && !merged && !can('customers.manage') && <Alert tone="blue">Lecture seule : vous n’avez pas la permission « Gérer les clients ».</Alert>}
      {outdatedPolicy && current && (
        <Alert tone="amber">
          <span className="flex flex-wrap items-center gap-x-3 gap-y-2">
            Cette fiche suit l’ancienne règle « {requirementLabel[customer.contactRequirement].toLowerCase()} » ; la règle actuelle est « {requirementLabel[current].toLowerCase()} ».
            <Button size="sm" variant="secondary" busy={busy === 'policy'} onClick={() => run('policy', async () => {
              await applyContactPolicy(customer.id);
              await reload();
            }, 'Règle actuelle appliquée.')}>Appliquer la règle actuelle</Button>
          </span>
        </Alert>
      )}

      <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-2">
        <div className="space-y-4">
          <Section icon={<Contact className="size-5" />} title="Identité et coordonnées" description={`Exigé : ${requirementLabel[customer.contactRequirement].toLowerCase()}.`}>
            <form className="grid grid-cols-1 gap-3 sm:grid-cols-2" onSubmit={event => {
              event.preventDefault();
              void run('contact', async () => {
                if (nameChanged) await renameCustomer(customer.id, form.name.trim());
                if (contactChanged) await changeContact(customer.id, form.email.trim() || null, form.phone.trim() || null);
                await reload();
              }, 'Fiche enregistrée.');
            }}>
              <Field label="Nom" className="sm:col-span-2">
                <Input required maxLength={150} disabled={!editable} value={form.name} onChange={event => setForm(value => ({...value, name: event.target.value}))} />
              </Field>
              <Field label="Email">
                <Input type="email" disabled={!editable} value={form.email} onChange={event => setForm(value => ({...value, email: event.target.value}))} placeholder="client@exemple.com" />
              </Field>
              <Field label="Téléphone" hint="Format international.">
                <Input type="tel" disabled={!editable} value={form.phone} onChange={event => setForm(value => ({...value, phone: event.target.value}))} placeholder="+229 01 97 00 00 00" />
              </Field>
              {editable && (
                <div className="flex gap-2 sm:col-span-2">
                  <Button type="submit" variant="secondary" busy={busy === 'contact'} disabled={(!nameChanged && !contactChanged) || !form.name.trim()}>Enregistrer</Button>
                  {(nameChanged || contactChanged) && <Button variant="ghost" onClick={() => setForm({name: customer.name, email: customer.email ?? '', phone: customer.phone ?? ''})}>Annuler</Button>}
                </div>
              )}
            </form>
          </Section>

          <Section icon={<Tag className="size-5" />} title="Tags" description="Habitudes, allergies déclarées, segments : 20 au plus.">
            {customer.tags.length === 0 ? <p className="text-sm text-ink-500">Aucun tag.</p> : (
              <ul className="flex flex-wrap gap-2">
                {customer.tags.map(item => (
                  <li key={item} className="inline-flex items-center gap-1 rounded-full bg-slate-100 py-1 pr-1 pl-3 text-sm font-medium">
                    {item}
                    {editable && (
                      <button type="button" aria-label={`Retirer le tag ${item}`} disabled={busy === `tag-${item}`} onClick={() => run(`tag-${item}`, async () => {
                        await removeTag(customer.id, item);
                        await reload();
                      })} className="grid size-6 place-items-center rounded-full text-ink-500 hover:bg-slate-200"><X className="size-3.5" /></button>
                    )}
                  </li>
                ))}
              </ul>
            )}
            {editable && customer.tags.length < 20 && (
              <form className="mt-3 flex gap-2" onSubmit={event => {
                event.preventDefault();
                void run('tag', async () => {
                  await addTag(customer.id, tag.trim());
                  setTag('');
                  await reload();
                });
              }}>
                <Input aria-label="Nouveau tag" maxLength={30} value={tag} onChange={event => setTag(event.target.value)} placeholder="VIP, Végétarien…" />
                <Button type="submit" variant="secondary" busy={busy === 'tag'} disabled={!tag.trim() || customer.tags.some(item => item.toLowerCase() === tag.trim().toLowerCase())}>
                  <Plus className="size-4" aria-hidden="true" />Ajouter
                </Button>
              </form>
            )}
          </Section>

          <Section icon={<Languages className="size-5" />} title="Préférences" description="Langue des messages et canaux que le client dit préférer.">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Field label="Langue">
                <Select disabled={!editable} value={language} onChange={event => setLanguage(event.target.value)}>
                  {!languages.some(item => item.value === language) && <option value={language}>{languageLabel(language)}</option>}
                  {languages.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
                </Select>
              </Field>
              <fieldset disabled={!editable}>
                <legend className="mb-1.5 text-sm font-medium">Canaux souhaités</legend>
                <div className="flex flex-wrap gap-x-4 gap-y-1">
                  {channels.map(channel => (
                    <label key={channel.value} className="flex items-center gap-2 text-sm">
                      <input type="checkbox" className="size-4 accent-brand-600" checked={wanted.includes(channel.value)}
                        onChange={event => setWanted(list => event.target.checked ? [...list, channel.value] : list.filter(item => item !== channel.value))} />
                      {channel.label}
                    </label>
                  ))}
                </div>
              </fieldset>
            </div>
            {editable && (
              <Button className="mt-4" variant="secondary" busy={busy === 'preferences'} disabled={!preferencesChanged} onClick={() => run('preferences', async () => {
                await setPreferences(customer.id, language, wanted);
                await reload();
              }, 'Préférences enregistrées.')}>Enregistrer les préférences</Button>
            )}
          </Section>
        </div>

        <div className="space-y-4">
          {!customer.isAnonymized && !merged && (
            <Section icon={<NotebookPen className="size-5" />} title="Notes de l’équipe" description="Internes : jamais montrées au client.">
              <CustomerNotes key={version} customerId={customer.id} editable={editable} />
            </Section>
          )}
          <Section icon={<ShieldCheck className="size-5" />} title="Consentements marketing" description="La preuve d’accord par canal, avec son historique.">
            {consents.error ? <Alert>{consents.error}</Alert> : !consents.data ? <Spinner /> : (
              <Consents customer={customer} consents={consents.data} editable={editable} onChanged={refresh} />
            )}
          </Section>
          {can('notifications.manage_preferences') && !customer.isAnonymized && (
            <Section icon={<Mails className="size-5" />} title="Messages de service" description="Confirmations, rappels et suivi de commande, canal par canal.">
              <ServiceMessages customerId={customer.id} editable />
            </Section>
          )}
          {can('loyalty.view') && (
            <Section icon={<Star className="size-5" />} title="Fidélité" description="Solde de points et récompenses utilisées.">
              <CustomerLoyalty customer={customer} />
            </Section>
          )}
          <Section icon={<HistoryIcon className="size-5" />} title="Historique" description="Commandes et réservations rattachées à cette fiche.">
            <History key={version} customerId={customer.id} />
          </Section>
        </div>
      </div>

      <MergeDialog customer={customer} open={merging} onClose={() => setMerging(false)} onDone={async () => {
        await refresh();
        setVersion(value => value + 1);
      }} />
      <Modal open={erasing} onClose={() => setErasing(false)} title="Anonymiser le client" footer={
        <>
          <Button variant="secondary" onClick={() => setErasing(false)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'erase'} disabled={confirmName.trim() !== customer.name} onClick={() => run('erase', async () => {
            await anonymize(customer.id);
            setErasing(false);
            await refresh();
          }, 'Données personnelles effacées.')}>Anonymiser définitivement</Button>
        </>
      }>
        <div className="space-y-4 text-sm text-ink-600">
          <p>Droit à l’effacement (RGPD) : le nom, l’email, le téléphone, les tags et les préférences sont effacés, et tous les consentements retirés. <strong>C’est irréversible.</strong></p>
          <p>Les commandes et réservations restent dans vos statistiques.</p>
          <Field label={`Tapez « ${customer.name} » pour confirmer`}>
            <Input autoComplete="off" value={confirmName} onChange={event => setConfirmName(event.target.value)} />
          </Field>
        </div>
      </Modal>
    </div>
  );
}
