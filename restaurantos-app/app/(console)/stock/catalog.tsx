'use client';
import {useEffect, useMemo, useState} from 'react';
import {ChefHat, Plus, Search, Trash2} from 'lucide-react';
import {all} from '@/lib/api';
import {money, percent} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  addRecipeIngredient, changeRecipeQuantity, compatibleUnits, createIngredient, createRecipe, deactivateIngredient, listIngredients, listRecipes,
  parseQuantity, quantity, recipeCosts, removeRecipeIngredient, renameIngredient, unitShort, units, type Ingredient, type Recipe, type RecipeCost, type Unit,
} from '@/lib/inventory';
import type {ProductSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';

export function Ingredients() {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [inactive, setInactive] = useState(false);
  const {data, error, reload} = useResource(signal => listIngredients(inactive, signal), [inactive]);
  const [name, setName] = useState('');
  const [unit, setUnit] = useState<Unit>('Kilogram');
  const [editing, setEditing] = useState<Ingredient | null>(null);
  const [rename, setRename] = useState('');
  const manage = can('inventory.manage');

  return (
    <div className="space-y-4">
      {manage && (
        <Card className="p-4">
          <form className="grid grid-cols-1 items-end gap-2 sm:grid-cols-[1fr_12rem_auto]" onSubmit={event => {
            event.preventDefault();
            void run('create', async () => {
              await createIngredient(name.trim(), unit);
              setName('');
              await reload();
            }, 'Ingrédient créé.');
          }}>
            <Field label="Nouvel ingrédient"><Input required maxLength={100} value={name} onChange={event => setName(event.target.value)} placeholder="Riz parfumé" /></Field>
            <Field label="Unité de base">
              <Select value={unit} onChange={event => setUnit(event.target.value as Unit)}>
                {units.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
              </Select>
            </Field>
            <Button type="submit" busy={busy === 'create'} disabled={!name.trim()}><Plus className="size-4" aria-hidden="true" />Créer</Button>
          </form>
        </Card>
      )}
      <label className="flex items-center gap-2 text-sm text-ink-600">
        <input type="checkbox" className="size-4 accent-brand-600" checked={inactive} onChange={event => setInactive(event.target.checked)} />Afficher les ingrédients désactivés
      </label>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.length === 0 ? (
        <Card className="p-6"><Empty title="Aucun ingrédient">Créez les ingrédients que vous achetez : riz, huile, poulet, boissons…</Empty></Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {data.map(ingredient => (
              <li key={ingredient.id} className="flex items-center gap-3 px-4 py-3 text-sm">
                <span className={cx('min-w-0 flex-1 truncate font-medium', !ingredient.isActive && 'text-ink-400')}>{ingredient.name}</span>
                <span className="text-ink-500">{units.find(item => item.value === ingredient.baseUnit)?.label}</span>
                {!ingredient.isActive && <Badge>Désactivé</Badge>}
                {manage && ingredient.isActive && (
                  <div className="flex gap-1">
                    <Button size="sm" variant="ghost" onClick={() => {
                      setEditing(ingredient);
                      setRename(ingredient.name);
                    }}>Renommer</Button>
                    <Button size="sm" variant="ghost" className="text-red-600" busy={busy === ingredient.id} onClick={() => run(ingredient.id, async () => {
                      await deactivateIngredient(ingredient.id);
                      await reload();
                    }, 'Ingrédient désactivé.')}>Désactiver</Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
      <Modal open={editing !== null} onClose={() => setEditing(null)} title="Renommer l’ingrédient" footer={
        <>
          <Button variant="secondary" onClick={() => setEditing(null)}>Annuler</Button>
          <Button busy={busy === 'rename'} disabled={!rename.trim() || rename.trim() === editing?.name} onClick={() => editing && run('rename', async () => {
            await renameIngredient(editing.id, rename.trim());
            setEditing(null);
            await reload();
          }, 'Ingrédient renommé.')}>Enregistrer</Button>
        </>
      }>
        <Field label="Nom"><Input maxLength={100} value={rename} onChange={event => setRename(event.target.value)} /></Field>
      </Modal>
    </div>
  );
}

/** Coût matière d'une portion et marge brute sur le prix de vente affiché. */
export function MarginSummary({cost, price, currency, compact}: {cost: RecipeCost | undefined; price: number; currency: string; compact?: boolean}) {
  if (!cost || cost.lines.length === 0) return null;
  const margin = price > 0 ? (price - cost.cost) / price : null;
  const tone = margin === null ? 'neutral' : margin >= 0.65 ? 'green' : margin >= 0.5 ? 'amber' : 'red';
  if (compact) {
    return (
      <span className="text-right text-xs text-ink-500">
        coût {money(cost.cost, currency)}{cost.isComplete ? '' : ' +?'}
        {margin !== null && <Badge tone={tone} className="ml-1">{percent(margin)}</Badge>}
      </span>
    );
  }
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-xl bg-slate-50 px-3 py-2 text-sm">
      <span>Coût matière : <strong>{money(cost.cost, currency)}</strong></span>
      {margin !== null && <span>Marge brute : <Badge tone={tone}>{percent(margin)}</Badge> ({money(price - cost.cost, currency)})</span>}
      {!cost.isComplete && <span className="text-xs text-ink-500">Incomplet : un ingrédient n’a pas encore de coût (saisissez un prix à la réception).</span>}
    </div>
  );
}

function RecipeDialog({product, recipe, cost, ingredients, onClose, onChanged}: {
  product: ProductSummary | null; recipe: Recipe | null; cost: RecipeCost | undefined; ingredients: Ingredient[]; onClose: () => void; onChanged: () => Promise<void>;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [ingredientId, setIngredientId] = useState('');
  const [amount, setAmount] = useState('');
  const [unit, setUnit] = useState<Unit>('Gram');
  const [edits, setEdits] = useState<Record<string, string>>({});
  useEffect(() => {
    setIngredientId('');
    setAmount('');
    setEdits({});
  }, [product?.id]);
  if (!product) return null;
  const manage = can('inventory.manage');
  const used = recipe?.ingredients.map(line => line.ingredientId) ?? [];
  const available = ingredients.filter(ingredient => ingredient.isActive && !used.includes(ingredient.id));
  const chosen = available.find(ingredient => ingredient.id === ingredientId);
  const options = chosen ? compatibleUnits(chosen.baseUnit).slice().reverse() : [];

  const ensure = async () => recipe?.id ?? (await createRecipe(product.id, null)).id;

  return (
    <Modal wide open onClose={onClose} title={`Recette · ${product.name}`}>
      <div className="space-y-4">
        <p className="text-sm text-ink-600">Quantités pour <strong>une</strong> portion vendue. Chaque commande lancée en cuisine décompte ces quantités du stock de l’établissement.</p>
        <MarginSummary cost={cost} price={product.basePrice} currency={product.currencyCode} />
        {!recipe || recipe.ingredients.length === 0 ? (
          <p className="rounded-xl bg-slate-50 p-4 text-sm text-ink-500">Aucun ingrédient : ce produit ne décompte rien.</p>
        ) : (
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {recipe.ingredients.map(line => {
              const edit = edits[line.ingredientId];
              return (
                <li key={line.ingredientId} className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm">
                  <span className="min-w-0 flex-1 truncate font-medium">{line.ingredientName ?? 'Ingrédient'}</span>
                  {cost && (() => {
                    const lineCost = cost.lines.find(entry => entry.ingredientId === line.ingredientId)?.cost;
                    return <span className="text-xs text-ink-500 tabular-nums">{lineCost == null ? 'coût ?' : money(lineCost, product.currencyCode)}</span>;
                  })()}
                  {manage ? (
                    <form className="flex items-center gap-1" onSubmit={event => {
                      event.preventDefault();
                      const parsed = parseQuantity(edit ?? '');
                      if (parsed !== null) void run(line.ingredientId, async () => {
                        await changeRecipeQuantity(recipe.id, line.ingredientId, {amount: parsed, unit: line.unit});
                        setEdits(current => {
                          const next = {...current};
                          delete next[line.ingredientId];
                          return next;
                        });
                        await onChanged();
                      }, 'Quantité enregistrée.');
                    }}>
                      <Input aria-label={`Quantité de ${line.ingredientName}`} inputMode="decimal" className="w-24" value={edit ?? String(line.quantity)}
                        onChange={event => setEdits(current => ({...current, [line.ingredientId]: event.target.value}))} />
                      <span className="w-8 text-ink-500">{unitShort(line.unit)}</span>
                      {edit !== undefined && parseQuantity(edit) !== line.quantity && <Button type="submit" size="sm" variant="secondary" busy={busy === line.ingredientId}>OK</Button>}
                    </form>
                  ) : <span>{quantity(line.quantity, line.unit)}</span>}
                  {manage && (
                    <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Retirer ${line.ingredientName}`} busy={busy === `rm-${line.ingredientId}`}
                      onClick={() => run(`rm-${line.ingredientId}`, async () => {
                        await removeRecipeIngredient(recipe.id, line.ingredientId);
                        await onChanged();
                      }, 'Ingrédient retiré.')}><Trash2 className="size-4" /></Button>
                  )}
                </li>
              );
            })}
          </ul>
        )}
        {manage && (
          <form className="grid grid-cols-1 items-end gap-2 rounded-xl bg-slate-50 p-3 sm:grid-cols-[1fr_7rem_6rem_auto]" onSubmit={event => {
            event.preventDefault();
            const parsed = parseQuantity(amount);
            if (parsed !== null && chosen) void run('add', async () => {
              const id = await ensure();
              await addRecipeIngredient(id, chosen.id, {amount: parsed, unit});
              setIngredientId('');
              setAmount('');
              await onChanged();
            }, 'Ingrédient ajouté à la recette.');
          }}>
            <Field label="Ingrédient">
              <Select required value={ingredientId} onChange={event => {
                setIngredientId(event.target.value);
                const next = available.find(ingredient => ingredient.id === event.target.value);
                if (next) setUnit(compatibleUnits(next.baseUnit).slice().reverse()[0]);
              }}>
                <option value="">Choisir</option>
                {available.map(ingredient => <option key={ingredient.id} value={ingredient.id}>{ingredient.name}</option>)}
              </Select>
            </Field>
            <Field label="Quantité"><Input inputMode="decimal" required value={amount} onChange={event => setAmount(event.target.value)} placeholder="150" /></Field>
            <Field label="Unité">
              <Select value={unit} disabled={!chosen} onChange={event => setUnit(event.target.value as Unit)}>
                {(options.length ? options : [unit]).map(option => <option key={option} value={option}>{unitShort(option)}</option>)}
              </Select>
            </Field>
            <Button type="submit" variant="secondary" busy={busy === 'add'} disabled={!chosen || !(parseQuantity(amount)! > 0)}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
          </form>
        )}
      </div>
    </Modal>
  );
}

export function Recipes() {
  const {location} = useConsole();
  const [search, setSearch] = useState('');
  const [openId, setOpenId] = useState<string | null>(null);
  const products = useResource(() => all<ProductSummary>('/catalog/products', {}, 1000), []);
  const recipes = useResource(signal => listRecipes(signal), []);
  const ingredients = useResource(signal => listIngredients(false, signal), []);
  const costs = useResource(signal => (location ? recipeCosts(location.id, signal) : Promise.resolve([])), [location?.id, recipes.data]);
  const costByRecipe = useMemo(() => new Map((costs.data ?? []).map(cost => [cost.recipeId, cost])), [costs.data]);
  const byProduct = useMemo(() => new Map((recipes.data ?? []).filter(recipe => !recipe.variantId).map(recipe => [recipe.productId, recipe])), [recipes.data]);
  const list = (products.data ?? [])
    .filter(product => product.status !== 'Archived')
    .filter(product => product.name.toLowerCase().includes(search.trim().toLowerCase()));
  const opened = list.find(product => product.id === openId) ?? null;
  const withRecipe = (products.data ?? []).filter(product => (byProduct.get(product.id)?.ingredients.length ?? 0) > 0).length;

  if (products.error || recipes.error) return <Alert>{products.error ?? recipes.error}</Alert>;
  if (!products.data || !recipes.data) return <Spinner />;

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
          <Input type="search" aria-label="Chercher un produit" className="pl-9" value={search} onChange={event => setSearch(event.target.value)} placeholder="Chercher un produit" />
        </div>
        <p className="text-sm text-ink-500">{withRecipe} produit{withRecipe > 1 ? 's' : ''} sur {products.data.length} avec une recette</p>
      </div>
      {ingredients.data && ingredients.data.length === 0 && <Alert tone="blue">Créez d’abord des ingrédients (onglet Ingrédients).</Alert>}
      {list.length === 0 ? <Card className="p-6"><Empty icon={<ChefHat className="size-8" />} title="Aucun produit">Ajoutez des produits dans « Menu & produits ».</Empty></Card> : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {list.map(product => {
              const recipe = byProduct.get(product.id);
              const lines = recipe?.ingredients ?? [];
              return (
                <li key={product.id}>
                  <button type="button" onClick={() => setOpenId(product.id)} className="flex w-full items-center gap-3 px-4 py-3 text-left text-sm hover:bg-slate-50">
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium">{product.name}</span>
                      <span className="block truncate text-ink-500">{lines.length ? lines.map(line => `${line.ingredientName} ${quantity(line.quantity, line.unit)}`).join(' · ') : 'Pas de recette'}</span>
                    </span>
                    <span className="flex flex-col items-end">
                      <span className="text-ink-500">{money(product.basePrice, product.currencyCode)}</span>
                      {recipe && <MarginSummary compact cost={costByRecipe.get(recipe.id)} price={product.basePrice} currency={product.currencyCode} />}
                    </span>
                    {lines.length > 0 ? <Badge tone="green">{lines.length} ingr.</Badge> : <Badge>À faire</Badge>}
                  </button>
                </li>
              );
            })}
          </ul>
        </Card>
      )}
      {location && <p className="text-xs text-ink-500">Coût matière calculé avec les coûts moyens du stock de {location.name}.</p>}
      <RecipeDialog product={opened} recipe={opened ? byProduct.get(opened.id) ?? null : null}
        cost={opened ? costByRecipe.get(byProduct.get(opened.id)?.id ?? '') : undefined} ingredients={ingredients.data ?? []}
        onClose={() => setOpenId(null)} onChanged={recipes.reload} />
    </div>
  );
}
