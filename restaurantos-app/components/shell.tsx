'use client';
import {useEffect, useRef, useState, type ReactNode} from 'react';
import Link from 'next/link';
import {usePathname, useRouter} from 'next/navigation';
import {
  BarChart3, Bell, Boxes, ChevronsUpDown, MapPin, Mails, Building2, Contact, CreditCard, Megaphone, MessageSquareText, Plug, ScrollText, Users, CalendarClock, CalendarDays, ChefHat, ChevronDown, ChevronsLeft, ChevronsRight, ClipboardList, Crown, Gift, Truck, LayoutDashboard, LogOut,
  Menu as MenuIcon, Repeat, Settings, Store, UserRound, UtensilsCrossed, Wallet, X, type LucideIcon,
} from 'lucide-react';
import {api, logout, leaveTenant, type Page} from '@/lib/api';
import {money} from '@/lib/format';
import {addOnOfferText, listAddOns, loadSubscription} from '@/lib/billing';
import {useResource} from '@/lib/hooks';
import {ConnectionStatus} from './connection';
import {GlobalSearch} from './search';
import {useConsole} from './console';
import {PlanBadge, usePlanFeatures} from './plan';
import {Feature} from '@/lib/plan';
import {cx, Select} from './ui';

interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  badge?: 'orders';
  /** Badge de plan quand aucun de ces droits n'est inclus. */
  features?: string[];
}

const main: NavItem[] = [
  {href: '/tableau-de-bord', label: 'Tableau de bord', icon: LayoutDashboard},
  {href: '/commandes', label: 'Commandes', icon: ClipboardList, badge: 'orders'},
  {href: '/caisse', label: 'Caisse', icon: Wallet},
  {href: '/reservations', label: 'Réservations', icon: CalendarDays},
  {href: '/cuisine', label: 'Cuisine', icon: ChefHat},
  {href: '/livraison', label: 'Livraison & retrait', icon: Truck},
  {href: '/salle', label: 'Salle & tables', icon: Store},
  {href: '/menu', label: 'Menu & produits', icon: UtensilsCrossed},
  {href: '/stock', label: 'Stock', icon: Boxes},
  {href: '/clients', label: 'Clients', icon: Contact},
  {href: '/marketing', label: 'Marketing', icon: Megaphone},
  {href: '/messages', label: 'Messages clients', icon: Mails},
  {href: '/avis', label: 'Avis', icon: MessageSquareText},
  {href: '/rapports', label: 'Rapports', icon: BarChart3},
];

const restaurant: NavItem[] = [
  {href: '/personnel', label: 'Personnel', icon: Users},
  {href: '/planning', label: 'Planning', icon: CalendarClock},
  {href: '/etablissements', label: 'Établissements', icon: Building2},
  {href: '/abonnement', label: 'Abonnement', icon: CreditCard},
  {href: '/parametres', label: 'Paramètres', icon: Settings},
  {href: '/journal', label: 'Journal d’activité', icon: ScrollText},
  {href: '/integrations', label: 'Intégrations', icon: Plug, features: [Feature.IntegrationsApi, Feature.IntegrationsWebhooks]},
  {href: '/profil', label: 'Mon profil', icon: UserRound},
];

const COLLAPSED_KEY = 'restaurantos.app.sidebar';

export function Logo() {
  return (
    <span className="flex items-center gap-2.5 font-semibold tracking-tight">
      <span className="grid size-9 place-items-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow-sm">
        <UtensilsCrossed className="size-5" aria-hidden="true" />
      </span>
      <span className="text-lg">
        Restau<span className="text-brand-600">Space</span>
      </span>
    </span>
  );
}

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase();
}

interface Subscription {
  subscription: {status: string; currentPeriodEndUtc: string; isComplimentary?: boolean; complimentaryUntilUtc?: string | null} | null;
  plan: {name: string; price: number; currencyCode: string; billingInterval: 'Monthly' | 'Yearly'} | null;
}

function PlanCard({collapsed}: {collapsed: boolean}) {
  const {can} = useConsole();
  // Rechargée avec les droits du plan, après chaque changement d'abonnement.
  const {version} = usePlanFeatures();
  const {data} = useResource(
    signal => (can('billing.view') ? api<Subscription>('/billing/subscription', {signal}).catch(() => null) : Promise.resolve(null)),
    [version],
  );
  if (collapsed || !data) return null;
  const trial = data.subscription?.status === 'Trialing';
  const paying = !!data.plan && data.plan.price > 0;
  const price = data.plan && paying ? `${money(data.plan.price, data.plan.currencyCode)} / ${data.plan.billingInterval === 'Yearly' ? 'an' : 'mois'}` : null;
  const trialEnd = trial && data.subscription ? new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short'}).format(new Date(data.subscription.currentPeriodEndUtc)) : null;
  const offered = !!data.subscription?.isComplimentary;
  const offerEnd = data.subscription?.complimentaryUntilUtc;
  return (
    <Link href={trialEnd && paying && can('billing.manage') ? '/abonnement?payer=1' : '/abonnement'} className="mx-3 mb-3 block rounded-2xl border border-amber-100 bg-gradient-to-br from-amber-50 to-orange-50 p-4 transition hover:border-amber-200">
      <div className="flex items-center gap-3">
        <span className="grid size-10 place-items-center rounded-xl bg-white text-amber-500 shadow-sm"><Crown className="size-5" aria-hidden="true" /></span>
        <div className="min-w-0">
          <p className="text-xs text-ink-500">{offered ? 'Offert par RestauSpace' : trialEnd ? `Essai gratuit jusqu’au ${trialEnd}` : data.plan ? 'Plan actuel' : 'Abonnement'}</p>
          <p className="truncate font-bold text-ink-900">{data.plan?.name ?? 'Choisir un plan'}</p>
          {offered
            ? <p className="text-sm text-ink-600">{offerEnd ? `jusqu’au ${new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(offerEnd))}` : 'sans limite de durée'}</p>
            : price && <p className="text-sm text-ink-600">{trialEnd ? `puis ${price}` : price}</p>}
        </div>
      </div>
      {trialEnd && paying && can('billing.manage') && (
        <span className="mt-3 flex items-center justify-center rounded-lg bg-white px-3 py-1.5 text-xs font-semibold text-amber-700 shadow-sm">Payer maintenant</span>
      )}
    </Link>
  );
}

const OFFER_BANNER_KEY = 'restauspace.offer-banner';
const longDay = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'long', year: 'numeric'}).format(new Date(iso));

/** Bandeau des offres RestauSpace en cours (plan, options) ; masquable jusqu'au prochain changement d'offre. */
function OfferBanner() {
  const {can} = useConsole();
  const {version} = usePlanFeatures();
  const allowed = can('billing.view');
  const {data} = useResource(
    signal => (allowed ? Promise.all([loadSubscription(signal).catch(() => null), listAddOns(signal).catch(() => null)]) : Promise.resolve(null)),
    [allowed, version],
  );
  const [hidden, setHidden] = useState<string | null>(null);
  useEffect(() => {
    try {
      setHidden(window.localStorage.getItem(OFFER_BANNER_KEY));
    } catch {}
  }, []);
  if (!data) return null;
  const [mine, addOns] = data;
  const lines: string[] = [];
  const sub = mine?.subscription;
  if (sub?.isComplimentary && mine) {
    const renewal = sub.complimentaryRenewalMonths ? `, renouvelé automatiquement tous les ${sub.complimentaryRenewalMonths} mois` : '';
    lines.push(`Plan ${mine.plan.name} offert ${sub.complimentaryUntilUtc ? `jusqu’au ${longDay(sub.complimentaryUntilUtc)}${renewal}` : 'sans limite de durée'}`);
  }
  for (const addOn of addOns?.active ?? []) {
    const text = addOnOfferText(addOn, longDay);
    if (text) lines.push(`Option ${addOn.addOn.name} : ${text.charAt(0).toLowerCase()}${text.slice(1)}`);
  }
  const signature = lines.join('|');
  if (!lines.length || hidden === signature) return null;
  const hide = () => {
    setHidden(signature);
    try {
      window.localStorage.setItem(OFFER_BANNER_KEY, signature);
    } catch {}
  };
  return (
    <div className="flex items-start gap-3 border-b border-brand-100 bg-brand-50 px-6 py-2 text-sm text-brand-900 print:hidden">
      <Gift className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />
      <p className="min-w-0 flex-1">
        <span className="font-semibold">Offert par RestauSpace : </span>
        {lines.join(' · ')}.{' '}
        <Link href="/abonnement" className="font-medium text-brand-700 underline-offset-2 hover:underline">Voir l’abonnement</Link>
      </p>
      <button type="button" onClick={hide} className="rounded p-0.5 text-brand-700 hover:bg-brand-100" aria-label="Masquer le bandeau">
        <X className="size-4" />
      </button>
    </div>
  );
}

function Sidebar({collapsed, onToggle, onNavigate, ordersWaiting}: {collapsed: boolean; onToggle?: () => void; onNavigate?: () => void; ordersWaiting: number}) {
  const pathname = usePathname();
  const {tenant, locations, location} = useConsole();
  const {blocked} = usePlanFeatures();
  const name = tenant.branding.displayName || tenant.name;

  const link = (item: NavItem) => {
    const active = pathname === item.href || pathname.startsWith(`${item.href}/`);
    const count = item.badge === 'orders' ? ordersWaiting : 0;
    const locked = !!item.features && item.features.every(blocked);
    return (
      <Link
        key={item.href}
        href={item.href}
        onClick={onNavigate}
        title={collapsed ? item.label : undefined}
        aria-current={active ? 'page' : undefined}
        className={cx(
          'relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition',
          collapsed && 'justify-center px-0',
          active
            ? 'bg-brand-50 text-brand-700 before:absolute before:top-2 before:bottom-2 before:-left-3 before:w-1 before:rounded-r-full before:bg-brand-600'
            : 'text-ink-600 hover:bg-slate-100 hover:text-ink-900',
        )}>
        <item.icon className={cx('size-[1.15rem] shrink-0', active ? 'text-brand-600' : 'text-ink-500')} aria-hidden="true" />
        {!collapsed && <span className="flex-1 truncate">{item.label}</span>}
        {!collapsed && locked && <PlanBadge feature={item.features![0]} />}
        {count > 0 && (
          <span className={cx('grid min-w-5 place-items-center rounded-full bg-brand-100 px-1.5 text-[11px] font-bold text-brand-700', collapsed && 'absolute -top-1 -right-1')}>
            {count > 99 ? '99+' : count}
          </span>
        )}
      </Link>
    );
  };

  return (
    <div className="flex h-full flex-col border-r border-slate-200/80 bg-white text-ink-800">
      <div className={cx('flex h-16 items-center gap-2 px-5', collapsed && 'justify-center px-2')}>
        <Link href="/tableau-de-bord" onClick={onNavigate} className="flex min-w-0 flex-1 items-center gap-2.5 font-bold tracking-tight" aria-label="RestauSpace, tableau de bord">
          <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow-sm shadow-brand-600/30">
            <UtensilsCrossed className="size-5" aria-hidden="true" />
          </span>
          {!collapsed && <span className="truncate text-lg">RestauSpace</span>}
        </Link>
        {onToggle && !collapsed && (
          <button type="button" onClick={onToggle} className="rounded-lg p-1.5 text-ink-500 hover:bg-slate-100 hover:text-ink-900" aria-label="Réduire le menu">
            <ChevronsLeft className="size-5" />
          </button>
        )}
      </div>
      <div className={cx('px-3 pb-3', collapsed && 'px-2')}>
        <Link href="/restaurants" onClick={onNavigate} title={collapsed ? name : 'Changer de restaurant'}
          className={cx('flex items-center gap-3 rounded-xl border border-slate-200 bg-white p-2.5 shadow-[0_1px_2px_rgba(15,23,42,0.04)] transition hover:border-slate-300 hover:bg-slate-50', collapsed && 'justify-center p-1.5')}>
          <span className="grid size-9 shrink-0 place-items-center overflow-hidden rounded-lg bg-gradient-to-br from-amber-400 to-orange-600 text-sm font-bold text-white">
            {tenant.branding.logoUrl ? <img src={tenant.branding.logoUrl} alt="" className="size-full object-cover" /> : initials(name) || <UtensilsCrossed className="size-5" />}
          </span>
          {!collapsed && (
            <>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold text-ink-900">{name}</span>
                <span className="block truncate text-xs text-ink-500" title="Établissement affiché">{locations.length > 1 && location ? location.name : 'Restaurant'}</span>
              </span>
              <ChevronsUpDown className="size-4 shrink-0 text-ink-500" aria-hidden="true" />
            </>
          )}
        </Link>
      </div>
      <nav aria-label="Navigation principale" className="flex-1 space-y-0.5 overflow-y-auto px-3 pb-4">
        {main.map(link)}
        <div className="my-4 border-t border-slate-100" />
        {!collapsed && <p className="px-3 pb-2 text-[11px] font-semibold tracking-widest text-ink-500 uppercase">Votre restaurant</p>}
        {restaurant.map(link)}
      </nav>
      <PlanCard collapsed={collapsed} />
      {onToggle && collapsed && (
        <button type="button" onClick={onToggle} className="mx-auto mb-4 rounded-lg p-2 text-ink-500 hover:bg-slate-100 hover:text-ink-900" aria-label="Agrandir le menu">
          <ChevronsRight className="size-5" />
        </button>
      )}
    </div>
  );
}

function LocationPicker() {
  const {locations, location, setLocationId} = useConsole();
  if (locations.length === 0) return <Link href="/etablissements" className="text-sm font-semibold text-brand-700">Créer un établissement</Link>;
  if (locations.length === 1) return null;
  return (
    <label className="relative flex min-w-0 items-center">
      <span className="sr-only">Établissement</span>
      <MapPin className="pointer-events-none absolute left-3 size-4 text-brand-600" aria-hidden="true" />
      <Select value={location?.id ?? ''} onChange={event => setLocationId(event.target.value)} className="h-10 max-w-72 min-w-0 truncate rounded-xl pl-9 font-medium" title="Établissement affiché">
        {locations.map(item => (
          <option key={item.id} value={item.id}>
            {item.name}
            {item.isFrozen ? ' (gelé)' : item.status !== 'Active' ? ' (fermé)' : ''}
          </option>
        ))}
      </Select>
    </label>
  );
}

interface InAppNotification {
  id: string;
  title: string | null;
  body: string;
  state: 'Unread' | 'Read' | 'Archived';
  createdAtUtc: string;
}

function Notifications() {
  const [open, setOpen] = useState(false);
  const {data, reload} = useResource(
    signal => api<Page<InAppNotification>>('/me/notifications', {query: {pageSize: 20}, signal}).then(page => page.items).catch(() => [] as InAppNotification[]),
    [],
    60_000,
  );
  const unread = data?.filter(item => item.state === 'Unread') ?? [];
  return (
    <div className="relative">
      <button type="button" onClick={() => setOpen(v => !v)} className="relative grid size-10 place-items-center rounded-xl text-ink-600 hover:bg-slate-100" aria-label={`Notifications${unread.length ? ` (${unread.length} non lues)` : ''}`}>
        <Bell className="size-5" />
        {unread.length > 0 && <span className="absolute top-2 right-2 size-2.5 rounded-full bg-red-500 ring-2 ring-white" />}
      </button>
      {open && (
        <div className="absolute right-0 z-40 mt-2 w-80 rounded-2xl border border-slate-200 bg-white p-2 shadow-xl">
          <div className="flex items-center justify-between px-3 py-2">
            <p className="text-sm font-semibold">Notifications</p>
            <Link href="/notifications" onClick={() => setOpen(false)} className="text-xs font-semibold text-brand-700 hover:underline">Tout voir</Link>
          </div>
          {(data ?? []).length === 0 && <p className="px-3 pb-3 text-sm text-ink-500">Aucune notification.</p>}
          <ul className="max-h-80 overflow-y-auto">
            {(data ?? []).slice(0, 8).map(item => (
              <li key={item.id}>
                <button
                  type="button"
                  onClick={async () => {
                    if (item.state === 'Unread') {
                      await api(`/me/notifications/${item.id}/read`, {method: 'POST'}).catch(() => undefined);
                      await reload();
                    }
                  }}
                  className="w-full rounded-xl px-3 py-2 text-left hover:bg-slate-50">
                  <span className={cx('block text-sm', item.state === 'Unread' ? 'font-semibold' : 'text-ink-600')}>{item.title ?? 'Notification'}</span>
                  <span className="line-clamp-2 block text-xs text-ink-500">{item.body}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function UserMenu() {
  const router = useRouter();
  const {tenant, email, isOwner} = useConsole();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const name = tenant.branding.displayName || tenant.name;
  return (
    <div ref={ref} className="relative">
      <button type="button" onClick={() => setOpen(v => !v)} className="flex items-center gap-3 rounded-xl py-1 pr-2 pl-1 hover:bg-slate-100" aria-expanded={open}>
        <span className="grid size-9 place-items-center rounded-full bg-gradient-to-br from-amber-400 to-orange-600 text-sm font-bold text-white">{initials(name)}</span>
        <span className="hidden text-left sm:block">
          <span className="block max-w-40 truncate text-sm font-semibold">{name}</span>
          <span className="block text-xs text-ink-500">{isOwner ? 'Propriétaire' : 'Équipe'}</span>
        </span>
        <ChevronDown className="hidden size-4 text-ink-500 sm:block" aria-hidden="true" />
      </button>
      {open && (
        <div className="absolute right-0 z-40 mt-2 w-64 rounded-2xl border border-slate-200 bg-white p-2 shadow-xl">
          <p className="truncate px-3 py-2 text-xs text-ink-500">{email}</p>
          <Link href="/profil" onClick={() => setOpen(false)} className="flex w-full items-center gap-2 rounded-xl px-3 py-2 text-sm hover:bg-slate-50">
            <UserRound className="size-4" aria-hidden="true" />
            Mon profil
          </Link>
          <button
            type="button"
            onClick={async () => {
              await leaveTenant();
              router.replace('/restaurants');
            }}
            className="flex w-full items-center gap-2 rounded-xl px-3 py-2 text-sm hover:bg-slate-50">
            <Repeat className="size-4" aria-hidden="true" />
            Changer de restaurant
          </button>
          <button
            type="button"
            onClick={async () => {
              await logout();
              router.replace('/connexion');
            }}
            className="flex w-full items-center gap-2 rounded-xl px-3 py-2 text-sm text-red-600 hover:bg-red-50">
            <LogOut className="size-4" aria-hidden="true" />
            Se déconnecter
          </button>
        </div>
      )}
    </div>
  );
}

export function Shell({children}: {children: ReactNode}) {
  const pathname = usePathname();
  const {tenant, location} = useConsole();
  const [open, setOpen] = useState(false);
  const [collapsed, setCollapsed] = useState(false);

  useEffect(() => setOpen(false), [pathname]);
  useEffect(() => {
    try {
      setCollapsed(window.localStorage.getItem(COLLAPSED_KEY) === '1');
    } catch {}
  }, []);
  const toggle = () =>
    setCollapsed(value => {
      try {
        window.localStorage.setItem(COLLAPSED_KEY, value ? '0' : '1');
      } catch {}
      return !value;
    });

  const {data: waiting} = useResource(
    signal => (location ? api<Page<unknown>>('/orders', {query: {locationId: location.id, status: 'Placed', pageSize: 1}, signal}).then(page => page.totalCount ?? page.items.length) : Promise.resolve(0)),
    [location?.id],
    20_000,
  );

  return (
    <div className={cx('min-h-dvh bg-[#f7f7fb] print:block print:bg-white lg:grid', collapsed ? 'lg:grid-cols-[5rem_minmax(0,1fr)]' : 'lg:grid-cols-[16rem_minmax(0,1fr)]')}>
      <aside className="sticky top-0 hidden h-dvh lg:block print:hidden">
        <Sidebar collapsed={collapsed} onToggle={toggle} ordersWaiting={waiting ?? 0} />
      </aside>

      {open && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button type="button" className="absolute inset-0 bg-ink-900/50" aria-label="Fermer le menu" onClick={() => setOpen(false)} />
          <aside className="relative h-full w-72 max-w-[85%] shadow-xl">
            <button type="button" onClick={() => setOpen(false)} className="absolute top-6 -right-12 rounded-lg bg-white p-2" aria-label="Fermer le menu">
              <X className="size-5" />
            </button>
            <Sidebar collapsed={false} onNavigate={() => setOpen(false)} ordersWaiting={waiting ?? 0} />
          </aside>
        </div>
      )}

      <div className="flex min-w-0 flex-col">
        <header className="sticky top-0 z-30 flex h-16 print:hidden items-center gap-3 border-b border-slate-200/70 bg-white/85 px-4 backdrop-blur sm:px-6 lg:px-8">
          <button type="button" onClick={() => setOpen(true)} className="-ml-1 rounded-lg p-2 hover:bg-slate-100 lg:hidden" aria-label="Ouvrir le menu">
            <MenuIcon className="size-5" />
          </button>
          <GlobalSearch pages={[...main, ...restaurant]} />
          <div className="ml-auto flex items-center gap-2">
            <ConnectionStatus />
            <LocationPicker />
            <Notifications />
            <UserMenu />
          </div>
        </header>
        {!tenant.canAcceptBusinessWrites && (
          <div className="border-b border-amber-200 bg-amber-50 px-6 py-2 text-sm text-amber-900">
            Restaurant {tenant.status === 'Suspended' ? 'suspendu' : 'fermé'} : la consultation reste possible, les modifications sont bloquées.
          </div>
        )}
        <OfferBanner />
        <main id="contenu" className="mx-auto w-full max-w-[1600px] min-w-0 flex-1 px-4 py-6 print:p-0 sm:px-6 lg:px-8 lg:py-8">
          {children}
        </main>
      </div>
    </div>
  );
}
