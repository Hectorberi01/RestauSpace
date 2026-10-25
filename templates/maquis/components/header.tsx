'use client';
import {useEffect, useState} from 'react';
import Link from 'next/link';
import {usePathname} from 'next/navigation';
import {CircleUser, Menu, ShoppingBag, X} from 'lucide-react';
import {config, hasSection} from '@/lib/config';
import {openingText} from '@/lib/format';
import {useCart} from '@/lib/local';
import {useSite} from './site-provider';
import {buttonClass, cx, Skeleton} from './ui';

const links = [
  {href: '/', label: 'Accueil'},
  {href: '/carte/', label: 'La carte'},
  {href: '/reserver/', label: 'Réserver'},
  ...(hasSection('reviews') ? [{href: '/#avis', label: 'Avis'}] : []),
  {href: '/#infos', label: 'Nous trouver'},
];

export function Logo({size = 'md'}: {size?: 'md' | 'sm'}) {
  const {front} = useSite();
  const logo = front?.restaurant.logoUrl?.startsWith('https://') ? front.restaurant.logoUrl : null;
  const box = size === 'md' ? 'size-12 rounded-[14px] text-[22px]' : 'size-10 rounded-xl text-lg';
  if (logo) {
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={logo} alt="" className={cx(box, 'shrink-0 bg-white object-cover')} />;
  }
  return (
    <span aria-hidden="true" className={cx(box, 'grid shrink-0 place-items-center bg-primary font-serif font-bold text-white')}>
      {front?.restaurant.name.trim().charAt(0).toUpperCase() ?? ''}
    </span>
  );
}

export function Header() {
  const {front, location, error} = useSite();
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const [cart] = useCart(location?.id ?? null);
  const count = cart.reduce((sum, line) => sum + line.quantity, 0);
  const status = location ? openingText(location) : null;

  useEffect(() => setOpen(false), [pathname]);

  return (
    <header className="border-b border-line/80 bg-cream/95 backdrop-blur">
      <div className="mx-auto flex h-[72px] max-w-6xl items-center gap-4 px-4 sm:px-6 lg:h-[84px]">
        <Link href="/" className="flex min-w-0 items-center gap-3 rounded-xl">
          <Logo />
          <span className="min-w-0">
            {front ? <span className="block truncate font-serif text-[22px] font-bold leading-tight lg:text-[26px]">{front.restaurant.name}</span> : !error && <Skeleton className="h-6 w-32" />}
            {location && (
              <span className="block truncate text-[13px] text-muted">
                <span className="hidden sm:inline">{[location.street, location.city].filter(Boolean).join(', ')}</span>
                {status && <span className={cx('sm:hidden', status.open ? 'font-semibold text-leaf' : '')}>{status.open && '● '}{status.text}</span>}
              </span>
            )}
          </span>
        </Link>

        <nav aria-label="Navigation principale" className="ml-auto hidden lg:block">
          <ul className="flex items-center gap-8 text-[15px] font-medium">
            {links.map(link => {
              const active = link.href === pathname;
              return (
                <li key={link.href}>
                  <Link href={link.href} aria-current={active ? 'page' : undefined} className={cx('rounded-md py-1 hover:text-primary', active && 'text-primary')}>{link.label}</Link>
                </li>
              );
            })}
          </ul>
        </nav>

        <div className="ml-auto flex items-center gap-2 lg:ml-6">
          <Link href="/compte/" aria-label="Mon compte" className="grid size-11 place-items-center rounded-full text-ink hover:bg-sand">
            <CircleUser className="size-6" aria-hidden="true" />
          </Link>
          <span className="hidden sm:block">
            <Link href={count > 0 ? '/commander/' : '/carte/'} className={buttonClass('primary', 'px-5 py-2.5')}>
              {count > 0 ? <><ShoppingBag className="size-4" aria-hidden="true" />Panier · {count}</> : 'Commander'}
            </Link>
          </span>
          <button type="button" className="grid size-11 place-items-center rounded-full hover:bg-sand lg:hidden" aria-expanded={open} aria-controls="menu-mobile"
            aria-label={open ? 'Fermer le menu' : 'Ouvrir le menu'} onClick={() => setOpen(value => !value)}>
            {open ? <X className="size-6" /> : <Menu className="size-6" />}
          </button>
        </div>
      </div>

      {open && (
        <nav id="menu-mobile" aria-label="Menu" className="border-t border-line bg-cream px-4 pb-5 pt-2 lg:hidden">
          <ul className="space-y-1 text-base font-medium">
            {links.map(link => (
              <li key={link.href}>
                <Link href={link.href} onClick={() => setOpen(false)} className={cx('block rounded-xl px-3 py-3 hover:bg-sand', link.href === pathname && 'text-primary')}>{link.label}</Link>
              </li>
            ))}
            <li><Link href="/compte/" onClick={() => setOpen(false)} className="block rounded-xl px-3 py-3 hover:bg-sand">Mon compte</Link></li>
          </ul>
          <Link href={count > 0 ? '/commander/' : '/carte/'} onClick={() => setOpen(false)} className={buttonClass('primary', 'mt-3 w-full')}>
            {count > 0 ? `Voir le panier · ${count}` : 'Commander'}
          </Link>
        </nav>
      )}
    </header>
  );
}
