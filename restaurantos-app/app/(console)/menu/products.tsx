'use client';
import {useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import {useSearchParams} from 'next/navigation';
import {Ban, Check, ChevronDown, Layers, SlidersHorizontal, CircleCheck, Eye, LayoutGrid, List, ListChecks, MoreHorizontal, PackageSearch, Plus, RotateCcw, Search, UtensilsCrossed} from 'lucide-react';
import {api, post} from '@/lib/api';
import {money} from '@/lib/format';
import {useMediaQuery, useResource} from '@/lib/hooks';
import {topProducts} from '@/lib/dashboard';
import {duplicateProduct, loadCatalog, outHere, setAvailableHere, setProductLocations, soldAt, type Catalog} from '@/lib/menu';
import type {LocationSummary, Product, ProductStatus, ProductSummary} from '@/lib/types';
import {useConsole} from '@/components/console';
import {accentOf, Alert, Badge, Breadcrumb, Button, Card, Empty, Modal, Spinner, StatCard, Tabs, cx, useAction} from '@/components/ui';
import {Cover} from './images';
import {ProductForm} from './product-form';
import {ProductPanel} from './product-panel';
import {statusDot, statusLabel} from './status';

const OFF_MENU = '__hors-carte';
const VIEW_KEY = 'restaurantos.app.menu.vue';
type StatusFilter = 'live' | ProductStatus;
type Sort = 'name' | 'price-asc' | 'price-desc' | 'popular';

function Kpi({icon, tone, label, value, hint}: {icon: ReactNode; tone?: string; label: string; value: number; hint: string}) {
  return <StatCard icon={icon} label={label} value={value} hint={hint} accent={accentOf(tone)} />;
}

function FilterSelect({label, value, onChange, children}: {label: string; value: string; onChange: (value: string) => void; children: ReactNode}) {
  return (
    <label className="block min-w-0">
      <span className="mb-1 block text-xs text-ink-500">{label}</span>
      <span className="relative block">
        <select value={value} onChange={event => onChange(event.target.value)} className="h-10 w-full appearance-none rounded-xl border border-slate-200 bg-white pr-9 pl-3 text-sm text-ink-800 focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none">
          {children}
        </select>
        <ChevronDown className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
      </span>
    </label>
  );
}

/** Interrupteur de vente : Actif ⇄ Indisponible (un brouillon est mis en vente). */
function SaleSwitch({product, locationId, onChanged}: {product: ProductSummary; locationId: string; onChanged: () => Promise<void>}) {
  const {can, locations} = useConsole();
  const {busy, run} = useAction();
  // Plusieurs établissements : l'interrupteur coupe la vente ICI seulement (rupture locale), pas dans tout le restaurant.
  const local = locations.length > 1 && product.status === 'Active';
  const on = product.status === 'Active' && !(local && outHere(product, locationId));
  const disabled = !can('catalog.manage') || product.status === 'Archived' || !!busy;
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      aria-label={on ? `Retirer ${product.name} de la vente` : `Mettre ${product.name} en vente`}
      disabled={disabled}
      onClick={event => {
        event.stopPropagation();
        void run('switch', async () => {
          if (local) await setAvailableHere(product.id, locationId, !on);
          else await post(`/catalog/products/${product.id}/${on ? 'mark-unavailable' : 'activate'}`);
          await onChanged();
        }, local ? (on ? 'En rupture dans cet établissement.' : 'De nouveau en vente ici.') : on ? 'Produit indisponible.' : 'Produit en vente.');
      }}
      className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', on ? 'bg-brand-600' : 'bg-slate-300')}>
      <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', on ? 'left-[1.375rem]' : 'left-0.5')} />
    </button>
  );
}

function CardMenu({product, onOpen, onEdit, onChanged, onDuplicated}: {
  product: ProductSummary;
  onOpen: () => void;
  onEdit: () => void;
  onChanged: () => Promise<void>;
  onDuplicated: (id: string) => void;
}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    const escape = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);
  const manage = can('catalog.manage') && product.status !== 'Archived';
  const item = 'block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50';
  return (
    <div ref={ref} className="absolute top-2 right-2 z-10" onClick={event => event.stopPropagation()}>
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Actions pour ${product.name}`}
        disabled={!!busy}
        onClick={() => setOpen(value => !value)}
        className="grid size-8 place-items-center rounded-lg bg-white/90 text-ink-700 shadow-sm hover:bg-white">
        <MoreHorizontal className="size-4" />
      </button>
      {open && (
        <div role="menu" className="absolute top-9 right-0 w-44 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); onOpen(); }}>Voir la fiche</button>
          {manage && <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); onEdit(); }}>Modifier</button>}
          {manage && (
            <button type="button" role="menuitem" className={item} onClick={() => {
              setOpen(false);
              void run('copy', async () => {
                const full = await api<Product>(`/catalog/products/${product.id}`);
                const id = await duplicateProduct(full);
                await onChanged();
                onDuplicated(id);
              }, 'Copie créée en brouillon.');
            }}>Dupliquer</button>
          )}
        </div>
      )}
    </div>
  );
}

/** Bouton principal « Ajouter un produit » et, à côté, les autres créations du catalogue. */
function AddMenu({onProduct, onFormula, onManage, onChoose}: {
  onProduct: () => void;
  onFormula: () => void;
  onManage: (view: 'menus' | 'options' | 'taxes') => void;
  onChoose?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    const escape = (event: KeyboardEvent) => event.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);
  const pick = (action: () => void) => () => {
    setOpen(false);
    action();
  };
  const entries: {icon: ReactNode; label: string; hint: string; action: () => void}[] = [
    {icon: <Plus className="size-4" />, label: 'Produit', hint: 'Plat, boisson, dessert…', action: onProduct},
    {icon: <Layers className="size-4" />, label: 'Formule', hint: 'Menu à étapes : plat, boisson, dessert au choix', action: onFormula},
    {icon: <LayoutGrid className="size-4" />, label: 'Catégorie', hint: 'Une section de la carte', action: () => onManage('menus')},
    {icon: <SlidersHorizontal className="size-4" />, label: 'Groupe d’options', hint: 'Suppléments, cuissons, sauces', action: () => onManage('options')},
    ...(onChoose ? [{icon: <ListChecks className="size-4" />, label: 'Produits vendus ici', hint: 'Choisir la carte de l’établissement', action: onChoose}] : []),
  ];
  return (
    <div ref={ref} className="relative flex">
      <Button onClick={onProduct} className="rounded-r-none"><Plus className="size-4" aria-hidden="true" />Ajouter un produit</Button>
      <Button aria-haspopup="menu" aria-expanded={open} aria-label="Autres ajouts" onClick={() => setOpen(value => !value)} className="rounded-l-none border-l border-white/25 px-2.5">
        <ChevronDown className={cx('size-4 transition', open && 'rotate-180')} aria-hidden="true" />
      </Button>
      {open && (
        <div role="menu" className="absolute top-12 right-0 z-20 w-72 rounded-xl border border-slate-200 bg-white p-1.5 shadow-lg">
          {entries.map(entry => (
            <button key={entry.label} type="button" role="menuitem" onClick={pick(entry.action)} className="flex w-full items-start gap-3 rounded-lg px-3 py-2 text-left hover:bg-slate-50">
              <span className="mt-0.5 grid size-7 shrink-0 place-items-center rounded-lg bg-brand-50 text-brand-600" aria-hidden="true">{entry.icon}</span>
              <span className="min-w-0">
                <span className="block text-sm font-semibold text-ink-900">{entry.label}</span>
                <span className="block text-xs text-ink-500">{entry.hint}</span>
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function MoreMenu({onManage}: {onManage: (view: 'menus' | 'options' | 'taxes') => void}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const item = 'block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50';
  return (
    <div ref={ref} className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label="Autres réglages du catalogue" onClick={() => setOpen(value => !value)} className="grid size-10 place-items-center rounded-xl border border-slate-200 bg-white text-ink-600 hover:bg-slate-50">
        <MoreHorizontal className="size-4" />
      </button>
      {open && (
        <div role="menu" className="absolute top-11 right-0 z-20 w-56 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); onManage('menus'); }}>Cartes &amp; catégories</button>
          <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); onManage('options'); }}>Groupes d’options</button>
          <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); onManage('taxes'); }}>Taxes</button>
        </div>
      )}
    </div>
  );
}

export function Products({location, onManage}: {location: LocationSummary; onManage: (view: 'menus' | 'options' | 'taxes') => void}) {
  const {can, locations} = useConsole();
  const params = useSearchParams();
  const [scope, setScope] = useState<'here' | 'others'>('here');
  const wide = useMediaQuery('(min-width: 1536px)');
  const [category, setCategory] = useState('all');
  const [status, setStatus] = useState<StatusFilter>('live');
  const [sort, setSort] = useState<Sort>('name');
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [choosing, setChoosing] = useState(false);
  const [view, setView] = useState<'grid' | 'list'>('grid');
  useEffect(() => {
    try {
      if (window.localStorage.getItem(VIEW_KEY) === 'list') setView('list');
    } catch {}
  }, []);
  const changeView = (next: 'grid' | 'list') => {
    setView(next);
    try {
      window.localStorage.setItem(VIEW_KEY, next);
    } catch {}
  };
  const [version, setVersion] = useState(0);
  const [form, setForm] = useState<{open: boolean; product: Product | null; formula?: boolean}>(() => ({open: params.get('nouveau') === '1' && can('catalog.manage'), product: null}));
  const [formulaOf, setFormulaOf] = useState<string | null>(null);
  const wanted = params.get('produit');
  useEffect(() => {
    if (wanted) setSelectedId(wanted);
  }, [wanted]);

  const catalog = useResource(signal => loadCatalog(location.id, signal), [location.id]);
  const top = useResource(signal => topProducts(location.id, 30, signal).catch(() => []), [location.id]);
  const reload = async () => {
    await catalog.reload();
    setVersion(value => value + 1);
  };

  const data: Catalog | null = catalog.data ?? null;
  // Les produits vendus dans l'établissement choisi ; « Autres établissements » montre ceux qui n'y sont pas vendus.
  const scoped = useMemo(
    () => (data?.products ?? []).filter(product => (scope === 'here') === soldAt(product, location.id)),
    [data, scope, location.id],
  );
  const elsewhere = useMemo(
    () => (data?.products ?? []).filter(product => product.status !== 'Archived' && !soldAt(product, location.id)).length,
    [data, location.id],
  );
  const shared = useMemo(
    () => (data?.products ?? []).filter(product => product.status !== 'Archived').every(product => !product.locationIds?.length),
    [data],
  );
  const live = useMemo(() => scoped.filter(product => product.status !== 'Archived'), [scoped]);
  const popular = useMemo(() => new Set((top.data ?? []).filter(entry => entry.quantity >= 2).slice(0, 3).map(entry => entry.productId)), [top.data]);
  const sold = useMemo(() => new Map((top.data ?? []).map(entry => [entry.productId, entry.quantity])), [top.data]);

  const inCategory = (product: ProductSummary, key: string) => {
    if (key === 'all') return true;
    if (key === OFF_MENU) return !data?.categoryOf.has(product.id);
    return !!data?.categories.find(entry => entry.key === key)?.productIds.has(product.id);
  };

  const list = useMemo(() => {
    const needle = query.trim().toLocaleLowerCase('fr');
    return scoped
      .filter(product => (status === 'live' ? product.status !== 'Archived'
        : status === 'Unavailable' ? product.status === 'Unavailable' || (product.status === 'Active' && outHere(product, location.id))
          : status === 'Active' ? product.status === 'Active' && !outHere(product, location.id)
            : product.status === status))
      .filter(product => inCategory(product, category))
      .filter(product => !needle || product.name.toLocaleLowerCase('fr').includes(needle))
      .sort((a, b) =>
        sort === 'price-asc' ? a.basePrice - b.basePrice
          : sort === 'price-desc' ? b.basePrice - a.basePrice
            : sort === 'popular' ? (sold.get(b.id) ?? 0) - (sold.get(a.id) ?? 0) || a.name.localeCompare(b.name, 'fr')
              : a.name.localeCompare(b.name, 'fr'),
      );
  }, [data, scoped, status, category, query, sort, sold, location.id]); // inCategory ne dépend que de data et category

  useEffect(() => {
    if (!wide || !data || selectedId) return;
    setSelectedId(list[0]?.id ?? null);
  }, [wide, data, list, selectedId]);

  if (catalog.error) return <Alert>{catalog.error}</Alert>;
  if (!data) return <Spinner label="Chargement du catalogue…" />;

  const offMenu = live.filter(product => !data.categoryOf.has(product.id)).length;
  const tabs = [
    {key: 'all', label: 'Tous', count: live.length},
    ...data.categories.map(entry => ({key: entry.key, label: entry.name, count: live.filter(product => entry.productIds.has(product.id)).length})),
    ...(offMenu > 0 ? [{key: OFF_MENU, label: 'Hors carte', count: offMenu}] : []),
  ];
  const active = live.filter(product => product.status === 'Active' && !outHere(product, location.id)).length;
  const unavailable = live.filter(product => product.status === 'Unavailable' || (product.status === 'Active' && outHere(product, location.id))).length;
  const drafts = live.filter(product => product.status === 'Draft').length;
  const filtered = category !== 'all' || status !== 'live' || query !== '' || sort !== 'name';
  const categoryName = (product: ProductSummary) => data.categoryOf.get(product.id)?.[0] ?? 'Hors carte';

  const panel = selectedId && (
    <ProductPanel
      key={selectedId}
      id={selectedId}
      catalog={data}
      popular={popular.has(selectedId)}
      version={version}
      initialTab={formulaOf === selectedId ? 'formula' : undefined}
      onClose={wide ? () => setSelectedId(null) : undefined}
      onChanged={reload}
      onEdit={product => setForm({open: true, product})}
      onDuplicated={setSelectedId}
    />
  );

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <Breadcrumb items={[{label: 'Tableau de bord', href: '/tableau-de-bord'}, {label: 'Menu & produits'}]} />
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Menu &amp; Produits</h1>
          <p className="mt-1 text-sm text-ink-500">
            {locations.length > 1 ? <>Carte et produits de <span className="font-semibold text-ink-700">{location.name}</span>. Les produits peuvent être partagés entre établissements.</> : 'Gérez vos catégories, produits, options et disponibilités.'}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 lg:shrink-0 lg:flex-nowrap">
          <Button variant="secondary" onClick={() => onManage('menus')}><LayoutGrid className="size-4" aria-hidden="true" />Cartes &amp; catégories</Button>
          <MoreMenu onManage={onManage} />
          {can('catalog.manage') && (
            <AddMenu
              onProduct={() => setForm({open: true, product: null})}
              onFormula={() => setForm({open: true, product: null, formula: true})}
              onManage={onManage}
              onChoose={locations.length > 1 ? () => setChoosing(true) : undefined}
            />
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <Kpi icon={<UtensilsCrossed className="size-6" />} tone="bg-violet-50 text-violet-600" label="Produits total" value={live.length} hint={`${drafts} brouillon${drafts > 1 ? 's' : ''}`} />
        <Kpi icon={<LayoutGrid className="size-6" />} tone="bg-emerald-50 text-emerald-600" label="Catégories" value={data.categories.length} hint={data.primary ? `Carte « ${data.primary.name} »${data.primary.status === 'Published' ? ' publiée' : ' en brouillon'}` : 'Aucune carte'} />
        <Kpi icon={<Eye className="size-6" />} tone="bg-orange-50 text-orange-500" label="Produits actifs" value={active} hint={live.length ? `${Math.round((active / live.length) * 100)} % du catalogue` : '—'} />
        <Kpi icon={<Ban className="size-6" />} tone="bg-rose-50 text-rose-500" label="Produits indisponibles" value={unavailable} hint="En rupture ou retirés de la vente" />
      </div>

      <div className="grid gap-6 2xl:grid-cols-[minmax(0,1fr)_25rem]">
        <Card className="min-w-0 p-4 sm:p-5">
          {locations.length > 1 && shared && scope === 'here' && (
            <div className="mb-4 flex flex-col gap-3 rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm sm:flex-row sm:items-center">
              <p className="flex-1 text-amber-900">Tous vos produits sont vendus dans tous les établissements : la carte est donc la même partout. Choisissez ceux que {location.name} vend.</p>
              {can('catalog.manage') && <Button size="sm" onClick={() => setChoosing(true)}><ListChecks className="size-4" aria-hidden="true" />Choisir les produits</Button>}
            </div>
          )}
          {locations.length > 1 && (
            <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
              <Tabs label="Établissement" value={scope} onChange={value => {
                setScope(value);
                setCategory('all');
                setSelectedId(null);
              }} items={[
                {value: 'here', label: `Vendus ici`},
                {value: 'others', label: `Autres établissements (${elsewhere})`},
              ]} />
              {can('catalog.manage') && !shared && (
                <Button variant="secondary" size="sm" onClick={() => setChoosing(true)}><ListChecks className="size-4" aria-hidden="true" />Produits vendus ici</Button>
              )}
              {scope === 'others' && <p className="w-full text-xs text-ink-500">Ces produits ne sont pas vendus à {location.name} : ouvrez la fiche pour les y ajouter.</p>}
            </div>
          )}
          <div className="mb-4 flex items-end gap-3 border-b border-slate-200">
            <div role="tablist" aria-label="Catégories" className="-mx-1 flex min-w-0 flex-1 gap-1 overflow-x-auto">
              {tabs.map(tab => (
                <button
                  key={tab.key}
                  type="button"
                  role="tab"
                  aria-selected={category === tab.key}
                  onClick={() => setCategory(tab.key)}
                  className={cx('-mb-px border-b-2 px-3 py-2.5 text-sm font-semibold whitespace-nowrap transition', category === tab.key ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                  {tab.label} ({tab.count})
                </button>
              ))}
            </div>
            <div className="mb-1.5 hidden shrink-0 rounded-lg border border-slate-200 p-0.5 sm:flex" role="group" aria-label="Affichage">
              {([['grid', LayoutGrid, 'Grille'], ['list', List, 'Liste']] as const).map(([value, Icon, label]) => (
                <button key={value} type="button" aria-pressed={view === value} aria-label={`Affichage en ${label.toLowerCase()}`} title={label} onClick={() => changeView(value)}
                  className={cx('grid size-7 place-items-center rounded-md transition', view === value ? 'bg-brand-50 text-brand-700' : 'text-ink-500 hover:text-ink-800')}>
                  <Icon className="size-4" aria-hidden="true" />
                </button>
              ))}
            </div>
          </div>

          <div className="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(0,1.5fr)_repeat(3,minmax(0,1fr))_auto] xl:items-end">
            <label className="relative block min-w-0">
              <span className="mb-1 block text-xs text-ink-500">Recherche</span>
              <Search className="pointer-events-none absolute bottom-3 left-3 size-4 text-ink-500" aria-hidden="true" />
              <input
                type="search"
                value={query}
                onChange={event => setQuery(event.target.value)}
                placeholder="Rechercher un produit…"
                className="h-10 w-full rounded-xl border border-slate-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-500 focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none"
              />
            </label>
            <FilterSelect label="Catégorie" value={category} onChange={setCategory}>
              {tabs.map(tab => <option key={tab.key} value={tab.key}>{tab.key === 'all' ? 'Toutes' : tab.label}</option>)}
            </FilterSelect>
            <FilterSelect label="Statut" value={status} onChange={value => setStatus(value as StatusFilter)}>
              <option value="live">Tous</option>
              <option value="Active">Actifs</option>
              <option value="Unavailable">Indisponibles</option>
              <option value="Draft">Brouillons</option>
              <option value="Archived">Archivés</option>
            </FilterSelect>
            <FilterSelect label="Trier" value={sort} onChange={value => setSort(value as Sort)}>
              <option value="name">Nom</option>
              <option value="popular">Les plus vendus</option>
              <option value="price-asc">Prix croissant</option>
              <option value="price-desc">Prix décroissant</option>
            </FilterSelect>
            <button
              type="button"
              disabled={!filtered}
              onClick={() => {
                setCategory('all');
                setStatus('live');
                setQuery('');
                setSort('name');
              }}
              className="flex h-10 items-center justify-center gap-2 rounded-xl border border-slate-200 bg-white px-3 text-sm font-medium text-ink-600 hover:bg-slate-50 disabled:opacity-50">
              <RotateCcw className="size-4" aria-hidden="true" />Réinitialiser
            </button>
          </div>

          {list.length === 0 ? (
            <Empty icon={<PackageSearch className="size-8" />} title={live.length === 0 ? 'Aucun produit' : 'Aucun produit pour ces filtres'}>
              {live.length === 0 && can('catalog.manage') ? 'Ajoutez vos plats et boissons, puis mettez-les en vente.' : undefined}
            </Empty>
          ) : view === 'list' ? (
            <ul className="divide-y divide-slate-100 overflow-hidden rounded-xl border border-slate-200">
              {list.map(product => {
                const here = product.status === 'Active' && outHere(product, location.id);
                return (
                  <li key={product.id}
                    className={cx('flex cursor-pointer items-center gap-3 px-3 py-2.5 transition hover:bg-slate-50', selectedId === product.id && 'bg-brand-50/60', product.status === 'Archived' && 'opacity-60')}
                    onClick={() => setSelectedId(product.id)}>
                    <Cover productId={product.id} name={product.name} seed={categoryName(product)} version={version} small className="size-11 shrink-0 rounded-lg" />
                    <button type="button" className="min-w-0 flex-1 text-left" onClick={() => setSelectedId(product.id)}>
                      <span className="block truncate font-semibold">{product.name}</span>
                      <span className="block truncate text-xs text-ink-500">{data.categoryOf.get(product.id)?.join(', ') ?? 'Hors carte'}</span>
                    </button>
                    <span className="hidden w-28 text-right text-sm font-semibold whitespace-nowrap sm:block">{money(product.basePrice, product.currencyCode)}</span>
                    <span className="hidden w-32 items-center gap-1.5 text-xs font-medium text-ink-600 md:flex">
                      <span className={cx('size-2 rounded-full', here ? 'bg-amber-500' : statusDot[product.status])} aria-hidden="true" />
                      {here ? 'En rupture ici' : statusLabel[product.status]}
                    </span>
                    <span onClick={event => event.stopPropagation()}>{scope === 'here' && <SaleSwitch product={product} locationId={location.id} onChanged={reload} />}</span>
                  </li>
                );
              })}
            </ul>
          ) : (
            <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 min-[1800px]:grid-cols-4">
              {list.map(product => (
                <li key={product.id}>
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={() => setSelectedId(product.id)}
                    onKeyDown={event => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        setSelectedId(product.id);
                      }
                    }}
                    className={cx(
                      'group relative h-full overflow-hidden rounded-2xl border bg-white text-left transition hover:-translate-y-0.5 hover:shadow-lg hover:shadow-slate-200/70 focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:outline-none',
                      selectedId === product.id ? 'border-brand-500 ring-2 ring-brand-100' : 'border-slate-200',
                      product.status === 'Archived' && 'opacity-60',
                    )}>
                    <div className="relative aspect-[16/10] overflow-hidden">
                      <Cover productId={product.id} name={product.name} seed={categoryName(product)} version={version} fill className="size-full transition duration-300 group-hover:scale-[1.04]" />
                      {popular.has(product.id) && <span className="absolute top-2.5 left-2.5 rounded-full bg-amber-300 px-2.5 py-0.5 text-xs font-semibold text-amber-950 shadow-sm">Populaire</span>}
                      {selectedId === product.id && <span className="absolute top-2.5 right-12 grid size-7 place-items-center rounded-full bg-brand-600 text-white shadow-md" aria-hidden="true"><Check className="size-4" /></span>}
                      <CardMenu
                        product={product}
                        onOpen={() => setSelectedId(product.id)}
                        onEdit={() => void api<Product>(`/catalog/products/${product.id}`).then(full => setForm({open: true, product: full}))}
                        onChanged={reload}
                        onDuplicated={setSelectedId}
                      />
                    </div>
                    <div className="p-3.5">
                      <p className="truncate font-semibold text-ink-900">{product.name}</p>
                      <p className="mt-0.5 text-sm font-semibold whitespace-nowrap">
                        {money(product.basePrice, product.currencyCode)}
                        {product.variantCount > 0 && <span className="ml-1 text-xs font-normal text-ink-500">· {product.variantCount} variante{product.variantCount > 1 ? 's' : ''}</span>}
                        {product.isFormula && <span className="ml-1 text-xs font-normal text-brand-700">· formule</span>}
                      </p>
                      <p className="mt-2 flex min-w-0 flex-wrap gap-1">
                        {(data.categoryOf.get(product.id) ?? ['Hors carte']).map(name => <Badge key={name} className="font-medium">{name}</Badge>)}
                      </p>
                      <div className="mt-3 flex items-center justify-between gap-2">
                        <span className="flex items-center gap-1.5 text-xs font-medium text-ink-600">
                          <span className={cx('size-2 rounded-full', product.status === 'Active' && outHere(product, location.id) ? 'bg-amber-500' : statusDot[product.status])} aria-hidden="true" />
                          {product.status === 'Active' && outHere(product, location.id) ? 'En rupture ici' : statusLabel[product.status]}
                        </span>
                        {scope === 'here' && <SaleSwitch product={product} locationId={location.id} onChanged={reload} />}
                      </div>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>

        {wide && (
          <aside className="min-w-0">
            <Card className="sticky top-24 max-h-[calc(100dvh-7rem)] overflow-y-auto p-5">
              {panel || (
                <div className="py-16 text-center text-sm text-ink-500">
                  <CircleCheck className="mx-auto mb-3 size-8" aria-hidden="true" />
                  Sélectionnez un produit pour voir sa fiche.
                </div>
              )}
            </Card>
          </aside>
        )}
      </div>

      {!wide && (
        <Modal open={!!selectedId} onClose={() => setSelectedId(null)} title="Fiche produit">
          <div className="pt-5">{panel}</div>
        </Modal>
      )}

      <ProductForm
        open={form.open}
        product={form.product}
        formula={form.formula}
        catalog={data}
        onClose={() => setForm({open: false, product: null})}
        onSaved={async id => {
          await reload();
          setFormulaOf(form.formula ? id : null);
          setSelectedId(id);
        }}
      />
      {choosing && <SoldHereDialog location={location} products={(data.products ?? []).filter(product => product.status !== 'Archived')} onClose={() => setChoosing(false)} onSaved={async () => {
        setChoosing(false);
        await reload();
      }} />}
    </div>
  );
}

/** Choix en une fois des produits vendus dans l'établissement : les autres disparaissent de sa caisse, de sa borne et de son site. */
function SoldHereDialog({location, products, onClose, onSaved}: {location: LocationSummary; products: ProductSummary[]; onClose: () => void; onSaved: () => Promise<void>}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const sorted = useMemo(() => [...products].sort((a, b) => a.name.localeCompare(b.name, 'fr')), [products]);
  const [chosen, setChosen] = useState(() => new Set(products.filter(product => soldAt(product, location.id)).map(product => product.id)));
  const onlyHere = (product: ProductSummary) => product.locationIds?.length === 1 && product.locationIds[0] === location.id;
  const toggle = (id: string) => setChosen(set => {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  });
  const changes = sorted.filter(product => chosen.has(product.id) !== soldAt(product, location.id));
  return (
    <Modal open onClose={onClose} title={`Produits vendus à ${location.name}`} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button disabled={changes.length === 0} busy={busy === 'here'} onClick={() => void run('here', async () => {
          for (const product of changes) {
            const ids = product.locationIds ?? [];
            const next = chosen.has(product.id)
              ? [...ids, location.id]
              : (ids.length ? ids : locations.map(item => item.id)).filter(id => id !== location.id);
            await setProductLocations(product.id, next);
          }
          await onSaved();
        }, 'Carte de l’établissement enregistrée.')}>Enregistrer{changes.length > 0 ? ` (${changes.length})` : ''}</Button>
      </>
    }>
      <div className="space-y-3 text-sm">
        <div className="flex items-center justify-between gap-2">
          <p className="text-ink-500">{chosen.size} produit{chosen.size > 1 ? 's' : ''} sur {sorted.length}</p>
          <div className="flex gap-2">
            <Button variant="ghost" size="sm" onClick={() => setChosen(new Set(sorted.map(product => product.id)))}>Tout cocher</Button>
            <Button variant="ghost" size="sm" onClick={() => setChosen(new Set(sorted.filter(onlyHere).map(product => product.id)))}>Tout décocher</Button>
          </div>
        </div>
        <ul className="max-h-[50vh] divide-y divide-slate-100 overflow-y-auto rounded-xl border border-slate-200">
          {sorted.map(product => (
            <li key={product.id}>
              <label className="flex items-center gap-3 px-3 py-2.5">
                <input type="checkbox" className="size-4 accent-brand-600" checked={chosen.has(product.id)} disabled={onlyHere(product)} onChange={() => toggle(product.id)} />
                <span className="min-w-0 flex-1 truncate">{product.name}</span>
                <span className="text-xs text-ink-500">{onlyHere(product) ? 'Vendu seulement ici' : product.locationIds?.length ? `${product.locationIds.length} établissement${product.locationIds.length > 1 ? 's' : ''}` : 'Partout'}</span>
              </label>
            </li>
          ))}
        </ul>
        <p className="text-xs text-ink-500">Un produit décoché reste vendu dans les autres établissements, mais plus dans ceux que vous ouvrirez plus tard. Pour retirer un produit vendu seulement ici, archivez-le depuis sa fiche.</p>
      </div>
    </Modal>
  );
}
