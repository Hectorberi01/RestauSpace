import type {Metadata} from 'next';
import Link from 'next/link';
import {ArrowRight, Braces, KeyRound, ShoppingBag, Webhook} from 'lucide-react';
import {DocArticle} from '@/components/doc-article';
import {loadDoc} from '@/lib/developer-docs';
import {site} from '@/content/site';

export const metadata: Metadata = {
  title: 'Documentation développeurs',
  description: 'API de vente en ligne et API d’intégration : construire le site, l’application mobile ou la borne d’un restaurant, et relier ses outils.',
  alternates: {canonical: '/developpeurs/documentation'},
};

const entries = [
  {href: '/developpeurs/prise-en-main', icon: KeyRound, title: 'Prise en main', text: 'Créer une clé, premier appel, tests et mise en production.'},
  {href: '/developpeurs/vente-commande', icon: ShoppingBag, title: 'Commande en ligne', text: 'Carte, panier, créneaux, commande, paiement et suivi.'},
  {href: '/developpeurs/webhooks', icon: Webhook, title: 'Webhooks', text: 'Être prévenu des commandes, paiements et réservations.'},
  {href: '/developpeurs/exemples-clients', icon: Braces, title: 'Clients prêts à l’emploi', text: 'TypeScript, Flutter, Swift et Kotlin.'},
];

export default function DevelopersHome() {
  const intro = (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2">
        {entries.map(({href, icon: Icon, title, text}) => (
          <Link key={href} href={href} className="group flex gap-3 rounded-2xl border border-slate-200 p-4 transition hover:border-brand-400 hover:shadow-sm">
            <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-700"><Icon className="size-5" aria-hidden="true" /></span>
            <span>
              <span className="flex items-center gap-1 font-semibold text-ink-900">{title}<ArrowRight className="size-4 opacity-0 transition group-hover:translate-x-0.5 group-hover:opacity-100" aria-hidden="true" /></span>
              <span className="mt-0.5 block text-sm text-ink-600">{text}</span>
            </span>
          </Link>
        ))}
      </div>
      <p className="text-sm text-ink-500">
        Description OpenAPI de la vente en ligne :{' '}
        <a href={`${site.apiUrl}/openapi/storefront.json`} className="break-all font-medium text-brand-700 underline underline-offset-2">{site.apiUrl}/openapi/storefront.json</a>
      </p>
    </div>
  );
  return <DocArticle doc={loadDoc('README.md')} intro={intro} next={{href: '/developpeurs/prise-en-main', label: 'Prise en main'}} />;
}
