'use client';
import {useState} from 'react';
import {Plus, Trash2} from 'lucide-react';
import {del, post, put} from '@/lib/api';
import {modifierGroups} from '@/lib/catalog';
import {money, parseAmount} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import type {ModifierGroup} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Spinner, cx, useAction} from '@/components/ui';
import {InlineEdit, nameOf} from './inline-edit';

export function useModifierGroups() {
  return useResource(() => modifierGroups(), []);
}

function rule(group: ModifierGroup) {
  if (group.minSelections === 0) return `Facultatif, jusqu’à ${group.maxSelections}`;
  if (group.minSelections === group.maxSelections) return `Obligatoire : ${group.minSelections} choix`;
  return `Obligatoire : ${group.minSelections} à ${group.maxSelections} choix`;
}

function GroupCard({group, manage, onChanged}: {group: ModifierGroup; manage: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [name, setName] = useState('');
  const [price, setPrice] = useState('0');
  const [min, setMin] = useState(String(group.minSelections));
  const [max, setMax] = useState(String(group.maxSelections));
  const base = `/catalog/modifier-groups/${group.id}`;
  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="font-semibold">
            <InlineEdit value={group.name} label="Nom du groupe" editable={manage} maxLength={120} parse={nameOf(120)} success="Groupe renommé."
              onSave={async (value: string) => {
                await put(`${base}/name`, {name: value});
                await onChanged();
              }} />
          </div>
          <p className="text-sm text-ink-500">{rule(group)}</p>
        </div>
        {!group.isSatisfiable && <Badge tone="amber">Pas assez d’options disponibles</Badge>}
      </div>
      <ul className="mt-3 divide-y divide-slate-100">
        {group.modifiers.map(modifier => (
          <li key={modifier.id} className="flex flex-wrap items-center justify-between gap-2 py-2 text-sm">
            <InlineEdit value={modifier.name} label={`Nom de l’option ${modifier.name}`} editable={manage} maxLength={120} parse={nameOf(120)} success="Option renommée."
              className={cx('min-w-0 flex-1 basis-full sm:basis-auto', !modifier.isAvailable && 'text-ink-500 line-through')}
              onSave={async (value: string) => {
                await put(`${base}/modifiers/${modifier.id}/name`, {name: value});
                await onChanged();
              }} />
            <span className="flex items-center gap-1">
              <InlineEdit value={String(modifier.priceDelta)} label={`Supplément de ${modifier.name}`} editable={manage} inputMode="decimal" suffix={group.currencyCode}
                inputClassName="w-24" className="mr-2 text-ink-600" success="Supplément enregistré."
                display={modifier.priceDelta ? `+${money(modifier.priceDelta, group.currencyCode)}` : 'Inclus'}
                parse={raw => {
                  const value = parseAmount(raw);
                  return value !== null && value >= 0 ? value : null;
                }}
                onSave={async (value: number) => {
                  await put(`${base}/modifiers/${modifier.id}/price`, {priceDelta: value});
                  await onChanged();
                }} />
              {manage && (
                <>
                  <Button size="sm" variant="ghost" busy={busy === `a-${modifier.id}`} onClick={() => run(`a-${modifier.id}`, async () => {
                    await put(`${base}/modifiers/${modifier.id}/availability`, {isAvailable: !modifier.isAvailable});
                    await onChanged();
                  })}>{modifier.isAvailable ? 'Épuisé' : 'Disponible'}</Button>
                  <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Supprimer ${modifier.name}`} busy={busy === `d-${modifier.id}`} onClick={() => run(`d-${modifier.id}`, async () => {
                    await del(`${base}/modifiers/${modifier.id}`);
                    await onChanged();
                  })}><Trash2 className="size-4" /></Button>
                </>
              )}
            </span>
          </li>
        ))}
        {group.modifiers.length === 0 && <li className="py-2 text-sm text-ink-500">Aucune option.</li>}
      </ul>
      {manage && (
        <div className="mt-4 space-y-3 border-t border-slate-100 pt-4">
          <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void run('add', async () => {
              await post(`${base}/modifiers`, {name: name.trim(), priceDelta: parseAmount(price) ?? 0});
              setName('');
              setPrice('0');
              await onChanged();
            });
          }}>
            <Field label="Nouvelle option" className="min-w-36 flex-1"><Input required maxLength={60} value={name} onChange={event => setName(event.target.value)} placeholder="Frites, Alloco…" /></Field>
            <Field label={`Supplément (${group.currencyCode})`} className="w-32"><Input inputMode="decimal" value={price} onChange={event => setPrice(event.target.value)} /></Field>
            <Button type="submit" busy={busy === 'add'}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
          </form>
          <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void run('rule', async () => {
              await put(`${base}/selection-rule`, {minSelections: Number(min) || 0, maxSelections: Number(max) || 1});
              await onChanged();
            }, 'Règle enregistrée.');
          }}>
            <Field label="Choix minimum" className="w-32"><Input type="number" min={0} value={min} onChange={event => setMin(event.target.value)} /></Field>
            <Field label="Choix maximum" className="w-32"><Input type="number" min={1} value={max} onChange={event => setMax(event.target.value)} /></Field>
            <Button type="submit" variant="secondary" busy={busy === 'rule'}>Enregistrer</Button>
          </form>
        </div>
      )}
    </Card>
  );
}

export function Options() {
  const {can} = useConsole();
  const manage = can('catalog.manage');
  const {data, error, reload} = useModifierGroups();
  const {busy, run} = useAction();
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({name: '', min: '0', max: '1'});

  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  return (
    <>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-ink-600">Accompagnements, cuissons, sauces… Un groupe d’options s’attache ensuite à un ou plusieurs produits.</p>
        {manage && <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouveau groupe</Button>}
      </div>
      {data.length === 0 && <Empty title="Aucun groupe d’options" />}
      <div className="grid gap-4 lg:grid-cols-2">
        {data.map(group => <GroupCard key={group.id} group={group} manage={manage} onChanged={reload} />)}
      </div>
      <Modal open={creating} onClose={() => setCreating(false)} title="Nouveau groupe d’options" footer={
        <>
          <Button variant="secondary" onClick={() => setCreating(false)}>Annuler</Button>
          <Button type="submit" form="create-group" busy={busy === 'create'}>Créer</Button>
        </>
      }>
        <form id="create-group" className="grid gap-4 sm:grid-cols-2" onSubmit={async event => {
          event.preventDefault();
          const ok = await run('create', async () => {
            await post('/catalog/modifier-groups', {name: form.name.trim(), minSelections: Number(form.min) || 0, maxSelections: Number(form.max) || 1});
            await reload();
          }, 'Groupe créé.');
          if (ok) {
            setCreating(false);
            setForm({name: '', min: '0', max: '1'});
          }
        }}>
          <Field label="Nom" className="sm:col-span-2"><Input required maxLength={60} value={form.name} onChange={event => setForm(f => ({...f, name: event.target.value}))} placeholder="Accompagnement" /></Field>
          <Field label="Choix minimum" hint="0 : facultatif."><Input type="number" min={0} value={form.min} onChange={event => setForm(f => ({...f, min: event.target.value}))} /></Field>
          <Field label="Choix maximum"><Input type="number" min={1} value={form.max} onChange={event => setForm(f => ({...f, max: event.target.value}))} /></Field>
        </form>
      </Modal>
    </>
  );
}
