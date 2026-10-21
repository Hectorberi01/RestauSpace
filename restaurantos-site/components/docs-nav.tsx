'use client';
import Link from 'next/link';
import {usePathname} from 'next/navigation';
import {
  BookOpenText, Boxes, CalendarCheck, ChevronDown, CreditCard, FileCode2, KeyRound, LayoutTemplate, ListTree, MonitorSmartphone, Rocket,
  ScrollText, ShoppingBag, UserRound, UtensilsCrossed, Webhook, type LucideIcon,
} from 'lucide-react';

const icons: Record<string, LucideIcon> = {
  documentation: BookOpenText,
  'prise-en-main': Rocket,
  conventions: ScrollText,
  'guide-site-restaurant': LayoutTemplate,
  'vente-catalogue': UtensilsCrossed,
  'vente-commande': ShoppingBag,
  'vente-paiement': CreditCard,
  'vente-comptes-clients': UserRound,
  'vente-reservations-questionnaires': CalendarCheck,
  'vente-borne': MonitorSmartphone,
  'integrations-cles': KeyRound,
  'integrations-ressources': Boxes,
  webhooks: Webhook,
  'reference-erreurs': ListTree,
  'exemples-clients': FileCode2,
};

interface NavGroup {
  group: string;
  pages: {slug: string; label: string}[];
}

function Links({groups, path}: {groups: NavGroup[]; path: string}) {
  const link = (href: string, label: string) => {
    const active = path === href;
    const Icon = icons[href.split('/').pop() ?? ''] ?? BookOpenText;
    return (
      <Link
        href={href}
        aria-current={active ? 'page' : undefined}
        className={`group flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm transition ${active ? 'bg-brand-50 font-semibold text-brand-700 shadow-[inset_2px_0_0_var(--color-brand-600)]' : 'text-ink-600 hover:bg-slate-50 hover:text-ink-900'}`}
      >
        <Icon className={`size-4 shrink-0 ${active ? 'text-brand-600' : 'text-slate-400 group-hover:text-ink-600'}`} aria-hidden="true" />
        {label}
      </Link>
    );
  };
  return (
    <div className="space-y-6">
      <div>{link('/developpeurs/documentation', 'Sommaire')}</div>
      {groups.map(group => (
        <div key={group.group}>
          <p className="mb-1.5 px-3 text-[0.7rem] font-bold uppercase tracking-[0.12em] text-ink-500">{group.group}</p>
          <ul className="space-y-0.5">
            {group.pages.map(page => <li key={page.slug}>{link(`/developpeurs/${page.slug}`, page.label)}</li>)}
          </ul>
        </div>
      ))}
    </div>
  );
}

/** Le sommaire de la documentation : colonne à gauche sur grand écran, menu dépliant sur mobile. */
export function DocsNav({groups}: {groups: NavGroup[]}) {
  const path = usePathname();
  return (
    <>
      <details className="group rounded-xl border border-slate-200 bg-white lg:hidden">
        <summary className="flex cursor-pointer list-none items-center justify-between px-4 py-3 text-sm font-semibold text-ink-800">
          Sommaire de la documentation
          <ChevronDown className="size-4 text-ink-500 transition group-open:rotate-180" aria-hidden="true" />
        </summary>
        <nav aria-label="Documentation" className="border-t border-slate-100 px-1 py-3">
          <Links groups={groups} path={path} />
        </nav>
      </details>
      <nav aria-label="Documentation" className="sticky top-24 hidden max-h-[calc(100vh-7rem)] overflow-y-auto pb-8 [scrollbar-width:thin] lg:block">
        <Links groups={groups} path={path} />
      </nav>
    </>
  );
}
