'use client';
import {useState, type FormEvent} from 'react';
import {Plus} from 'lucide-react';
import {api, post, put} from '@/lib/api';
import {parseAmount, percent} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import type {TaxCategory} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Button, Card, Empty, Field, Input, Modal, Spinner, useAction} from '@/components/ui';
import {InlineEdit, nameOf} from './inline-edit';

export function useTaxCategories() {
  return useResource(signal => api<TaxCategory[]>('/catalog/tax-categories', {signal}), []);
}

export function Taxes() {
  const {can, tenant} = useConsole();
  const manage = can('catalog.manage');
  const {data, error, reload} = useTaxCategories();
  const {busy, run} = useAction();
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState({code: '', name: '', rate: ''});

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const rate = parseAmount(form.rate);
    if (rate === null) return;
    const ok = await run('create', async () => {
      await post('/catalog/tax-categories', {code: form.code.trim().toUpperCase(), name: form.name.trim(), rate: rate / 100});
      await reload();
    }, 'Catégorie de taxe créée.');
    if (ok) {
      setCreating(false);
      setForm({code: '', name: '', rate: ''});
    }
  };

  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  return (
    <>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-ink-600">Chaque produit a une catégorie de taxe. Vos prix sont saisis {tenant.pricesIncludeTax ? 'TTC (taxes incluses)' : 'HT (hors taxes)'}.</p>
        {manage && <Button onClick={() => setCreating(true)}><Plus className="size-4" aria-hidden="true" />Nouvelle catégorie</Button>}
      </div>
      {data.length === 0 ? (
        <Empty title="Aucune catégorie de taxe">Créez-en au moins une (par exemple TVA 18 % au Bénin, ou TVA 10 % pour la restauration sur place en France) avant d’ajouter des produits.</Empty>
      ) : (
        <Card className="divide-y divide-slate-100">
          {data.map(tax => (
            <div key={tax.id} className="flex items-center justify-between gap-3 px-5 py-3">
              <div className="min-w-0">
                <div className="flex min-w-0 items-center gap-1 font-semibold">
                  <InlineEdit value={tax.name} label={`Nom de ${tax.code}`} editable={manage} maxLength={100} parse={nameOf(100)} success="Taxe renommée."
                    onSave={async (name: string) => {
                      await put(`/catalog/tax-categories/${tax.id}/name`, {name});
                      await reload();
                    }} />
                  <span className="shrink-0 font-normal text-ink-500">· {tax.code}</span>
                </div>
              </div>
              <div className="flex items-center gap-3">
                <InlineEdit value={String(Math.round(tax.rate * 1e6) / 1e4).replace('.', ',')} display={<span className="font-semibold">{percent(tax.rate)}</span>}
                  label={`Taux de ${tax.code} en %`} editable={manage} inputMode="decimal" suffix="%" inputClassName="w-20"
                  success="Taux enregistré : il s’applique aux articles ajoutés aux commandes à partir de maintenant."
                  parse={raw => {
                    const value = parseAmount(raw);
                    return value !== null && value >= 0 && value <= 100 ? Math.round(value * 1e4) / 1e6 : null;
                  }}
                  onSave={async (rate: number) => {
                    await put(`/catalog/tax-categories/${tax.id}/rate`, {rate});
                    await reload();
                  }} />
                {manage && (
                  <Button size="sm" variant="ghost" busy={busy === tax.id} onClick={() => run(tax.id, async () => {
                    await post(`/catalog/tax-categories/${tax.id}/archive`);
                    await reload();
                  }, 'Catégorie archivée.')}>Archiver</Button>
                )}
              </div>
            </div>
          ))}
        </Card>
      )}
      <Modal open={creating} onClose={() => setCreating(false)} title="Nouvelle catégorie de taxe" footer={
        <>
          <Button variant="secondary" onClick={() => setCreating(false)}>Annuler</Button>
          <Button type="submit" form="create-tax" busy={busy === 'create'}>Créer</Button>
        </>
      }>
        <form id="create-tax" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
          <Field label="Code"><Input required maxLength={20} value={form.code} onChange={event => setForm(f => ({...f, code: event.target.value}))} placeholder="TVA18" /></Field>
          <Field label="Taux (%)"><Input required inputMode="decimal" value={form.rate} onChange={event => setForm(f => ({...f, rate: event.target.value}))} placeholder="18" /></Field>
          <Field label="Nom" className="sm:col-span-2"><Input required maxLength={60} value={form.name} onChange={event => setForm(f => ({...f, name: event.target.value}))} placeholder="TVA normale" /></Field>
        </form>
      </Modal>
    </>
  );
}
