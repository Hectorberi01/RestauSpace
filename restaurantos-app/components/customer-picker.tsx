'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {Contact, Plus, Search, UserPlus, UserRound, X} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {createCustomer, initials, loadCustomer, searchCustomers, searchQuery, type Customer} from '@/lib/customers';
import {useConsole} from './console';
import {Button, Field, Input, Modal, Spinner, cx, useAction} from './ui';

/** Recherche d'une fiche client, ou création rapide (nom + coordonnées) quand on a le droit de gérer les clients. */
export function CustomerPickerDialog({open, initial, success, onClose, onPick}: {
  open: boolean;
  success?: string;
  initial?: {name?: string; phone?: string; email?: string};
  onClose: () => void;
  onPick: (customer: Customer) => Promise<void> | void;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({name: '', phone: '', email: ''});

  useEffect(() => {
    if (!open) return;
    const start = initial?.phone?.trim().startsWith('+') ? initial.phone.trim() : initial?.name?.trim() ?? '';
    setText(start);
    setQuery(start);
    setCreating(false);
    setForm({name: initial?.name?.trim() ?? '', phone: initial?.phone?.trim() ?? '', email: initial?.email?.trim() ?? ''});
    // Seulement à l'ouverture : une saisie en cours ne doit pas être écrasée.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    const timer = window.setTimeout(() => setQuery(text), 300);
    return () => window.clearTimeout(timer);
  }, [text]);

  const results = useResource(
    signal => (open && !creating ? searchCustomers({...searchQuery(query, '', false, 1), pageSize: 8}, signal) : Promise.resolve(null)),
    [open, creating, query],
  );

  const pick = (customer: Customer) => run('pick', async () => {
    await onPick(customer);
    onClose();
  }, success);

  return (
    <Modal open={open} onClose={onClose} title={creating ? 'Nouvelle fiche client' : 'Choisir un client'} footer={creating ? (
      <>
        <Button variant="secondary" onClick={() => setCreating(false)}>Retour</Button>
        <Button type="submit" form="quick-customer" busy={busy === 'create'} disabled={!form.name.trim()}>Créer et rattacher</Button>
      </>
    ) : (
      <Button variant="secondary" onClick={onClose}>Fermer</Button>
    )}>
      {creating ? (
        <form id="quick-customer" className="space-y-3" onSubmit={event => {
          event.preventDefault();
          void run('create', async () => {
            const {id} = await createCustomer({
              name: form.name.trim(), email: form.email.trim() || null, phone: form.phone.trim() || null, tags: [], preferences: null,
            });
            await onPick(await loadCustomer(id));
            onClose();
          }, 'Fiche client créée.');
        }}>
          <Field label="Nom"><Input required maxLength={150} autoComplete="off" value={form.name} onChange={event => setForm(value => ({...value, name: event.target.value}))} /></Field>
          <Field label="Téléphone" hint="Format international."><Input type="tel" autoComplete="off" value={form.phone} placeholder="+229 01 97 00 00 00" onChange={event => setForm(value => ({...value, phone: event.target.value}))} /></Field>
          <Field label="Email"><Input type="email" autoComplete="off" value={form.email} onChange={event => setForm(value => ({...value, email: event.target.value}))} /></Field>
          <p className="text-xs text-ink-500">Tags, préférences et consentements se complètent ensuite sur la fiche.</p>
        </form>
      ) : (
        <div className="space-y-3">
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
            <Input type="search" aria-label="Rechercher un client" className="pl-9" autoFocus value={text} onChange={event => setText(event.target.value)}
              placeholder="Nom, email exact ou +229…" />
          </div>
          {results.error ? <p className="text-sm text-red-600">{results.error}</p> : !results.data ? <Spinner label="Recherche…" /> : results.data.items.length === 0 ? (
            <p className="py-4 text-center text-sm text-ink-500">Aucun client ne correspond.</p>
          ) : (
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {results.data.items.map(customer => (
                <li key={customer.id}>
                  <button type="button" disabled={busy === 'pick'} onClick={() => void pick(customer)}
                    className="flex w-full items-center gap-3 px-3 py-2.5 text-left text-sm hover:bg-slate-50 disabled:opacity-50">
                    <span className="grid size-9 shrink-0 place-items-center rounded-full bg-brand-50 text-xs font-semibold text-brand-700" aria-hidden="true">{initials(customer.name)}</span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium">{customer.name}</span>
                      <span className="block truncate text-ink-500">{[customer.phone, customer.email].filter(Boolean).join(' · ') || 'Sans coordonnées'}</span>
                    </span>
                    {customer.tags[0] && <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-ink-600">{customer.tags[0]}</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
          {can('customers.manage') && (
            <Button variant="ghost" className="w-full" onClick={() => {
              if (!form.name && text && !text.startsWith('+') && !text.includes('@')) setForm(value => ({...value, name: text.trim()}));
              setCreating(true);
            }}><UserPlus className="size-4" aria-hidden="true" />Créer une nouvelle fiche</Button>
          )}
        </div>
      )}
    </Modal>
  );
}

/**
 * La fiche client rattachée (commande ou réservation) : nom et lien vers la fiche, avec rattacher, changer et détacher.
 * Sans le droit de voir les clients, seule la présence d'un rattachement est indiquée.
 */
export function LinkedCustomer({customerId, editable, initial, onChange, className}: {
  customerId: string | null;
  initial?: {name?: string; phone?: string; email?: string};
  editable: boolean;
  onChange: (customerId: string | null) => Promise<void>;
  className?: string;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [picking, setPicking] = useState(false);
  const visible = can('customers.view');
  const {data: customer} = useResource(
    signal => (customerId && visible ? loadCustomer(customerId, signal).catch(() => null) : Promise.resolve(null)),
    [customerId, visible],
  );
  const canEdit = editable && visible;
  if (!customerId && !canEdit) return null;

  return (
    <div className={cx('flex items-center gap-3', className)}>
      <span className={cx('grid size-10 shrink-0 place-items-center rounded-full text-xs font-semibold', customerId ? 'bg-brand-50 text-brand-700' : 'bg-slate-100 text-ink-500')} aria-hidden="true">
        {customer ? initials(customer.name) : customerId ? <UserRound className="size-5" /> : <Contact className="size-5" />}
      </span>
      <div className="min-w-0 flex-1 text-sm">
        {customerId ? (
          <>
            <p className="text-xs text-ink-500">Fiche client</p>
            {customer ? (
              <Link href={`/clients/${customer.id}`} className="block truncate font-semibold hover:text-brand-700">{customer.name}</Link>
            ) : (
              <p className="font-semibold">{visible ? 'Chargement…' : 'Client rattaché'}</p>
            )}
          </>
        ) : (
          <p className="text-ink-500">Aucune fiche client rattachée.</p>
        )}
      </div>
      {canEdit && (
        <div className="flex shrink-0 gap-1">
          <Button size="sm" variant="secondary" onClick={() => setPicking(true)}>
            {customerId ? 'Changer' : <><Plus className="size-4" aria-hidden="true" />Rattacher</>}
          </Button>
          {customerId && (
            <Button size="sm" variant="ghost" aria-label="Détacher la fiche client" busy={busy === 'detach'}
              onClick={() => run('detach', () => onChange(null), 'Fiche client détachée.')}><X className="size-4" /></Button>
          )}
        </div>
      )}
      <CustomerPickerDialog open={picking} initial={initial} success="Fiche client rattachée." onClose={() => setPicking(false)} onPick={async picked => {
        await onChange(picked.id);
      }} />
    </div>
  );
}
