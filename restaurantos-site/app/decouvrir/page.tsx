import type {Metadata} from 'next';
import {CallToAction, Highlight, Pill} from '@/components/section';
import {modules} from '@/content/features';
import {Tour, type Journey} from './tour';

export const metadata: Metadata = {
  title: 'Visite guidée',
  description: 'Suivez une commande, un client et une journée de gestion dans RestauSpace, écran par écran.',
  alternates: {canonical: '/decouvrir'},
};

const scripts: {id: string; title: string; intro: string; steps: [string, string][]}[] = [
  {
    id: 'service',
    title: 'Le service',
    intro: 'Une commande, de la prise jusqu’à l’encaissement.',
    steps: [
      ['vente-en-ligne', 'Le client commande sur votre site'],
      ['commandes', 'La commande arrive avec toutes les autres'],
      ['cuisine', 'La cuisine prépare, poste par poste'],
      ['salle', 'La salle suit l’état de chaque table'],
      ['caisse', 'Vous encaissez comme le client le souhaite'],
    ],
  },
  {
    id: 'clients',
    title: 'Vos clients',
    intro: 'De la réservation à la visite suivante.',
    steps: [
      ['reservations', 'Il réserve sa table en ligne'],
      ['clients', 'Vous le reconnaissez à son arrivée'],
      ['avis', 'Il donne son avis après le repas'],
      ['marketing', 'Une offre le fait revenir'],
    ],
  },
  {
    id: 'gestion',
    title: 'La gestion',
    intro: 'Ce qui se passe en coulisses, sans tableur.',
    steps: [
      ['menu', 'Votre carte, tenue à jour une seule fois'],
      ['stock', 'Le stock se déduit tout seul'],
      ['personnel', 'Chacun accède à ce dont il a besoin'],
      ['rapports', 'Les chiffres du jour, prêts pour le comptable'],
      ['etablissements', 'Plusieurs adresses, une seule vue'],
    ],
  },
];

export default function Discover() {
  const journeys: Journey[] = scripts.map(script => ({
    id: script.id,
    title: script.title,
    intro: script.intro,
    steps: script.steps.flatMap(([id, moment]) => {
      const module = modules.find(item => item.id === id);
      return module ? [{id, moment, title: module.title, icon: module.icon, summary: module.summary, points: module.points.slice(0, 3), src: module.visual.src, portrait: module.visual.src.includes('phone')}] : [];
    }),
  }));
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="pointer-events-none absolute -right-40 top-0 size-[32rem] rounded-full bg-brand-200/30 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto max-w-7xl px-4 pb-6 pt-12 sm:px-6 lg:pt-16">
          <div className="animate-rise"><Pill>Visite guidée</Pill></div>
          <h1 className="animate-rise mt-5 max-w-3xl text-4xl font-bold leading-[1.1] tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl">
            Découvrez RestauSpace <Highlight>en situation</Highlight>
          </h1>
          <p className="animate-rise mt-5 max-w-2xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">
            Choisissez un parcours : les écrans défilent tout seuls, ou cliquez sur une étape pour vous y arrêter. Ce sont les écrans réels de la console.
          </p>
        </div>
      </section>
      <section className="overflow-x-clip bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-20 pt-6 sm:px-6">
          <Tour journeys={journeys} />
        </div>
      </section>
      <CallToAction title="Envie de le voir avec votre propre menu ?" />
    </>
  );
}
