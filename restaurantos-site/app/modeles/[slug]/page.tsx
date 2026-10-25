import type {Metadata} from 'next';
import Link from 'next/link';
import {notFound} from 'next/navigation';
import {ArrowLeft, CircleCheck, RefreshCw} from 'lucide-react';
import {ButtonLink, Highlight, Pill} from '@/components/section';
import {demoSteps, liveUpdates, requirements, templateBySlug, templates} from '@/content/templates';
import {DemoViewer} from './demo-viewer';

type Props = {params: Promise<{slug: string}>};

export const dynamicParams = false;

export function generateStaticParams() {
  return templates.filter(item => item.status === 'disponible' && item.demoUrl).map(item => ({slug: item.slug}));
}

export async function generateMetadata({params}: Props): Promise<Metadata> {
  const template = templateBySlug((await params).slug);
  if (!template) return {};
  return {title: `Modèle ${template.name}`, description: `${template.tagline} Essayez la démo du modèle ${template.name}, branché sur l’API RestauSpace.`, alternates: {canonical: `/modeles/${template.slug}`}};
}

function Checklist({title, items, icon: Icon = CircleCheck}: {title: string; items: string[]; icon?: typeof CircleCheck}) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-6">
      <h2 className="font-semibold text-ink-900">{title}</h2>
      <ul className="mt-4 space-y-2.5">
        {items.map(item => (
          <li key={item} className="flex gap-2.5 text-sm leading-relaxed text-ink-700"><Icon className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />{item}</li>
        ))}
      </ul>
    </div>
  );
}

export default async function TemplatePage({params}: Props) {
  const template = templateBySlug((await params).slug);
  if (!template?.demoUrl) notFound();
  const want = `/contact?sujet=modele&modele=${template.slug}`;
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="pointer-events-none absolute -right-40 top-0 size-[32rem] rounded-full bg-brand-200/30 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto flex max-w-7xl flex-col gap-6 px-4 pb-6 pt-10 sm:px-6 lg:flex-row lg:items-end lg:justify-between lg:pt-14">
          <div>
            <Link href="/modeles" className="inline-flex items-center gap-1.5 text-sm font-semibold text-ink-600 hover:text-brand-700">
              <ArrowLeft className="size-4" aria-hidden="true" /> Tous les modèles
            </Link>
            <div className="animate-rise mt-5"><Pill>Modèle {template.style.toLowerCase()}</Pill></div>
            <h1 className="animate-rise mt-4 text-4xl font-bold leading-[1.1] tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl">
              Modèle <Highlight>{template.name}</Highlight>
            </h1>
            <p className="animate-rise mt-4 max-w-2xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">{template.description}</p>
          </div>
          <div className="animate-rise shrink-0 [--rise-delay:240ms]">
            <ButtonLink href={want}>Je veux ce modèle</ButtonLink>
          </div>
        </div>
      </section>

      <section className="overflow-x-clip bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-16 pt-6 sm:px-6">
          <DemoViewer name={template.name} demoUrl={template.demoUrl} steps={demoSteps} />
        </div>
      </section>

      <section className="bg-slate-50/80">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 sm:py-20">
          <div data-reveal-stagger className="grid gap-6 lg:grid-cols-3">
            <Checklist title="Ce que contient le modèle" items={template.features} />
            <Checklist title="Ce qui se met à jour tout seul" items={liveUpdates} icon={RefreshCw} />
            <Checklist title="Prérequis" items={requirements} />
          </div>
        </div>
      </section>

      <section className="bg-white px-4 py-16 sm:px-6 sm:py-20">
        <div data-reveal className="relative mx-auto max-w-7xl overflow-hidden rounded-3xl bg-gradient-to-br from-ink-900 via-brand-700 to-violet-700 px-6 py-12 shadow-2xl shadow-brand-900/30 sm:px-12">
          <div className="pointer-events-none absolute -right-20 -top-24 size-96 rounded-full bg-violet-400/30 blur-3xl" aria-hidden="true" />
          <div className="relative flex flex-col gap-8 md:flex-row md:items-center md:justify-between">
            <div>
              <span className="inline-flex rounded-full bg-white/10 px-3 py-1 text-xs font-semibold text-white ring-1 ring-white/20">Modèle {template.name}</span>
              <h2 className="mt-4 max-w-xl text-3xl font-bold tracking-tight text-white sm:text-[2.2rem] sm:leading-tight">Ce modèle vous plaît{'\u00a0'}?</h2>
              <p className="mt-3 max-w-xl text-brand-100">La vente en ligne des modèles ouvre bientôt. Laissez-nous vos coordonnées : nous revenons vers vous pour la mise en place.</p>
            </div>
            <div className="flex shrink-0 flex-col gap-3 sm:w-64">
              <ButtonLink href={want} variant="light">Je veux ce modèle</ButtonLink>
              <Link href="/contact?sujet=site" className="btn-motion inline-flex items-center justify-center rounded-xl border border-white/30 px-5 py-3 text-sm font-semibold text-white hover:bg-white/10">Site sur mesure</Link>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
