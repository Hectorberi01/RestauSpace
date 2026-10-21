'use client';
import {useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent} from 'react';
import {useRouter} from 'next/navigation';
import {ClipboardList, CornerDownLeft, Loader2, Search, UserRound, UtensilsCrossed, type LucideIcon} from 'lucide-react';
import {api, type Page} from '@/lib/api';
import {money} from '@/lib/format';
import {searchCustomers, searchQuery} from '@/lib/customers';
import type {ProductSummary} from '@/lib/types';
import {useConsole} from './console';
import {cx} from './ui';

export interface SearchPage {
  href: string;
  label: string;
  icon: LucideIcon;
}

interface Hit {
  key: string;
  group: string;
  label: string;
  detail?: string;
  icon: LucideIcon;
  href: string;
}

/** Compare sans accents ni casse : « bissap » trouve « Bissap », « eau » trouve « Eau minérale ». */
const fold = (value: string) => value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLocaleLowerCase('fr');

/**
 * Recherche globale (⌘K / Ctrl K) : pages de la console, commande par numéro, produits du catalogue et clients.
 * Les produits sont chargés une fois à l'ouverture ; les clients sont cherchés côté serveur, seulement avec la
 * permission « Voir les clients ».
 */
export function GlobalSearch({pages}: {pages: SearchPage[]}) {
  const router = useRouter();
  const {can} = useConsole();
  const dialog = useRef<HTMLDialogElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [mac, setMac] = useState(false);
  const [text, setText] = useState('');
  const [active, setActive] = useState(0);
  const [products, setProducts] = useState<ProductSummary[] | null>(null);
  const [customers, setCustomers] = useState<Hit[]>([]);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    setMac(/Mac|iPhone|iPad/.test(navigator.platform));
    const shortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setOpen(true);
      }
    };
    document.addEventListener('keydown', shortcut);
    return () => document.removeEventListener('keydown', shortcut);
  }, []);

  useEffect(() => {
    const element = dialog.current;
    if (!element) return;
    if (open && !element.open) {
      element.showModal();
      setText('');
      setActive(0);
      requestAnimationFrame(() => input.current?.focus());
    }
    if (!open && element.open) element.close();
  }, [open]);

  useEffect(() => {
    if (!open || products) return;
    api<Page<ProductSummary>>('/catalog/products', {query: {page: 1, pageSize: 200}})
      .then(page => setProducts(page.items.filter(product => product.status !== 'Archived')))
      .catch(() => setProducts([]));
  }, [open, products]);

  const query = text.trim();
  useEffect(() => {
    setCustomers([]);
    if (!open || !can('customers.view') || query.length < 2 || /^#?\d+$/.test(query)) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setSearching(true);
      searchCustomers({...searchQuery(query, '', false, 1), pageSize: 5}, controller.signal)
        .then(page => setCustomers(page.items.map(customer => ({
          key: `c-${customer.id}`,
          group: 'Clients',
          label: customer.name,
          detail: customer.phone ?? customer.email ?? undefined,
          icon: UserRound,
          href: `/clients/${customer.id}`,
        }))))
        .catch(() => undefined)
        .finally(() => setSearching(false));
    }, 250);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [open, query, can]);

  const hits = useMemo(() => {
    const needle = fold(query);
    const list: Hit[] = [];
    const number = query.match(/^#?(\d+)$/)?.[1];
    if (number) list.push({key: `o-${number}`, group: 'Commandes', label: `Commande n° ${number}`, detail: 'Ouvrir dans Commandes', icon: ClipboardList, href: `/commandes?numero=${number}`});
    list.push(...pages
      .filter(page => !needle || fold(page.label).includes(needle))
      .slice(0, needle ? 5 : 8)
      .map(page => ({key: `p-${page.href}`, group: 'Pages', label: page.label, icon: page.icon, href: page.href})));
    if (needle) {
      list.push(...(products ?? [])
        .filter(product => fold(product.name).includes(needle))
        .slice(0, 6)
        .map(product => ({key: `m-${product.id}`, group: 'Produits', label: product.name, detail: money(product.basePrice, product.currencyCode), icon: UtensilsCrossed, href: `/menu?produit=${product.id}`})));
    }
    list.push(...customers);
    return list;
  }, [query, pages, products, customers]);

  useEffect(() => setActive(0), [query]);

  const go = (hit: Hit | undefined) => {
    if (!hit) return;
    setOpen(false);
    router.push(hit.href);
  };

  const onKey = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActive(value => Math.min(value + 1, hits.length - 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActive(value => Math.max(value - 1, 0));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      go(hits[active]);
    }
  };

  let lastGroup = '';
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}
        className="relative hidden h-10 w-full max-w-md items-center rounded-xl border border-slate-200 bg-white pr-3 pl-10 text-left text-sm text-ink-500 shadow-[0_1px_2px_rgba(15,23,42,0.03)] transition hover:border-slate-300 md:flex">
        <Search className="pointer-events-none absolute top-1/2 left-3.5 size-4 -translate-y-1/2" aria-hidden="true" />
        <span className="flex-1 truncate">Rechercher une commande, un produit, un client…</span>
        <kbd className="rounded-md border border-slate-200 bg-slate-50 px-1.5 py-0.5 font-sans text-[11px] font-medium" aria-hidden="true">{mac ? '⌘' : 'Ctrl'} K</kbd>
      </button>
      <button type="button" onClick={() => setOpen(true)} className="grid size-10 place-items-center rounded-xl text-ink-600 hover:bg-slate-100 md:hidden" aria-label="Rechercher">
        <Search className="size-5" />
      </button>

      <dialog ref={dialog} aria-label="Recherche"
        onClose={() => setOpen(false)}
        onClick={event => event.target === event.currentTarget && setOpen(false)}
        className="mx-auto mt-[12vh] w-[calc(100%-2rem)] max-w-xl overflow-hidden rounded-2xl p-0 text-ink-900 shadow-2xl backdrop:bg-ink-900/40">
        {open && (
          <div className="flex max-h-[70dvh] flex-col">
            <div className="flex items-center gap-3 border-b border-slate-100 px-4">
              <Search className="size-5 shrink-0 text-ink-500" aria-hidden="true" />
              <input ref={input} value={text} onChange={event => setText(event.target.value)} onKeyDown={onKey}
                placeholder="Numéro de commande, produit, client, page…" aria-label="Rechercher"
                role="combobox" aria-expanded="true" aria-controls="recherche-resultats" aria-activedescendant={hits[active] ? `recherche-${hits[active].key}` : undefined}
                className="h-14 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-ink-500" style={{outline: 'none'}} />
              {searching && <Loader2 className="size-4 animate-spin text-ink-500" aria-label="Recherche en cours" />}
              <kbd className="rounded-md border border-slate-200 bg-slate-50 px-1.5 py-0.5 font-sans text-[11px] text-ink-500">Échap</kbd>
            </div>
            <ul id="recherche-resultats" role="listbox" className="min-h-0 flex-1 overflow-y-auto p-2">
              {hits.length === 0 && <li className="px-3 py-8 text-center text-sm text-ink-500">Aucun résultat pour « {query} ».</li>}
              {hits.map((hit, index) => {
                const header = hit.group !== lastGroup;
                lastGroup = hit.group;
                return (
                  <li key={hit.key} role="presentation">
                    {header && <p className="px-3 pt-3 pb-1 text-[11px] font-semibold tracking-widest text-ink-500 uppercase">{hit.group}</p>}
                    <button id={`recherche-${hit.key}`} type="button" role="option" aria-selected={index === active}
                      onMouseEnter={() => setActive(index)} onClick={() => go(hit)}
                      className={cx('flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm', index === active ? 'bg-brand-50 text-brand-800' : 'text-ink-800')}>
                      <span className={cx('grid size-8 shrink-0 place-items-center rounded-lg', index === active ? 'bg-white text-brand-600' : 'bg-slate-100 text-ink-500')}>
                        <hit.icon className="size-4" aria-hidden="true" />
                      </span>
                      <span className="min-w-0 flex-1 truncate font-medium">{hit.label}</span>
                      {hit.detail && <span className="truncate text-xs text-ink-500">{hit.detail}</span>}
                      {index === active && <CornerDownLeft className="size-4 shrink-0 text-brand-600" aria-hidden="true" />}
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>
        )}
      </dialog>
    </>
  );
}
