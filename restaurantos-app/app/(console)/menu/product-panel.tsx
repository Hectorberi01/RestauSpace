'use client';
import {useState} from 'react';
import {Archive, Copy, MapPin, Pencil, Plus, Power, Trash2, X} from 'lucide-react';
import {api, del, post, put} from '@/lib/api';
import {money, parseAmount} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {recipeCosts} from '@/lib/inventory';
import {allergenName, duplicateProduct, loadAllergens, onlineChannelLabel, outHere, setAvailableHere, setProductLocations, soldAt, type Catalog} from '@/lib/menu';
import type {AllergenCode, OnlineChannel, Product} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Field, Input, Modal, Spinner, cx, useAction} from '@/components/ui';
import {Carousel, Gallery} from './images';
import {useModifierGroups} from './options';
import {useTaxCategories} from './taxes';
import {InlineEdit} from './inline-edit';
import {statusLabel, statusTone} from './status';
import {MarginSummary} from '../stock/catalog';
import {PriceOverrides} from './prices';
import {FormulaEditor} from './formula';

type Tab = 'details' | 'prices' | 'variants' | 'formula' | 'options' | 'online';

const channels = Object.keys(onlineChannelLabel) as OnlineChannel[];

function Info({label, children}: {label: string; children: React.ReactNode}) {
  return (
    <div className="grid grid-cols-[8rem_minmax(0,1fr)] gap-3 py-1.5 text-sm">
      <dt className="text-ink-500">{label}</dt>
      <dd className="min-w-0 text-ink-800">{children}</dd>
    </div>
  );
}

export function ProductPanel({id, catalog, popular, version, initialTab, onClose, onChanged, onEdit, onDuplicated}: {
  id: string;
  initialTab?: 'formula';
  catalog: Catalog;
  popular: boolean;
  version: number;
  onClose?: () => void;
  onChanged: () => Promise<void>;
  onEdit: (product: Product) => void;
  onDuplicated: (id: string) => void;
}) {
  const allergens = useResource(() => loadAllergens(), []);
  const {can, tenant, location, locations} = useConsole();
  const [choosing, setChoosing] = useState(false);
  const manage = can('catalog.manage');
  const costs = useResource(signal => (location ? recipeCosts(location.id, signal).catch(() => []) : Promise.resolve([])), [location?.id, id, version]);
  const {busy, run} = useAction();
  const [tab, setTab] = useState<Tab>(initialTab ?? 'details');
  const [cover, setCover] = useState(0);
  const [archiving, setArchiving] = useState(false);
  const {data: product, error, reload} = useResource(signal => api<Product>(`/catalog/products/${id}`, {signal}), [id, version]);
  const {data: taxes} = useTaxCategories();
  const {data: groups} = useModifierGroups();
  const [variant, setVariant] = useState({name: '', delta: '0'});

  if (error) return <Alert>{error}</Alert>;
  if (!product) return <Spinner />;

  const base = `/catalog/products/${product.id}`;
  const categories = catalog.categoryOf.get(product.id) ?? [];
  const tax = taxes?.find(item => item.id === product.taxCategoryId);
  const changed = async () => {
    await reload();
    await onChanged();
  };

  const tabs: {value: Tab; label: string}[] = [
    {value: 'details', label: 'Détails'},
    {value: 'prices', label: (product.priceOverrides ?? []).length ? `Prix (${product.priceOverrides!.length})` : 'Prix'},
    ...(product.isFormula ? [] : [{value: 'variants' as Tab, label: `Variantes (${product.variants.length})`}]),
    {value: 'formula', label: product.formulaSteps?.length ? `Formule (${product.formulaSteps.length})` : 'Formule'},
    {value: 'options', label: `Options (${product.modifierGroupIds.length})`},
    {value: 'online', label: 'En ligne'},
  ];
  const editable = manage && product.status !== 'Archived';
  const toggleAllergen = (code: AllergenCode) => run(`al-${code}`, async () => {
    const next = product.allergens.includes(code) ? product.allergens.filter(item => item !== code) : [...product.allergens, code];
    await put(`${base}/allergens`, {allergens: next});
    await changed();
  });
  const toggleChannel = (channel: OnlineChannel) => run(`ch-${channel}`, async () => {
    const next = product.hiddenChannels.includes(channel) ? product.hiddenChannels.filter(item => item !== channel) : [...product.hiddenChannels, channel];
    await put(`${base}/hidden-channels`, {channels: next});
    await changed();
  });

  return (
    <div className="flex flex-col">
      <div className="relative mb-4 aspect-[16/11] overflow-hidden rounded-xl border border-slate-100">
        <Carousel productId={product.id} name={product.name} seed={categories[0] ?? product.name} version={cover} className="size-full" />
        {onClose && (
          <button type="button" onClick={onClose} aria-label="Fermer le détail" className="absolute top-3 right-3 grid size-8 place-items-center rounded-full bg-ink-900/55 text-white hover:bg-ink-900/75">
            <X className="size-4" />
          </button>
        )}
      </div>

      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="flex flex-wrap items-center gap-2 text-xl font-bold">
            <span className="truncate">{product.name}</span>
            <Badge tone={statusTone[product.status]}>{statusLabel[product.status]}</Badge>
            {product.isFormula && <Badge tone="brand">Formule</Badge>}
          </h2>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {popular && <Badge tone="amber">Populaire</Badge>}
            {categories.map(name => <Badge key={name} tone="blue">{name}</Badge>)}
            {categories.length === 0 && <Badge>Hors carte</Badge>}
          </div>
        </div>
        {manage && product.status !== 'Archived' && (
          <Button size="sm" variant="secondary" onClick={() => onEdit(product)}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>
        )}
      </div>

      <div role="tablist" aria-label="Fiche produit" className="mt-4 flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map(item => (
          <button
            key={item.value}
            type="button"
            role="tab"
            aria-selected={tab === item.value}
            onClick={() => setTab(item.value)}
            className={cx('-mb-px border-b-2 px-3 py-2 text-sm font-semibold whitespace-nowrap', tab === item.value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
            {item.label}
          </button>
        ))}
      </div>

      {tab === 'details' && (
        <div className="space-y-5 pt-4">
          <section>
            <h3 className="mb-1 text-sm font-semibold">Informations générales</h3>
            <dl className="divide-y divide-slate-100">
              <Info label="Description">{product.description || <span className="text-ink-500">—</span>}</Info>
              <Info label="Catégorie">{categories.join(', ') || 'Hors carte'}</Info>
              <Info label={`Prix ${tenant.pricesIncludeTax ? 'TTC' : 'HT'}`}><span className="font-semibold">{money(product.basePrice, product.currencyCode)}</span></Info>
              <Info label="Taxe">{tax ? `${tax.name} (${Math.round(tax.rate * 1000) / 10} %)` : '—'}</Info>
              {(() => {
                const cost = costs.data?.find(entry => entry.productId === product.id && entry.variantId === null);
                if (!cost || cost.lines.length === 0) return null;
                const net = tenant.pricesIncludeTax && tax ? product.basePrice / (1 + tax.rate) : product.basePrice;
                return <Info label="Coût matière (HT)"><MarginSummary cost={cost} price={net} currency={product.currencyCode} /></Info>;
              })()}
              <Info label="Statut">
                <Badge tone={statusTone[product.status]}>{statusLabel[product.status]}</Badge>
                {product.status === 'Active' && !product.isSellable && <span className="ml-2 text-xs text-ink-500">non vendable</span>}
              </Info>
            </dl>
          </section>
          {locations.length > 1 && location && (
            <section>
              <h3 className="mb-1 flex items-center gap-1.5 text-sm font-semibold"><MapPin className="size-4 text-ink-500" aria-hidden="true" />Établissements</h3>
              <dl className="divide-y divide-slate-100">
                <Info label="Vendu dans">
                  {(product.locationIds ?? []).length === 0
                    ? 'Tous les établissements'
                    : locations.filter(item => product.locationIds!.includes(item.id)).map(item => item.name).join(', ')}
                </Info>
                {soldAt(product, location.id) && product.status === 'Active' && (
                  <Info label={location.name}>
                    <span className="flex flex-wrap items-center gap-2">
                      {outHere(product, location.id) ? <Badge tone="amber">En rupture ici</Badge> : <Badge tone="green">En vente ici</Badge>}
                      {editable && (
                        <Button size="sm" variant="ghost" busy={busy === 'here'} onClick={() => void run('here', async () => {
                          await setAvailableHere(product.id, location.id, outHere(product, location.id));
                          await changed();
                        }, outHere(product, location.id) ? 'De nouveau en vente ici.' : 'En rupture dans cet établissement.')}>
                          {outHere(product, location.id) ? 'Remettre en vente ici' : 'Rupture ici'}
                        </Button>
                      )}
                    </span>
                  </Info>
                )}
              </dl>
              {editable && (
                <div className="mt-2 flex flex-wrap gap-2">
                  {!soldAt(product, location.id) && (
                    <Button size="sm" busy={busy === 'add-here'} onClick={() => void run('add-here', async () => {
                      await setProductLocations(product.id, [...(product.locationIds ?? []), location.id]);
                      await changed();
                    }, `Vendu aussi à ${location.name}.`)}><Plus className="size-4" aria-hidden="true" />Vendre aussi à {location.name}</Button>
                  )}
                  <Button size="sm" variant="secondary" onClick={() => setChoosing(true)}>Choisir les établissements</Button>
                </div>
              )}
            </section>
          )}
          <section>
            <h3 className="mb-2 text-sm font-semibold">Photos</h3>
            <Gallery productId={product.id} onChanged={() => setCover(value => value + 1)} />
          </section>
        </div>
      )}

      {tab === 'variants' && (
        <section className="space-y-3 pt-4">
          <p className="text-sm text-ink-500">Taille, cuisson… chaque variante ajuste le prix de base ({money(product.basePrice, product.currencyCode)}) d’un écart, positif ou négatif.</p>
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {product.variants.length === 0 && <li className="px-4 py-3 text-sm text-ink-500">Aucune variante : le produit se vend au prix de base.</li>}
            {product.variants.map(item => (
              <li key={item.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 text-sm">
                <span className={item.isAvailable ? 'font-medium' : 'text-ink-500 line-through'}>{item.name}</span>
                <span className="flex items-center gap-1">
                  <InlineEdit value={String(item.priceDelta)} label={`Écart de prix de ${item.name}`} editable={manage && product.status !== 'Archived'} inputMode="decimal"
                    suffix={product.currencyCode} inputClassName="w-24" className="mr-2 whitespace-nowrap" success="Prix de la variante enregistré."
                    display={<>{money(item.price, product.currencyCode)} <span className="text-xs text-ink-500">({item.priceDelta >= 0 ? '+' : ''}{money(item.priceDelta, product.currencyCode)})</span></>}
                    parse={raw => {
                      const value = parseAmount(raw);
                      return value !== null && product.basePrice + value >= 0 ? value : null;
                    }}
                    onSave={async (value: number) => {
                      await put(`${base}/variants/${item.id}/price-delta`, {priceDelta: value});
                      await changed();
                    }} />
                  {manage && (
                    <>
                      <Button size="sm" variant="ghost" busy={busy === `va-${item.id}`} onClick={() => run(`va-${item.id}`, async () => { await put(`${base}/variants/${item.id}/availability`, {isAvailable: !item.isAvailable}); await changed(); })}>{item.isAvailable ? 'Épuisée' : 'Disponible'}</Button>
                      <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Supprimer ${item.name}`} busy={busy === `vd-${item.id}`} onClick={() => run(`vd-${item.id}`, async () => { await del(`${base}/variants/${item.id}`); await changed(); })}><Trash2 className="size-4" /></Button>
                    </>
                  )}
                </span>
              </li>
            ))}
          </ul>
          {manage && product.status !== 'Archived' && (
            <form className="flex flex-wrap items-end gap-2" onSubmit={event => {
              event.preventDefault();
              void run('variant', async () => {
                await post(`${base}/variants`, {name: variant.name.trim(), priceDelta: parseAmount(variant.delta) ?? 0, sku: null});
                setVariant({name: '', delta: '0'});
                await changed();
              }, 'Variante ajoutée.');
            }}>
              <Field label="Nouvelle variante" className="min-w-36 flex-1"><Input required maxLength={60} value={variant.name} onChange={event => setVariant(v => ({...v, name: event.target.value}))} placeholder="Grande portion" /></Field>
              <Field label="Écart de prix" className="w-32"><Input inputMode="decimal" value={variant.delta} onChange={event => setVariant(v => ({...v, delta: event.target.value}))} /></Field>
              <Button type="submit" busy={busy === 'variant'}><Plus className="size-4" aria-hidden="true" />Ajouter</Button>
            </form>
          )}
        </section>
      )}

      {tab === 'options' && (
        <section className="space-y-3 pt-4">
          <p className="text-sm text-ink-500">Suppléments et choix proposés à la commande (accompagnement, sauce…).</p>
          {groups?.length === 0 && <p className="text-sm text-ink-500">Aucun groupe d’options : créez-en depuis le menu « ⋯ » puis « Options ».</p>}
          <div className="flex flex-wrap gap-2">
            {groups?.map(group => {
              const attached = product.modifierGroupIds.includes(group.id);
              return (
                <button
                  key={group.id}
                  type="button"
                  disabled={!manage || !!busy || product.status === 'Archived'}
                  aria-pressed={attached}
                  onClick={() => run(`g-${group.id}`, async () => {
                    if (attached) await del(`${base}/modifier-groups/${group.id}`);
                    else await post(`${base}/modifier-groups`, {modifierGroupId: group.id});
                    await changed();
                  })}
                  className={cx('rounded-full border px-3 py-1.5 text-sm font-medium transition disabled:opacity-60', attached ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-300 text-ink-600 hover:border-slate-400')}>
                  {attached ? '✓ ' : '+ '}{group.name}
                  <span className="ml-1 text-xs text-ink-500">({group.modifiers.length})</span>
                </button>
              );
            })}
          </div>
        </section>
      )}

      {tab === 'formula' && <FormulaEditor product={product} catalog={catalog} editable={editable} onChanged={changed} />}

      {tab === 'prices' && <PriceOverrides product={product} editable={editable} onChanged={changed} />}

      {tab === 'online' && (
        <div className="space-y-5 pt-4">
          <section>
            <h3 className="text-sm font-semibold">Allergènes</h3>
            <p className="mb-2 text-sm text-ink-500">Affichés sur le site, l’application et la borne (14 allergènes à déclaration obligatoire).</p>
            <div className="flex flex-wrap gap-2">
              {(allergens.data ?? []).map(code => {
                const on = product.allergens.includes(code);
                return (
                  <button
                    key={code}
                    type="button"
                    disabled={!editable || !!busy}
                    aria-pressed={on}
                    onClick={() => void toggleAllergen(code)}
                    className={cx('rounded-full border px-3 py-1.5 text-sm font-medium transition disabled:opacity-60', on ? 'border-amber-500 bg-amber-50 text-amber-800' : 'border-slate-300 text-ink-600 hover:border-slate-400')}>
                    {on ? '✓ ' : ''}{allergenName(code)}
                  </button>
                );
              })}
            </div>
          </section>
          <section>
            <h3 className="text-sm font-semibold">Canaux de vente en ligne</h3>
            <p className="mb-2 text-sm text-ink-500">Décochez un canal pour y masquer le produit ; il reste vendu en salle et sur les autres canaux.</p>
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {channels.map(channel => {
                const visible = !product.hiddenChannels.includes(channel);
                return (
                  <li key={channel} className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm">
                    <label htmlFor={`channel-${channel}`} className="font-medium">{onlineChannelLabel[channel]}</label>
                    <span className="flex items-center gap-2">
                      <span className={visible ? 'text-emerald-700' : 'text-ink-500'}>{visible ? 'Visible' : 'Masqué'}</span>
                      <input
                        id={`channel-${channel}`}
                        type="checkbox"
                        className="size-4 accent-brand-600"
                        checked={visible}
                        disabled={!editable || !!busy}
                        onChange={() => void toggleChannel(channel)} />
                    </span>
                  </li>
                );
              })}
            </ul>
          </section>
        </div>
      )}

      {manage && product.status !== 'Archived' && (
        <div className="mt-6 grid grid-cols-3 gap-2 border-t border-slate-100 pt-4">
          <Button variant="secondary" size="sm" busy={busy === 'copy'} onClick={() => run('copy', async () => {
            const copy = await duplicateProduct(product);
            await onChanged();
            onDuplicated(copy);
          }, 'Copie créée en brouillon.')}>
            <Copy className="size-4" aria-hidden="true" />Dupliquer
          </Button>
          {product.status === 'Active' ? (
            <Button variant="secondary" size="sm" className="text-amber-700" busy={busy === 'off'} onClick={() => run('off', async () => { await post(`${base}/mark-unavailable`); await changed(); }, 'Produit indisponible.')}>
              <Power className="size-4" aria-hidden="true" />Désactiver
            </Button>
          ) : (
            <Button variant="secondary" size="sm" className="text-emerald-700" busy={busy === 'on'} onClick={() => run('on', async () => { await post(`${base}/activate`); await changed(); }, 'Produit en vente.')}>
              <Power className="size-4" aria-hidden="true" />Activer
            </Button>
          )}
          <Button variant="secondary" size="sm" className="text-red-600" onClick={() => setArchiving(true)}>
            <Archive className="size-4" aria-hidden="true" />Archiver
          </Button>
        </div>
      )}
      {choosing && (
        <LocationsDialog product={product} onClose={() => setChoosing(false)} onSaved={async () => {
          setChoosing(false);
          await changed();
        }} />
      )}
      <Modal
        open={archiving}
        onClose={() => setArchiving(false)}
        title="Archiver le produit"
        footer={
          <>
            <Button variant="secondary" onClick={() => setArchiving(false)}>Annuler</Button>
            <Button variant="danger" busy={busy === 'archive'} onClick={() => void run('archive', async () => {
              await post(`${base}/archive`);
              setArchiving(false);
              await changed();
            }, 'Produit archivé.')}>Archiver</Button>
          </>
        }>
        <p className="text-sm text-ink-600">« {product.name} » ne pourra plus être vendu ni remis en vente. Il reste visible dans l’historique des commandes.</p>
      </Modal>
    </div>
  );
}

/** Les établissements où le produit est vendu : tous, ou une sélection (le retirer d'un établissement le retire de sa carte en ligne et de sa caisse). */
function LocationsDialog({product, onClose, onSaved}: {product: Product; onClose: () => void; onSaved: () => Promise<void>}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const [everywhere, setEverywhere] = useState((product.locationIds ?? []).length === 0);
  const [chosen, setChosen] = useState<string[]>(product.locationIds?.length ? product.locationIds : locations.map(item => item.id));
  const toggle = (id: string) => setChosen(list => (list.includes(id) ? list.filter(item => item !== id) : [...list, id]));
  return (
    <Modal open onClose={onClose} title={`Où vendre « ${product.name} » ?`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button disabled={!everywhere && chosen.length === 0} busy={busy === 'locations'} onClick={() => void run('locations', async () => {
          await setProductLocations(product.id, everywhere ? [] : chosen);
          await onSaved();
        }, 'Établissements enregistrés.')}>Enregistrer</Button>
      </>
    }>
      <div className="space-y-3 text-sm">
        <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3">
          <input type="radio" name="where" className="mt-0.5 accent-brand-600" checked={everywhere} onChange={() => setEverywhere(true)} />
          <span><span className="font-semibold">Tous les établissements</span><span className="block text-ink-500">Y compris ceux que vous ouvrirez plus tard.</span></span>
        </label>
        <label className="flex items-start gap-3 rounded-xl border border-slate-200 p-3">
          <input type="radio" name="where" className="mt-0.5 accent-brand-600" checked={!everywhere} onChange={() => setEverywhere(false)} />
          <span className="flex-1">
            <span className="font-semibold">Certains établissements</span>
            <span className="mt-2 block space-y-1.5">
              {locations.map(item => (
                <label key={item.id} className="flex items-center gap-2">
                  <input type="checkbox" className="size-4 accent-brand-600" disabled={everywhere} checked={everywhere || chosen.includes(item.id)} onChange={() => toggle(item.id)} />
                  {item.name}
                </label>
              ))}
            </span>
          </span>
        </label>
        <p className="text-xs text-ink-500">Ailleurs, le produit n’apparaît ni sur la caisse, ni sur la carte en ligne, ni sur la borne. Il reste dans la bibliothèque du restaurant.</p>
      </div>
    </Modal>
  );
}
