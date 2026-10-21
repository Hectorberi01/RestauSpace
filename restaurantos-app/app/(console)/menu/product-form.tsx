'use client';
import {useEffect, useState, type FormEvent} from 'react';
import {post, put} from '@/lib/api';
import {parseAmount} from '@/lib/format';
import {setCategory, type Catalog, setProductLocations} from '@/lib/menu';
import type {Product} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Button, Field, Input, Modal, Select, Textarea, useAction} from '@/components/ui';
import {useTaxCategories} from './taxes';

interface Form {
  name: string;
  price: string;
  taxCategoryId: string;
  description: string;
  sectionId: string;
}

/** Création (product absent) ou modification d'un produit, catégorie comprise (section de la carte principale). */
export function ProductForm({open, product, catalog, formula, onClose, onSaved}: {
  open: boolean;
  formula?: boolean;
  product?: Product | null;
  catalog: Catalog;
  onClose: () => void;
  onSaved: (id: string) => Promise<void> | void;
}) {
  const {currency, tenant, location, locations} = useConsole();
  const [onlyHere, setOnlyHere] = useState(true);
  const {data: taxes} = useTaxCategories();
  const {busy, run} = useAction();
  const menu = catalog.primary;
  const currentSection = product && menu ? menu.sections.find(section => section.productIds.includes(product.id))?.id ?? '' : '';
  const [form, setForm] = useState<Form>({name: '', price: '', taxCategoryId: '', description: '', sectionId: ''});
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setError(null);
    setForm({
      name: product?.name ?? '',
      price: product ? String(product.basePrice) : '',
      taxCategoryId: product?.taxCategoryId ?? '',
      description: product?.description ?? '',
      sectionId: currentSection,
    });
  }, [open, product, currentSection]);

  const taxId = form.taxCategoryId || taxes?.[0]?.id || '';
  const set = (key: keyof Form) => (event: {target: {value: string}}) => setForm(current => ({...current, [key]: event.target.value}));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    const name = form.name.trim();
    const price = parseAmount(form.price);
    if (!name) return setError('Indiquez le nom du produit.');
    if (price === null || price < 0) return setError('Indiquez un prix valide.');
    if (!taxId) return setError('Choisissez une taxe.');
    const description = form.description.trim() || null;
    const ok = await run('save', async () => {
      let id = product?.id;
      if (!id) {
        id = (await post<{id: string}>('/catalog/products', {name, basePrice: price, taxCategoryId: taxId, description})).id;
        if (onlyHere && location && locations.length > 1) await setProductLocations(id, [location.id]);
      } else {
        const base = `/catalog/products/${id}`;
        if (name !== product!.name) await put(`${base}/name`, {name});
        if (price !== product!.basePrice) await put(`${base}/base-price`, {basePrice: price});
        if (taxId !== product!.taxCategoryId) await put(`${base}/tax-category`, {taxCategoryId: taxId});
        if (description !== (product!.description ?? null)) await put(`${base}/description`, {description});
      }
      if (menu && form.sectionId !== currentSection) await setCategory(menu, id, form.sectionId || null);
      await onSaved(id);
    }, product ? 'Produit enregistré.' : 'Produit créé en brouillon.');
    if (ok) onClose();
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={product ? 'Modifier le produit' : formula ? 'Nouvelle formule' : 'Ajouter un produit'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Annuler</Button>
          <Button type="submit" form="product-form" busy={busy === 'save'} disabled={!taxId}>{product ? 'Enregistrer' : 'Créer'}</Button>
        </>
      }>
      {taxes && taxes.length === 0 ? (
        <Alert tone="amber">Créez d’abord une taxe (menu « ⋯ » puis « Taxes »).</Alert>
      ) : (
        <form id="product-form" onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
          {error && <div className="sm:col-span-2"><Alert>{error}</Alert></div>}
          <Field label="Nom" className="sm:col-span-2"><Input required maxLength={120} value={form.name} onChange={set('name')} placeholder="Poulet braisé" /></Field>
          <Field label={`Prix ${tenant.pricesIncludeTax ? 'TTC' : 'HT'} (${currency})`}><Input required inputMode="decimal" value={form.price} onChange={set('price')} /></Field>
          <Field label="Taxe">
            <Select value={taxId} onChange={set('taxCategoryId')}>
              {taxes?.map(tax => <option key={tax.id} value={tax.id}>{tax.name}</option>)}
              {product && taxes && !taxes.some(tax => tax.id === product.taxCategoryId) && <option value={product.taxCategoryId}>Taxe archivée</option>}
            </Select>
          </Field>
          <Field
            label="Catégorie"
            className="sm:col-span-2"
            hint={!menu ? 'Créez une carte et ses catégories pour classer vos produits.' : menu.status === 'Published' ? `Carte « ${menu.name} » publiée : elle sera republiée automatiquement.` : `Carte « ${menu.name} ».`}>
            <Select value={form.sectionId} onChange={set('sectionId')} disabled={!menu || menu.sections.length === 0}>
              <option value="">Hors carte</option>
              {menu?.sections.map(section => <option key={section.id} value={section.id}>{section.name}</option>)}
            </Select>
          </Field>
          <Field label="Description" className="sm:col-span-2"><Textarea rows={3} maxLength={500} value={form.description} onChange={set('description')} placeholder="Ingrédients, accompagnement…" /></Field>
          {!product && location && locations.length > 1 && (
            <label className="flex items-start gap-2 text-sm sm:col-span-2">
              <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={onlyHere} onChange={event => setOnlyHere(event.target.checked)} />
              <span>Vendu seulement à <span className="font-semibold">{location.name}</span><span className="block text-xs text-ink-500">Décochez pour le proposer dans tous les établissements.</span></span>
            </label>
          )}
        </form>
      )}
    </Modal>
  );
}
