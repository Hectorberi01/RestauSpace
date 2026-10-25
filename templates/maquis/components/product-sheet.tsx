'use client';
import {useState} from 'react';
import {Minus, Plus} from 'lucide-react';
import {linePrice, newIdempotencyKey, type CartComponent, type CartLine, type Catalog, type CatalogFormulaChoice, type CatalogFormulaStep, type CatalogModifierGroup, type CatalogProduct} from '@/lib/storefront';
import {allergenLabel, money} from '@/lib/format';
import {Button, Sheet, choiceClass, cx, inputClass} from './ui';
import {Plate} from './art';

const sameChoice = (a: {productId: string; variantId: string | null}, b: {productId: string; variantId: string | null}) =>
  a.productId === b.productId && a.variantId === b.variantId;

const rule = (min: number, max: number) =>
  min === 0 ? `jusqu’à ${max}, facultatif` : min === max ? `${min} obligatoire${min > 1 ? 's' : ''}` : `${min} à ${max}`;

/** Groupes d'options et leurs règles min/max ; un groupe à choix unique se coche comme un bouton radio. */
function ModifierFieldsets({groups, currency, selectedIn, onToggle, compact, scope}: {
  groups: CatalogModifierGroup[];
  currency: string;
  selectedIn: (group: CatalogModifierGroup) => string[];
  onToggle: (group: CatalogModifierGroup, modifierId: string) => void;
  compact?: boolean;
  scope?: string;
}) {
  return (
    <>
      {groups.map(group => {
        const selected = selectedIn(group);
        return (
          <fieldset key={group.id} className={compact ? 'mt-3' : 'mt-5'}>
            <legend className={cx('mb-2 font-semibold', compact && 'text-sm')}>{group.name} <span className="text-sm font-normal text-muted">({rule(group.minSelections, group.maxSelections)})</span></legend>
            <div className="space-y-2">
              {group.modifiers.map(modifier => {
                const on = selected.includes(modifier.id);
                const full = !on && group.maxSelections > 1 && selected.length >= group.maxSelections;
                return (
                  <label key={modifier.id} className={choiceClass(on, cx('flex cursor-pointer items-center justify-between gap-3', compact ? 'px-3 py-2' : 'px-4 py-3', (!modifier.isAvailable || full) && 'cursor-not-allowed opacity-50'))}>
                    <span className="flex items-center gap-3">
                      <input type={group.maxSelections === 1 ? 'radio' : 'checkbox'} name={`group-${group.id}${scope ? `-${scope}` : ''}`} className="size-4 accent-[var(--primary)]"
                        checked={on} disabled={!modifier.isAvailable || full} onChange={() => onToggle(group, modifier.id)} />
                      {modifier.name}{!modifier.isAvailable && ' (épuisé)'}
                    </span>
                    {modifier.priceDelta > 0 && <span className="text-muted">+ {money(modifier.priceDelta, currency)}</span>}
                  </label>
                );
              })}
            </div>
          </fieldset>
        );
      })}
    </>
  );
}

/** Fiche produit : variante, options, étapes de formule, quantité et note pour la cuisine. */
export function ProductSheet({product, catalog, index, onClose, onAdd}: {
  product: CatalogProduct;
  catalog: Catalog;
  index: number;
  onClose: () => void;
  onAdd: (line: CartLine) => void;
}) {
  const groupsOf = (ids: string[]) => ids.map(id => catalog.modifierGroups.find(group => group.id === id)).filter((group): group is CatalogModifierGroup => !!group);
  const groups = groupsOf(product.modifierGroupIds);
  const steps = product.formulaSteps ?? [];
  const [variantId, setVariantId] = useState<string | null>(product.variants.find(variant => variant.isAvailable)?.id ?? null);
  const [chosen, setChosen] = useState<Record<string, string[]>>({});
  const [picked, setPicked] = useState<Record<string, CartComponent[]>>({});
  const [quantity, setQuantity] = useState(1);
  const [note, setNote] = useState('');
  const currency = catalog.currencyCode;

  const toggle = (group: CatalogModifierGroup, modifierId: string) => setChosen(current => {
    const list = current[group.id] ?? [];
    if (list.includes(modifierId)) return {...current, [group.id]: list.filter(id => id !== modifierId)};
    if (group.maxSelections === 1) return {...current, [group.id]: [modifierId]};
    return list.length >= group.maxSelections ? current : {...current, [group.id]: [...list, modifierId]};
  });

  const pick = (step: CatalogFormulaStep, choice: CatalogFormulaChoice) => setPicked(current => {
    const list = current[step.id] ?? [];
    if (list.some(item => sameChoice(item, choice))) return {...current, [step.id]: list.filter(item => !sameChoice(item, choice))};
    const added: CartComponent = {stepId: step.id, productId: choice.productId, variantId: choice.variantId, modifierIds: []};
    if (step.maxChoices === 1) return {...current, [step.id]: [added]};
    return list.length >= step.maxChoices ? current : {...current, [step.id]: [...list, added]};
  });

  const toggleComponent = (step: CatalogFormulaStep, component: CartComponent, group: CatalogModifierGroup, modifierId: string) => setPicked(current => ({
    ...current,
    [step.id]: (current[step.id] ?? []).map(item => {
      if (!sameChoice(item, component)) return item;
      const inGroup = item.modifierIds.filter(id => group.modifiers.some(modifier => modifier.id === id));
      if (item.modifierIds.includes(modifierId)) return {...item, modifierIds: item.modifierIds.filter(id => id !== modifierId)};
      if (group.maxSelections === 1) return {...item, modifierIds: [...item.modifierIds.filter(id => !inGroup.includes(id)), modifierId]};
      return inGroup.length >= group.maxSelections ? item : {...item, modifierIds: [...item.modifierIds, modifierId]};
    }),
  }));

  const components = steps.flatMap(step => picked[step.id] ?? []);
  const componentsReady = steps.every(step => (picked[step.id]?.length ?? 0) >= step.minChoices)
    && components.every(component => {
      const choice = steps.find(step => step.id === component.stepId)?.choices.find(item => sameChoice(item, component));
      return groupsOf(choice?.modifierGroupIds ?? []).every(group =>
        component.modifierIds.filter(id => group.modifiers.some(modifier => modifier.id === id)).length >= group.minSelections);
    });
  const missing = groups.filter(group => (chosen[group.id]?.length ?? 0) < group.minSelections);
  const line: CartLine = {
    key: newIdempotencyKey(),
    productId: product.id,
    variantId,
    modifierIds: groups.flatMap(group => chosen[group.id] ?? []),
    quantity,
    note,
    ...(steps.length > 0 ? {components} : {}),
  };
  const total = linePrice(line, catalog) ?? 0;
  const ready = product.isAvailable && (product.variants.length === 0 || variantId !== null) && missing.length === 0 && componentsReady;

  return (
    <Sheet onClose={onClose} title={product.name} footer={
      <div className="flex items-center gap-3">
        <div className="flex items-center rounded-xl border border-line">
          <button type="button" aria-label="Un de moins" className="grid size-11 place-items-center disabled:opacity-40" disabled={quantity <= 1} onClick={() => setQuantity(value => value - 1)}><Minus className="size-4" /></button>
          <span className="w-8 text-center font-semibold" aria-live="polite">{quantity}</span>
          <button type="button" aria-label="Un de plus" className="grid size-11 place-items-center disabled:opacity-40" disabled={quantity >= 99} onClick={() => setQuantity(value => value + 1)}><Plus className="size-4" /></button>
        </div>
        <Button className="flex-1" disabled={!ready} onClick={() => onAdd(line)}>
          {product.isAvailable ? `Ajouter · ${money(total, currency)}` : 'Épuisé pour aujourd’hui'}
        </Button>
      </div>
    }>
      <div className="mb-4 grid place-items-center rounded-2xl py-5" style={{background: ['#fbe2c8', '#fde8d2', '#fbdccd', '#e9efd8'][index % 4]}}>
        <Plate src={product.images[0]} index={index} size={170} muted={!product.isAvailable} />
      </div>
      {product.description && <p className="leading-relaxed text-muted">{product.description}</p>}
      <p className="mt-3 text-sm text-muted">
        <span className="font-semibold text-ink">Allergènes : </span>
        {product.allergens.length > 0 ? product.allergens.map(code => allergenLabel[code] ?? code).join(', ') : 'liste disponible sur demande au restaurant.'}
      </p>

      {product.variants.length > 0 && (
        <fieldset className="mt-5">
          <legend className="mb-2 font-semibold">Choisissez <span className="text-sm font-normal text-muted">(obligatoire)</span></legend>
          <div className="space-y-2">
            {product.variants.map(variant => (
              <label key={variant.id} className={choiceClass(variantId === variant.id, cx('flex cursor-pointer items-center justify-between gap-3 px-4 py-3', !variant.isAvailable && 'cursor-not-allowed opacity-50'))}>
                <span className="flex items-center gap-3">
                  <input type="radio" name="variant" className="size-4 accent-[var(--primary)]" checked={variantId === variant.id} disabled={!variant.isAvailable} onChange={() => setVariantId(variant.id)} />
                  {variant.name}{!variant.isAvailable && ' (épuisé)'}
                </span>
                <span className="font-medium">{money(variant.price, currency)}</span>
              </label>
            ))}
          </div>
        </fieldset>
      )}

      {steps.map(step => {
        const list = picked[step.id] ?? [];
        return (
          <fieldset key={step.id} className="mt-5">
            <legend className="mb-2 font-semibold">{step.name} <span className="text-sm font-normal text-muted">({step.minChoices === step.maxChoices && step.minChoices > 0 ? `${step.minChoices} au choix` : rule(step.minChoices, step.maxChoices)})</span></legend>
            <div className="space-y-2">
              {step.choices.map(choice => {
                const on = list.some(item => sameChoice(item, choice));
                const full = !on && step.maxChoices > 1 && list.length >= step.maxChoices;
                const component = list.find(item => sameChoice(item, choice));
                const own = groupsOf(choice.modifierGroupIds);
                return (
                  <div key={`${choice.productId}/${choice.variantId ?? ''}`} className={choiceClass(on, (!choice.isAvailable || full) && 'opacity-50')}>
                    <label className="flex cursor-pointer items-center justify-between gap-3 px-4 py-3">
                      <span className="flex items-center gap-3">
                        <input type={step.maxChoices === 1 ? 'radio' : 'checkbox'} name={`step-${step.id}`} className="size-4 accent-[var(--primary)]"
                          checked={on} disabled={!choice.isAvailable || full} onChange={() => pick(step, choice)} />
                        <span>{choice.name}{choice.variantName && <span className="text-muted"> · {choice.variantName}</span>}{!choice.isAvailable && ' (épuisé)'}</span>
                      </span>
                      {choice.supplement > 0 && <span className="text-muted">+ {money(choice.supplement, currency)}</span>}
                    </label>
                    {component && own.length > 0 && (
                      <div className="border-t border-line px-4 pb-3">
                        <ModifierFieldsets groups={own} currency={currency} compact scope={`${step.id}-${choice.productId}-${choice.variantId ?? ''}`}
                          selectedIn={group => component.modifierIds.filter(id => group.modifiers.some(modifier => modifier.id === id))}
                          onToggle={(group, id) => toggleComponent(step, component, group, id)} />
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </fieldset>
        );
      })}

      <ModifierFieldsets groups={groups} currency={currency} selectedIn={group => chosen[group.id] ?? []} onToggle={toggle} />

      <label className="mt-5 block">
        <span className="mb-2 block font-semibold">Note pour la cuisine <span className="text-sm font-normal text-muted">(facultatif)</span></span>
        <textarea value={note} onChange={event => setNote(event.target.value.slice(0, 200))} rows={2} placeholder="Sans piment, bien cuit…" className={inputClass} />
      </label>
    </Sheet>
  );
}

/** Un produit sans choix à faire s'ajoute directement, sans fiche. */
export const isSimple = (product: CatalogProduct) =>
  product.variants.length === 0 && product.modifierGroupIds.length === 0 && (product.formulaSteps?.length ?? 0) === 0;
