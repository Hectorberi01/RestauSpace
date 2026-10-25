import type {Metadata} from 'next';
import {CallToAction, Highlight, Pill} from '@/components/section';
import {Simulator} from './simulator';

export const metadata: Metadata = {
  title: 'Simulateur de prix',
  description: 'Indiquez vos établissements, votre équipe, vos commandes et vos besoins (site, campagnes, API) : le simulateur vous conseille le plan RestauSpace adapté et son prix, options comprises.',
  alternates: {canonical: '/simulateur'},
};

export default function Simulation() {
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="pointer-events-none absolute -left-40 top-0 size-[30rem] rounded-full bg-sky-100/60 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto max-w-7xl px-4 pb-8 pt-12 sm:px-6 lg:pt-16">
          <div className="animate-rise"><Pill>Simulateur</Pill></div>
          <h1 className="animate-rise mt-5 max-w-3xl text-4xl font-bold leading-[1.1] tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl">
            Quel plan pour <Highlight>votre restaurant</Highlight> ?
          </h1>
          <p className="animate-rise mt-5 max-w-2xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">
            Réglez votre activité et cochez vos besoins : le plan conseillé et son prix, options comprises, se calculent en direct à partir de la grille tarifaire publiée.
          </p>
        </div>
      </section>
      <section className="overflow-x-clip bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-20 pt-4 sm:px-6">
          <Simulator />
        </div>
      </section>
      <CallToAction />
    </>
  );
}
