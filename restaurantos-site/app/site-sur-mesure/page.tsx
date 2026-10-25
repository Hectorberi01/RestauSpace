import type {Metadata} from 'next';
import Image from 'next/image';
import Link from 'next/link';
import {
  ArrowRight, CalendarCheck, CircleCheck, CreditCard, Globe, MessagesSquare, PenTool, RefreshCw, Rocket, Search, Server, Smartphone, Code2,
} from 'lucide-react';
import {Accordion} from '@/components/accordion';
import {ButtonLink, Highlight, Pill, Section} from '@/components/section';

export const metadata: Metadata = {
  title: 'Site sur mesure',
  description: 'Un site à l’image de votre restaurant, réalisé par l’équipe RestauSpace : carte toujours à jour, commande et paiement en ligne, réservation. Sur devis.',
  alternates: {canonical: '/site-sur-mesure'},
};

const quote = '/contact?sujet=site';

const included = [
  {icon: RefreshCw, title: 'Carte toujours à jour', text: 'Plats, prix, photos, ruptures et horaires viennent de votre console : rien à ressaisir sur le site.'},
  {icon: CreditCard, title: 'Commande et paiement', text: 'Sur place, à emporter ou en livraison, payé sur place ou en ligne par carte bancaire, selon vos réglages.'},
  {icon: CalendarCheck, title: 'Réservation de table', text: 'Vos clients choisissent l’heure et le nombre de couverts, avec acompte si vous le demandez.'},
  {icon: Smartphone, title: 'Pensé pour le téléphone', text: 'La plupart de vos clients commandent depuis leur téléphone : le site est conçu d’abord pour eux.'},
  {icon: Globe, title: 'Votre nom de domaine', text: 'Le site est à votre nom, sur votre adresse (par exemple www.votre-restaurant.fr).'},
  {icon: Server, title: 'Hébergement chez vous', text: 'Un site statique, simple à héberger : nous vous conseillons un hébergeur et faisons la mise en ligne.'},
  {icon: Search, title: 'Bases du référencement', text: 'Titres, description, mentions légales et page de confidentialité préparés pour votre restaurant.'},
  {icon: Code2, title: 'Branché sur RestauSpace', text: 'Commandes, paiements et réservations arrivent dans votre console, avec celles prises en salle.'},
];

const steps = [
  {icon: MessagesSquare, title: 'Échange', text: 'Nous parlons de votre restaurant, de votre clientèle et de ce que le site doit faire. Vous recevez un devis.'},
  {icon: PenTool, title: 'Maquette', text: 'Nous vous proposons une maquette à vos couleurs. Vous la validez ou demandez des ajustements.'},
  {icon: Code2, title: 'Réalisation', text: 'Le site est développé et branché sur votre carte RestauSpace. Vous le testez avant la mise en ligne.'},
  {icon: Rocket, title: 'Mise en ligne', text: 'Nom de domaine, hébergement, paiement : nous mettons le site en ligne et passons une commande d’essai avec vous.'},
];

const faq = [
  {q: 'Combien coûte un site sur mesure ?', a: 'Le prix dépend du nombre de pages, du design et des fonctions souhaitées. Vous recevez un devis détaillé après notre premier échange, sans engagement.'},
  {q: 'Combien de temps faut-il ?', a: 'Le délai est indiqué dans le devis. Il dépend surtout du design demandé et de la rapidité avec laquelle les photos et les textes sont prêts.'},
  {q: 'Qui héberge le site ?', a: 'Vous : le site est à votre nom, chez l’hébergeur de votre choix. C’est un site statique, simple et peu coûteux à héberger. Nous vous conseillons et faisons la mise en ligne.'},
  {q: 'Que faut-il côté RestauSpace ?', a: 'L’API RestauSpace, qui relie le site à votre console : incluse dans les plans Pro et Multi-sites, en option « API et webhooks » sur Essentiel.'},
  {q: 'Dois-je modifier le site quand ma carte change ?', a: 'Non. La carte, les prix, les photos, les ruptures et les horaires se changent dans la console et apparaissent aussitôt sur le site. Seuls les changements de design ou de structure passent par nous.'},
  {q: 'J’ai déjà un site : faut-il le refaire ?', a: 'Pas forcément. La commande en ligne peut être ajoutée à votre site actuel : par votre prestataire, à l’aide de notre documentation pour les développeurs, ou par notre équipe, sur devis.'},
];

export default function CustomSite() {
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="pointer-events-none absolute -right-40 top-0 size-[32rem] rounded-full bg-brand-200/30 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto grid max-w-7xl items-center gap-12 px-4 pb-14 pt-12 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:pt-16">
          <div>
            <div className="animate-rise"><Pill>Site sur mesure</Pill></div>
            <h1 className="animate-rise mt-5 text-4xl font-bold leading-[1.1] tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl">
              Un site à votre image, <Highlight>branché sur RestauSpace</Highlight>
            </h1>
            <p className="animate-rise mt-5 max-w-xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">
              Notre équipe réalise le site de votre restaurant : vos couleurs, vos photos, votre nom de domaine. La carte, les commandes, les paiements et les réservations passent par votre console, sans rien ressaisir.
            </p>
            <div className="animate-rise mt-7 flex flex-col gap-3 [--rise-delay:240ms] sm:flex-row">
              <ButtonLink href={quote}>Demander un devis</ButtonLink>
              <ButtonLink href="#exemple" variant="outline">Voir un exemple</ButtonLink>
            </div>
            <p className="animate-rise mt-4 text-sm text-ink-500 [--rise-delay:300ms]">Sur devis, sans engagement.</p>
          </div>
          <div className="animate-rise relative hidden [--rise-delay:200ms] sm:block">
            <div className="overflow-hidden rounded-2xl shadow-2xl shadow-brand-900/15 ring-1 ring-black/5">
              <div className="relative aspect-[5/3]">
                <Image src="/site-sur-mesure/exemple-maquis.webp" alt="Exemple de site de restaurant : accueil sur ordinateur et carte sur téléphone" fill priority sizes="(min-width: 1024px) 600px, 100vw" className="object-cover" />
              </div>
            </div>
          </div>
        </div>
      </section>

      <Section eyebrow="Ce qui est inclus" title={<>Tout ce qu’il faut pour <Highlight>vendre en ligne</Highlight></>} intro="Le site affiche vos données RestauSpace en direct : il reste juste, même quand votre carte change tous les jours.">
        <ul data-reveal-stagger className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {included.map(item => (
            <li key={item.title} className="rounded-2xl border border-slate-200 bg-white p-5">
              <span className="grid size-10 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-brand-100"><item.icon className="size-5" aria-hidden="true" /></span>
              <h3 className="mt-4 font-semibold text-ink-900">{item.title}</h3>
              <p className="mt-1.5 text-sm leading-relaxed text-ink-600">{item.text}</p>
            </li>
          ))}
        </ul>
      </Section>

      <Section tone="muted" eyebrow="Les étapes" title={<>De l’idée <Highlight>à la première commande</Highlight></>} intro="Vous suivez chaque étape et validez avant la mise en ligne.">
        <ol data-reveal-stagger className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {steps.map((step, index) => (
            <li key={step.title} className="relative rounded-2xl border border-slate-200 bg-white p-5">
              <div className="flex items-center gap-3">
                <span className="grid size-9 place-items-center rounded-full bg-brand-600 text-sm font-bold text-white">{index + 1}</span>
                <step.icon className="size-5 text-brand-600" aria-hidden="true" />
              </div>
              <h3 className="mt-4 font-semibold text-ink-900">{step.title}</h3>
              <p className="mt-1.5 text-sm leading-relaxed text-ink-600">{step.text}</p>
            </li>
          ))}
        </ol>
      </Section>

      <Section id="exemple" eyebrow="Exemple" title={<>Un exemple de <Highlight>réalisation possible</Highlight></>} intro="Maquette d’un restaurant fictif : accueil sur ordinateur, carte et panier sur téléphone. Votre site est conçu à votre image.">
        <figure>
          <div className="overflow-hidden rounded-3xl shadow-2xl shadow-brand-900/10 ring-1 ring-black/5">
            <div className="relative aspect-[5/3]">
              <Image src="/site-sur-mesure/exemple-maquis.webp" alt="Maquette d’un site de restaurant : plats mis en avant, réservation, horaires et carte sur téléphone" fill sizes="(min-width: 1280px) 1200px, 100vw" className="object-cover" />
            </div>
          </div>
          <figcaption className="mt-3 text-center text-sm text-ink-500">Maquette d’exemple : restaurant fictif.</figcaption>
        </figure>
      </Section>

      <Section tone="muted" eyebrow="Questions" title={<>Questions <Highlight>fréquentes</Highlight></>}>
        <div className="grid gap-8 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
          <Accordion items={faq} defaultOpen={0} />
          <div className="self-start rounded-2xl border border-brand-100 bg-white p-6 shadow-sm">
            <h3 className="font-semibold text-ink-900">Ce qu’il vous faut</h3>
            <ul className="mt-4 space-y-3">
              {['Un compte RestauSpace avec l’API (incluse dans Pro et Multi-sites, en option sur Essentiel).', 'Vos photos, votre logo et quelques textes de présentation.', 'Un nom de domaine (nous pouvons vous aider à le choisir).'].map(item => (
                <li key={item} className="flex gap-2.5 text-sm leading-relaxed text-ink-700"><CircleCheck className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />{item}</li>
              ))}
            </ul>
            <div className="mt-6 flex flex-col gap-2 border-t border-slate-100 pt-5 text-sm">
              <Link href="/tarifs" className="inline-flex items-center gap-1.5 font-semibold text-brand-700 hover:underline">Voir les plans <ArrowRight className="size-4" aria-hidden="true" /></Link>
              <Link href="/developpeurs/guide-site-restaurant" className="inline-flex items-center gap-1.5 font-semibold text-brand-700 hover:underline">Vous avez un développeur ? Le guide technique <ArrowRight className="size-4" aria-hidden="true" /></Link>
            </div>
          </div>
        </div>
      </Section>

      <section className="bg-white px-4 py-16 sm:px-6 sm:py-20">
        <div data-reveal className="relative mx-auto max-w-7xl overflow-hidden rounded-3xl bg-gradient-to-br from-ink-900 via-brand-700 to-violet-700 px-6 py-12 shadow-2xl shadow-brand-900/30 sm:px-12">
          <div className="pointer-events-none absolute -right-20 -top-24 size-96 rounded-full bg-violet-400/30 blur-3xl" aria-hidden="true" />
          <div className="relative flex flex-col gap-8 md:flex-row md:items-center md:justify-between">
            <div>
              <span className="inline-flex rounded-full bg-white/10 px-3 py-1 text-xs font-semibold text-white ring-1 ring-white/20">Site sur mesure</span>
              <h2 className="mt-4 max-w-xl text-3xl font-bold tracking-tight text-white sm:text-[2.2rem] sm:leading-tight">Parlons de votre site</h2>
              <p className="mt-3 max-w-xl text-brand-100">Dites-nous ce que vous voulez : nous revenons vers vous avec une proposition et un devis.</p>
            </div>
            <div className="flex shrink-0 flex-col gap-3 sm:w-64">
              <ButtonLink href={quote} variant="light">Demander un devis</ButtonLink>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
