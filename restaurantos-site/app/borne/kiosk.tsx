'use client';
import {useCallback, useEffect, useMemo, useRef, useState} from 'react';
import QRCode from 'qrcode';
import {CheckCircle2, Minus, Plus, Printer, RotateCcw, ShoppingBag, Smartphone, Store, Trash2, UtensilsCrossed, X} from 'lucide-react';
import {
  accentOf,
  errorMessage,
  linePrice,
  loadCatalog,
  modeLabel,
  money,
  newIdempotencyKey,
  setKioskCredentials,
  storefront,
  StorefrontError,
  toPayload,
  componentLabel,
  type CartLine,
  type Catalog,
  type CatalogProduct,
  type GuestOrder,
  type PlacedOrder,
  type Quote,
  type ServiceMode,
  type Storefront,
  type StorefrontLocation,
} from '@/lib/storefront';
import {AccentButton, cx, LanguageSwitch, Notice, Spinner} from '@/components/order-ui';
import {ProductSheet} from '../commander/[locationId]/ordering';
import {tr, useLang} from '@/lib/i18n';
import {safeCheckoutUrl} from '@/lib/checkout';

// La borne : une tablette en plein écran, appairée à UN établissement. La clé de l'application borne arrive dans le
// fragment de l'adresse (#cle=…, jamais envoyé au serveur du site) ; le jeton d'appareil est remis à l'appairage. Les
// deux restent dans le navigateur de la tablette. Aucune donnée personnelle n'est demandée au client.

const KEY_STORAGE = 'restaurantos.kiosk.key';
const DEVICE_STORAGE = 'restaurantos.kiosk.device';
const IDLE_SECONDS = 75;
const IDLE_COUNTDOWN = 15;
const DONE_SECONDS = 20;

const read = (name: string) => {
  try {
    return window.localStorage.getItem(name);
  } catch {
    return null;
  }
};

const write = (name: string, value: string | null) => {
  try {
    if (value === null) window.localStorage.removeItem(name);
    else window.localStorage.setItem(name, value);
  } catch {}
};

interface Loaded {
  front: Storefront;
  location: StorefrontLocation;
  catalog: Catalog;
}

type Screen =
  | {kind: 'welcome'}
  | {kind: 'menu'; mode: ServiceMode}
  | {kind: 'paying'; placed: PlacedOrder; checkoutUrl: string}
  | {kind: 'done'; placed: PlacedOrder; paid: boolean};

export function Kiosk() {
  useLang();
  const [key, setKey] = useState<string | null | undefined>(undefined);
  const [device, setDevice] = useState<string | null>(null);
  const [data, setData] = useState<Loaded | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const refresh = useCallback(() => setRevision(value => value + 1), []);

  useEffect(() => {
    const load = () => {
      const fromHash = new URLSearchParams(window.location.hash.slice(1)).get('cle');
      const fresh = fromHash?.startsWith('pk_') ? fromHash : null;
      if (fresh) {
        if (fresh !== read(KEY_STORAGE)) write(DEVICE_STORAGE, null);
        write(KEY_STORAGE, fresh);
        window.history.replaceState(null, '', window.location.pathname);
      }

      const storedKey = fresh ?? read(KEY_STORAGE);
      const storedDevice = read(DEVICE_STORAGE);
      setKioskCredentials(storedKey ?? '', storedDevice);
      setDevice(storedDevice);
      setKey(storedKey);
    };
    load();
    window.addEventListener('hashchange', load);
    return () => window.removeEventListener('hashchange', load);
  }, []);

  const forget = useCallback(() => {
    write(DEVICE_STORAGE, null);
    setKioskCredentials(read(KEY_STORAGE) ?? '', null);
    setDevice(null);
    setData(null);
  }, []);

  useEffect(() => {
    if (!key || !device) return;
    const controller = new AbortController();
    setError(null);
    storefront<Storefront>('/', {signal: controller.signal})
      .then(async front => {
        const location = front?.locations[0];
        if (!front || !location) {
          setError(tr('Cette borne ne sert aucun établissement : vérifiez son application dans la console.'));
          return;
        }

        const catalog = await loadCatalog(location.id, controller.signal);
        setData({front, location, catalog});
      })
      .catch(reason => {
        if (controller.signal.aborted) return;
        if (reason instanceof StorefrontError && (reason.status === 401 || reason.code === 'storefront.kiosk.device_required')) forget();
        else setError(errorMessage(reason));
      });
    return () => controller.abort();
  }, [key, device, revision, forget]);

  if (key === undefined) return <Frame><Spinner /></Frame>;
  if (!key) {
    return (
      <Frame>
        <div className="max-w-xl space-y-4 text-center">
          <h1 className="text-3xl font-bold">{tr('Borne non configurée')}</h1>
          <p className="text-lg text-slate-300">
            {tr('Ouvrez sur cette tablette l’adresse de la borne affichée dans la console du restaurant (Paramètres, Vente en ligne).')}
          </p>
        </div>
      </Frame>
    );
  }

  if (!device) {
    return (
      <Pairing onPaired={token => {
        write(DEVICE_STORAGE, token);
        setKioskCredentials(key, token);
        setDevice(token);
      }} onKeyRefused={() => {
        write(KEY_STORAGE, null);
        setKey(null);
      }} />
    );
  }

  if (error) {
    return (
      <Frame>
        <div className="max-w-xl space-y-6 text-center">
          <p className="text-xl">{error}</p>
          <button type="button" onClick={refresh} className="rounded-2xl bg-white px-8 py-4 text-lg font-semibold text-slate-900">{tr('Réessayer')}</button>
        </div>
      </Frame>
    );
  }

  if (!data) return <Frame><Spinner label={tr('Ouverture de la borne…')} /></Frame>;
  return <Ordering data={data} onRefresh={refresh} onRevoked={forget} />;
}

function Frame({children}: {children: React.ReactNode}) {
  return <div className="grid min-h-screen place-items-center bg-slate-900 p-8 text-white">{children}</div>;
}

/** Écran d'appairage : un code à 6 chiffres, renouvelé à expiration ; la borne interroge jusqu'à sa saisie dans la console. */
function Pairing({onPaired, onKeyRefused}: {onPaired: (token: string) => void; onKeyRefused: () => void}) {
  const [code, setCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let stopped = false;
    let timer: number | undefined;
    const controller = new AbortController();
    setCode(null);
    setError(null);
    storefront<{pairingId: string; code: string; secret: string; expiresAtUtc: string}>('/kiosk/pairings', {method: 'POST', signal: controller.signal})
      .then(started => {
        if (!started || stopped) return;
        setCode(started.code);
        const poll = async () => {
          try {
            const state = await storefront<{state: 'Pending' | 'Paired' | 'Expired'; deviceToken: string | null}>(
              `/kiosk/pairings/${started.pairingId}/claim`, {method: 'POST', body: {secret: started.secret}, signal: controller.signal});
            if (stopped || !state) return;
            if (state.state === 'Paired' && state.deviceToken) {
              onPaired(state.deviceToken);
              return;
            }

            if (state.state === 'Expired' || Date.now() >= new Date(started.expiresAtUtc).getTime()) {
              setAttempt(value => value + 1);
              return;
            }
          } catch (reason) {
            if (stopped) return;
            if (reason instanceof StorefrontError && reason.code === 'storefront.kiosk.pairing_invalid') {
              setAttempt(value => value + 1);
              return;
            }
          }

          timer = window.setTimeout(poll, 3000);
        };
        timer = window.setTimeout(poll, 3000);
      })
      .catch(reason => {
        if (stopped) return;
        if (reason instanceof StorefrontError && reason.status === 401) onKeyRefused();
        else {
          setError(errorMessage(reason));
          timer = window.setTimeout(() => setAttempt(value => value + 1), 10000);
        }
      });
    return () => {
      stopped = true;
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [attempt, onPaired, onKeyRefused]);

  return (
    <Frame>
      <div className="max-w-2xl space-y-8 text-center">
        <h1 className="text-3xl font-bold">{tr('Appairer cette borne')}</h1>
        <p className="text-lg text-slate-300">
          {tr('Dans la console du restaurant, ouvrez Paramètres, Vente en ligne, Bornes, puis « Appairer une borne » et saisissez ce code.')}
        </p>
        {code ? (
          <p className="font-mono text-7xl font-bold tracking-[0.25em] sm:text-8xl" aria-label={tr('Code {p0}', {p0: code.split('').join(' ')})}>
            {code.slice(0, 3)} {code.slice(3)}
          </p>
        ) : error ? (
          <p className="text-xl text-red-300">{error}</p>
        ) : (
          <Spinner label={tr('Demande d’un code…')} />
        )}
        <p className="text-sm text-slate-400">{tr('Le code est valable 10 minutes ; un nouveau code s’affiche ensuite.')}</p>
      </div>
    </Frame>
  );
}

function Ordering({data, onRefresh, onRevoked}: {data: Loaded; onRefresh: () => void; onRevoked: () => void}) {
  const {front, location, catalog} = data;
  const [screen, setScreen] = useState<Screen>({kind: 'welcome'});
  const [cart, setCart] = useState<CartLine[]>([]);
  const accent = accentOf(front.restaurant.primaryColor);

  const restart = useCallback(() => {
    setCart([]);
    setScreen({kind: 'welcome'});
    onRefresh();
  }, [onRefresh]);

  const fail = useCallback((reason: unknown) => {
    if (reason instanceof StorefrontError && (reason.status === 401 || reason.code === 'storefront.kiosk.device_required')) onRevoked();
  }, [onRevoked]);

  return (
    <div style={{'--accent': accent} as React.CSSProperties} className="min-h-screen select-none bg-slate-50">
      {screen.kind === 'welcome' && (
        <Welcome front={front} location={location} onStart={mode => {
          document.documentElement.requestFullscreen?.().catch(() => {});
          setScreen({kind: 'menu', mode});
        }} />
      )}
      {screen.kind === 'menu' && (
        <MenuScreen
          front={front}
          location={location}
          catalog={catalog}
          mode={screen.mode}
          cart={cart}
          onCart={setCart}
          onMode={mode => setScreen({kind: 'menu', mode})}
          onCancel={restart}
          onFailure={fail}
          onPlaced={(placed, checkoutUrl) => {
            setCart([]);
            setScreen(checkoutUrl ? {kind: 'paying', placed, checkoutUrl} : {kind: 'done', placed, paid: false});
          }} />
      )}
      {screen.kind === 'paying' && (
        <Paying placed={screen.placed} checkoutUrl={screen.checkoutUrl} currency={catalog.currencyCode}
          onPaid={() => setScreen({kind: 'done', placed: screen.placed, paid: true})}
          onCancel={restart} />
      )}
      {screen.kind === 'done' && <Done placed={screen.placed} paid={screen.paid} onNext={restart} />}
      <WakeLock />
    </div>
  );
}

/** Garde l'écran allumé tant que la borne est ouverte (si le navigateur le permet). */
function WakeLock() {
  useEffect(() => {
    let sentinel: {release: () => Promise<void>} | null = null;
    const request = () => {
      const wakeLock = (navigator as Navigator & {wakeLock?: {request: (type: 'screen') => Promise<{release: () => Promise<void>}>}}).wakeLock;
      wakeLock?.request('screen').then(lock => (sentinel = lock)).catch(() => {});
    };
    request();
    const onVisible = () => document.visibilityState === 'visible' && request();
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      document.removeEventListener('visibilitychange', onVisible);
      sentinel?.release().catch(() => {});
    };
  }, []);
  return null;
}

function Logo({front, className}: {front: Storefront; className: string}) {
  const logo = front.restaurant.logoUrl?.startsWith('https://') ? front.restaurant.logoUrl : null;
  return logo ? (
    // eslint-disable-next-line @next/next/no-img-element
    <img src={logo} alt="" className={cx('rounded-3xl bg-white object-cover', className)} />
  ) : (
    <span className={cx('grid place-items-center rounded-3xl bg-white/15', className)}><UtensilsCrossed className="size-1/2" aria-hidden="true" /></span>
  );
}

const modeIcon: Record<ServiceMode, typeof Store> = {DineIn: UtensilsCrossed, Takeaway: ShoppingBag, Delivery: ShoppingBag};

function Welcome({front, location, onStart}: {front: Storefront; location: StorefrontLocation; onStart: (mode: ServiceMode) => void}) {
  const modes = location.serviceModes.filter(mode => mode !== 'Delivery');
  const open = location.acceptsOrdersNow && modes.length > 0;
  return (
    <div className="relative flex min-h-screen flex-col items-center justify-center gap-10 bg-[var(--accent)] p-8 text-center text-white">
      <LanguageSwitch onAccent className="absolute top-6 right-6 text-base [&_button]:px-5 [&_button]:py-2" />
      <Logo front={front} className="size-32" />
      <div className="space-y-2">
        <h1 className="text-5xl font-bold">{front.restaurant.name}</h1>
        <p className="text-xl text-white/85">{location.name}</p>
      </div>
      {open ? (
        <div className="w-full max-w-3xl space-y-5">
          <p className="text-2xl font-semibold">{tr('Commandez ici')}</p>
          <div className={cx('grid gap-5', modes.length > 1 && 'sm:grid-cols-2')}>
            {modes.map(mode => {
              const Icon = modeIcon[mode];
              return (
                <button key={mode} type="button" onClick={() => onStart(mode)}
                  className="flex flex-col items-center gap-4 rounded-3xl bg-white px-8 py-10 text-3xl font-bold text-slate-900 shadow-xl transition active:scale-[0.98]">
                  <Icon className="size-14 text-[var(--accent)]" aria-hidden="true" />{modeLabel[mode]}
                </button>
              );
            })}
          </div>
        </div>
      ) : (
        <p className="max-w-xl rounded-3xl bg-white/15 px-8 py-6 text-2xl font-semibold">
          {location.isPaused ? tr('Les commandes à la borne sont en pause. Adressez-vous au comptoir.') : tr('L’établissement ne prend pas de commandes pour le moment.')}
        </p>
      )}
    </div>
  );
}

/** Ramène la borne à l'accueil après une inactivité, avec un compte à rebours que le client peut interrompre. */
function useIdle(active: boolean, onTimeout: () => void) {
  const [countdown, setCountdown] = useState<number | null>(null);
  const last = useRef(0);

  useEffect(() => {
    if (!active) return;
    last.current = Date.now();
    const touch = () => {
      last.current = Date.now();
      setCountdown(null);
    };
    const events = ['pointerdown', 'keydown', 'wheel', 'touchstart'] as const;
    events.forEach(name => window.addEventListener(name, touch, {passive: true}));
    const timer = window.setInterval(() => {
      const idle = (Date.now() - last.current) / 1000;
      if (idle >= IDLE_SECONDS + IDLE_COUNTDOWN) onTimeout();
      else if (idle >= IDLE_SECONDS) setCountdown(Math.ceil(IDLE_SECONDS + IDLE_COUNTDOWN - idle));
    }, 1000);
    return () => {
      events.forEach(name => window.removeEventListener(name, touch));
      window.clearInterval(timer);
    };
  }, [active, onTimeout]);

  return countdown;
}

function MenuScreen({front, location, catalog, mode, cart, onCart, onMode, onCancel, onFailure, onPlaced}: {
  front: Storefront;
  location: StorefrontLocation;
  catalog: Catalog;
  mode: ServiceMode;
  cart: CartLine[];
  onCart: (lines: CartLine[]) => void;
  onMode: (mode: ServiceMode) => void;
  onCancel: () => void;
  onFailure: (reason: unknown) => void;
  onPlaced: (placed: PlacedOrder, checkoutUrl: string | null) => void;
}) {
  const [product, setProduct] = useState<CatalogProduct | null>(null);
  const [review, setReview] = useState(false);
  const closeReview = useCallback(() => setReview(false), []);
  const products = useMemo(() => new Map(catalog.products.map(item => [item.id, item])), [catalog]);
  const sections = catalog.menus.flatMap(menu => menu.sections.map(section => ({...section, key: `${menu.id}-${section.id}`})))
    .filter(section => section.productIds.some(id => products.has(id)));
  const [current, setCurrent] = useState(sections[0]?.key ?? '');
  const section = sections.find(item => item.key === current) ?? sections[0];
  const countdown = useIdle(true, onCancel);
  const count = cart.reduce((sum, line) => sum + line.quantity, 0);
  const estimate = cart.reduce((sum, line) => sum + (linePrice(line, catalog) ?? 0), 0);
  const modes = location.serviceModes.filter(item => item !== 'Delivery');

  return (
    <div className="flex min-h-screen flex-col pb-28">
      <header className="flex items-center gap-4 bg-[var(--accent)] px-6 py-4 text-white">
        <Logo front={front} className="size-12" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-xl font-bold">{front.restaurant.name}</p>
          {modes.length > 1 ? (
            <div className="mt-1 flex gap-2" role="group" aria-label={tr('Mode')}>
              {modes.map(item => (
                <button key={item} type="button" aria-pressed={mode === item} onClick={() => onMode(item)}
                  className={cx('rounded-full px-4 py-1 text-sm font-semibold', mode === item ? 'bg-white text-slate-900' : 'bg-white/15 text-white')}>
                  {modeLabel[item]}
                </button>
              ))}
            </div>
          ) : (
            <p className="text-sm text-white/85">{modeLabel[mode]}</p>
          )}
        </div>
        <LanguageSwitch onAccent className="text-sm [&_button]:px-4 [&_button]:py-2" />
        <button type="button" onClick={onCancel} className="flex items-center gap-2 rounded-2xl bg-white/15 px-5 py-3 text-base font-semibold">
          <RotateCcw className="size-5" aria-hidden="true" />{tr('Recommencer')}
        </button>
      </header>

      <nav aria-label={tr('Catégories')} className="sticky top-0 z-30 border-b border-slate-200 bg-white">
        <div className="flex gap-3 overflow-x-auto px-6 py-4">
          {sections.map(item => (
            <button key={item.key} type="button" aria-pressed={item.key === section?.key} onClick={() => setCurrent(item.key)}
              className={cx('shrink-0 rounded-2xl px-6 py-3 text-lg font-semibold', item.key === section?.key ? 'bg-[var(--accent)] text-white' : 'bg-slate-100 text-slate-700')}>
              {item.name}
            </button>
          ))}
        </div>
      </nav>

      {!section ? (
        <div className="p-8"><Notice>{tr('Aucun menu n’est publié pour le moment.')}</Notice></div>
      ) : (
        <ul className="grid gap-4 p-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {section.productIds.map(id => products.get(id)).filter((item): item is CatalogProduct => !!item).map(item => {
            const from = item.variants.length > 0 ? Math.min(...item.variants.map(variant => variant.price)) : item.price;
            return (
              <li key={item.id}>
                <button type="button" disabled={!item.isAvailable} onClick={() => setProduct(item)}
                  className="flex h-full w-full flex-col overflow-hidden rounded-3xl border border-slate-200 bg-white text-left shadow-sm transition active:scale-[0.98] disabled:opacity-50">
                  {item.images[0] ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={item.thumbnails?.[0] ?? item.images[0]} alt="" loading="lazy" className="aspect-[4/3] w-full object-cover" />
                  ) : (
                    <span className="grid aspect-[4/3] w-full place-items-center bg-slate-100 text-slate-300"><UtensilsCrossed className="size-12" aria-hidden="true" /></span>
                  )}
                  <span className="flex flex-1 flex-col gap-1 p-4">
                    <span className="text-lg font-bold leading-tight">{item.name}</span>
                    {item.description && <span className="line-clamp-2 text-sm text-slate-500">{item.description}</span>}
                    <span className="mt-auto pt-2 text-lg font-bold">{item.variants.length > 0 ? tr('Dès ') : ''}{money(from, catalog.currencyCode)}{(item.formulaSteps?.length ?? 0) > 0 && <span className="ml-2 text-sm font-semibold text-[var(--accent)]">{tr('Formule')}</span>}</span>
                    {!item.isAvailable && <span className="text-sm font-semibold text-red-700">{tr('Épuisé')}</span>}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <div className="fixed inset-x-0 bottom-0 z-40 border-t border-slate-200 bg-white px-6 py-4">
        <AccentButton className="flex w-full justify-between rounded-2xl py-5 text-xl" disabled={count === 0} onClick={() => setReview(true)}>
          <span className="flex items-center gap-3"><ShoppingBag className="size-6" aria-hidden="true" />{count === 0 ? tr('Choisissez vos produits') : tr('Ma commande ({p0})', {p0: count})}</span>
          {count > 0 && <span>{money(estimate, catalog.currencyCode)}</span>}
        </AccentButton>
      </div>

      {product && (
        <ProductSheet product={product} catalog={catalog} onClose={() => setProduct(null)} onAdd={line => {
          onCart([...cart, line]);
          setProduct(null);
        }} />
      )}

      {review && (
        <Review location={location} catalog={catalog} mode={mode} cart={cart} onCart={onCart} onClose={closeReview}
          onFailure={onFailure} onPlaced={onPlaced} />
      )}

      {countdown !== null && (
        <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-6" role="alertdialog" aria-label={tr('Toujours là ?')}>
          <div className="w-full max-w-md space-y-6 rounded-3xl bg-white p-8 text-center">
            <p className="text-3xl font-bold">{tr('Toujours là ?')}</p>
            <p className="text-lg text-slate-600">{tr('La commande sera effacée dans')} {countdown} {tr('s.')}</p>
            <AccentButton className="w-full rounded-2xl py-4 text-lg">{tr('Je continue')}</AccentButton>
          </div>
        </div>
      )}
    </div>
  );
}

interface Payment {
  mode: 'Counter' | 'Online';
  provider: string | null;
  method: string | null;
}

/** La commande avant validation : devis du serveur, puis paiement au comptoir ou par QR code sur le téléphone du client. */
function Review({location, catalog, mode, cart, onCart, onClose, onFailure, onPlaced}: {
  location: StorefrontLocation;
  catalog: Catalog;
  mode: ServiceMode;
  cart: CartLine[];
  onCart: (lines: CartLine[]) => void;
  onClose: () => void;
  onFailure: (reason: unknown) => void;
  onPlaced: (placed: PlacedOrder, checkoutUrl: string | null) => void;
}) {
  const [quote, setQuote] = useState<Quote | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const idempotency = useRef(newIdempotencyKey());
  const payload = useMemo(() => cart.map(toPayload), [cart]);

  useEffect(() => {
    idempotency.current = newIdempotencyKey();
    if (cart.length === 0) {
      onClose();
      return;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      storefront<Quote>('/cart/quote', {method: 'POST', body: {locationId: location.id, mode, lines: payload}, signal: controller.signal})
        .then(result => {
          setQuote(result);
          setError(null);
        })
        .catch(reason => {
          if (controller.signal.aborted) return;
          onFailure(reason);
          setError(errorMessage(reason));
        });
    }, 200);
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [payload, mode, location.id, cart.length, onClose, onFailure]);

  const setQuantity = (lineKey: string, quantity: number) =>
    onCart(quantity <= 0 ? cart.filter(line => line.key !== lineKey) : cart.map(line => (line.key === lineKey ? {...line, quantity: Math.min(99, quantity)} : line)));

  const place = async (payment: Payment) => {
    if (!quote) return;
    setBusy(payment.mode === 'Counter' ? 'counter' : `${payment.provider}`);
    setError(null);
    try {
      const placed = await storefront<PlacedOrder>('/orders', {
        method: 'POST',
        idempotencyKey: idempotency.current,
        body: {locationId: location.id, mode, lines: payload, payment: payment.mode, customer: null, scheduledForUtc: null, expectedTotal: quote.total},
      });
      if (!placed) return;
      if (payment.mode === 'Counter') {
        onPlaced(placed, null);
        return;
      }

      const token = encodeURIComponent(placed.trackingToken);
      const started = await storefront<{paymentId: string; checkoutUrl: string | null}>(`/orders/${token}/payments`, {
        method: 'POST',
        idempotencyKey: newIdempotencyKey(),
        body: {provider: payment.provider, method: payment.method, payerPhone: null, returnUrl: null},
      }).catch(() => null);
      const checkout = safeCheckoutUrl(started?.checkoutUrl);
      if (checkout) {
        onPlaced(placed, checkout);
        return;
      }

      await storefront(`/orders/${token}/cancel`, {method: 'POST'}).catch(() => {});
      idempotency.current = newIdempotencyKey();
      setError(tr('Le paiement par téléphone n’est pas disponible pour le moment : payez au comptoir.'));
    } catch (reason) {
      onFailure(reason);
      setError(errorMessage(reason));
      if (reason instanceof StorefrontError && reason.status >= 400 && reason.status < 500) idempotency.current = newIdempotencyKey();
      if (reason instanceof StorefrontError && reason.code === 'storefront.checkout.price_changed') setQuote(null);
    } finally {
      setBusy(null);
    }
  };

  const currency = quote?.currencyCode ?? catalog.currencyCode;
  const online = quote?.canPayOnline ? quote.onlinePaymentOptions.filter(option => option.methods.length > 0) : [];
  const ready = !!quote && quote.meetsMinimum && !busy;

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/50" onMouseDown={event => event.target === event.currentTarget && !busy && onClose()}>
      <div role="dialog" aria-modal="true" aria-label={tr('Ma commande')} className="flex h-full w-full max-w-2xl flex-col bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b border-slate-100 px-6 py-5">
          <h2 className="text-2xl font-bold">{tr('Ma commande ·')} {modeLabel[mode]}</h2>
          <button type="button" onClick={onClose} disabled={!!busy} aria-label={tr('Fermer')} className="grid size-12 place-items-center rounded-full bg-slate-100"><X className="size-6" /></button>
        </div>
        <ul className="flex-1 divide-y divide-slate-100 overflow-y-auto px-6">
          {cart.map((line, index) => {
            const quoted = quote?.lines[index];
            const product = catalog.products.find(item => item.id === line.productId);
            return (
              <li key={line.key} className="flex items-center gap-4 py-4">
                <div className="min-w-0 flex-1">
                  <p className="text-lg font-semibold">{quoted?.name ?? product?.name}{quoted?.variantName ? ` · ${quoted.variantName}` : ''}</p>
                  {quoted?.components?.map((component, position) => <p key={position} className="text-slate-600">{component.stepName} : {componentLabel(component)}</p>)}
                  {quoted && quoted.modifiers.length > 0 && <p className="text-slate-500">{quoted.modifiers.map(modifier => modifier.name).join(', ')}</p>}
                  {line.note.trim() && <p className="italic text-slate-500">« {line.note.trim()} »</p>}
                </div>
                <div className="flex items-center gap-2">
                  <button type="button" aria-label={tr('Retirer un')} onClick={() => setQuantity(line.key, line.quantity - 1)} className="grid size-12 place-items-center rounded-xl border border-slate-200">
                    {line.quantity === 1 ? <Trash2 className="size-5" /> : <Minus className="size-5" />}
                  </button>
                  <span className="w-8 text-center text-lg font-bold">{line.quantity}</span>
                  <button type="button" aria-label={tr('Ajouter un')} onClick={() => setQuantity(line.key, line.quantity + 1)} className="grid size-12 place-items-center rounded-xl border border-slate-200"><Plus className="size-5" /></button>
                </div>
                <span className="w-28 text-right text-lg font-semibold">{quoted ? money(quoted.lineTotal, currency) : '…'}</span>
              </li>
            );
          })}
        </ul>
        <div className="space-y-4 border-t border-slate-100 px-6 py-5">
          {error && <Notice tone="error">{error}</Notice>}
          {quote && !quote.meetsMinimum && quote.minimumOrderAmount !== null && (
            <Notice tone="warning">{tr('Minimum de commande :')} {money(quote.minimumOrderAmount, currency)}.</Notice>
          )}
          <div className="flex items-baseline justify-between text-2xl font-bold">
            <span>{tr('Total')}</span><span>{quote ? money(quote.total, currency) : '…'}</span>
          </div>
          <div className={cx('grid gap-3', quote?.canPayAtCounter && online.length > 0 && 'sm:grid-cols-2')}>
            {quote?.canPayAtCounter && (
              <AccentButton className="flex-col rounded-2xl py-5 text-lg" disabled={!ready} busy={busy === 'counter'} onClick={() => void place({mode: 'Counter', provider: null, method: null})}>
                <Store className="size-7" aria-hidden="true" />{tr('Payer au comptoir')}
              </AccentButton>
            )}
            {online.map(option => (
              <AccentButton key={option.provider} className="flex-col rounded-2xl py-5 text-lg" disabled={!ready} busy={busy === option.provider}
                onClick={() => void place({mode: 'Online', provider: option.provider, method: option.methods[0]})}>
                <Smartphone className="size-7" aria-hidden="true" />{tr('Payer avec mon téléphone')}
                <span className="text-sm font-normal opacity-90">{option.displayName}</span>
              </AccentButton>
            ))}
          </div>
          {quote && !quote.canPayAtCounter && online.length === 0 && (
            <Notice tone="warning">{tr('Aucun moyen de paiement n’est proposé à cette borne : adressez-vous au comptoir.')}</Notice>
          )}
          {quote && !quote.canPayAtCounter && location.payment.payAtCounter && location.payment.counterLimit !== null && online.length > 0 && (
            <p className="text-center text-sm text-slate-500">{tr('Au-delà de')} {money(location.payment.counterLimit, currency)}{tr(', le paiement se fait par téléphone.')}</p>
          )}
        </div>
      </div>
    </div>
  );
}

/** Paiement par téléphone : le client scanne le QR code ; la borne suit la commande jusqu'au paiement ou à l'abandon. */
function Paying({placed, checkoutUrl, currency, onPaid, onCancel}: {
  placed: PlacedOrder;
  checkoutUrl: string;
  currency: string;
  onPaid: () => void;
  onCancel: () => void;
}) {
  const [image, setImage] = useState<string | null>(null);
  const [left, setLeft] = useState<number | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const token = encodeURIComponent(placed.trackingToken);

  useEffect(() => {
    QRCode.toDataURL(checkoutUrl, {margin: 1, width: 360, errorCorrectionLevel: 'M'}).then(setImage).catch(() => setImage(null));
  }, [checkoutUrl]);

  const cancel = useCallback(async () => {
    setCancelling(true);
    await storefront(`/orders/${token}/cancel`, {method: 'POST'}).catch(() => {});
    onCancel();
  }, [token, onCancel]);

  useEffect(() => {
    let stopped = false;
    let timer: number | undefined;
    const poll = async () => {
      try {
        const order = await storefront<GuestOrder>(`/orders/${token}`);
        if (stopped || !order) return;
        if (order.paymentState === 'Paid') {
          onPaid();
          return;
        }

        if (order.status === 'Cancelled') {
          onCancel();
          return;
        }
      } catch {}
      if (!stopped) timer = window.setTimeout(poll, 3000);
    };
    timer = window.setTimeout(poll, 3000);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
    };
  }, [token, onPaid, onCancel]);

  useEffect(() => {
    if (!placed.payBeforeUtc) return;
    const deadline = new Date(placed.payBeforeUtc).getTime();
    const timer = window.setInterval(() => {
      const seconds = Math.max(0, Math.round((deadline - Date.now()) / 1000));
      setLeft(seconds);
      if (seconds === 0) void cancel();
    }, 1000);
    return () => window.clearInterval(timer);
  }, [placed.payBeforeUtc, cancel]);

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-8 p-8 text-center">
      <div className="space-y-2">
        <p className="text-lg font-semibold text-slate-500">{tr('Commande n°')} {placed.number}</p>
        <h1 className="text-4xl font-bold">{tr('Scannez pour payer')} {money(placed.total, currency)}</h1>
        <p className="text-lg text-slate-600">{tr('Avec l’appareil photo de votre téléphone, puis suivez les instructions.')}</p>
      </div>
      <div className="grid size-[380px] place-items-center rounded-3xl bg-white p-4 shadow-xl">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        {image ? <img src={image} alt={tr('QR code de paiement')} className="size-full" /> : <Spinner label={tr('Préparation du QR code…')} />}
      </div>
      <p className="flex items-center gap-3 text-lg text-slate-600">
        <span className="size-3 animate-pulse rounded-full bg-[var(--accent)]" aria-hidden="true" />
        {tr('En attente du paiement')}{left !== null ? tr(' · {p0} min {p1}', {p0: Math.floor(left / 60), p1: String(left % 60).padStart(2, '0')}) : ''}
      </p>
      <button type="button" disabled={cancelling} onClick={() => void cancel()} className="rounded-2xl border border-slate-300 bg-white px-8 py-4 text-lg font-semibold text-slate-700 disabled:opacity-50">
        {tr('Annuler la commande')}
      </button>
    </div>
  );
}

function Done({placed, paid, onNext}: {placed: PlacedOrder; paid: boolean; onNext: () => void}) {
  const [left, setLeft] = useState(DONE_SECONDS);
  useEffect(() => {
    const timer = window.setInterval(() => setLeft(value => value - 1), 1000);
    return () => window.clearInterval(timer);
  }, []);
  useEffect(() => {
    if (left <= 0) onNext();
  }, [left, onNext]);

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-8 bg-[var(--accent)] p-8 text-center text-white">
      <CheckCircle2 className="size-20" aria-hidden="true" />
      <div className="space-y-2">
        <p className="text-2xl font-semibold">{tr('Merci ! Votre commande')}</p>
        <p className="text-8xl font-black tracking-tight">{placed.number}</p>
      </div>
      <div className="max-w-xl space-y-3 text-2xl">
        {placed.ticketPrinted ? (
          <p className="flex items-center justify-center gap-3"><Printer className="size-7" aria-hidden="true" />{tr('Prenez votre ticket.')}</p>
        ) : (
          <p>{tr('Retenez votre numéro.')}</p>
        )}
        <p className="font-semibold">
          {paid ? tr('Commande payée : nous vous appelons dès qu’elle est prête.') : tr('Présentez-vous au comptoir pour payer.')}
        </p>
      </div>
      <button type="button" onClick={onNext} className="rounded-2xl bg-white px-10 py-5 text-xl font-bold text-slate-900">
        {tr('Nouvelle commande (')}{Math.max(0, left)})
      </button>
    </div>
  );
}
