import type {Metadata} from 'next';
import Link from 'next/link';
import {ArrowRight, Calculator, CalendarCheck, CircleCheck, LayoutGrid, Tag} from 'lucide-react';
import {Accordion} from '@/components/accordion';
import {ButtonLink, Highlight, Pill} from '@/components/section';
import {Capture} from '@/components/visual';
import {modules} from '@/content/features';
import {faq} from '@/content/faq';
import {price, tiers, trialDays} from '@/content/plans';
import {Pricing} from './pricing';

export const metadata: Metadata = {
  title: 'Tarifs',
  description: 'Quatre plans en euros HT ou en francs CFA, tous les modules inclus, 14 jours d’essai.',
  alternates: {canonical: '/tarifs'},
};

function Tile({icon: Icon, value, label, className}: {icon: typeof Tag; value: string; label: string; className: string}) {
  return (
    <div className={`animate-float absolute z-10 flex items-center gap-3 rounded-2xl bg-white/95 px-4 py-3 shadow-xl shadow-brand-900/15 ring-1 ring-white backdrop-blur ${className}`}>
      <span className="grid size-10 place-items-center rounded-xl bg-brand-50 text-brand-600"><Icon className="size-5" aria-hidden="true" /></span>
      <span>
        <span className="block text-lg font-bold leading-tight text-ink-900">{value}</span>
        <span className="block text-xs text-ink-500">{label}</span>
      </span>
    </div>
  );
}

export default function Prices() {
  const questions = faq.find(group => group.title === 'Tarifs et abonnement')?.questions ?? [];
  const half = Math.ceil(questions.length / 2);
  const from = tiers('fr').find(tier => tier.monthly.price > 0);
  return (
    <>
      <section className="relative -mt-16 overflow-hidden bg-cover bg-center" style={{backgroundImage: 'url(/backgrounds/hero.webp)'}}>
        <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 pb-16 pt-28 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,0.9fr)] lg:pb-20 lg:pt-32">
          <div>
            <div className="animate-rise"><Pill>Tarifs</Pill></div>
            <h1 className="animate-rise mt-5 text-4xl font-bold leading-[1.08] tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl">
              Un prix clair, <span className="block"><Highlight>tous les modules inclus</Highlight></span>
            </h1>
            <p className="animate-rise mt-5 max-w-xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">
              Des plans simples, adaptés à chaque étape de votre activité. Ils ne diffèrent que par les volumes : établissements, équipe, commandes et messages.
            </p>
            <ul className="animate-rise mt-7 flex flex-wrap gap-x-6 gap-y-2 text-sm text-ink-700 [--rise-delay:240ms]">
              {[`${trialDays} jours d’essai gratuit`, 'Changement de plan à tout moment'].map(item => (
                <li key={item} className="flex items-center gap-2"><CircleCheck className="size-5 fill-brand-600 text-white" aria-hidden="true" />{item}</li>
              ))}
            </ul>
          </div>
          <div className="animate-rise relative mx-auto hidden w-full max-w-lg pb-8 pt-4 [--rise-delay:200ms] sm:block">
            <div className="overflow-hidden rounded-2xl bg-white p-2 shadow-2xl shadow-brand-900/20 ring-1 ring-slate-200/80 lg:[transform:perspective(1800px)_rotateY(-10deg)_rotateX(4deg)]">
              <Capture visual={{src: '/captures/rapports.webp', focus: '0% 0%'}} alt="Rapports de RestauSpace" sizes="(min-width: 1024px) 520px, 90vw" className="aspect-[16/11] rounded-xl" />
            </div>
            {from && <Tile icon={Tag} value={`${price(from.monthly.price, 'EUR')} HT`} label="par mois, à partir de" className="-left-6 top-10 [--float-delay:-1s]" />}
            <Tile icon={LayoutGrid} value={`${modules.length} modules`} label="inclus dans chaque plan" className="-right-4 top-1/3 [--float-delay:-3s]" />
            <Tile icon={CalendarCheck} value={`${trialDays} jours`} label="d’essai en service réel" className="bottom-0 left-10 [--float-delay:-4.5s]" />
          </div>
        </div>
      </section>

      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-16 pt-8 sm:px-6">
          <Pricing />
          <Link href="/simulateur" data-reveal className="group mt-8 flex flex-col items-start justify-between gap-4 rounded-2xl border border-slate-200 bg-white p-6 transition hover:border-brand-200 hover:shadow-lg hover:shadow-brand-900/5 sm:flex-row sm:items-center">
            <span className="flex items-center gap-4">
              <span className="grid size-12 shrink-0 place-items-center rounded-xl bg-brand-600 text-white shadow-lg shadow-brand-600/30"><Calculator className="size-6" aria-hidden="true" /></span>
              <span>
                <span className="block font-semibold text-ink-900">Pas sûr du plan qu’il vous faut ?</span>
                <span className="block text-sm text-ink-600">Indiquez votre activité : le simulateur vous conseille un plan et calcule son prix.</span>
              </span>
            </span>
            <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-brand-700">Ouvrir le simulateur <ArrowRight className="size-4 transition group-hover:translate-x-0.5" aria-hidden="true" /></span>
          </Link>
        </div>
      </section>

      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-20 sm:px-6">
          <div data-reveal className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <Pill>Questions fréquentes</Pill>
              <h2 className="mt-3 text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.2rem]">Tout ce que vous devez <Highlight>savoir</Highlight></h2>
              <p className="mt-2 text-ink-600">Les réponses aux questions les plus courantes sur nos tarifs et nos plans.</p>
            </div>
            <ButtonLink href="/faq" variant="outline" className="self-start !py-2.5 sm:self-auto">Voir toutes les questions</ButtonLink>
          </div>
          <div data-reveal className="mt-8 grid gap-3 md:grid-cols-2">
            <Accordion items={questions.slice(0, half)} />
            <Accordion items={questions.slice(half)} />
          </div>
        </div>
      </section>

      <section className="bg-white px-4 pb-20 sm:px-6">
        <div data-reveal className="relative mx-auto max-w-7xl overflow-hidden rounded-3xl bg-gradient-to-br from-ink-900 via-brand-700 to-violet-700 px-6 py-12 shadow-2xl shadow-brand-900/30 sm:px-12">
          <div className="pointer-events-none absolute -right-20 -top-24 size-96 rounded-full bg-violet-400/30 blur-3xl" aria-hidden="true" />
          <div className="pointer-events-none absolute -bottom-32 left-1/4 size-80 rounded-full bg-brand-400/20 blur-3xl" aria-hidden="true" />
          <div className="relative flex flex-col gap-8 md:flex-row md:items-center md:justify-between">
            <div>
              <span className="inline-flex rounded-full bg-white/10 px-3 py-1 text-xs font-semibold text-white ring-1 ring-white/20">Prêt à commencer ?</span>
              <h2 className="mt-4 max-w-xl text-3xl font-bold tracking-tight text-white sm:text-[2.2rem] sm:leading-tight">Choisissez le plan qui correspond à votre restaurant</h2>
              <p className="mt-3 max-w-xl text-brand-100">Testez gratuitement pendant {trialDays} jours, en service réel. Tous les modules sont inclus.</p>
            </div>
            <div className="flex shrink-0 flex-col gap-3 sm:w-64">
              <ButtonLink href="/contact" demo>Demander une démo</ButtonLink>
              <Link href="/fonctionnalites" className="btn-motion inline-flex items-center justify-center rounded-xl border border-white/30 px-5 py-3 text-sm font-semibold text-white hover:bg-white/10">Voir toutes les fonctionnalités</Link>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
