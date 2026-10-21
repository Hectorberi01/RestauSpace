import type {Metadata} from 'next';
import Link from 'next/link';
import {ArrowRight, Headset, HelpCircle, Mail, MessageSquareText, UtensilsCrossed} from 'lucide-react';
import {ButtonLink, Highlight, Pill} from '@/components/section';
import {faq} from '@/content/faq';
import {FaqBrowser, FaqSearch} from './faq-browser';

export const metadata: Metadata = {
  title: 'Questions fréquentes',
  description: 'Démarrage, tarifs, fonctionnalités, sécurité des données, équipe, intégrations et paiements : les réponses aux questions des restaurateurs.',
  alternates: {canonical: '/faq'},
};

function Bubble({question, className}: {question: string; className: string}) {
  return (
    <div className={`animate-float absolute w-60 rounded-2xl bg-white/90 p-4 shadow-xl shadow-brand-900/10 ring-1 ring-white backdrop-blur ${className}`}>
      <p className="text-sm font-semibold text-ink-900">{question}</p>
      <span className="mt-3 block h-2 w-40 rounded-full bg-slate-100" />
      <span className="mt-2 block h-2 w-28 rounded-full bg-slate-100" />
    </div>
  );
}

export default function Faq() {
  const structured = {
    '@context': 'https://schema.org',
    '@type': 'FAQPage',
    mainEntity: faq.flatMap(group => group.questions).map(item => ({'@type': 'Question', name: item.q, acceptedAnswer: {'@type': 'Answer', text: item.a}})),
  };
  return (
    <>
      <section className="relative -mt-16 overflow-hidden bg-cover bg-center" style={{backgroundImage: 'url(/backgrounds/hero.webp)'}}>
        <div className="mx-auto grid max-w-7xl items-center gap-10 px-4 pb-16 pt-28 sm:px-6 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)] lg:pb-20 lg:pt-32">
          <div>
            <div className="animate-rise"><Pill>FAQ</Pill></div>
            <h1 className="animate-rise mt-5 text-4xl font-bold tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl lg:text-[3.3rem]">
              Questions <Highlight>fréquentes</Highlight>
            </h1>
            <p className="animate-rise mt-5 max-w-xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">
              Tout ce que vous devez savoir sur RestauSpace. Vous ne trouvez pas votre réponse ? Écrivez-nous : nous répondons à chaque message.
            </p>
            <div className="animate-rise [--rise-delay:240ms]"><FaqSearch /></div>
          </div>
          <div className="animate-rise relative hidden h-80 [--rise-delay:200ms] lg:block" aria-hidden="true">
            <div className="absolute inset-x-8 inset-y-4 rounded-[3rem] bg-white/40 ring-1 ring-white/70 backdrop-blur-sm" />
            <span className="animate-float absolute left-4 top-8 grid size-16 place-items-center rounded-2xl bg-gradient-to-br from-brand-500 to-violet-600 text-white shadow-xl shadow-brand-600/30 [--float-delay:-2s]"><HelpCircle className="size-8" /></span>
            <Bubble question="Puis-je importer mon menu existant ?" className="left-24 top-2 -rotate-3 [--float-delay:-1s]" />
            <Bubble question="Puis-je changer de plan ou résilier ?" className="bottom-4 right-10 rotate-2 [--float-delay:-3.5s]" />
            <span className="animate-float absolute right-2 top-4 grid size-16 place-items-center rounded-2xl bg-white text-brand-600 shadow-xl shadow-brand-900/10 [--float-delay:-4s]"><UtensilsCrossed className="size-8" /></span>
          </div>
        </div>
      </section>

      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-20 pt-10 sm:px-6">
          <FaqBrowser groups={faq} />
        </div>
      </section>

      <section className="bg-white px-4 pb-20 sm:px-6">
        <div data-reveal className="relative mx-auto max-w-7xl overflow-hidden rounded-3xl bg-gradient-to-br from-brand-50 via-white to-violet-100 px-6 py-12 ring-1 ring-brand-100 sm:px-12">
          <div className="pointer-events-none absolute -right-16 -top-16 size-80 rounded-full bg-violet-200/50 blur-3xl" aria-hidden="true" />
          <div className="relative flex flex-col gap-10 md:flex-row md:items-center md:justify-between">
            <div>
              <Pill>Toujours une question ?</Pill>
              <h2 className="mt-4 text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.2rem]">Notre équipe est là pour <Highlight>vous aider</Highlight></h2>
              <p className="mt-2 text-ink-600">Écrivez-nous ou demandez une démonstration avec votre propre menu.</p>
              <div className="mt-6 flex flex-col gap-3 sm:flex-row">
                <Link href="/contact" className="btn-motion group inline-flex items-center justify-center gap-2 rounded-xl bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-brand-600/25 hover:bg-brand-700">
                  <Mail className="size-4" aria-hidden="true" /> Nous contacter <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
                </Link>
                <ButtonLink href="/contact" demo variant="outline">Demander une démo</ButtonLink>
              </div>
            </div>
            <div className="relative mx-auto hidden size-44 shrink-0 md:block" aria-hidden="true">
              <span className="absolute inset-0 rounded-full bg-gradient-to-br from-brand-500 to-violet-600 shadow-2xl shadow-brand-600/30" />
              <Headset className="absolute inset-0 m-auto size-20 text-white" />
              <span className="animate-float absolute -left-10 top-2 grid size-14 place-items-center rounded-2xl bg-white text-brand-600 shadow-xl shadow-brand-900/10"><MessageSquareText className="size-7" /></span>
            </div>
          </div>
        </div>
      </section>
      <script type="application/ld+json" dangerouslySetInnerHTML={{__html: JSON.stringify(structured).replace(/</g, '\\u003c')}} />
    </>
  );
}
