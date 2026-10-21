'use client';
import {useEffect, useMemo, useState} from 'react';
import {AlertTriangle, ArrowRightLeft, Boxes, ClipboardCheck, Coins, LogOut, PackagePlus, Plus, Search, Trash2, Warehouse} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  compatibleUnits, consume, count, createIngredient, createItem, listAllItems, listIngredients, listItems, listMovements, movementLabel, parseMoney,
  parseQuantity, quantity, receive, setPolicy, setThreshold, setUnitCost, transfer, unitCost, unitShort, units, waste, wasteReasons, type StockItem, type Unit,
} from '@/lib/inventory';
import {money} from '@/lib/format';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, StatCard, cx, useAction} from '@/components/ui';

const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

type Action = 'receipt' | 'exit' | 'waste' | 'count' | 'transfer';

function QuantityField({label, unit, value, chosen, onValue, onUnit, hint}: {
  label: string; unit: Unit; value: string; chosen: Unit; onValue: (value: string) => void; onUnit: (unit: Unit) => void; hint?: string;
}) {
  const options = compatibleUnits(unit);
  return (
    <Field label={label} hint={hint}>
      <div className="flex gap-2">
        <div className="min-w-0 flex-1"><Input inputMode="decimal" required value={value} onChange={event => onValue(event.target.value)} placeholder="0" /></div>
        {options.length > 1 ? (
          <Select aria-label="Unité" className="w-24! shrink-0" value={chosen} onChange={event => onUnit(event.target.value as Unit)}>
            {options.map(option => <option key={option} value={option}>{unitShort(option)}</option>)}
          </Select>
        ) : <span className="grid w-16 shrink-0 place-items-center rounded-xl bg-slate-100 text-sm text-ink-600">{unitShort(unit)}</span>}
      </div>
    </Field>
  );
}

function ItemDialog({item, initial, onClose, onChanged}: {item: StockItem | null; initial: Action; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can, locations, currency} = useConsole();
  const {busy, run} = useAction();
  const [action, setAction] = useState<Action>(initial);
  const [amount, setAmount] = useState('');
  const [paid, setPaid] = useState('');
  const [cost, setCost] = useState('');
  const [unit, setUnit] = useState<Unit>('Piece');
  const [note, setNote] = useState('');
  const [target, setTarget] = useState('');
  const [threshold, setThresholdValue] = useState('');
  const [page, setPage] = useState(1);
  useEffect(() => {
    if (!item) return;
    setAction(initial);
    setAmount('');
    setUnit(item.unit);
    setNote(initial === 'waste' ? wasteReasons[0] : '');
    setThresholdValue(String(item.reorderThreshold));
    setPaid('');
    setPage(1);
    // Seulement quand un autre article s'ouvre.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [item?.id, initial]);
  useEffect(() => {
    setCost(item?.averageUnitCost == null ? '' : String(item.averageUnitCost));
  }, [item?.id, item?.averageUnitCost]);
  const others = useResource(
    signal => (item && locations.length > 1 ? listAllItems(signal).then(list => list.filter(other => other.ingredientId === item.ingredientId && other.id !== item.id)) : Promise.resolve([])),
    [item?.id, locations.length],
  );
  const history = useResource(signal => (item ? listMovements(item.id, page, signal) : Promise.resolve(null)), [item?.id, item?.currentQuantity, page]);
  if (!item) return null;

  const operate = can('inventory.operate');
  const manage = can('inventory.manage');
  const value = parseQuantity(amount);
  const name = item.ingredientName ?? 'Ingrédient';
  const actions: {value: Action; label: string; icon: typeof Plus}[] = [
    {value: 'receipt', label: 'Réception', icon: PackagePlus},
    {value: 'exit', label: 'Sortie', icon: LogOut},
    {value: 'waste', label: 'Perte', icon: Trash2},
    {value: 'count', label: 'Inventaire', icon: ClipboardCheck},
    ...((others.data ?? []).length > 0 ? [{value: 'transfer' as Action, label: 'Transfert', icon: ArrowRightLeft}] : []),
  ];
  const locationName = (id: string) => locations.find(location => location.id === id)?.name ?? 'Établissement';
  const invalid = value === null || value < 0 || (action !== 'count' && value === 0) || (action === 'waste' && !note.trim())
    || (action === 'count' && !note.trim()) || (action === 'transfer' && !target) || (action === 'receipt' && Number.isNaN(parseMoney(paid)));
  const parsedCost = parseMoney(cost);

  const submit = () => run('move', async () => {
    const input = {amount: value!, unit};
    if (action === 'receipt') await receive(item.id, input, note.trim() || null, parseMoney(paid));
    if (action === 'exit') await consume(item.id, input, note.trim() || null);
    if (action === 'waste') await waste(item.id, input, note.trim());
    if (action === 'count') await count(item.id, input, note.trim());
    if (action === 'transfer') await transfer(item.id, target, input);
    setAmount('');
    setPaid('');
    setUnit(item.unit);
    await onChanged();
  }, {receipt: 'Réception enregistrée.', exit: 'Sortie enregistrée.', waste: 'Perte enregistrée.', count: 'Inventaire enregistré.', transfer: 'Transfert enregistré.'}[action]);

  return (
    <Modal wide open onClose={onClose} title={name}>
      <div className="space-y-5">
        <div className="flex flex-wrap items-end gap-x-6 gap-y-2">
          <div>
            <p className="text-sm text-ink-500">En stock</p>
            <p className={cx('text-[1.75rem] leading-tight font-bold tracking-tight', item.currentQuantity < 0 ? 'text-red-700' : item.isLowStock ? 'text-amber-700' : 'text-ink-900')}>{quantity(item.currentQuantity, item.unit)}</p>
          </div>
          <p className="text-sm text-ink-500">Seuil d’alerte : {quantity(item.reorderThreshold, item.unit)}</p>
          {item.averageUnitCost !== null && (
            <p className="text-sm text-ink-500">Coût moyen : {unitCost(item.averageUnitCost, item.unit, currency)} · valeur {money(Math.max(item.currentQuantity, 0) * item.averageUnitCost, currency)}</p>
          )}
          {item.isLowStock && <Badge tone="amber">À réapprovisionner</Badge>}
        </div>

        {operate && (
          <section className="rounded-2xl border border-slate-200 p-4">
            <div className="mb-3 flex flex-wrap gap-1" role="tablist" aria-label="Mouvement">
              {actions.map(option => (
                <button key={option.value} type="button" role="tab" aria-selected={action === option.value} onClick={() => {
                  setAction(option.value);
                  setNote(option.value === 'waste' ? wasteReasons[0] : '');
                }}
                  className={cx('flex items-center gap-1.5 rounded-xl px-3 py-2 text-sm font-semibold', action === option.value ? 'bg-brand-50 text-brand-700' : 'text-ink-600 hover:bg-slate-100')}>
                  <option.icon className="size-4" aria-hidden="true" />{option.label}
                </button>
              ))}
            </div>
            <form className="grid grid-cols-1 gap-3 sm:grid-cols-2" onSubmit={event => {
              event.preventDefault();
              void submit();
            }}>
              <QuantityField label={action === 'count' ? 'Quantité comptée' : 'Quantité'} unit={item.unit} value={amount} chosen={unit} onValue={setAmount} onUnit={setUnit}
                hint={action === 'count' ? 'Le stock est remplacé par ce chiffre ; l’écart est enregistré.' : undefined} />
              {action === 'receipt' && <Field label="Référence (facultatif)" hint="N° de bon de livraison, fournisseur…"><Input maxLength={100} value={note} onChange={event => setNote(event.target.value)} /></Field>}
              {action === 'receipt' && (
                <Field label={`Prix payé pour cette quantité (facultatif${currency ? `, ${currency}` : ''})`} hint="Met à jour le coût moyen de l’article, utilisé pour le coût matière des recettes.">
                  <Input inputMode="decimal" value={paid} onChange={event => setPaid(event.target.value)} placeholder="0" />
                </Field>
              )}
              {action === 'exit' && <Field label="Motif ou destination (facultatif)" hint="Repas du personnel, prélèvement pour un événement…"><Input maxLength={100} value={note} onChange={event => setNote(event.target.value)} /></Field>}
              {action === 'waste' && (
                <Field label="Motif">
                  <Select value={note} onChange={event => setNote(event.target.value)}>
                    {wasteReasons.map(reason => <option key={reason} value={reason}>{reason}</option>)}
                  </Select>
                </Field>
              )}
              {action === 'count' && <Field label="Motif"><Input required maxLength={200} value={note} onChange={event => setNote(event.target.value)} placeholder="Inventaire hebdomadaire" /></Field>}
              {action === 'transfer' && (
                <Field label="Vers">
                  <Select required value={target} onChange={event => setTarget(event.target.value)}>
                    <option value="">Choisir un établissement</option>
                    {(others.data ?? []).map(other => <option key={other.id} value={other.id}>{locationName(other.locationId)} ({quantity(other.currentQuantity, other.unit)})</option>)}
                  </Select>
                </Field>
              )}
              <div className="sm:col-span-2">
                <Button type="submit" busy={busy === 'move'} disabled={invalid}>
                  {{receipt: 'Enregistrer la réception', exit: 'Enregistrer la sortie', waste: 'Enregistrer la perte', count: 'Enregistrer l’inventaire', transfer: 'Transférer'}[action]}
                </Button>
              </div>
            </form>
          </section>
        )}

        {manage && (
          <section className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <form className="flex items-end gap-2" onSubmit={event => {
              event.preventDefault();
              const parsed = parseQuantity(threshold);
              if (parsed !== null) void run('threshold', async () => {
                await setThreshold(item.id, {amount: parsed, unit: item.unit});
                await onChanged();
              }, 'Seuil enregistré.');
            }}>
              <Field label={`Seuil d’alerte (${unitShort(item.unit)})`} className="flex-1"><Input inputMode="decimal" value={threshold} onChange={event => setThresholdValue(event.target.value)} /></Field>
              <Button type="submit" variant="secondary" busy={busy === 'threshold'} disabled={parseQuantity(threshold) === null || parseQuantity(threshold) === item.reorderThreshold}>OK</Button>
            </form>
            <form className="flex items-end gap-2" onSubmit={event => {
              event.preventDefault();
              if (!Number.isNaN(parsedCost)) void run('cost', async () => {
                await setUnitCost(item.id, parsedCost);
                await onChanged();
              }, 'Coût enregistré.');
            }}>
              <Field label={`Coût unitaire (${currency ? `${currency} / ` : ''}${unitShort(item.unit)})`} className="flex-1" hint="Calculé à chaque réception chiffrée ; corrigez-le ici si besoin.">
                <Input inputMode="decimal" value={cost} onChange={event => setCost(event.target.value)} placeholder="Inconnu" />
              </Field>
              <Button type="submit" variant="secondary" busy={busy === 'cost'} disabled={Number.isNaN(parsedCost) || parsedCost === item.averageUnitCost}>OK</Button>
            </form>
            <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3 text-sm sm:col-span-2">
              <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={item.negativeStockPolicy === 'Allow'} disabled={busy === 'policy'}
                onChange={event => void run('policy', async () => {
                  await setPolicy(item.id, event.target.checked ? 'Allow' : 'Forbid');
                  await onChanged();
                }, 'Réglage enregistré.')} />
              <span><span className="font-medium">Autoriser le stock négatif</span><span className="block text-xs text-ink-500">Les ventes continuent même si le stock affiché est épuisé.</span></span>
            </label>
          </section>
        )}

        <section>
          <h3 className="mb-2 font-semibold">Mouvements</h3>
          {history.error ? <Alert>{history.error}</Alert> : !history.data ? <Spinner /> : history.data.items.length === 0 ? (
            <p className="text-sm text-ink-500">Aucun mouvement.</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {history.data.items.map(movement => (
                <li key={movement.id} className="flex items-center gap-3 py-2 text-sm">
                  <div className="min-w-0 flex-1">
                    <p className="font-medium">{movementLabel[movement.type]}</p>
                    <p className="truncate text-xs text-ink-500">{stamp(movement.occurredAtUtc)}{movement.reason ? ` · ${movement.reason}` : ''}{movement.reference && (movement.type === 'Receipt' || movement.type === 'Consumption') ? ` · ${movement.reference}` : ''}{movement.unitCost !== null ? ` · ${unitCost(movement.unitCost, movement.unit, currency)}` : ''}</p>
                  </div>
                  <span className={cx('font-semibold tabular-nums', movement.quantityDelta > 0 ? 'text-emerald-700' : movement.quantityDelta < 0 ? 'text-red-700' : 'text-ink-500')}>
                    {movement.quantityDelta > 0 ? '+' : ''}{quantity(movement.quantityDelta, movement.unit)}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {history.data && (page > 1 || history.data.hasMore) && (
            <div className="mt-2 flex justify-end gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récents</Button>
              <Button size="sm" variant="secondary" disabled={!history.data.hasMore} onClick={() => setPage(page + 1)}>Plus anciens</Button>
            </div>
          )}
        </section>
      </div>
    </Modal>
  );
}

function AddItemDialog({open, locationId, stocked, onClose, onDone}: {open: boolean; locationId: string; stocked: string[]; onClose: () => void; onDone: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const ingredients = useResource(signal => (open ? listIngredients(false, signal) : Promise.resolve(null)), [open]);
  const [ingredientId, setIngredientId] = useState('');
  const [threshold, setThresholdValue] = useState('');
  const [allowNegative, setAllowNegative] = useState(true);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [baseUnit, setBaseUnit] = useState<Unit>('Kilogram');
  useEffect(() => {
    if (!open) return;
    setIngredientId('');
    setThresholdValue('');
    setAllowNegative(true);
    setCreating(false);
    setName('');
  }, [open]);
  const available = (ingredients.data ?? []).filter(ingredient => !stocked.includes(ingredient.id));
  const chosen = available.find(ingredient => ingredient.id === ingredientId);
  const unit = creating ? baseUnit : chosen?.baseUnit;

  return (
    <Modal open={open} onClose={onClose} title="Suivre un ingrédient ici" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="add-item" busy={busy === 'add'} disabled={creating ? !name.trim() : !ingredientId}>Ajouter au stock</Button>
      </>
    }>
      <form id="add-item" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('add', async () => {
          const id = creating ? (await createIngredient(name.trim(), baseUnit)).id : ingredientId;
          const amount = parseQuantity(threshold);
          await createItem(locationId, id, amount !== null && unit ? {amount, unit} : null, allowNegative ? 'Allow' : 'Forbid');
          await onDone();
          onClose();
        }, 'Ingrédient ajouté au stock.');
      }}>
        {creating ? (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_10rem]">
            <Field label="Nouvel ingrédient"><Input required maxLength={100} value={name} onChange={event => setName(event.target.value)} placeholder="Poulet entier" /></Field>
            <Field label="Unité de base">
              <Select value={baseUnit} onChange={event => setBaseUnit(event.target.value as Unit)}>
                {units.map(item => <option key={item.value} value={item.value}>{item.label}</option>)}
              </Select>
            </Field>
          </div>
        ) : (
          <Field label="Ingrédient">
            {!ingredients.data ? <Spinner /> : (
              <Select required value={ingredientId} onChange={event => setIngredientId(event.target.value)}>
                <option value="">{available.length ? 'Choisir' : 'Tous les ingrédients sont déjà suivis ici'}</option>
                {available.map(ingredient => <option key={ingredient.id} value={ingredient.id}>{ingredient.name} ({unitShort(ingredient.baseUnit)})</option>)}
              </Select>
            )}
          </Field>
        )}
        {can('inventory.manage') && (
          <button type="button" className="text-sm font-semibold text-brand-700 hover:underline" onClick={() => setCreating(!creating)}>
            {creating ? 'Choisir un ingrédient existant' : '+ Créer un nouvel ingrédient'}
          </button>
        )}
        <Field label={`Seuil d’alerte${unit ? ` (${unitShort(unit)})` : ''}`} hint="En dessous, l’ingrédient apparaît dans la liste à réapprovisionner.">
          <Input inputMode="decimal" value={threshold} onChange={event => setThresholdValue(event.target.value)} placeholder="0" />
        </Field>
        <label className="flex items-start gap-3 text-sm">
          <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" checked={allowNegative} onChange={event => setAllowNegative(event.target.checked)} />
          <span>Autoriser le stock négatif <span className="block text-xs text-ink-500">Recommandé au démarrage : une vente n’est jamais bloquée par un stock pas encore saisi.</span></span>
        </label>
      </form>
    </Modal>
  );
}

export function StockItems() {
  const {location, can, currency} = useConsole();
  const [lowOnly, setLowOnly] = useState(false);
  const [search, setSearch] = useState('');
  const [opened, setOpened] = useState<{id: string; action: Action} | null>(null);
  const [adding, setAdding] = useState(false);
  const {data, error, reload} = useResource(signal => (location ? listItems(location.id, false, signal) : Promise.resolve([])), [location?.id]);
  const items = useMemo(() => (data ?? [])
    .filter(item => !lowOnly || item.isLowStock)
    .filter(item => (item.ingredientName ?? '').toLowerCase().includes(search.trim().toLowerCase()))
    .sort((a, b) => Number(b.isLowStock) - Number(a.isLowStock) || (a.ingredientName ?? '').localeCompare(b.ingredientName ?? '')), [data, lowOnly, search]);
  const low = (data ?? []).filter(item => item.isLowStock).length;
  const priced = (data ?? []).filter(item => item.averageUnitCost !== null);
  const value = priced.reduce((sum, item) => sum + Math.max(item.currentQuantity, 0) * item.averageUnitCost!, 0);
  const current = data?.find(item => item.id === opened?.id) ?? null;

  if (!location) return <Alert tone="blue">Choisissez un établissement pour voir son stock.</Alert>;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard icon={<Boxes />} label="Ingrédients suivis" value={data?.length ?? '…'} />
        <StatCard
          icon={<AlertTriangle />}
          accent={low > 0 ? 'amber' : 'neutral'}
          label="À réapprovisionner"
          value={data ? low : '…'}
          hint={lowOnly ? 'Filtre actif · cliquer pour tout voir' : low > 0 ? 'Cliquer pour filtrer' : undefined}
          onClick={() => setLowOnly(!lowOnly)}
          active={lowOnly}
        />
        <StatCard icon={<Coins />} label="Valeur du stock" value={data ? money(value, currency) : '…'} hint={data && priced.length < data.length ? `${data.length - priced.length} sans coût connu` : undefined} />
      </div>

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
          <Input type="search" aria-label="Chercher un ingrédient" className="pl-9" value={search} onChange={event => setSearch(event.target.value)} placeholder="Chercher un ingrédient" />
        </div>
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" className="size-4 accent-brand-600" checked={lowOnly} onChange={event => setLowOnly(event.target.checked)} />À réapprovisionner seulement
        </label>
        {can('inventory.manage') && <Button onClick={() => setAdding(true)}><Plus className="size-4" aria-hidden="true" />Suivre un ingrédient</Button>}
      </div>

      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : items.length === 0 ? (
        <Card className="p-6">
          <Empty icon={<Warehouse className="size-8" />} title={data.length ? 'Aucun ingrédient ne correspond' : 'Aucun ingrédient suivi dans cet établissement'}>
            {data.length ? 'Changez la recherche ou le filtre.' : 'Ajoutez les ingrédients à suivre, puis liez-les aux produits dans « Recettes » : chaque vente décompte le stock.'}
          </Empty>
        </Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {items.map(item => {
              const ratio = item.reorderThreshold > 0 ? Math.max(0, Math.min(1, item.currentQuantity / (item.reorderThreshold * 3))) : 1;
              return (
                <li key={item.id} className="flex flex-wrap items-center gap-3 px-4 py-3 sm:flex-nowrap">
                  <button type="button" onClick={() => setOpened({id: item.id, action: 'receipt'})} className="min-w-0 flex-1 text-left">
                    <span className="flex items-center gap-2 font-medium">{item.ingredientName ?? 'Ingrédient'}{item.isLowStock && <Badge tone={item.currentQuantity <= 0 ? 'red' : 'amber'}>{item.currentQuantity <= 0 ? 'Épuisé' : 'Bas'}</Badge>}</span>
                    <span className="mt-1.5 block h-1.5 w-full max-w-60 overflow-hidden rounded-full bg-slate-100" aria-hidden="true">
                      <span className={cx('block h-full rounded-full', item.isLowStock ? 'bg-amber-500' : 'bg-emerald-500')} style={{width: `${ratio * 100}%`}} />
                    </span>
                  </button>
                  <div className="w-32 text-right">
                    <p className={cx('font-semibold tabular-nums', item.currentQuantity < 0 && 'text-red-700')}>{quantity(item.currentQuantity, item.unit)}</p>
                    <p className="text-xs text-ink-500">seuil {quantity(item.reorderThreshold, item.unit)}</p>
                    {item.averageUnitCost !== null && <p className="text-xs text-ink-500">{unitCost(item.averageUnitCost, item.unit, currency)}</p>}
                  </div>
                  {can('inventory.operate') && (
                    <div className="flex gap-1">
                      <Button size="sm" variant="secondary" onClick={() => setOpened({id: item.id, action: 'receipt'})}><PackagePlus className="size-4" aria-hidden="true" />Réception</Button>
                      <Button size="sm" variant="ghost" aria-label={`Sortie : ${item.ingredientName}`} title="Sortie" onClick={() => setOpened({id: item.id, action: 'exit'})}><LogOut className="size-4" /></Button>
                      <Button size="sm" variant="ghost" aria-label={`Perte : ${item.ingredientName}`} title="Perte" onClick={() => setOpened({id: item.id, action: 'waste'})}><Trash2 className="size-4" /></Button>
                      <Button size="sm" variant="ghost" aria-label={`Inventaire : ${item.ingredientName}`} title="Inventaire" onClick={() => setOpened({id: item.id, action: 'count'})}><ClipboardCheck className="size-4" /></Button>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        </Card>
      )}
      <ItemDialog item={current} initial={opened?.action ?? 'receipt'} onClose={() => setOpened(null)} onChanged={reload} />
      <AddItemDialog open={adding} locationId={location.id} stocked={(data ?? []).map(item => item.ingredientId)} onClose={() => setAdding(false)} onDone={reload} />
    </div>
  );
}
