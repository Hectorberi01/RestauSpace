'use client';
import {useEffect, useMemo, useState} from 'react';
import {ArrowDown, ArrowUp, Plus, Trash2} from 'lucide-react';
import {put} from '@/lib/api';
import {productDetail} from '@/lib/catalog';
import {money, parseAmount} from '@/lib/format';
import type {Catalog} from '@/lib/menu';
import type {Product} from '@/lib/types';
import {Alert, Button, Field, Input, Select, useAction} from '@/components/ui';

interface DraftChoice {
  productId: string;
  variantId: string | null;
  supplement: string;
}

interface DraftStep {
  key: string;
  id: string | null;
  name: string;
  minChoices: number;
  maxChoices: number;
  choices: DraftChoice[];
}

const MaxSteps = 10;
const MaxChoices = 40;

const fromProduct = (product: Product): DraftStep[] =>
  (product.formulaSteps ?? []).map(step => ({
    key: step.id,
    id: step.id,
    name: step.name,
    minChoices: step.minChoices,
    maxChoices: step.maxChoices,
    choices: step.choices.map(choice => ({productId: choice.productId, variantId: choice.variantId, supplement: String(choice.supplement)})),
  }));

const newStep = (name: string): DraftStep => ({key: crypto.randomUUID(), id: null, name, minChoices: 1, maxChoices: 1, choices: []});

/** Problème bloquant d'une étape, dit avant l'envoi (l'API refuse de toute façon). */
function stepProblem(step: DraftStep): string | null {
  if (!step.name.trim()) return 'Donnez un nom à l’étape.';
  if (step.choices.length === 0) return 'Proposez au moins un produit.';
  if (step.maxChoices < 1 || step.maxChoices > 10 || step.minChoices < 0 || step.minChoices > step.maxChoices) return 'Le minimum va de 0 au maximum, le maximum de 1 à 10.';
  if (step.minChoices > step.choices.length) return 'Le minimum dépasse le nombre de produits proposés.';
  if (step.choices.some(choice => parseAmount(choice.supplement) === null || (parseAmount(choice.supplement) ?? 0) < 0)) return 'Un supplément est un montant positif ou nul.';
  return null;
}

/**
 * Éditeur de formule (menu composé) : des étapes (« Entrée », « Plat », « Boisson »), chacune avec les produits proposés
 * et leur supplément éventuel. Les options propres à chaque produit restent proposées à la commande.
 */
export function FormulaEditor({product, catalog, editable, onChanged}: {product: Product; catalog: Catalog; editable: boolean; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [steps, setSteps] = useState<DraftStep[]>(() => fromProduct(product));
  const [variants, setVariants] = useState<Map<string, Product>>(new Map());
  const [adding, setAdding] = useState<Record<string, {productId: string; variantId: string}>>({});
  const isFormula = (product.formulaSteps?.length ?? 0) > 0;

  useEffect(() => setSteps(fromProduct(product)), [product]);

  const offerable = useMemo(
    () => catalog.products
      .filter(item => item.id !== product.id && item.status !== 'Archived' && !item.isFormula)
      .sort((a, b) => a.name.localeCompare(b.name, 'fr')),
    [catalog.products, product.id],
  );
  const summaryOf = (id: string) => catalog.products.find(item => item.id === id);

  const needDetail = useMemo(
    () => [...new Set(steps.flatMap(step => step.choices.filter(choice => choice.variantId).map(choice => choice.productId)))],
    [steps],
  );
  const loadVariants = (id: string) => {
    if (variants.has(id)) return;
    void productDetail(id).then(detail => setVariants(current => new Map(current).set(id, detail))).catch(() => undefined);
  };
  useEffect(() => {
    needDetail.forEach(loadVariants);
  }, [needDetail]);

  const update = (key: string, change: (step: DraftStep) => DraftStep) => setSteps(current => current.map(step => (step.key === key ? change(step) : step)));
  const move = (index: number, delta: number) => setSteps(current => {
    const next = [...current];
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item);
    return next;
  });
  const choiceLabel = (choice: DraftChoice) => {
    const name = summaryOf(choice.productId)?.name ?? 'Produit retiré';
    const variant = choice.variantId ? variants.get(choice.productId)?.variants.find(item => item.id === choice.variantId)?.name : null;
    return variant ? `${name} · ${variant}` : name;
  };

  const problems = steps.map(stepProblem);
  const names = steps.map(step => step.name.trim().toLocaleLowerCase('fr'));
  const duplicateName = names.some((name, index) => name && names.indexOf(name) !== index);
  const dirty = JSON.stringify(steps.map(({key: _key, ...rest}) => rest)) !== JSON.stringify(fromProduct(product).map(({key: _key, ...rest}) => rest));

  const save = (next: DraftStep[]) => run('save', async () => {
    await put(`/catalog/products/${product.id}/formula`, {
      steps: next.map(step => ({
        id: step.id,
        name: step.name.trim(),
        minChoices: step.minChoices,
        maxChoices: step.maxChoices,
        choices: step.choices.map(choice => ({productId: choice.productId, variantId: choice.variantId, supplement: parseAmount(choice.supplement) ?? 0})),
      })),
    });
    await onChanged();
  }, next.length ? 'Formule enregistrée.' : 'Le produit n’est plus une formule.');

  if (product.variants.length > 0) {
    return <p className="pt-4 text-sm text-ink-500">Une formule n’a pas de variantes : son prix est fixe, suppléments à part. Retirez les variantes de ce produit pour en faire une formule.</p>;
  }

  return (
    <section className="space-y-4 pt-4">
      <p className="text-sm text-ink-500">
        Menu composé vendu {money(product.basePrice, product.currencyCode)} : le client choisit un produit à chaque étape. Le supplément s’ajoute au prix de la formule ;
        les options de chaque produit (cuisson, sauce…) restent proposées. La cuisine et le stock voient chaque produit choisi.
      </p>
      {steps.length === 0 && !editable && <p className="text-sm text-ink-500">Ce produit n’est pas une formule.</p>}

      {steps.map((step, index) => {
        const pending = adding[step.key] ?? {productId: '', variantId: ''};
        const pendingDetail = pending.productId ? variants.get(pending.productId) : undefined;
        const pendingHasVariants = (summaryOf(pending.productId)?.variantCount ?? 0) > 0;
        const canAdd = pending.productId !== '' && (!pendingHasVariants || pending.variantId !== '') && step.choices.length < MaxChoices
          && !step.choices.some(choice => choice.productId === pending.productId && (choice.variantId ?? '') === pending.variantId);
        return (
          <div key={step.key} className="space-y-3 rounded-xl border border-slate-200 p-3">
            <div className="flex flex-wrap items-end gap-2">
              <Field label={`Étape ${index + 1}`} className="min-w-36 flex-1">
                <Input value={step.name} maxLength={120} disabled={!editable} placeholder="Plat" onChange={event => update(step.key, item => ({...item, name: event.target.value}))} />
              </Field>
              <Field label="Minimum" className="w-24">
                <Input type="number" min={0} max={10} value={step.minChoices} disabled={!editable}
                  onChange={event => update(step.key, item => ({...item, minChoices: Math.max(0, Math.min(10, Number(event.target.value) || 0))}))} />
              </Field>
              <Field label="Maximum" className="w-24">
                <Input type="number" min={1} max={10} value={step.maxChoices} disabled={!editable}
                  onChange={event => update(step.key, item => ({...item, maxChoices: Math.max(1, Math.min(10, Number(event.target.value) || 1))}))} />
              </Field>
              {editable && (
                <span className="flex gap-1 pb-0.5">
                  <Button size="sm" variant="ghost" aria-label="Monter l’étape" disabled={index === 0} onClick={() => move(index, -1)}><ArrowUp className="size-4" /></Button>
                  <Button size="sm" variant="ghost" aria-label="Descendre l’étape" disabled={index === steps.length - 1} onClick={() => move(index, 1)}><ArrowDown className="size-4" /></Button>
                  <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Supprimer l’étape ${step.name}`} onClick={() => setSteps(current => current.filter(item => item.key !== step.key))}><Trash2 className="size-4" /></Button>
                </span>
              )}
            </div>
            <p className="text-xs text-ink-500">{step.minChoices === 0 ? 'Facultative' : 'Obligatoire'} · {step.minChoices === step.maxChoices ? `${step.maxChoices} produit${step.maxChoices > 1 ? 's' : ''}` : `de ${step.minChoices} à ${step.maxChoices} produits`}</p>

            <ul className="divide-y divide-slate-100 rounded-lg border border-slate-100">
              {step.choices.length === 0 && <li className="px-3 py-2 text-sm text-ink-500">Aucun produit proposé.</li>}
              {step.choices.map(choice => (
                <li key={`${choice.productId}/${choice.variantId ?? ''}`} className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm">
                  <span className="min-w-0 flex-1 font-medium">{choiceLabel(choice)}</span>
                  <label className="flex items-center gap-1 text-xs text-ink-500">
                    Supplément
                    <Input className="h-8 w-24" inputMode="decimal" value={choice.supplement} disabled={!editable} aria-label={`Supplément de ${choiceLabel(choice)}`}
                      onChange={event => update(step.key, item => ({...item, choices: item.choices.map(other => (other === choice ? {...other, supplement: event.target.value} : other))}))} />
                  </label>
                  {editable && (
                    <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Retirer ${choiceLabel(choice)}`}
                      onClick={() => update(step.key, item => ({...item, choices: item.choices.filter(other => other !== choice)}))}><Trash2 className="size-4" /></Button>
                  )}
                </li>
              ))}
            </ul>

            {editable && (
              <div className="flex flex-wrap items-end gap-2">
                <Field label="Proposer un produit" className="min-w-44 flex-1">
                  <Select value={pending.productId} onChange={event => {
                    const productId = event.target.value;
                    setAdding(current => ({...current, [step.key]: {productId, variantId: ''}}));
                    if ((summaryOf(productId)?.variantCount ?? 0) > 0) loadVariants(productId);
                  }}>
                    <option value="">Choisir…</option>
                    {offerable.map(item => <option key={item.id} value={item.id}>{item.name}{item.status !== 'Active' ? ' (hors vente)' : ''}</option>)}
                  </Select>
                </Field>
                {pendingHasVariants && (
                  <Field label="Variante" className="w-40">
                    <Select value={pending.variantId} onChange={event => setAdding(current => ({...current, [step.key]: {...pending, variantId: event.target.value}}))}>
                      <option value="">Choisir…</option>
                      {(pendingDetail?.variants ?? []).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
                    </Select>
                  </Field>
                )}
                <Button variant="secondary" disabled={!canAdd} onClick={() => {
                  update(step.key, item => ({...item, choices: [...item.choices, {productId: pending.productId, variantId: pending.variantId || null, supplement: '0'}]}));
                  setAdding(current => ({...current, [step.key]: {productId: '', variantId: ''}}));
                }}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
              </div>
            )}
            {problems[index] && step.choices.length + step.name.length > 0 && <p className="text-xs text-amber-700">{problems[index]}</p>}
          </div>
        );
      })}

      {duplicateName && <Alert tone="amber">Deux étapes portent le même nom.</Alert>}

      {editable && (
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" disabled={steps.length >= MaxSteps}
            onClick={() => setSteps(current => [...current, newStep(['Entrée', 'Plat', 'Dessert', 'Boisson'].find(name => !names.includes(name.toLocaleLowerCase('fr'))) ?? '')])}>
            <Plus className="size-4" aria-hidden="true" />{steps.length === 0 ? 'Faire de ce produit une formule' : 'Ajouter une étape'}
          </Button>
          {steps.length > 0 && (
            <Button busy={busy === 'save'} disabled={!dirty || problems.some(Boolean) || duplicateName} onClick={() => void save(steps)}>Enregistrer la formule</Button>
          )}
          {isFormula && (
            <Button variant="ghost" className="text-red-600" busy={busy === 'save' && steps.length === 0} onClick={() => { setSteps([]); void save([]); }}>Ce n’est plus une formule</Button>
          )}
        </div>
      )}
    </section>
  );
}
