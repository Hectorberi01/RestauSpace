import type {Metadata} from 'next';
import Link from 'next/link';
import {ArrowRight, CircleCheck, Play} from 'lucide-react';
import {CountUp} from '@/components/count-up';
import {CallToAction, Highlight, Pill} from '@/components/section';
import {tiers} from '@/content/plans';
import {BrowserFrame, Capture, ModuleIcon, PhoneFrame} from '@/components/visual';
import {filters, modules, type Module} from '@/content/features';
import {ModuleFilter} from './module-filter';

export const metadata: Metadata = {
  title: 'Fonctionnalités',
  description: 'Commandes, cuisine, réservations, menu, salle, stock, personnel, clients, marketing, rapports, plusieurs établissements, encaissement, vente en ligne et API.',
  alternates: {canonical: '/fonctionnalites'},
};

function ModuleCard({module}: {module: Module}) {
  return (
    <article className="card-lift flex h-full flex-col rounded-2xl border border-slate-200 bg-white p-5 shadow-sm hover:border-brand-200 hover:shadow-lg hover:shadow-brand-900/5 sm:p-6">
      <div className="flex items-center gap-3">
        <ModuleIcon icon={module.icon} />
        <div className="min-w-0">
          <h2 className="text-xl font-bold leading-tight tracking-tight text-ink-900">{module.title}</h2>
          <span className="mt-1 inline-block rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-semibold text-brand-700 ring-1 ring-brand-100">{module.badge}</span>
        </div>
      </div>
      <p className="mt-3 text-sm leading-relaxed text-ink-600">{module.summary}</p>
      {module.plans && <p className="mt-2 text-xs font-medium text-brand-700">{module.plans}</p>}
      <ul className="mt-4 grid flex-1 content-start gap-x-6 gap-y-2 sm:grid-cols-2">
        {module.points.map(point => (
          <li key={point} className="flex gap-2.5 text-sm text-ink-800"><CircleCheck className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />{point}</li>
        ))}
      </ul>
      {module.link && (
        <Link href={module.link.href} className="mt-5 inline-flex items-center gap-1.5 text-sm font-semibold text-brand-700 hover:text-brand-600">
          {module.link.label} <ArrowRight className="size-4" aria-hidden="true" />
        </Link>
      )}
    </article>
  );
}

export default function Features() {
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 pb-16 pt-12 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:pt-16">
          <div>
            <Pill>Fonctionnalités</Pill>
            <h1 className="mt-5 text-4xl font-bold leading-tight tracking-tight text-ink-900 sm:text-5xl">
              Tout ce qu’il vous faut pour gérer <Highlight>votre restaurant</Highlight>
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-ink-600">
              Une solution complète pour simplifier votre quotidien, gagner du temps en service et offrir une meilleure
              expérience à vos clients. Certains modules, comme les campagnes ou l’API, sont inclus à partir du plan Pro ; le site de commande en ligne est une option sur tous les plans.
            </p>
            <Link href="/decouvrir" className="group mt-7 inline-flex items-center gap-2 text-sm font-semibold text-brand-700">
              <span className="grid size-9 place-items-center rounded-full bg-white text-brand-600 shadow-md shadow-brand-900/10 ring-1 ring-brand-100 transition group-hover:scale-110"><Play className="ml-0.5 size-4 fill-current" aria-hidden="true" /></span>
              Voir les modules en situation : la visite guidée
            </Link>
            <dl className="mt-8 grid max-w-md grid-cols-3 gap-4 border-t border-slate-200 pt-6">
              {[[`${modules.length}`, 'modules'], [`${tiers('fr').length}`, 'plans au choix'], ['1', 'mois d’essai']].map(([value, label]) => (
                <div key={label} className="flex flex-col-reverse">
                  <dt className="mt-0.5 text-sm text-ink-500">{label}</dt>
                  <dd className="text-3xl font-bold tracking-tight text-ink-900"><CountUp value={value} /></dd>
                </div>
              ))}
            </dl>
          </div>
          <div className="relative pb-10 pr-6">
            <BrowserFrame label="console.restauspace.app">
              <Capture visual={{src: '/captures/hero-dashboard.webp', focus: '0% 0%'}} alt="Tableau de bord de RestauSpace" sizes="(min-width: 1024px) 600px, 100vw" priority className="aspect-[16/10]" />
            </BrowserFrame>
            <PhoneFrame className="absolute -bottom-2 right-0 w-[26%] min-w-28 rotate-3">
              <Capture visual={{src: '/captures/hero-phone.webp', focus: '50% 0%'}} alt="Site de commande sur téléphone" sizes="180px" className="aspect-[9/19]" />
            </PhoneFrame>
          </div>
        </div>
      </section>
      <ModuleFilter filters={filters} cards={modules.map(module => ({id: module.id, filter: module.filter, node: <ModuleCard module={module} />}))} />
      <CallToAction title="Prêt à transformer la gestion de votre restaurant ?" />
    </>
  );
}
