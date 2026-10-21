'use client';
import {useEffect, useMemo, useState, type FormEvent} from 'react';
import {Minus, Plus} from 'lucide-react';
import {productDetail} from '@/lib/catalog';
import type {ComponentChoice} from '@/lib/formulas';
import {money} from '@/lib/format';
import type {FormulaStep, ModifierGroup, Product} from '@/lib/types';
import {Alert, Button, Field, Input, Modal, Spinner, cx} from './ui';

export interface ItemChoice {
  productId: string;
  quantity: number;
  variantId: string | null;
  modifierIds: string[];
  note: string | null;
  /** Formule : les produits choisis à chaque étape. */
  components?: ComponentChoice[];
}

type Picked = Record<string, ComponentChoice[]>;

const choiceKey = (choice: {productId: string; variantId: string | null}) => `${choice.productId}/${choice.variantId ?? ''}`;

const stepRule = (step: FormulaStep) =>
  step.minChoices === 0 ? `facultatif, jusqu’à ${step.maxChoices}`
    : step.minChoices === step.maxChoices ? (step.minChoices === 1 ? '1 au choix' : `${step.minChoices} au choix`)
      : `${step.minChoices} à ${step.maxChoices}`;

const groupRule = (group: ModifierGroup) =>
  group.minSelections > 0 ? `obligatoire, ${group.minSelections === group.maxSelections ? group.minSelections : `${group.minSelections} à ${group.maxSelections}`}` : `jusqu’à ${group.maxSelections}`;

/** Bascule d'une option dans un groupe : remplace le choix d'un groupe à choix unique, plafonne les autres. */
function toggleIn(group: ModifierGroup, list: string[], id: string): string[] {
  if (list.includes(id)) return list.filter(item => item !== id);
  const own = list.filter(item => group.modifiers.some(modifier => modifier.id === item));
  if (group.maxSelections === 1) return [...list.filter(item => !own.includes(item)), id];
  if (own.length >= group.maxSelections) return list;
  return [...list, id];
}

function OptionGroups({groups, selected, currency, onToggle, compact}: {
  groups: ModifierGroup[];
  selected: string[];
  currency: string;
  onToggle: (group: ModifierGroup, id: string) => void;
  compact?: boolean;
}) {
  return (
    <>
      {groups.map(group => (
        <fieldset key={group.id}>
          <legend className={cx('mb-2 font-semibold', compact ? 'text-xs' : 'text-sm')}>
            {group.name} <span className="font-normal text-ink-500">{groupRule(group)}</span>
          </legend>
          <div className="grid gap-2 sm:grid-cols-2">
            {group.modifiers.filter(modifier => modifier.isAvailable).map(modifier => {
              const checked = selected.includes(modifier.id);
              return (
                <label key={modifier.id} className={cx('flex cursor-pointer items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm', checked ? 'border-brand-500 bg-brand-50' : 'border-slate-200 bg-white')}>
                  <span className="flex items-center gap-2"><input type="checkbox" checked={checked} onChange={() => onToggle(group, modifier.id)} />{modifier.name}</span>
                  {modifier.priceDelta !== 0 && <span className="text-ink-600">+{money(modifier.priceDelta, currency)}</span>}
                </label>
              );
            })}
          </div>
        </fieldset>
      ))}
    </>
  );
}

/** Prix unitaire (variante et options comprises) et libellés choisis, pour un panier tenu avant la commande. */
export interface ItemSummary {
  unitPrice: number;
  details: string[];
}

/**
 * Choix de la variante, des options et de la quantité d'un produit avant de l'ajouter à une commande. Pour une formule :
 * les produits de chaque étape, chacun avec ses propres options.
 */
export function ItemOptions({product, groups, currency, onClose, onAdd, initial}: {
  product: Product;
  groups: ModifierGroup[];
  currency: string;
  onClose: () => void;
  onAdd: (body: ItemChoice, summary: ItemSummary) => Promise<boolean>;
  /** Ligne existante à recomposer : ses choix sont repris. */
  initial?: {variantId: string | null; modifierIds: string[]; quantity: number; note: string | null; components?: ComponentChoice[]};
}) {
  const steps = useMemo(() => product.formulaSteps ?? [], [product.formulaSteps]);
  const variants = product.variants.filter(variant => variant.isAvailable);
  const attached = groups.filter(group => product.modifierGroupIds.includes(group.id));
  const [variantId, setVariantId] = useState(initial?.variantId ?? variants[0]?.id);
  const [selected, setSelected] = useState<Record<string, string[]>>(() =>
    Object.fromEntries(attached.map(group => [group.id, group.modifiers.filter(modifier => initial?.modifierIds.includes(modifier.id)).map(modifier => modifier.id)])));
  const [quantity, setQuantity] = useState(initial?.quantity ?? 1);
  const [note, setNote] = useState(initial?.note ?? '');
  const [busy, setBusy] = useState(false);
  const [details, setDetails] = useState<Map<string, Product> | null>(steps.length > 0 ? null : new Map());
  const [detailError, setDetailError] = useState<string | null>(null);
  const [picked, setPicked] = useState<Picked>(() =>
    Object.fromEntries(steps.map(step => [step.id, (initial?.components ?? []).filter(component => component.stepId === step.id)])));

  useEffect(() => {
    if (steps.length === 0) return;
    const ids = [...new Set(steps.flatMap(step => step.choices.map(choice => choice.productId)))];
    Promise.all(ids.map(id => productDetail(id).catch(() => null)))
      .then(found => setDetails(new Map(found.filter((item): item is Product => !!item).map(item => [item.id, item]))))
      .catch(() => setDetailError('Les produits de la formule n’ont pas pu être chargés.'));
  }, [steps]);

  const choiceName = (choice: {productId: string; variantId: string | null}) => {
    const detail = details?.get(choice.productId);
    const variant = choice.variantId ? detail?.variants.find(item => item.id === choice.variantId) : undefined;
    return {name: detail?.name ?? 'Produit', variant: variant?.name ?? null, available: !!detail && detail.isSellable && (!choice.variantId || !!variant?.isAvailable)};
  };
  const groupsOf = (productId: string) => groups.filter(group => details?.get(productId)?.modifierGroupIds.includes(group.id));
  const supplementOf = (step: FormulaStep, component: ComponentChoice) =>
    step.choices.find(choice => choiceKey(choice) === choiceKey(component))?.supplement ?? 0;
  const pick = (step: FormulaStep, choice: {productId: string; variantId: string | null}) =>
    setPicked(current => {
      const list = current[step.id] ?? [];
      if (list.some(item => choiceKey(item) === choiceKey(choice))) return {...current, [step.id]: list.filter(item => choiceKey(item) !== choiceKey(choice))};
      const added: ComponentChoice = {stepId: step.id, productId: choice.productId, variantId: choice.variantId, modifierIds: []};
      if (step.maxChoices === 1) return {...current, [step.id]: [added]};
      if (list.length >= step.maxChoices) return current;
      return {...current, [step.id]: [...list, added]};
    });
  const toggleComponent = (step: FormulaStep, component: ComponentChoice, group: ModifierGroup, id: string) =>
    setPicked(current => ({
      ...current,
      [step.id]: (current[step.id] ?? []).map(item => (choiceKey(item) === choiceKey(component) ? {...item, modifierIds: toggleIn(group, item.modifierIds, id)} : item)),
    }));
  const components = steps.flatMap(step => picked[step.id] ?? []);
  const stepsMissing = steps.filter(step => (picked[step.id]?.length ?? 0) < step.minChoices);
  const componentsMissing = components.filter(component =>
    groupsOf(component.productId).some(group => group.modifiers.filter(modifier => component.modifierIds.includes(modifier.id)).length < group.minSelections));
  const componentsPrice = steps.reduce((sum, step) => sum + (picked[step.id] ?? []).reduce((total, component) =>
    total + supplementOf(step, component) + groupsOf(component.productId).flatMap(group => group.modifiers).filter(modifier => component.modifierIds.includes(modifier.id)).reduce((add, modifier) => add + modifier.priceDelta, 0), 0), 0);

  const missing = [...attached.filter(group => (selected[group.id]?.length ?? 0) < group.minSelections), ...stepsMissing, ...componentsMissing];
  const toggle = (group: ModifierGroup, id: string) =>
    setSelected(current => {
      const list = current[group.id] ?? [];
      if (list.includes(id)) return {...current, [group.id]: list.filter(item => item !== id)};
      if (group.maxSelections === 1) return {...current, [group.id]: [id]};
      if (list.length >= group.maxSelections) return current;
      return {...current, [group.id]: [...list, id]};
    });
  const unit =
    (variants.find(variant => variant.id === variantId)?.price ?? product.basePrice) +
    attached.flatMap(group => group.modifiers.filter(modifier => selected[group.id]?.includes(modifier.id))).reduce((sum, modifier) => sum + modifier.priceDelta, 0) +
    componentsPrice;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    const chosen = attached.flatMap(group => group.modifiers.filter(modifier => selected[group.id]?.includes(modifier.id)));
    const variant = variants.find(item => item.id === variantId);
    const componentDetails = components.map(component => {
      const label = choiceName(component);
      const options = groupsOf(component.productId).flatMap(group => group.modifiers).filter(modifier => component.modifierIds.includes(modifier.id)).map(modifier => modifier.name);
      return `${label.name}${label.variant ? ` (${label.variant})` : ''}${options.length ? ` + ${options.join(', ')}` : ''}`;
    });
    const ok = await onAdd(
      {
        productId: product.id, quantity, variantId: variantId ?? null, modifierIds: Object.values(selected).flat(), note: note.trim() || null,
        ...(steps.length > 0 ? {components} : {}),
      },
      {unitPrice: unit, details: [variant?.name, ...componentDetails, ...chosen.map(modifier => modifier.name)].filter((item): item is string => !!item)},
    );
    setBusy(false);
    if (ok) onClose();
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={product.name}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Annuler</Button>
          <Button type="submit" form="item-options" busy={busy} disabled={missing.length > 0}>{initial ? 'Enregistrer' : 'Ajouter'} · {money(unit * quantity, currency)}</Button>
        </>
      }>
      <form id="item-options" onSubmit={submit} className="space-y-5">
        {variants.length > 0 && (
          <fieldset>
            <legend className="mb-2 text-sm font-semibold">Variante</legend>
            <div className="grid gap-2 sm:grid-cols-2">
              {variants.map(variant => (
                <label key={variant.id} className={cx('flex cursor-pointer items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm', variantId === variant.id ? 'border-brand-500 bg-brand-50' : 'border-slate-200')}>
                  <span className="flex items-center gap-2"><input type="radio" name="variant" checked={variantId === variant.id} onChange={() => setVariantId(variant.id)} />{variant.name}</span>
                  <span className="font-semibold">{money(variant.price, currency)}</span>
                </label>
              ))}
            </div>
          </fieldset>
        )}
        {steps.length > 0 && detailError && <Alert>{detailError}</Alert>}
        {steps.length > 0 && !details && !detailError && <Spinner label="Chargement de la formule…" />}
        {details && steps.map(step => {
          const list = picked[step.id] ?? [];
          return (
            <fieldset key={step.id}>
              <legend className="mb-2 text-sm font-semibold">{step.name} <span className="font-normal text-ink-500">{stepRule(step)}</span></legend>
              <div className="grid gap-2 sm:grid-cols-2">
                {step.choices.map(choice => {
                  const label = choiceName(choice);
                  const checked = list.some(item => choiceKey(item) === choiceKey(choice));
                  return (
                    <label key={choiceKey(choice)} className={cx('flex items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm', checked ? 'border-brand-500 bg-brand-50' : 'border-slate-200', label.available || checked ? 'cursor-pointer' : 'cursor-not-allowed opacity-50')}>
                      <span className="flex items-center gap-2">
                        <input type={step.maxChoices === 1 ? 'radio' : 'checkbox'} name={`step-${step.id}`} checked={checked} disabled={!label.available && !checked} onChange={() => pick(step, choice)} />
                        <span>{label.name}{label.variant && <span className="text-ink-500"> · {label.variant}</span>}{!label.available && <span className="block text-xs text-ink-500">Indisponible</span>}</span>
                      </span>
                      {choice.supplement > 0 && <span className="text-ink-600">+{money(choice.supplement, currency)}</span>}
                    </label>
                  );
                })}
              </div>
              {list.filter(component => groupsOf(component.productId).length > 0).map(component => (
                <div key={choiceKey(component)} className="mt-3 space-y-3 rounded-xl bg-slate-50 p-3">
                  <p className="text-xs font-semibold text-ink-600">Options · {choiceName(component).name}</p>
                  <OptionGroups groups={groupsOf(component.productId)} selected={component.modifierIds} currency={currency} compact
                    onToggle={(group, id) => toggleComponent(step, component, group, id)} />
                </div>
              ))}
            </fieldset>
          );
        })}
        <OptionGroups groups={attached} selected={Object.values(selected).flat()} currency={currency} onToggle={toggle} />
        <div className="flex items-end gap-4">
          <Field label="Quantité">
            <div className="flex items-center gap-2">
              <Button variant="secondary" size="sm" aria-label="Moins" onClick={() => setQuantity(q => Math.max(1, q - 1))}><Minus className="size-4" /></Button>
              <span className="w-8 text-center font-semibold">{quantity}</span>
              <Button variant="secondary" size="sm" aria-label="Plus" onClick={() => setQuantity(q => Math.min(99, q + 1))}><Plus className="size-4" /></Button>
            </div>
          </Field>
        </div>
        <Field label="Note pour la cuisine">
          <Input value={note} maxLength={200} onChange={event => setNote(event.target.value)} placeholder="Sans piment, bien cuit…" />
        </Field>
      </form>
    </Modal>
  );
}
