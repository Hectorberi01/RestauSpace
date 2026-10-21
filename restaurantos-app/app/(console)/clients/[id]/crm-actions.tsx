'use client';
import {useEffect, useState} from 'react';
import {Download, Merge, Search} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  customerOrders, customerReservations, day, exportCustomer, mergeCustomer, searchCustomers, searchQuery, type Customer,
} from '@/lib/customers';
import {findAccountOfCustomer} from '@/lib/marketing';
import {useConsole} from '@/components/console';
import {Alert, Button, Field, Input, Modal, Spinner, cx, useAction} from '@/components/ui';

/** Fusion d'un doublon dans cette fiche : on cherche l'autre fiche, on vérifie, on confirme. */
export function MergeDialog({customer, open, onClose, onDone}: {customer: Customer; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const [chosen, setChosen] = useState<Customer | null>(null);
  useEffect(() => {
    if (!open) return;
    setText(customer.phone ?? customer.email ?? customer.name);
    setChosen(null);
  }, [open, customer]);
  useEffect(() => {
    const timer = setTimeout(() => setQuery(text.trim()), 350);
    return () => clearTimeout(timer);
  }, [text]);
  const results = useResource(
    signal => (open && query.length >= 2 ? searchCustomers(searchQuery(query, '', false, 1), signal) : Promise.resolve(null)),
    [open, query],
  );
  const candidates = (results.data?.items ?? []).filter(item => item.id !== customer.id);

  return (
    <Modal wide open={open} onClose={onClose} title="Fusionner un doublon" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button variant="danger" busy={busy === 'merge'} disabled={!chosen} onClick={() => run('merge', async () => {
          await mergeCustomer(customer.id, chosen!.id);
          onClose();
          await onDone();
        }, 'Fiches fusionnées.')}><Merge className="size-4" aria-hidden="true" />Fusionner dans cette fiche</Button>
      </>
    }>
      <div className="space-y-4 text-sm">
        <p className="text-ink-600">
          La fiche choisie est absorbée par <strong>{customer.name}</strong> : coordonnées et tags manquants, notes, consentements les plus récents,
          puis commandes, réservations, avis et points de fidélité. Elle ne garde ensuite aucune donnée personnelle. <strong>C’est irréversible.</strong>
        </p>
        <Field label="Chercher la fiche en double">
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
            <Input className="pl-9" value={text} onChange={event => setText(event.target.value)} placeholder="Nom, email ou téléphone" />
          </div>
        </Field>
        {results.error ? <Alert>{results.error}</Alert> : query.length < 2 ? null : !results.data ? <Spinner /> : candidates.length === 0 ? (
          <p className="text-ink-500">Aucune autre fiche ne correspond.</p>
        ) : (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {candidates.map(item => (
              <li key={item.id}>
                <label className={cx('flex cursor-pointer items-center gap-3 px-3 py-2', chosen?.id === item.id && 'bg-brand-50')}>
                  <input type="radio" name="duplicate" className="size-4 accent-brand-600" checked={chosen?.id === item.id} onChange={() => setChosen(item)} />
                  <span className="min-w-0 flex-1">
                    <span className="block font-medium">{item.name}</span>
                    <span className="block truncate text-xs text-ink-500">{[item.email, item.phone, `depuis le ${day(item.createdAtUtc)}`].filter(Boolean).join(' · ')}</span>
                  </span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  );
}

async function allPages<T>(load: (page: number) => Promise<{items: T[]; hasMore: boolean}>, max = 50): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; page <= max; page++) {
    const result = await load(page);
    items.push(...result.items);
    if (!result.hasMore) break;
  }
  return items;
}

/** Droit d'accès : un fichier JSON avec la fiche, les consentements, les notes, les commandes, réservations et la fidélité. */
export function ExportButton({customer}: {customer: Customer}) {
  const {can, tenant} = useConsole();
  const {busy, run} = useAction();
  return (
    <Button variant="secondary" busy={busy === 'export'} onClick={() => run('export', async () => {
      const [data, orders, reservations, loyalty] = await Promise.all([
        exportCustomer(customer.id),
        can('orders.take') || can('orders.process') ? allPages(page => customerOrders(customer.id, page)).catch(() => null) : Promise.resolve(null),
        can('reservations.manage') ? allPages(page => customerReservations(customer.id, page)).catch(() => null) : Promise.resolve(null),
        can('loyalty.view') ? findAccountOfCustomer(customer.id).catch(() => null) : Promise.resolve(null),
      ]);
      const file = {restaurant: tenant.name, ...data, orders, reservations, loyalty};
      const blob = new Blob([JSON.stringify(file, null, 2)], {type: 'application/json'});
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = `donnees-client-${customer.id}.json`;
      link.click();
      URL.revokeObjectURL(link.href);
    }, 'Export téléchargé.')}><Download className="size-4" aria-hidden="true" />Exporter ses données</Button>
  );
}
