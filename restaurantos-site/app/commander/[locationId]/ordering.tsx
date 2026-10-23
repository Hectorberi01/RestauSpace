'use client';
import {useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {
  ArrowLeft, Beer, Bike, CakeSlice, CalendarDays, Check, CircleUser, Clock, Coffee, CupSoda, Fish, Gift, IceCreamCone, Leaf, Minus, Pizza, Plus, Salad, Sandwich,
  ShoppingBag, Soup, Tag, Trash2, Utensils, UtensilsCrossed, X, type LucideIcon,
} from 'lucide-react';
import {
  accentOf,
  allergenLabel,
  clock,
  couponRefusal,
  errorMessage,
  linePrice,
  loadAccount,
  loadCatalog,
  methodLabel,
  modeLabel,
  money,
  newIdempotencyKey,
  onCustomerChange,
  readCustomerToken,
  readCart,
  rememberOrder,
  storefront,
  StorefrontError,
  toPayload,
  writeCart,
  componentLabel,
  type CartComponent,
  type CartLine,
  type Catalog,
  type CatalogFormulaChoice,
  type CatalogFormulaStep,
  type CatalogProduct,
  type CustomerAccount,
  type PaymentMode,
  type PlacedOrder,
  type Quote,
  type ServiceMode,
  type Slots,
  type Storefront,
  type StorefrontLocation,
} from '@/lib/storefront';
import {AccentButton, cx, LanguageSwitch, Notice, Sheet, Spinner} from '@/components/order-ui';
import {currentLang, intlLocale, tr, useLang} from '@/lib/i18n';

interface Loaded {
  storefront: Storefront;
  location: StorefrontLocation;
  catalog: Catalog;
}

/** Commande en ligne d'un établissement : menu, fiche produit, panier, puis validation et paiement. */
/** Table d'un QR code : la commande y est servie, sans coordonnées ni créneau. */
export interface TableContext {
  token: string;
  label: string;
  areaName: string;
}

export function Ordering({locationId, table}: {locationId: string; table?: TableContext}) {
  useLang();
  const [data, setData] = useState<Loaded | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [product, setProduct] = useState<CatalogProduct | null>(null);
  const [checkout, setCheckout] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([storefront<Storefront>('/', {signal: controller.signal}), loadCatalog(locationId, controller.signal)])
      .then(([front, catalog]) => {
        const location = front?.locations.find(item => item.id === locationId);
        if (!front || !location) {
          setError(tr('Cet établissement ne prend pas de commandes sur ce site.'));
          return;
        }

        setData({storefront: front, location, catalog});
        setCart(readCart(locationId).filter(line => catalog.products.some(item => item.id === line.productId)));
      })
      .catch(reason => !controller.signal.aborted && setError(reason instanceof StorefrontError && reason.status === 404
        ? tr('Cet établissement ne prend pas de commandes sur ce site.')
        : errorMessage(reason)));
    return () => controller.abort();
  }, [locationId]);

  const update = useCallback((next: CartLine[]) => {
    setCart(next);
    writeCart(locationId, next);
  }, [locationId]);

  if (error) return <Page><Notice tone="error">{error}</Notice><BackLink /></Page>;
  if (!data) return <Page><Spinner /></Page>;

  const {storefront: front, location, catalog} = data;
  const accent = accentOf(front.restaurant.primaryColor);
  const count = cart.reduce((sum, line) => sum + line.quantity, 0);
  const estimate = cart.reduce((sum, line) => sum + (linePrice(line, catalog) ?? 0), 0);

  return (
    <div style={{'--accent': accent} as React.CSSProperties} className="min-h-screen bg-slate-50 pb-28">
      <Header front={front} location={location} booking={!table} cover={catalog.products.find(item => item.images[0]?.startsWith('https://'))?.images[0] ?? null} />
      {table && (
        <div className="mx-auto max-w-5xl px-4 pt-4">
          <p className="flex items-center gap-2 rounded-xl bg-white px-4 py-3 text-sm font-semibold shadow-sm ring-1 ring-slate-200">
            <Utensils className="size-4 text-[var(--accent)]" aria-hidden="true" />{tr('Table')} {table.label} · {table.areaName} {tr('— votre commande vous sera servie à table.')}
          </p>
        </div>
      )}
      {!location.acceptsOrdersNow && (
        <div className="mx-auto max-w-5xl px-4 pt-4">
          <Notice tone="warning">
            <span className="flex items-start gap-2">
              <Clock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
              {location.isPaused
                ? tr('Les commandes en ligne sont en pause. Vous pouvez consulter le menu.')
                : location.nextOpeningUtc
                  ? tr('L’établissement est fermé : il ouvre à {p0}. Vous pourrez choisir une heure de retrait.', {p0: clock(location.nextOpeningUtc, location.timeZoneId)})
                  : tr('L’établissement ne prend pas de commandes pour le moment.')}
            </span>
          </Notice>
        </div>
      )}
      <Menu catalog={catalog} onOpen={setProduct} />

      {count > 0 && (
        <div className="fixed inset-x-0 bottom-0 z-40 border-t border-slate-200 bg-white/95 px-4 py-3 backdrop-blur">
          <AccentButton className="mx-auto flex w-full max-w-5xl justify-between" onClick={() => setCheckout(true)}>
            <span className="flex items-center gap-2"><ShoppingBag className="size-5" aria-hidden="true" />{tr('Voir le panier (')}{count})</span>
            <span>{money(estimate, catalog.currencyCode)}</span>
          </AccentButton>
        </div>
      )}

      {product && (
        <ProductSheet
          product={product}
          catalog={catalog}
          onClose={() => setProduct(null)}
          onAdd={line => {
            update([...cart, line]);
            setProduct(null);
          }} />
      )}

      {checkout && (
        <Checkout
          front={front}
          location={location}
          catalog={catalog}
          table={table}
          cart={cart}
          onCart={update}
          onClose={() => setCheckout(false)} />
      )}
    </div>
  );
}

function Page({children}: {children: React.ReactNode}) {
  return <div className="mx-auto min-h-screen max-w-xl space-y-4 bg-slate-50 px-4 py-10">{children}</div>;
}

function BackLink() {
  return <Link href="/commander" className="inline-flex items-center gap-2 text-sm font-medium text-ink-600 hover:text-ink-900"><ArrowLeft className="size-4" />{tr('Choisir un autre établissement')}</Link>;
}

function Header({front, location, booking, cover}: {front: Storefront; location: StorefrontLocation; booking: boolean; cover: string | null}) {
  const logo = front.restaurant.logoUrl?.startsWith('https://') ? front.restaurant.logoUrl : null;
  return (
    <header className="relative overflow-hidden bg-[var(--accent)] text-white">
      {cover && (
        // Une photo du menu en fond, à droite, fondue dans la couleur du restaurant.
        // eslint-disable-next-line @next/next/no-img-element
        <img src={cover} alt="" className="absolute inset-y-0 right-0 hidden h-full w-1/2 object-cover sm:block" />
      )}
      <div className="absolute inset-0 bg-gradient-to-r from-[var(--accent)] from-45% via-[var(--accent)]/85 to-black/10" aria-hidden="true" />
      <div className="relative mx-auto flex max-w-5xl items-center gap-4 px-4 py-7 sm:py-10">
        {logo ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={logo} alt="" className="size-16 shrink-0 rounded-2xl bg-white object-cover shadow-lg ring-2 ring-white/40" />
        ) : (
          <span className="grid size-16 shrink-0 place-items-center rounded-2xl bg-white/15 ring-2 ring-white/30"><UtensilsCrossed className="size-8" aria-hidden="true" /></span>
        )}
        <div className="min-w-0 flex-1">
          <p className="flex items-center gap-1.5 text-xs font-semibold tracking-wide text-white/80 uppercase"><Utensils className="size-3.5" aria-hidden="true" />{tr('Notre menu')}</p>
          <h1 className="truncate text-2xl font-bold sm:text-3xl">{front.restaurant.name}</h1>
          <p className="truncate text-sm text-white/85">{location.name} · {[location.street, location.city].filter(Boolean).join(', ')}</p>
          <div className="mt-2 flex flex-wrap gap-2">
            <p className="inline-flex items-center gap-1.5 rounded-full bg-black/15 px-2.5 py-1 text-xs font-medium text-white backdrop-blur">
              <Clock className="size-3.5" aria-hidden="true" />{tr('Prêt en')} {location.preparationMinutes} {tr('min environ')}
            </p>
            {booking && <Link href="/reserver" className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-2.5 py-1 text-xs font-medium text-white backdrop-blur hover:bg-white/25">
              <CalendarDays className="size-3.5" aria-hidden="true" />{tr('Réserver une table')}
            </Link>}
          </div>
        </div>
        <LanguageSwitch onAccent className="self-start" />
        <Link href={`/compte?retour=${encodeURIComponent(`/commander/${location.id}`)}`} aria-label={tr('Mon compte')}
          className="grid size-11 shrink-0 place-items-center self-start rounded-full bg-white/15 backdrop-blur hover:bg-white/25">
          <CircleUser className="size-6" aria-hidden="true" />
        </Link>
      </div>
    </header>
  );
}

/** Une icône pour une catégorie, d'après son nom (aucune donnée de plus n'est nécessaire). */
const sectionIcons: [RegExp, LucideIcon][] = [
  [/salade|entr[ée]e/i, Leaf],
  [/dessert|g[âa]teau|p[âa]tisserie/i, CakeSlice],
  [/glace/i, IceCreamCone],
  [/bi[èe]re|alcool/i, Beer],
  [/caf[ée]|th[ée]|chaud/i, Coffee],
  [/boisson|jus|soda|cocktail/i, CupSoda],
  [/pizza/i, Pizza],
  [/sandwich|burger|snack/i, Sandwich],
  [/soupe|potage/i, Soup],
  [/poisson|fruits? de mer/i, Fish],
  [/accompagnement|l[ée]gume/i, Salad],
];

const sectionIcon = (name: string) => sectionIcons.find(([pattern]) => pattern.test(name))?.[1] ?? Utensils;

function Menu({catalog, onOpen}: {catalog: Catalog; onOpen: (product: CatalogProduct) => void}) {
  const products = useMemo(() => new Map(catalog.products.map(product => [product.id, product])), [catalog]);
  const sections = useMemo(
    () => catalog.menus.flatMap(menu => menu.sections.map(section => ({...section, key: `${menu.id}-${section.id}`}))),
    [catalog]);
  const [active, setActive] = useState<string | null>(null);
  const nav = useRef<HTMLDivElement>(null);

  // La catégorie à l'écran est mise en avant dans la barre, qui défile pour la garder visible.
  useEffect(() => {
    const observer = new IntersectionObserver(entries => {
      const visible = entries.filter(entry => entry.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
      if (visible) setActive(visible.target.id);
    }, {rootMargin: '-90px 0px -60% 0px'});
    sections.forEach(section => {
      const element = document.getElementById(section.key);
      if (element) observer.observe(element);
    });
    return () => observer.disconnect();
  }, [sections]);

  useEffect(() => {
    const tab = active ? nav.current?.querySelector<HTMLElement>(`[data-section="${active}"]`) : null;
    tab?.scrollIntoView({block: 'nearest', inline: 'center', behavior: 'smooth'});
  }, [active]);

  if (sections.length === 0) {
    return <div className="mx-auto max-w-5xl px-4 py-10"><Notice>{tr('Aucun menu n’est publié pour le moment.')}</Notice></div>;
  }

  return (
    <>
      <nav aria-label={tr('Catégories')} className="sticky top-0 z-30 border-b border-slate-200 bg-white/95 shadow-sm backdrop-blur">
        <div ref={nav} className="mx-auto flex max-w-5xl gap-2 overflow-x-auto px-4 py-3 [scrollbar-width:none]">
          {sections.map(section => {
            const Icon = sectionIcon(section.name);
            const current = (active ?? sections[0].key) === section.key;
            return (
              <a key={section.key} href={`#${section.key}`} data-section={section.key} aria-current={current ? 'true' : undefined}
                className={cx('inline-flex shrink-0 items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold transition',
                  current ? 'bg-[var(--accent)] text-white shadow-sm' : 'bg-slate-100 text-ink-600 hover:bg-slate-200 hover:text-ink-900')}>
                <Icon className="size-4" aria-hidden="true" />{section.name}
              </a>
            );
          })}
        </div>
      </nav>
      <div className="mx-auto max-w-5xl space-y-10 px-4 py-6">
        {sections.map(section => {
          const Icon = sectionIcon(section.name);
          return (
            <section key={section.key} id={section.key} className="scroll-mt-24">
              <h2 className="mb-4 flex items-center gap-2.5 text-xl font-bold">
                <span className="grid size-9 place-items-center rounded-xl bg-[var(--accent)]/10 text-[var(--accent)]" aria-hidden="true"><Icon className="size-5" /></span>
                {section.name}
              </h2>
              <ul className="grid gap-4 md:grid-cols-2">
                {section.productIds.map(id => products.get(id)).filter(Boolean).map(product => (
                  <li key={product!.id}>
                    <ProductCard product={product!} currency={catalog.currencyCode} icon={Icon} onOpen={() => onOpen(product!)} />
                  </li>
                ))}
              </ul>
            </section>
          );
        })}
      </div>
    </>
  );
}

function ProductCard({product, currency, icon: Icon, onOpen}: {product: CatalogProduct; currency: string; icon: LucideIcon; onOpen: () => void}) {
  const from = product.variants.length > 0 ? Math.min(...product.variants.map(variant => variant.price)) : product.price;
  const image = product.thumbnails?.[0] ?? product.images[0];
  return (
    <button
      type="button"
      onClick={onOpen}
      disabled={!product.isAvailable}
      aria-label={`${product.name}, ${product.variants.length > 0 ? tr('à partir de ') : ''}${money(from, currency)}${product.isAvailable ? '' : ', indisponible'}`}
      className="group flex h-full min-h-36 w-full overflow-hidden rounded-2xl border border-slate-200/80 bg-white text-left shadow-sm transition enabled:hover:-translate-y-0.5 enabled:hover:border-[var(--accent)]/40 enabled:hover:shadow-md disabled:opacity-60">
      <span className="relative block w-32 shrink-0 self-stretch overflow-hidden bg-slate-100 sm:w-44">
        {image ? (
          <>
            {/* Photo entière quel que soit son format, posée sur un fond flouté d'elle-même : rien n'est rogné ni déformé. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={image} alt="" aria-hidden="true" loading="lazy" className="absolute inset-0 size-full scale-110 object-cover opacity-60 blur-xl" />
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={image} alt="" loading="lazy" className={cx('absolute inset-0 size-full object-contain transition duration-300 group-enabled:group-hover:scale-105', !product.isAvailable && 'grayscale')} />
          </>
        ) : (
          <span className="grid size-full place-items-center bg-[var(--accent)]/10 text-[var(--accent)]" aria-hidden="true"><Icon className="size-10 opacity-70" /></span>
        )}
      </span>
      <span className="flex min-w-0 flex-1 flex-col p-4">
        <span className="block text-base font-semibold text-ink-900">{product.name}</span>
        {product.description && <span className="mt-1 line-clamp-2 block text-sm text-ink-500">{product.description}</span>}
        <span className="mt-auto flex items-end justify-between gap-2 pt-3">
          <span className="font-bold text-[color-mix(in_srgb,var(--accent)_70%,black)]">
            {product.variants.length > 0 && <span className="text-sm font-medium">{tr('À partir de')} </span>}{money(from, currency)}
            {(product.formulaSteps?.length ?? 0) > 0 && <span className="ml-2 rounded-full bg-[var(--accent)]/10 px-2 py-0.5 align-middle text-xs font-semibold text-[var(--accent)]">{tr('Formule')}</span>}
          </span>
          {product.isAvailable ? (
            <span className="grid size-9 shrink-0 place-items-center rounded-full bg-[var(--accent)]/10 text-[var(--accent)] transition group-hover:bg-[var(--accent)] group-hover:text-white" aria-hidden="true">
              <Plus className="size-5" />
            </span>
          ) : (
            <span className="rounded-full bg-red-50 px-2 py-0.5 text-xs font-semibold text-red-700">{tr('Indisponible')}</span>
          )}
        </span>
      </span>
    </button>
  );
}

type Group = Catalog['modifierGroups'][number];

const sameChoice = (a: {productId: string; variantId: string | null}, b: {productId: string; variantId: string | null}) =>
  a.productId === b.productId && a.variantId === b.variantId;

const stepRule = (step: CatalogFormulaStep) =>
  step.minChoices === 0
    ? tr('jusqu’à {p0}, facultatif', {p0: step.maxChoices})
    : step.minChoices === step.maxChoices
      ? (step.minChoices === 1 ? tr('1 au choix') : `${step.minChoices} ${tr('au choix')}`)
      : `${step.minChoices} à ${step.maxChoices}`;

/** Groupes d'options et leurs règles min/max ; un groupe à choix unique se coche comme un bouton radio. */
function ModifierFieldsets({groups, currency, selectedIn, onToggle, compact, scope}: {
  groups: Group[];
  currency: string;
  selectedIn: (group: Group) => string[];
  onToggle: (group: Group, modifierId: string) => void;
  compact?: boolean;
  /** Distingue les boutons radio d'un même groupe proposé à plusieurs produits d'une formule. */
  scope?: string;
}) {
  return (
    <>
      {groups.map(group => {
        const selected = selectedIn(group);
        const rule = group.minSelections === 0
          ? tr('jusqu’à {p0}, facultatif', {p0: group.maxSelections})
          : group.minSelections === group.maxSelections
            ? `${group.minSelections} obligatoire${group.minSelections > 1 ? 's' : ''}`
            : `${group.minSelections} à ${group.maxSelections}`;
        return (
          <fieldset key={group.id} className={compact ? 'mt-3' : 'mt-5'}>
            <legend className={cx('mb-2 font-semibold', compact && 'text-sm')}>{group.name} <span className="text-sm font-normal text-ink-500">({rule})</span></legend>
            <div className="space-y-2">
              {group.modifiers.map(modifier => {
                const on = selected.includes(modifier.id);
                const full = !on && group.maxSelections > 1 && selected.length >= group.maxSelections;
                return (
                  <label key={modifier.id} className={cx('flex items-center justify-between gap-3 rounded-xl border bg-white', compact ? 'px-3 py-2 text-sm' : 'px-4 py-3', on ? 'border-[var(--accent)]' : 'border-slate-200', (!modifier.isAvailable || full) && 'opacity-50')}>
                    <span className="flex items-center gap-3">
                      <input
                        type={group.maxSelections === 1 ? 'radio' : 'checkbox'}
                        name={`group-${group.id}${scope ? `-${scope}` : ''}`}
                        className="size-4 accent-[var(--accent)]"
                        checked={on}
                        disabled={!modifier.isAvailable || full}
                        onChange={() => onToggle(group, modifier.id)} />
                      {modifier.name}{!modifier.isAvailable && tr(' (épuisé)')}
                    </span>
                    {modifier.priceDelta > 0 && <span className="text-sm text-ink-600">+ {money(modifier.priceDelta, currency)}</span>}
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

/**
 * Fiche produit : variante, options (règles min/max du groupe), quantité et note cuisine. Pour une formule : le produit
 * de chaque étape, chacun avec ses propres options.
 */
export function ProductSheet({product, catalog, onClose, onAdd}: {
  product: CatalogProduct;
  catalog: Catalog;
  onClose: () => void;
  onAdd: (line: CartLine) => void;
}) {
  useLang();
  const groups = product.modifierGroupIds.map(id => catalog.modifierGroups.find(group => group.id === id)).filter(Boolean) as Catalog['modifierGroups'];
  const [variantId, setVariantId] = useState<string | null>(product.variants.find(variant => variant.isAvailable)?.id ?? null);
  const [chosen, setChosen] = useState<Record<string, string[]>>({});
  const [quantity, setQuantity] = useState(1);
  const [note, setNote] = useState('');
  const steps = product.formulaSteps ?? [];
  const [picked, setPicked] = useState<Record<string, CartComponent[]>>({});

  const pick = (step: CatalogFormulaStep, choice: CatalogFormulaChoice) => setPicked(current => {
    const list = current[step.id] ?? [];
    if (list.some(item => sameChoice(item, choice))) return {...current, [step.id]: list.filter(item => !sameChoice(item, choice))};
    const added: CartComponent = {stepId: step.id, productId: choice.productId, variantId: choice.variantId, modifierIds: []};
    if (step.maxChoices === 1) return {...current, [step.id]: [added]};
    return list.length >= step.maxChoices ? current : {...current, [step.id]: [...list, added]};
  });
  const toggleComponent = (step: CatalogFormulaStep, component: CartComponent, group: Group, modifierId: string) => setPicked(current => ({
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
      return (choice?.modifierGroupIds ?? []).every(id => {
        const group = catalog.modifierGroups.find(item => item.id === id);
        return !group || component.modifierIds.filter(modifierId => group.modifiers.some(modifier => modifier.id === modifierId)).length >= group.minSelections;
      });
    });

  const toggle = (groupId: string, modifierId: string, max: number) => setChosen(current => {
    const list = current[groupId] ?? [];
    if (list.includes(modifierId)) return {...current, [groupId]: list.filter(id => id !== modifierId)};
    if (max === 1) return {...current, [groupId]: [modifierId]};
    return list.length >= max ? current : {...current, [groupId]: [...list, modifierId]};
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
    <Sheet
      open
      onClose={onClose}
      title={product.name}
      footer={
        <div className="flex items-center gap-3">
          <div className="flex items-center rounded-xl border border-slate-200">
            <button type="button" aria-label={tr('Retirer un')} className="grid size-11 place-items-center disabled:opacity-40" disabled={quantity <= 1} onClick={() => setQuantity(value => value - 1)}><Minus className="size-4" /></button>
            <span className="w-8 text-center font-semibold" aria-live="polite">{quantity}</span>
            <button type="button" aria-label={tr('Ajouter un')} className="grid size-11 place-items-center disabled:opacity-40" disabled={quantity >= 99} onClick={() => setQuantity(value => value + 1)}><Plus className="size-4" /></button>
          </div>
          <AccentButton className="flex-1" disabled={!ready} onClick={() => onAdd(line)}>
            {tr('Ajouter ·')} {money(total, catalog.currencyCode)}
          </AccentButton>
        </div>
      }>
      {product.images[0] && (
        <span className="relative mb-4 block aspect-[4/3] max-h-[40vh] w-full overflow-hidden rounded-2xl bg-slate-100">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={product.images[0]} alt="" aria-hidden="true" className="absolute inset-0 size-full scale-110 object-cover opacity-60 blur-2xl" />
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={product.images[0]} alt="" className="absolute inset-0 size-full object-contain" />
        </span>
      )}
      {product.description && <p className="text-ink-600">{product.description}</p>}
      {product.allergens.length > 0 && (
        <p className="mt-3 text-sm text-ink-600">
          <span className="font-semibold">{tr('Allergènes :')} </span>{product.allergens.map(code => allergenLabel[code] ?? code).join(', ')}
        </p>
      )}

      {product.variants.length > 0 && (
        <fieldset className="mt-5">
          <legend className="mb-2 font-semibold">{tr('Choisissez')} <span className="text-sm font-normal text-ink-500">{tr('(obligatoire)')}</span></legend>
          <div className="space-y-2">
            {product.variants.map(variant => (
              <label key={variant.id} className={cx('flex items-center justify-between gap-3 rounded-xl border px-4 py-3', variantId === variant.id ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200', !variant.isAvailable && 'opacity-50')}>
                <span className="flex items-center gap-3">
                  <input type="radio" name="variant" className="size-4 accent-[var(--accent)]" checked={variantId === variant.id} disabled={!variant.isAvailable} onChange={() => setVariantId(variant.id)} />
                  {variant.name}{!variant.isAvailable && tr(' (épuisé)')}
                </span>
                <span className="text-sm font-medium">{money(variant.price, catalog.currencyCode)}</span>
              </label>
            ))}
          </div>
        </fieldset>
      )}

      {steps.map(step => {
        const list = picked[step.id] ?? [];
        return (
          <fieldset key={step.id} className="mt-5">
            <legend className="mb-2 font-semibold">{step.name} <span className="text-sm font-normal text-ink-500">({stepRule(step)})</span></legend>
            <div className="space-y-2">
              {step.choices.map(choice => {
                const on = list.some(item => sameChoice(item, choice));
                const full = !on && step.maxChoices > 1 && list.length >= step.maxChoices;
                const component = list.find(item => sameChoice(item, choice));
                const own = choice.modifierGroupIds.map(id => catalog.modifierGroups.find(group => group.id === id)).filter(Boolean) as Catalog['modifierGroups'];
                return (
                  <div key={`${choice.productId}/${choice.variantId ?? ''}`} className={cx('rounded-xl border', on ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200', (!choice.isAvailable || full) && 'opacity-50')}>
                    <label className="flex items-center justify-between gap-3 px-4 py-3">
                      <span className="flex items-center gap-3">
                        <input
                          type={step.maxChoices === 1 ? 'radio' : 'checkbox'}
                          name={`step-${step.id}`}
                          className="size-4 accent-[var(--accent)]"
                          checked={on}
                          disabled={!choice.isAvailable || full}
                          onChange={() => pick(step, choice)} />
                        <span>{choice.name}{choice.variantName && <span className="text-ink-500"> · {choice.variantName}</span>}{!choice.isAvailable && tr(' (épuisé)')}</span>
                      </span>
                      {choice.supplement > 0 && <span className="text-sm text-ink-600">+ {money(choice.supplement, catalog.currencyCode)}</span>}
                    </label>
                    {component && own.length > 0 && (
                      <div className="border-t border-slate-200 px-4 pb-3">
                        <ModifierFieldsets groups={own} currency={catalog.currencyCode} compact scope={`${step.id}-${choice.productId}-${choice.variantId ?? ''}`}
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

      <ModifierFieldsets groups={groups} currency={catalog.currencyCode} selectedIn={group => chosen[group.id] ?? []}
        onToggle={(group, id) => toggle(group.id, id, group.maxSelections)} />

      <label className="mt-5 block">
        <span className="mb-2 block font-semibold">{tr('Note pour la cuisine')} <span className="text-sm font-normal text-ink-500">{tr('(facultatif)')}</span></span>
        <textarea value={note} onChange={event => setNote(event.target.value.slice(0, 200))} rows={2} placeholder={tr('Sans piment, bien cuit…')}
          className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm focus:border-[var(--accent)] focus:outline-none" />
      </label>
    </Sheet>
  );
}

interface Choice {
  payment: PaymentMode;
  provider: string | null;
  method: string | null;
}

/** Un créneau lisible dans le fuseau de l'établissement : « lun. 19 janv. » et « 12:30 ». */
function slotParts(utc: string, timeZone: string) {
  const at = new Date(utc);
  try {
    return {
      day: new Intl.DateTimeFormat(intlLocale(), {weekday: 'short', day: 'numeric', month: 'short', timeZone}).format(at),
      time: new Intl.DateTimeFormat(intlLocale(), {hour: '2-digit', minute: '2-digit', timeZone}).format(at),
    };
  } catch {
    return {day: at.toLocaleDateString(intlLocale()), time: clock(utc)};
  }
}

/** Panier et validation : devis du serveur, mode, zone et adresse de livraison, code promo, créneau, coordonnées, paiement. */
function Checkout({front, location, catalog, table, cart, onCart, onClose}: {
  front: Storefront;
  location: StorefrontLocation;
  catalog: Catalog;
  table?: TableContext;
  cart: CartLine[];
  onCart: (lines: CartLine[]) => void;
  onClose: () => void;
}) {
  const router = useRouter();
  const zones = location.delivery?.zones ?? [];
  const modes: ServiceMode[] = table ? ['DineIn'] : location.serviceModes.filter(mode => mode !== 'Delivery' || zones.length > 0);
  const [mode, setMode] = useState<ServiceMode>(modes[0] ?? 'Takeaway');
  const [zoneId, setZoneId] = useState<string>(zones[0]?.id ?? '');
  const [address, setAddress] = useState({line: '', landmark: '', instructions: ''});
  const [couponInput, setCouponInput] = useState('');
  const [couponCode, setCouponCode] = useState<string | null>(null);
  const [quote, setQuote] = useState<Quote | null>(null);
  const [quoteError, setQuoteError] = useState<string | null>(null);
  const [when, setWhen] = useState<'now' | 'later'>(location.acceptsOrdersNow ? 'now' : 'later');
  const [slots, setSlots] = useState<Slots | null>(null);
  const [slot, setSlot] = useState('');
  const [customer, setCustomer] = useState({name: '', phone: '', email: '', consent: false});
  const [choice, setChoice] = useState<Choice>({payment: 'Counter', provider: null, method: null});
  const signedIn = useSyncExternalStore(onCustomerChange, () => readCustomerToken() !== null, () => false);
  const [account, setAccount] = useState<CustomerAccount | null>(null);
  const [rewardId, setRewardId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const idempotency = useRef(newIdempotencyKey());
  const delivering = mode === 'Delivery';
  const zone = zones.find(item => item.id === zoneId) ?? null;

  const payload = useMemo(() => cart.map(toPayload), [cart]);

  // Client connecté : sa fiche remplit les coordonnées, ses points ouvrent les récompenses.
  useEffect(() => {
    if (!signedIn) {
      setAccount(null);
      setRewardId(null);
      return;
    }

    const controller = new AbortController();
    loadAccount(controller.signal)
      .then(loaded => {
        setAccount(loaded);
        if (loaded) setCustomer(current => ({...current, name: loaded.name, phone: loaded.phone ?? current.phone, email: loaded.email ?? ''}));
      })
      .catch(() => {});
    return () => controller.abort();
  }, [signedIn]);
  useEffect(() => {
    idempotency.current = newIdempotencyKey();
    if (cart.length === 0) {
      setQuote(null);
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      storefront<Quote>('/cart/quote', {
        method: 'POST',
        body: {locationId: location.id, mode, lines: payload, couponCode, deliveryZoneId: delivering && zoneId ? zoneId : null, rewardId: account ? rewardId : null},
        signal: controller.signal,
      })
        .then(result => {
          setQuote(result);
          setQuoteError(null);
          setChoice(current => {
            if (!result) return current;
            if (current.payment === 'Counter' && !result.canPayAtCounter && result.canPayOnline) {
              const option = result.onlinePaymentOptions[0];
              return {payment: 'Online', provider: option.provider, method: option.methods[0] ?? null};
            }
            return current;
          });
        })
        .catch(reason => !controller.signal.aborted && setQuoteError(errorMessage(reason)));
    }, 250);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [payload, mode, location.id, cart.length, couponCode, delivering, zoneId, rewardId, account]);

  useEffect(() => {
    if (when !== 'later') return;
    const controller = new AbortController();
    const query = new URLSearchParams({mode});
    if (delivering && zoneId) query.set('zoneId', zoneId);
    storefront<Slots>(`/locations/${location.id}/slots?${query}`, {signal: controller.signal})
      .then(result => {
        setSlots(result);
        setSlot(current => (result?.slots.some(item => item.startUtc === current && item.isAvailable) ? current : ''));
      })
      .catch(reason => !controller.signal.aborted && setQuoteError(errorMessage(reason)));
    return () => controller.abort();
  }, [when, mode, delivering, zoneId, location.id]);

  const days = useMemo(() => {
    if (!slots) return [];
    const grouped = new Map<string, {startUtc: string; time: string; isAvailable: boolean}[]>();
    for (const item of slots.slots) {
      const {day, time} = slotParts(item.startUtc, slots.timeZoneId);
      grouped.set(day, [...(grouped.get(day) ?? []), {startUtc: item.startUtc, time, isAvailable: item.isAvailable}]);
    }
    return [...grouped.entries()];
  }, [slots]);

  const setQuantity = (key: string, quantity: number) =>
    onCart(quantity <= 0 ? cart.filter(line => line.key !== key) : cart.map(line => (line.key === key ? {...line, quantity: Math.min(99, quantity)} : line)));

  const couponOk = !couponCode || quote?.coupon?.isValid === true;
  const deliveryOk = !delivering || (!!zone && customer.phone.trim() !== '' && address.line.trim() !== '');
  const contactOk = table ? true : customer.name.trim() !== '' && (customer.phone.trim() !== '' || customer.email.trim() !== '');
  const canSubmit = quote && quote.meetsMinimum && cart.length > 0 && contactOk
    && deliveryOk && couponOk
    && (when === 'now' ? location.acceptsOrdersNow : slot !== '')
    && (choice.payment === 'Counter' ? quote.canPayAtCounter : choice.provider !== null);

  const submit = async () => {
    if (!quote || !canSubmit) return;
    setBusy(true);
    setError(null);
    try {
      const placed = await storefront<PlacedOrder>('/orders', {
        method: 'POST',
        idempotencyKey: idempotency.current,
        humanCheck: true,
        body: {
          locationId: location.id,
          mode,
          lines: payload,
          payment: choice.payment,
          customer: table && !customer.name.trim()
            ? null
            : {name: customer.name.trim(), phone: customer.phone.trim() || null, email: customer.email.trim() || null, marketingConsent: customer.consent, language: currentLang()},
          scheduledForUtc: !table && when === 'later' ? slot : null,
          tableToken: table?.token ?? null,
          expectedTotal: quote.total,
          couponCode: quote.coupon?.isValid ? quote.coupon.code : null,
          delivery: delivering
            ? {zoneId, addressLine: address.line.trim(), landmark: address.landmark.trim() || null, instructions: address.instructions.trim() || null}
            : null,
          rewardId: account && quote.reward ? quote.reward.rewardId : null,
        },
      });
      if (!placed) return;
      rememberOrder(placed.trackingToken, placed.number);
      onCart([]);
      const tracking = `/commande?jeton=${encodeURIComponent(placed.trackingToken)}`;
      if (choice.payment === 'Online' && choice.provider) {
        try {
          const started = await storefront<{paymentId: string; checkoutUrl: string | null}>(
            `/orders/${encodeURIComponent(placed.trackingToken)}/payments`,
            {
              method: 'POST',
              idempotencyKey: newIdempotencyKey(),
              body: {provider: choice.provider, method: choice.method, payerPhone: customer.phone.trim() || null, returnUrl: `${window.location.origin}/commande?jeton=${encodeURIComponent(placed.trackingToken)}`},
            });
          if (started?.checkoutUrl) {
            window.location.assign(started.checkoutUrl);
            return;
          }

          router.push(`${tracking}&telephone=1`);
          return;
        } catch {
          router.push(tracking);
          return;
        }
      }

      router.push(tracking);
    } catch (reason) {
      setError(errorMessage(reason));
      // Un refus (4xx) est définitif pour cette tentative : la suivante, corrigée, est une nouvelle demande.
      if (reason instanceof StorefrontError && reason.status >= 400 && reason.status < 500) idempotency.current = newIdempotencyKey();
      if (reason instanceof StorefrontError && reason.code === 'storefront.checkout.price_changed') setQuote(null);
      if (reason instanceof StorefrontError && reason.code === 'storefront.checkout.slot_full') setSlot('');
    } finally {
      setBusy(false);
    }
  };

  const currency = quote?.currencyCode ?? catalog.currencyCode;
  const input = 'w-full rounded-xl border border-slate-300 px-3 py-2.5';
  return (
    <Sheet
      open
      onClose={onClose}
      title={tr('Votre commande')}
      footer={
        <div className="space-y-3">
          {error && <Notice tone="error">{error}</Notice>}
          <AccentButton className="w-full" busy={busy} disabled={!canSubmit} onClick={() => void submit()}>
            {choice.payment === 'Online' ? tr('Commander et payer') : tr('Commander')}{quote ? ` · ${money(quote.total, currency)}` : ''}
          </AccentButton>
        </div>
      }>
      {cart.length === 0 && <p className="py-6 text-center text-ink-500">{tr('Votre panier est vide.')}</p>}
      <ul className="divide-y divide-slate-100">
        {cart.map((line, index) => {
          const quoted = quote?.lines[index];
          const product = catalog.products.find(item => item.id === line.productId);
          return (
            <li key={line.key} className="flex items-start gap-3 py-3">
              <div className="min-w-0 flex-1">
                <p className="font-medium">{quoted?.name ?? product?.name}{quoted?.variantName ? ` · ${quoted.variantName}` : ''}</p>
                {quoted?.components?.map((component, position) => <p key={position} className="text-sm text-ink-600">{component.stepName} : {componentLabel(component)}</p>)}
                {quoted && quoted.modifiers.length > 0 && <p className="text-sm text-ink-500">{quoted.modifiers.map(modifier => modifier.name).join(', ')}</p>}
                {line.note.trim() && <p className="text-sm italic text-ink-500">« {line.note.trim()} »</p>}
                <div className="mt-2 flex items-center gap-2">
                  <button type="button" aria-label={tr('Retirer un')} onClick={() => setQuantity(line.key, line.quantity - 1)} className="grid size-8 place-items-center rounded-lg border border-slate-200">
                    {line.quantity === 1 ? <Trash2 className="size-4" /> : <Minus className="size-4" />}
                  </button>
                  <span className="w-6 text-center text-sm font-semibold">{line.quantity}</span>
                  <button type="button" aria-label={tr('Ajouter un')} onClick={() => setQuantity(line.key, line.quantity + 1)} className="grid size-8 place-items-center rounded-lg border border-slate-200"><Plus className="size-4" /></button>
                </div>
              </div>
              <span className="text-sm font-semibold">{quoted ? money(quoted.lineTotal, currency) : '…'}</span>
            </li>
          );
        })}
      </ul>

      {cart.length > 0 && (
        <div className="mt-3 border-t border-slate-100 pt-3">
          {couponCode && quote?.coupon ? (
            quote.coupon.isValid ? (
              <p className="flex items-center justify-between gap-3 rounded-xl bg-emerald-50 px-3 py-2 text-sm text-emerald-900">
                <span className="flex items-center gap-2"><Check className="size-4" aria-hidden="true" />{quote.coupon.code} · {quote.coupon.label}</span>
                <button type="button" aria-label={tr('Retirer le code promo')} onClick={() => setCouponCode(null)} className="grid size-7 place-items-center rounded-full hover:bg-emerald-100"><X className="size-4" /></button>
              </p>
            ) : (
              <div className="flex items-center justify-between gap-3 rounded-xl bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">
                <span>{quote.coupon.code} : {couponRefusal(quote.coupon.refusalCode)}</span>
                <button type="button" aria-label={tr('Retirer le code promo')} onClick={() => setCouponCode(null)} className="grid size-7 place-items-center rounded-full hover:bg-red-100"><X className="size-4" /></button>
              </div>
            )
          ) : (
            <form className="flex gap-2" onSubmit={event => {
              event.preventDefault();
              const code = couponInput.trim().toUpperCase();
              if (code) setCouponCode(code);
            }}>
              <label className="relative flex-1">
                <span className="sr-only">{tr('Code promo')}</span>
                <Tag className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-400" aria-hidden="true" />
                <input value={couponInput} maxLength={32} placeholder={tr('Code promo')} autoCapitalize="characters" onChange={event => setCouponInput(event.target.value)}
                  className="w-full rounded-xl border border-slate-300 py-2 pl-9 pr-3 text-sm uppercase placeholder:normal-case" />
              </label>
              <button type="submit" disabled={!couponInput.trim()} className="rounded-xl border border-slate-300 px-4 text-sm font-semibold disabled:opacity-50">{tr('Appliquer')}</button>
            </form>
          )}
        </div>
      )}

      {quoteError && <div className="mt-3"><Notice tone="error">{quoteError}</Notice></div>}
      {quote && (
        <dl className="mt-3 space-y-1 border-t border-slate-100 pt-3 text-sm">
          {(quote.discountTotal > 0 || quote.deliveryFee !== null) && (
            <div className="flex justify-between text-ink-600"><dt>{tr('Sous-total')}</dt><dd>{money(quote.subTotal, currency)}</dd></div>
          )}
          {quote.discountTotal > 0 && (
            <div className="flex justify-between text-emerald-700">
              <dt>{tr('Remise')}{[quote.coupon?.isValid ? quote.coupon.code : null, quote.reward?.name].filter(Boolean).length > 0 ? ` (${[quote.coupon?.isValid ? quote.coupon.code : null, quote.reward?.name].filter(Boolean).join(', ')})` : ''}</dt>
              <dd>−{money(quote.discountTotal, currency)}</dd>
            </div>
          )}
          {quote.deliveryFee !== null && (
            <div className="flex justify-between text-ink-600"><dt>{tr('Livraison')}{zone ? ` · ${zone.name}` : ''}</dt><dd>{quote.deliveryFee > 0 ? money(quote.deliveryFee, currency) : tr('Offerte')}</dd></div>
          )}
          <div className="flex justify-between text-base font-bold"><dt>{quote.pricesIncludeTax ? tr('Total TTC') : tr('Total')}</dt><dd>{money(quote.total, currency)}</dd></div>
          {quote.taxTotal > 0 && <div className="flex justify-between text-ink-500"><dt>{tr('Dont taxes')}</dt><dd>{money(quote.taxTotal, currency)}</dd></div>}
          {!quote.meetsMinimum && quote.minimumOrderAmount !== null && (
            <div className="pt-2"><Notice tone="warning">{tr('Minimum de commande')}{delivering && zone ? ` pour ${zone.name}` : ''} : {money(quote.minimumOrderAmount, currency)} {tr('(hors livraison).')}</Notice></div>
          )}
        </dl>
      )}

      {cart.length > 0 && (
        <div className="mt-6 space-y-6">
          {modes.length > 1 && (
            <fieldset>
              <legend className="mb-2 font-semibold">{tr('Mode')}</legend>
              <div className={cx('grid gap-2', modes.length === 3 ? 'grid-cols-3' : 'grid-cols-2')}>
                {modes.map(item => (
                  <button key={item} type="button" aria-pressed={mode === item} onClick={() => setMode(item)}
                    className={cx('rounded-xl border px-3 py-3 text-sm font-medium', mode === item ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                    {modeLabel[item]}
                  </button>
                ))}
              </div>
            </fieldset>
          )}

          {delivering && (
            <fieldset className="space-y-3">
              <legend className="mb-2 flex items-center gap-2 font-semibold"><Bike className="size-4" aria-hidden="true" />{tr('Livraison')}</legend>
              <label className="block text-sm">
                <span className="mb-1 block text-ink-600">{tr('Votre quartier')}</span>
                <select value={zoneId} onChange={event => setZoneId(event.target.value)} className={input}>
                  {zones.map(item => (
                    <option key={item.id} value={item.id}>
                      {item.name} · {item.fee > 0 ? money(item.fee, currency) : 'livraison offerte'} · ~{item.estimatedMinutes} {tr('min')}
                    </option>
                  ))}
                </select>
              </label>
              {zone && zone.minimumOrder > 0 && <p className="text-xs text-ink-500">{tr('Minimum de commande pour')} {zone.name} : {money(zone.minimumOrder, currency)}.</p>}
              <input autoComplete="street-address" placeholder={tr('Adresse (rue, maison, étage)')} value={address.line} maxLength={200}
                onChange={event => setAddress(current => ({...current, line: event.target.value}))} className={input} aria-label={tr('Adresse')} />
              <input placeholder={tr('Repère (facultatif) : face à la pharmacie…')} value={address.landmark} maxLength={200}
                onChange={event => setAddress(current => ({...current, landmark: event.target.value}))} className={input} aria-label={tr('Repère')} />
              <textarea placeholder={tr('Consignes au livreur (facultatif)')} value={address.instructions} maxLength={500} rows={2}
                onChange={event => setAddress(current => ({...current, instructions: event.target.value}))} className={cx(input, 'text-sm')} aria-label={tr('Consignes au livreur')} />
            </fieldset>
          )}

          {!table && (
          <fieldset>
            <legend className="mb-2 font-semibold">{delivering ? tr('Livrée quand ?') : tr('Quand ?')}</legend>
            <div className="grid grid-cols-2 gap-2">
              <button type="button" aria-pressed={when === 'now'} disabled={!location.acceptsOrdersNow} onClick={() => setWhen('now')}
                className={cx('rounded-xl border px-4 py-3 text-sm font-medium disabled:opacity-50', when === 'now' ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                {tr('Dès que possible')}
              </button>
              <button type="button" aria-pressed={when === 'later'} onClick={() => setWhen('later')}
                className={cx('rounded-xl border px-4 py-3 text-sm font-medium', when === 'later' ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                {tr('Choisir l’heure')}
              </button>
            </div>
            {when === 'now' && delivering && zone && location.delivery && (
              <p className="mt-2 text-xs text-ink-500">{tr('Livraison estimée vers')} {clock(new Date(Date.now() + (location.delivery.leadMinutes + zone.estimatedMinutes) * 60000).toISOString(), location.timeZoneId)}.</p>
            )}
            {when === 'later' && (
              <label className="mt-2 block text-sm">
                <span className="mb-1 block text-ink-600">{tr('Créneau')}</span>
                {!slots ? (
                  <span className="block text-ink-500">{tr('Chargement des créneaux…')}</span>
                ) : days.length === 0 ? (
                  <span className="block text-ink-500">{tr('Aucun créneau dans les 7 prochains jours.')}</span>
                ) : (
                  <select value={slot} onChange={event => setSlot(event.target.value)} className={input}>
                    <option value="">{tr('Choisissez un créneau')}</option>
                    {days.map(([day, items]) => (
                      <optgroup key={day} label={day}>
                        {items.map(item => (
                          <option key={item.startUtc} value={item.startUtc} disabled={!item.isAvailable}>
                            {day} · {item.time}{item.isAvailable ? '' : ' (complet)'}
                          </option>
                        ))}
                      </optgroup>
                    ))}
                  </select>
                )}
              </label>
            )}
          </fieldset>
          )}

          {account?.loyalty.enabled && account.loyalty.rewards.length > 0 && (
            <fieldset>
              <legend className="mb-2 flex items-center gap-2 font-semibold"><Gift className="size-4" aria-hidden="true" />{tr('Mes points ·')} {account.loyalty.balance.toLocaleString('fr-FR')}</legend>
              <div className="space-y-2">
                <label className={cx('flex items-center gap-3 rounded-xl border px-4 py-3 text-sm', rewardId === null ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                  <input type="radio" name="reward" className="size-4 accent-[var(--accent)]" checked={rewardId === null} onChange={() => setRewardId(null)} />
                  {tr('Garder mes points')}
                </label>
                {account.loyalty.rewards.map(reward => (
                  <label key={reward.id} className={cx('flex items-center gap-3 rounded-xl border px-4 py-3 text-sm', rewardId === reward.id ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200', !reward.affordable && 'opacity-50')}>
                    <input type="radio" name="reward" className="size-4 accent-[var(--accent)]" disabled={!reward.affordable} checked={rewardId === reward.id} onChange={() => setRewardId(reward.id)} />
                    <span className="flex-1">{reward.name} <span className="text-ink-500">· {reward.pointCost.toLocaleString('fr-FR')} {tr('points')}</span></span>
                    <span className="font-medium text-emerald-700">−{money(reward.discount, currency)}</span>
                  </label>
                ))}
              </div>
            </fieldset>
          )}

          {account ? (
            <fieldset className="space-y-3">
              <legend className="mb-2 font-semibold">{tr('Vos coordonnées')}</legend>
              <p className="flex items-center gap-2 rounded-xl bg-slate-50 px-4 py-3 text-sm">
                <CircleUser className="size-5 text-ink-500" aria-hidden="true" />
                <span className="min-w-0 flex-1 truncate">{account.name}{account.phone ? ` · ${account.phone}` : account.email ? ` · ${account.email}` : ''}</span>
                <Link href={`/compte?retour=${encodeURIComponent(`/commander/${location.id}`)}`} className="text-xs font-medium text-[var(--accent)] hover:underline">{tr('Mon compte')}</Link>
              </p>
              {delivering && !account.phone && (
                <input autoComplete="tel" type="tel" placeholder={tr('Téléphone (obligatoire pour la livraison)')} value={customer.phone} onChange={event => setCustomer(c => ({...c, phone: event.target.value}))} className={input} aria-label={tr('Téléphone')} />
              )}
            </fieldset>
          ) : (
          <fieldset className="space-y-3">
            <legend className="mb-2 font-semibold">{tr('Vos coordonnées')}{table ? ' (facultatif)' : ''}</legend>
            {table && <p className="text-sm text-ink-500">{tr('Laissez votre nom et votre téléphone pour être prévenu ou cumuler des points ; sinon, commandez directement.')}</p>}
            <p className="text-sm text-ink-500">
              {tr('Déjà client ?')} <Link href={`/compte?retour=${encodeURIComponent(`/commander/${location.id}`)}`} className="font-medium text-[var(--accent)] hover:underline">{tr('Connectez-vous')}</Link> {tr('pour retrouver vos points.')}
            </p>
            <input autoComplete="name" placeholder={tr('Nom')} value={customer.name} onChange={event => setCustomer(c => ({...c, name: event.target.value}))} className={input} aria-label={tr('Nom')} />
            <input autoComplete="tel" type="tel" placeholder={delivering ? tr('Téléphone (obligatoire pour la livraison)') : tr('Téléphone (ex. +229 01 97 00 00 00)')} value={customer.phone} onChange={event => setCustomer(c => ({...c, phone: event.target.value}))} className={input} aria-label={tr('Téléphone')} />
            <input autoComplete="email" type="email" placeholder={tr('E-mail (facultatif avec un téléphone)')} value={customer.email} onChange={event => setCustomer(c => ({...c, email: event.target.value}))} className={input} aria-label="E-mail" />
            <label className="flex items-start gap-3 text-sm text-ink-600">
              <input type="checkbox" className="mt-0.5 size-4 accent-[var(--accent)]" checked={customer.consent} onChange={event => setCustomer(c => ({...c, consent: event.target.checked}))} />
              {tr('J’accepte de recevoir les offres de')} {front.restaurant.name}.
            </label>
          </fieldset>
          )}

          {quote && (
            <fieldset>
              <legend className="mb-2 font-semibold">{tr('Paiement')}</legend>
              <div className="space-y-2">
                {location.payment.payAtCounter && (
                  <label className={cx('flex items-center gap-3 rounded-xl border px-4 py-3 text-sm', choice.payment === 'Counter' ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200', !quote.canPayAtCounter && 'opacity-50')}>
                    <input type="radio" name="payment" className="size-4 accent-[var(--accent)]" disabled={!quote.canPayAtCounter} checked={choice.payment === 'Counter'}
                      onChange={() => setChoice({payment: 'Counter', provider: null, method: null})} />
                    <span>{table ? tr('Au serveur, à table') : delivering ? tr('À la livraison') : tr('Au comptoir, au retrait')}{!quote.canPayAtCounter && location.payment.counterLimit !== null ? tr(' (jusqu’à {p0})', {p0: money(location.payment.counterLimit, currency)}) : ''}</span>
                  </label>
                )}
                {quote.onlinePaymentOptions.flatMap(option => option.methods.map(method => {
                  const on = choice.payment === 'Online' && choice.provider === option.provider && choice.method === method;
                  return (
                    <label key={`${option.provider}-${method}`} className={cx('flex items-center gap-3 rounded-xl border px-4 py-3 text-sm', on ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                      <input type="radio" name="payment" className="size-4 accent-[var(--accent)]" checked={on} onChange={() => setChoice({payment: 'Online', provider: option.provider, method})} />
                      <span>{tr('En ligne ·')} {methodLabel[method] ?? method} <span className="text-ink-500">({option.displayName})</span></span>
                    </label>
                  );
                }))}
              </div>
            </fieldset>
          )}
        </div>
      )}
    </Sheet>
  );
}
