'use client';
import {useEffect, useId, useLayoutEffect, useRef, useState} from 'react';
import Image from 'next/image';
import Link from 'next/link';
import {usePathname} from 'next/navigation';
import {
  ArrowRight, BarChart3, ChevronDown, CircleHelp, Code2, Home, LayoutGrid, LayoutTemplate, LogIn, Mail, Menu, Play, Settings2, Tag, UtensilsCrossed, X, type LucideIcon,
} from 'lucide-react';
import {modules} from '@/content/features';
import {featureMenu, nav, site} from '@/content/site';
import {Brand} from './brand';
import {DemoLink} from './demo-modal';
import {ModuleIcon} from './icon';

const navIcons: Record<string, LucideIcon> = {'/': Home, '/fonctionnalites': LayoutGrid, '/tarifs': Tag, '/site-sur-mesure': LayoutTemplate, '/faq': CircleHelp, '/developpeurs': Code2, '/contact': Mail};
const groupIcons: Record<(typeof featureMenu)[number]['icon'], LucideIcon> = {service: UtensilsCrossed, gestion: Settings2, croissance: BarChart3};
const groups = featureMenu.map(group => ({...group, items: group.modules.flatMap(id => modules.filter(module => module.id === id))}));

export function Header() {
  const path = usePathname();
  const menuId = useId();
  const [open, setOpen] = useState(false);
  const [mega, setMega] = useState(false);
  const [mobileFeatures, setMobileFeatures] = useState(false);
  const [scrolled, setScrolled] = useState(false);
  const [bar, setBar] = useState<{left: number; width: number} | null>(null);
  const links = useRef<(HTMLElement | null)[]>([]);
  const closeTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const active = (href: string) => (href === '/' ? path === '/' : path === href || path.startsWith(`${href}/`));
  const current = nav.findIndex(item => active(item.href));

  useEffect(() => {
    setOpen(false);
    setMega(false);
  }, [path]);

  useEffect(() => {
    const update = () => setScrolled(window.scrollY > 8);
    update();
    window.addEventListener('scroll', update, {passive: true});
    return () => window.removeEventListener('scroll', update);
  }, []);

  useEffect(() => {
    if (!open && !mega) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      setMega(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, mega]);

  useEffect(() => {
    document.body.style.overflow = open ? 'hidden' : '';
    return () => {
      document.body.style.overflow = '';
    };
  }, [open]);

  useLayoutEffect(() => {
    const measure = () => {
      const link = current >= 0 ? links.current[current] : null;
      setBar(link && link.offsetWidth ? {left: link.offsetLeft + 12, width: link.offsetWidth - 24} : null);
    };
    measure();
    window.addEventListener('resize', measure);
    return () => window.removeEventListener('resize', measure);
  }, [current]);

  const openMega = () => {
    clearTimeout(closeTimer.current);
    setMega(true);
  };
  const leaveMega = () => {
    clearTimeout(closeTimer.current);
    closeTimer.current = setTimeout(() => setMega(false), 180);
  };

  return (
    <header className="sticky top-0 z-40 px-2 pt-2 sm:px-3">
      <div className="relative mx-auto max-w-7xl" onMouseLeave={leaveMega}>
        <div className={`relative flex h-14 items-center justify-between gap-4 border px-3 backdrop-blur-xl transition-[background-color,box-shadow,border-color,border-radius] duration-300 sm:px-4 ${mega ? 'rounded-t-2xl border-slate-200/80 border-b-transparent bg-white shadow-xl shadow-brand-900/10' : `rounded-2xl ${scrolled ? 'border-slate-200/80 bg-white/85 shadow-lg shadow-brand-900/10' : 'border-white/70 bg-white/70 shadow-md shadow-brand-900/5'}`}`}>
          <Brand />
          <nav className="relative hidden h-full items-center gap-0.5 md:flex" aria-label="Navigation principale">
            {nav.map((item, index) => {
              const isActive = active(item.href);
              if (item.href === '/fonctionnalites') {
                return (
                  <button
                    key={item.href}
                    ref={element => {
                      links.current[index] = element;
                    }}
                    type="button"
                    aria-expanded={mega}
                    aria-controls={menuId}
                    onClick={() => setMega(value => !value)}
                    onMouseEnter={openMega}
                    className={`inline-flex items-center gap-1 rounded-xl px-2 py-2 text-sm font-medium lg:px-3 transition-colors ${mega ? 'bg-brand-50 text-brand-700' : isActive ? 'text-brand-700 hover:bg-slate-100/70' : 'text-ink-600 hover:bg-slate-100/70 hover:text-ink-900'}`}
                  >
                    {item.label}
                    <ChevronDown className={`size-4 transition-transform duration-300 ${mega ? 'rotate-180' : ''}`} aria-hidden="true" />
                  </button>
                );
              }
              return (
                <Link
                  key={item.href}
                  ref={element => {
                    links.current[index] = element;
                  }}
                  href={item.href}
                  onMouseEnter={leaveMega}
                  aria-current={isActive ? 'page' : undefined}
                  className={`rounded-xl px-2 py-2 text-sm font-medium lg:px-3 transition-colors hover:bg-slate-100/70 ${item.href === '/' ? 'max-xl:hidden' : ''} ${isActive ? 'text-brand-700' : 'text-ink-600 hover:text-ink-900'}`}
                >
                  {item.label}
                </Link>
              );
            })}
            <span
              aria-hidden="true"
              className={`absolute bottom-0 h-0.5 rounded-full bg-brand-600 transition-all duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)] ${bar && !mega ? 'opacity-100' : 'opacity-0'}`}
              style={bar ?? {left: 0, width: 0}}
            />
          </nav>
          <div className="hidden items-center gap-2 lg:flex">
            <a href={site.consoleUrl} className="btn-motion hidden whitespace-nowrap rounded-xl border border-slate-200 bg-white px-4 py-2 text-sm font-semibold text-ink-800 hover:border-slate-300 hover:bg-slate-50 xl:block">
              Se connecter
            </a>
            <DemoLink className="btn-motion group inline-flex items-center gap-2 whitespace-nowrap rounded-xl bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-sm shadow-brand-600/25 hover:bg-brand-700 hover:shadow-lg hover:shadow-brand-600/30">
              Demander une démo <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
            </DemoLink>
          </div>
          <button
            type="button"
            className="grid size-10 place-items-center rounded-xl text-ink-800 transition hover:bg-slate-100 md:hidden"
            aria-expanded={open}
            aria-controls="menu-mobile"
            aria-label="Ouvrir le menu"
            onClick={() => setOpen(true)}
          >
            <Menu className="size-6" aria-hidden="true" />
          </button>
        </div>

        <div
          id={menuId}
          onMouseEnter={openMega}
          inert={!mega}
          className={`absolute inset-x-0 top-full hidden origin-top rounded-b-2xl border border-t-slate-100 border-x-slate-200/80 border-b-slate-200/80 bg-white p-4 shadow-2xl shadow-brand-900/15 transition duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)] md:block ${mega ? 'translate-y-0 opacity-100' : 'pointer-events-none -translate-y-2 opacity-0'}`}
        >
          <div className="grid gap-4 lg:grid-cols-[17rem_minmax(0,1fr)]">
            <div className="relative hidden overflow-hidden rounded-2xl bg-gradient-to-br from-brand-50 via-white to-sky-50 p-5 ring-1 ring-brand-100 lg:block">
              <span className="inline-flex rounded-full bg-brand-100/70 px-2.5 py-0.5 text-xs font-semibold text-brand-700">Tout pour votre restaurant</span>
              <p className="mt-3 text-xl font-bold leading-snug text-ink-900">Des outils complets pour réussir</p>
              <p className="mt-2 text-sm leading-relaxed text-ink-600">De la commande à la fidélisation, tout dans un seul logiciel.</p>
              <div className="mt-4 flex flex-col items-start gap-2">
                <Link href="/fonctionnalites" className="btn-motion group inline-flex items-center gap-1.5 rounded-xl bg-brand-600 px-3.5 py-2 text-xs font-semibold text-white shadow-md shadow-brand-600/25 hover:bg-brand-700">
                  Voir toutes les fonctionnalités <ArrowRight className="size-3.5 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
                </Link>
                <Link href="/decouvrir" className="inline-flex items-center gap-1.5 text-xs font-semibold text-brand-700 hover:underline">
                  <Play className="size-3 fill-current" aria-hidden="true" /> Faire la visite guidée
                </Link>
              </div>
              <div className="relative -mb-5 mt-5 h-28 overflow-hidden rounded-t-xl shadow-lg shadow-brand-900/10 ring-1 ring-slate-200">
                <Image src="/captures/hero-dashboard.webp" alt="" fill sizes="240px" className="object-cover object-left-top" />
              </div>
            </div>
            <div className="grid gap-2 sm:grid-cols-3">
              {groups.map((group, index) => {
                const GroupIcon = groupIcons[group.icon];
                return (
                  <div key={group.title} className={`px-2 py-1 ${index > 0 ? 'sm:border-l sm:border-slate-100 sm:pl-4' : ''}`}>
                    <p className="flex items-center gap-2.5 px-2 py-1.5 text-sm font-bold text-ink-900">
                      <span className="grid size-8 place-items-center rounded-lg bg-brand-50 text-brand-600"><GroupIcon className="size-4" aria-hidden="true" /></span>
                      {group.title}
                    </p>
                    <ul className="mt-1 space-y-0.5">
                      {group.items.map(module => (
                        <li key={module.id}>
                          <Link href={module.link?.href ?? `/fonctionnalites#${module.id}`} className="group flex items-start gap-3 rounded-xl p-2 transition hover:bg-slate-50">
                            <span className="transition-transform duration-300 group-hover:scale-110"><ModuleIcon icon={module.icon} size="size-9" /></span>
                            <span className="min-w-0">
                              <span className="block text-sm font-semibold text-ink-900 group-hover:text-brand-700">{module.title}</span>
                              <span className="block text-xs leading-snug text-ink-500">{module.short}</span>
                            </span>
                          </Link>
                        </li>
                      ))}
                    </ul>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>

      <div className={`fixed inset-0 z-50 md:hidden ${open ? '' : 'pointer-events-none'}`} inert={!open}>
        <div className={`absolute inset-0 bg-ink-900/40 backdrop-blur-sm transition-opacity duration-300 ${open ? 'opacity-100' : 'opacity-0'}`} onClick={() => setOpen(false)} aria-hidden="true" />
        <nav
          id="menu-mobile"
          aria-label="Navigation mobile"
          className={`absolute inset-y-2 right-2 flex w-[min(20rem,calc(100%-1rem))] flex-col overflow-y-auto rounded-2xl bg-white p-4 shadow-2xl transition-transform duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)] ${open ? 'translate-x-0' : 'translate-x-[110%]'}`}
        >
          <div className="flex items-center justify-between">
            <Brand />
            <button type="button" onClick={() => setOpen(false)} aria-label="Fermer le menu" className="grid size-10 place-items-center rounded-xl bg-slate-100 text-ink-800 transition hover:rotate-90 hover:bg-slate-200">
              <X className="size-5" aria-hidden="true" />
            </button>
          </div>
          <ul className="mt-5 space-y-1">
            {nav.map((item, index) => {
              const Icon = navIcons[item.href] ?? LayoutGrid;
              const isActive = active(item.href);
              const style = {transitionDelay: open ? `${80 + index * 35}ms` : '0ms'};
              const row = `flex w-full items-center gap-3 rounded-xl px-3 py-3 text-[0.95rem] font-medium transition duration-300 ${open ? 'translate-x-0 opacity-100' : 'translate-x-4 opacity-0'} ${isActive ? 'bg-brand-50 text-brand-700' : 'text-ink-800 hover:bg-slate-50'}`;
              if (item.href === '/fonctionnalites') {
                return (
                  <li key={item.href}>
                    <button type="button" style={style} aria-expanded={mobileFeatures} aria-controls="menu-mobile-features" onClick={() => setMobileFeatures(value => !value)} className={row}>
                      <Icon className="size-5" aria-hidden="true" />
                      <span className="flex-1 text-left">{item.label}</span>
                      <ChevronDown className={`size-4 transition-transform duration-300 ${mobileFeatures ? 'rotate-180' : ''}`} aria-hidden="true" />
                    </button>
                    <div id="menu-mobile-features" className={`grid transition-[grid-template-rows] duration-300 ${mobileFeatures ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]'}`}>
                      <div className="overflow-hidden" inert={!mobileFeatures}>
                        <div className="ml-5 mt-1 space-y-3 border-l border-slate-200 py-2 pl-4">
                          {groups.map(group => (
                            <div key={group.title}>
                              <p className="text-xs font-semibold uppercase tracking-wider text-ink-500">{group.title}</p>
                              <ul className="mt-1">
                                {group.items.map(module => (
                                  <li key={module.id}>
                                    <Link href={module.link?.href ?? `/fonctionnalites#${module.id}`} onClick={() => setOpen(false)} className="block rounded-lg px-2 py-1.5 text-sm text-ink-700 hover:bg-slate-50 hover:text-brand-700">{module.title}</Link>
                                  </li>
                                ))}
                              </ul>
                            </div>
                          ))}
                          <Link href="/fonctionnalites" onClick={() => setOpen(false)} className="inline-flex items-center gap-1 px-2 text-sm font-semibold text-brand-700">
                            Toutes les fonctionnalités <ArrowRight className="size-3.5" aria-hidden="true" />
                          </Link>
                        </div>
                      </div>
                    </div>
                  </li>
                );
              }
              return (
                <li key={item.href}>
                  <Link href={item.href} style={style} aria-current={isActive ? 'page' : undefined} className={row}>
                    <Icon className="size-5" aria-hidden="true" />
                    {item.label}
                  </Link>
                </li>
              );
            })}
          </ul>
          <div className="mt-auto space-y-2 border-t border-slate-100 pt-4">
            <a href={site.consoleUrl} className="flex items-center gap-3 rounded-xl px-3 py-3 text-[0.95rem] font-medium text-ink-800 hover:bg-slate-50">
              <LogIn className="size-5" aria-hidden="true" /> Se connecter
            </a>
            <DemoLink onClick={() => setOpen(false)} className="btn-motion group flex items-center justify-center gap-2 rounded-xl bg-brand-600 px-4 py-3.5 font-semibold text-white shadow-lg shadow-brand-600/25">
              Demander une démo <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
            </DemoLink>
          </div>
        </nav>
      </div>
    </header>
  );
}
