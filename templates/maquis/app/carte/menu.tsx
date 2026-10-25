'use client';
import {useEffect, useMemo, useRef, useState} from 'react';
import Link from 'next/link';
import {useRouter, useSearchParams} from 'next/navigation';
import {Clock, Minus, Plus, ShoppingBag, Trash2} from 'lucide-react';
import {linePrice, newIdempotencyKey, type CartLine, type Catalog, type CatalogProduct, type ServiceMode, type StorefrontLocation} from '@/lib/storefront';
import {clock, modeLabel, money, openingText, priceParts} from '@/lib/format';
import {useCart, useModeChoice, writeModeChoice} from '@/lib/local';
import {useSite} from '@/components/site-provider';
import {productIndex, sectionsOf, useCatalog} from '@/components/use-catalog';
import {isSimple, ProductSheet} from '@/components/product-sheet';
import {Plate, pastel} from '@/components/art';
import {Button, ButtonLink, Notice, Skeleton, cx, inputClass} from '@/components/ui';

/** Les modes proposés : la livraison seulement avec des quartiers desservis. */
export const modesOf = (location: StorefrontLocation) =>
  location.serviceModes.filter(mode => mode !== 'Delivery' || (location.delivery?.zones.length ?? 0) > 0);

/** Heure de retrait ou de livraison estimée pour une commande passée maintenant. */
export function readyAt(location: StorefrontLocation, mode: ServiceMode, zoneId: string | null) {
  if (!location.acceptsOrdersNow) return null;
  const zone = location.delivery?.zones.find(item => item.id === zoneId);
  const minutes = mode === 'Delivery' && zone && location.delivery ? location.delivery.leadMinutes + zone.estimatedMinutes : location.preparationMinutes;
  return clock(new Date(Date.now() + minutes * 60_000).toISOString(), location.timeZoneId);
}

/** « Riz, Piment · Sans oignon » : les choix d'une ligne du panier, d'après la carte. */
export function lineDetails(line: CartLine, catalog: Catalog) {
  const product = catalog.products.find(item => item.id === line.productId);
  const modifiers = catalog.modifierGroups.flatMap(group => group.modifiers);
  const names = (ids: string[]) => modifiers.filter(modifier => ids.includes(modifier.id)).map(modifier => modifier.name);
  const variant = product?.variants.find(item => item.id === line.variantId)?.name;
  const components = (line.components ?? []).map(component => {
    const choice = product?.formulaSteps?.find(step => step.id === component.stepId)?.choices
      .find(item => item.productId === component.productId && item.variantId === component.variantId);
    return [choice?.name, ...names(component.modifierIds)].filter(Boolean).join(' + ');
  });
  return {name: product?.name ?? 'Article', details: [variant, ...components, ...names(line.modifierIds)].filter(Boolean).join(', ')};
}

export function Menu() {
  const {front, location, error: siteError, chooseLocation, retry} = useSite();
  const params = useSearchParams();
  const router = useRouter();
  const {catalog, error, retry: retryCatalog} = useCatalog(location?.id ?? null);
  const [cart, setCart] = useCart(location?.id ?? null);
  const choice = useModeChoice();
  const [sheet, setSheet] = useState<CatalogProduct | null>(null);
  const opened = useRef(false);

  const modes = location ? modesOf(location) : [];
  const asked = params.get('mode');
  const mode: ServiceMode = modes.find(item => item === asked) ?? modes.find(item => item === choice.mode) ?? modes[0] ?? 'Takeaway';
  const zones = location?.delivery?.zones ?? [];
  const zoneId = zones.find(zone => zone.id === choice.zoneId)?.id ?? zones[0]?.id ?? null;

  // Le mode demandé par l'adresse (« Se faire livrer ») devient le choix mémorisé.
  useEffect(() => {
    if (location && asked && modes.includes(asked as ServiceMode) && choice.mode !== asked) writeModeChoice(asked as ServiceMode);
  }, [location, asked, modes, choice.mode]);

  // Un produit demandé par l'adresse (bouton « + » de l'accueil) s'ouvre une fois la carte chargée.
  useEffect(() => {
    if (!catalog || opened.current) return;
    opened.current = true;
    const product = catalog.products.find(item => item.id === params.get('produit'));
    if (product?.isAvailable) {
      setSheet(product);
      document.getElementById(`produit-${product.id}`)?.scrollIntoView({block: 'center'});
    }
  }, [catalog, params]);

  const closeSheet = () => {
    setSheet(null);
    if (params.has('produit') && location) router.replace(`/carte/?etablissement=${location.id}`, {scroll: false});
  };

  const add = (line: CartLine) => setCart([...cart, line]);
  const simpleKey = (productId: string) => cart.find(line => line.productId === productId && !line.variantId && line.modifierIds.length === 0 && !line.note.trim() && !line.components?.length);
  const quickAdd = (product: CatalogProduct) => {
    const existing = simpleKey(product.id);
    if (existing) setCart(cart.map(line => (line === existing ? {...line, quantity: Math.min(99, line.quantity + 1)} : line)));
    else add({key: newIdempotencyKey(), productId: product.id, variantId: null, modifierIds: [], quantity: 1, note: ''});
  };
  const setQuantity = (key: string, quantity: number) =>
    setCart(quantity <= 0 ? cart.filter(line => line.key !== key) : cart.map(line => (line.key === key ? {...line, quantity: Math.min(99, quantity)} : line)));

  if (siteError) {
    return (
      <div className="mx-auto max-w-2xl px-4 py-10">
        <Notice tone="error">{siteError}</Notice>
        <Button variant="ghost" className="mt-4" onClick={retry}>Réessayer</Button>
      </div>
    );
  }

  const ordering = front?.orderingEnabled !== false;
  const count = cart.reduce((sum, line) => sum + line.quantity, 0);
  const estimate = catalog ? cart.reduce((sum, line) => sum + (linePrice(line, catalog) ?? 0), 0) : 0;
  const status = location ? openingText(location) : null;
  const ready = location ? readyAt(location, mode, zoneId) : null;

  return (
    <div className="pb-28 lg:pb-0">
      <div className="mx-auto max-w-6xl px-4 pt-8 sm:px-6">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="font-serif text-4xl font-bold">La carte</h1>
            {status ? <p className={cx('mt-1 flex items-center gap-2 text-sm font-semibold', status.open ? 'text-leaf' : 'text-muted')}><Clock className="size-4" aria-hidden="true" />{status.text}{location && status.open ? ` · prêt en ${location.preparationMinutes} min environ` : ''}</p> : <Skeleton className="mt-2 h-5 w-48" />}
          </div>
          {front && front.locations.length > 1 && location && (
            <label className="w-full sm:w-auto">
              <span className="mb-1 block text-sm font-semibold">Établissement</span>
              <select value={location.id} onChange={event => {
                chooseLocation(event.target.value);
                router.replace(`/carte/?etablissement=${event.target.value}`, {scroll: false});
              }} className={cx(inputClass, 'sm:min-w-64')}>
                {front.locations.map(item => <option key={item.id} value={item.id}>{item.name} · {item.city}</option>)}
              </select>
            </label>
          )}
        </div>

        <div className="mt-4 space-y-3">
          {!ordering && <Notice tone="warning">La commande en ligne n’est pas disponible : consultez la carte et appelez le restaurant.</Notice>}
          {ordering && location && !location.acceptsOrdersNow && (
            <Notice tone="warning">
              {location.isPaused ? 'Les commandes en ligne sont en pause pour le moment. Vous pouvez consulter la carte.'
                : location.nextOpeningUtc ? `L’établissement est fermé : il ouvre à ${clock(location.nextOpeningUtc, location.timeZoneId)}. Vous pourrez choisir une heure.`
                : 'L’établissement ne prend pas de commandes pour le moment.'}
            </Notice>
          )}
        </div>

        {ordering && location && modes.length > 1 && (
          <div className="mt-5 grid max-w-xl gap-3 sm:grid-cols-[1fr_auto] sm:items-center">
            <div role="radiogroup" aria-label="Mode de commande" className={cx('grid rounded-2xl border border-line bg-white p-1 text-sm font-semibold', modes.length === 3 ? 'grid-cols-3' : 'grid-cols-2')}>
              {modes.map(item => (
                <button key={item} type="button" role="radio" aria-checked={mode === item} onClick={() => writeModeChoice(item)}
                  className={cx('rounded-xl px-2 py-2.5 transition', mode === item ? 'bg-ink text-white' : 'text-muted hover:text-ink')}>
                  {modeLabel[item]}
                </button>
              ))}
            </div>
          </div>
        )}
        {ordering && mode === 'Delivery' && zones.length > 0 && (
          <label className="mt-3 block max-w-xl">
            <span className="mb-1 block text-sm font-semibold">Votre quartier</span>
            <select value={zoneId ?? ''} onChange={event => writeModeChoice('Delivery', event.target.value)} className={inputClass}>
              {zones.map(zone => (
                <option key={zone.id} value={zone.id}>
                  {zone.name} · {zone.fee > 0 ? money(zone.fee, front?.restaurant.currencyCode ?? 'XOF') : 'livraison offerte'} · ~{zone.estimatedMinutes} min{zone.minimumOrder > 0 ? ` · minimum ${money(zone.minimumOrder, front?.restaurant.currencyCode ?? 'XOF')}` : ''}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>

      {error ? (
        <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6"><Notice tone="error">{error}</Notice><Button variant="ghost" className="mt-4" onClick={retryCatalog}>Réessayer</Button></div>
      ) : !catalog ? (
        <div className="mx-auto max-w-6xl space-y-4 px-4 py-8 sm:px-6" aria-busy="true" aria-label="Chargement de la carte">
          <Skeleton className="h-10 w-full max-w-lg rounded-full" />
          {Array.from({length: 4}, (_, index) => <Skeleton key={index} className="h-24 w-full max-w-2xl rounded-2xl" />)}
        </div>
      ) : (
        <div className="mx-auto mt-4 grid max-w-6xl gap-8 sm:px-6 lg:grid-cols-[1fr_360px]">
          <MenuSections catalog={catalog} ordering={ordering} cart={cart} onOpen={setSheet} onQuickAdd={quickAdd} quantityOf={product => simpleKey(product.id)}
            onQuantity={setQuantity} />
          {ordering && (
            <aside aria-label="Panier" className="hidden lg:block">
              <div className="sticky top-[calc(var(--top,0px)+16px)] mt-4 rounded-[22px] border border-line bg-white p-5 shadow-[0_6px_18px_rgba(90,44,16,.06)]">
                <h2 className="flex items-center gap-2 font-serif text-xl font-bold"><ShoppingBag className="size-5 text-primary" aria-hidden="true" />Votre panier</h2>
                {cart.length === 0 ? (
                  <p className="py-6 text-center text-sm text-muted">Votre panier est vide. Choisissez un plat pour commencer.</p>
                ) : (
                  <>
                    <ul className="mt-3 max-h-[50vh] divide-y divide-line overflow-y-auto">
                      {cart.map(line => {
                        const {name, details} = lineDetails(line, catalog);
                        return (
                          <li key={line.key} className="flex items-start gap-3 py-3 text-sm">
                            <div className="min-w-0 flex-1">
                              <p className="font-semibold">{name}</p>
                              {details && <p className="text-muted">{details}</p>}
                              {line.note.trim() && <p className="italic text-muted">« {line.note.trim()} »</p>}
                              <Stepper quantity={line.quantity} onChange={value => setQuantity(line.key, value)} label={name} />
                            </div>
                            <span className="font-semibold">{money(linePrice(line, catalog) ?? 0, catalog.currencyCode)}</span>
                          </li>
                        );
                      })}
                    </ul>
                    <p className="mt-3 flex justify-between border-t border-line pt-3 font-bold"><span>Sous-total</span><span>{money(estimate, catalog.currencyCode)}</span></p>
                    <p className="mt-1 text-xs text-muted">{modeLabel[mode]}{ready ? ` · ${mode === 'Delivery' ? 'livré' : 'prêt'} vers ${ready}` : ''}. Le total exact est calculé à l’étape suivante.</p>
                    <ButtonLink href={`/commander/?etablissement=${location!.id}`} className="mt-4 w-full">Commander</ButtonLink>
                  </>
                )}
              </div>
            </aside>
          )}
        </div>
      )}

      {ordering && count > 0 && catalog && location && (
        <div className="fixed inset-x-0 bottom-0 z-30 p-3 lg:hidden">
          <Link href={`/commander/?etablissement=${location.id}`}
            className="mx-auto flex max-w-xl items-center gap-3.5 rounded-[20px] bg-primary px-5 py-3.5 text-white shadow-[0_16px_30px_color-mix(in_srgb,var(--primary)_35%,transparent)]">
            <span className="grid size-9 place-items-center rounded-[10px] bg-white/20 font-bold">{count}</span>
            <span className="min-w-0">
              <b className="block text-[17px]">Voir le panier</b>
              <small className="block truncate text-xs opacity-90">{modeLabel[mode]}{ready ? ` · ${mode === 'Delivery' ? 'livré' : 'prêt'} vers ${ready}` : ''}</small>
            </span>
            <span className="ml-auto text-lg font-bold">{money(estimate, catalog.currencyCode)}</span>
          </Link>
        </div>
      )}

      {sheet && catalog && (
        <ProductSheet product={sheet} catalog={catalog} index={productIndex(catalog, sheet.id)} onClose={closeSheet}
          onAdd={line => {
            add(line);
            closeSheet();
          }} />
      )}
    </div>
  );
}

function Stepper({quantity, onChange, label, className = 'mt-2'}: {quantity: number; onChange: (value: number) => void; label: string; className?: string}) {
  return (
    <span className={cx(className, 'inline-flex items-center gap-2 rounded-xl border border-line bg-white p-1 font-bold')}>
      <button type="button" aria-label={quantity === 1 ? `Retirer : ${label}` : `Une unité de moins : ${label}`} onClick={() => onChange(quantity - 1)} className="grid size-8 place-items-center rounded-lg bg-sand hover:brightness-95">
        {quantity === 1 ? <Trash2 className="size-4" aria-hidden="true" /> : <Minus className="size-4" aria-hidden="true" />}
      </button>
      <span className="w-6 text-center text-sm" aria-live="polite">{quantity}</span>
      <button type="button" aria-label={`Une unité de plus : ${label}`} onClick={() => onChange(quantity + 1)} disabled={quantity >= 99} className="grid size-8 place-items-center rounded-lg bg-primary text-white hover:brightness-110 disabled:opacity-50">
        <Plus className="size-4" aria-hidden="true" />
      </button>
    </span>
  );
}

function MenuSections({catalog, ordering, cart, onOpen, onQuickAdd, quantityOf, onQuantity}: {
  catalog: Catalog;
  ordering: boolean;
  cart: CartLine[];
  onOpen: (product: CatalogProduct) => void;
  onQuickAdd: (product: CatalogProduct) => void;
  quantityOf: (product: CatalogProduct) => CartLine | undefined;
  onQuantity: (key: string, quantity: number) => void;
}) {
  const sections = useMemo(() => sectionsOf(catalog), [catalog]);
  const [active, setActive] = useState<string | null>(null);
  const nav = useRef<HTMLDivElement>(null);

  // La catégorie à l'écran est mise en avant dans la barre.
  useEffect(() => {
    const observer = new IntersectionObserver(entries => {
      const visible = entries.filter(entry => entry.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
      if (visible) setActive(visible.target.id);
    }, {rootMargin: '-160px 0px -60% 0px'});
    sections.forEach(section => {
      const element = document.getElementById(section.key);
      if (element) observer.observe(element);
    });
    return () => observer.disconnect();
  }, [sections]);

  useEffect(() => {
    const tab = active ? nav.current?.querySelector<HTMLElement>(`[data-section="${active}"]`) : null;
    if (tab && nav.current) nav.current.scrollTo({left: tab.offsetLeft - nav.current.clientWidth / 2 + tab.clientWidth / 2, behavior: 'smooth'});
  }, [active]);

  if (sections.length === 0) return <div className="px-4 py-8 sm:px-0"><Notice>La carte n’est pas encore publiée.</Notice></div>;
  const inCart = (product: CatalogProduct) => cart.filter(line => line.productId === product.id).reduce((sum, line) => sum + line.quantity, 0);

  return (
    <div className="min-w-0">
      <nav aria-label="Catégories" className="sticky top-[var(--top,0px)] z-20 border-b border-line bg-cream/95 backdrop-blur">
        <div ref={nav} className="no-scrollbar flex gap-2 overflow-x-auto px-4 py-3 sm:px-0">
          {sections.map(section => {
            const current = (active ?? sections[0].key) === section.key;
            return (
              <a key={section.key} href={`#${section.key}`} data-section={section.key} aria-current={current ? 'true' : undefined}
                className={cx('shrink-0 rounded-full border px-4 py-2 text-sm font-medium transition', current ? 'border-ink bg-ink text-white' : 'border-line bg-white hover:border-ink/40')}>
                {section.name}
              </a>
            );
          })}
        </div>
      </nav>
      <div className="space-y-8 px-4 py-6 sm:px-0">
        {sections.map(section => (
          <section key={section.key} id={section.key} className="scroll-mt-[calc(var(--top,0px)+72px)]">
            <h2 className="mb-2 font-serif text-2xl font-bold">{section.name}</h2>
            <ul className="grid gap-x-8 md:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">
              {section.products.map(product => {
                const index = productIndex(catalog, product.id);
                const from = product.variants.length > 0 ? Math.min(...product.variants.map(variant => variant.price)) : product.price;
                const price = priceParts(from, catalog.currencyCode);
                const simpleLine = isSimple(product) ? quantityOf(product) : undefined;
                const count = inCart(product);
                return (
                  <li key={product.id} id={`produit-${product.id}`} className={cx('flex items-center gap-4 border-b border-line py-3', !product.isAvailable && 'opacity-55')}>
                    <span className="grid size-[84px] shrink-0 place-items-center overflow-hidden rounded-[18px]" style={{background: pastel[index % 4]}}>
                      <Plate src={product.thumbnails?.[0] ?? product.images[0]} index={index} size={74} muted={!product.isAvailable} />
                    </span>
                    <button type="button" onClick={() => onOpen(product)} className="min-w-0 flex-1 rounded-lg text-left" aria-label={`${product.name}, détails${product.isAvailable ? '' : ', épuisé'}`}>
                      <span className="block font-semibold leading-snug">{product.name}{count > 0 && !simpleLine && <span className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-semibold text-primary">× {count}</span>}</span>
                      <span className="mt-0.5 line-clamp-2 block text-[13px] leading-snug text-muted">{product.isAvailable ? product.description : 'Épuisé pour aujourd’hui'}</span>
                      <span className="mt-1.5 block text-[15px] font-bold">
                        {product.variants.length > 0 && <span className="text-xs font-medium text-muted">dès </span>}{price.amount} <span className="text-xs font-medium text-muted">{price.unit}</span>
                        {(product.formulaSteps?.length ?? 0) > 0 && <span className="ml-2 rounded-full bg-accent/30 px-2 py-0.5 text-xs font-semibold">Formule</span>}
                      </span>
                    </button>
                    {!product.isAvailable ? (
                      <span className="shrink-0 rounded-full border border-line bg-white px-2.5 py-1 text-xs font-medium">Épuisé</span>
                    ) : !ordering ? null : simpleLine ? (
                      <Stepper quantity={simpleLine.quantity} onChange={value => onQuantity(simpleLine.key, value)} label={product.name} className="shrink-0" />
                    ) : (
                      <button type="button" aria-label={`Ajouter ${product.name}`} onClick={() => (isSimple(product) ? onQuickAdd(product) : onOpen(product))}
                        className="grid size-10 shrink-0 place-items-center rounded-[11px] bg-primary text-white hover:brightness-110">
                        <Plus className="size-5" aria-hidden="true" />
                      </button>
                    )}
                  </li>
                );
              })}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}
