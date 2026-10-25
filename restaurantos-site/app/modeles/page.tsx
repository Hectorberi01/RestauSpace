import type {Metadata} from 'next';
import Image from 'next/image';
import Link from 'next/link';
import {ArrowRight, CircleCheck, Hourglass, LayoutTemplate, Plug, RefreshCw, Server} from 'lucide-react';
import {ButtonLink, Highlight, Pill, Section} from '@/components/section';
import {BrowserFrame} from '@/components/visual';
import {requirements, templates, type SiteTemplate} from '@/content/templates';

export const metadata: Metadata = {
  title: 'Modèles de site',
  description: 'Des modèles de site de commande pour votre restaurant, branchés sur l’API RestauSpace : essayez la démo, la carte et les commandes viennent de la console.',
  alternates: {canonical: '/modeles'},
};

const principles = [
  {icon: Server, title: 'Le site reste chez vous', text: 'Vous l’hébergez où vous voulez, sous votre nom de domaine. RestauSpace n’héberge que les démos.'},
  {icon: Plug, title: 'Il se branche sur l’API', text: 'Une clé publique suffit : carte, commandes, paiements et réservations passent par RestauSpace.'},
  {icon: RefreshCw, title: 'La carte se met à jour seule', text: 'Un prix change, un plat est épuisé : le site l’affiche aussitôt, sans rien redéployer.'},
];

function TemplateCard({template}: {template: SiteTemplate}) {
  const available = template.status === 'disponible' && template.demoUrl;
  return (
    <article className={`flex h-full flex-col overflow-hidden rounded-2xl border bg-white ${available ? 'card-lift border-slate-200 shadow-sm hover:border-brand-200 hover:shadow-xl hover:shadow-brand-900/5' : 'border-dashed border-slate-300'}`}>
      <div className="relative aspect-[16/10] overflow-hidden border-b border-slate-100">
        {template.preview ? (
          <Image src={template.preview} alt={`Aperçu du modèle ${template.name}`} fill sizes="(min-width: 1024px) 400px, (min-width: 640px) 50vw, 100vw" className="object-cover object-left-top" />
        ) : (
          <div className="grid size-full place-items-center bg-gradient-to-br from-slate-50 via-white to-brand-50/60">
            <span className="grid size-14 place-items-center rounded-2xl bg-white text-ink-500 shadow-md shadow-brand-900/5 ring-1 ring-slate-200"><LayoutTemplate className="size-6" aria-hidden="true" /></span>
          </div>
        )}
        <span className={`absolute left-3 top-3 rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ${available ? 'bg-white/95 text-brand-700 ring-brand-100' : 'bg-white text-ink-600 ring-slate-200'}`}>{template.style}</span>
      </div>
      <div className="flex flex-1 flex-col p-5 sm:p-6">
        <div className="flex items-center justify-between gap-3">
          <h2 className="text-xl font-bold tracking-tight text-ink-900">{template.name}</h2>
          {!available && <span className="inline-flex items-center gap-1.5 rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-semibold text-ink-600"><Hourglass className="size-3.5" aria-hidden="true" />Bientôt</span>}
        </div>
        <p className="mt-2 text-sm leading-relaxed text-ink-600">{template.tagline}</p>
        {available ? (
          <div className="mt-auto pt-5">
            <Link href={`/modeles/${template.slug}`} className="btn-motion group inline-flex items-center justify-center gap-2 rounded-xl bg-brand-600 px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-brand-600/25 hover:bg-brand-700">
              Voir la démo <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
            </Link>
          </div>
        ) : (
          <p className="mt-auto pt-5 text-sm text-ink-500">En préparation : pas encore de démo.</p>
        )}
      </div>
    </article>
  );
}

export default function Templates() {
  const first = templates.find(item => item.status === 'disponible');
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="pointer-events-none absolute -right-40 top-0 size-[32rem] rounded-full bg-brand-200/30 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto grid max-w-7xl items-center gap-12 px-4 pb-14 pt-12 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:pt-16">
          <div>
            <div className="animate-rise"><Pill>Modèles de site</Pill></div>
            <h1 className="animate-rise mt-5 text-4xl font-bold leading-[1.1] tracking-tight text-ink-900 [--rise-delay:80ms] sm:text-5xl">
              Un site de commande <Highlight>prêt à brancher</Highlight>
            </h1>
            <p className="animate-rise mt-5 max-w-xl text-lg leading-relaxed text-ink-600 [--rise-delay:160ms]">
              Choisissez un modèle, essayez sa démo comme un client, puis mettez-le en ligne chez vous : il se branche sur RestauSpace et affiche votre carte, vos horaires et vos avis.
            </p>
            <div className="animate-rise mt-7 flex flex-col gap-3 [--rise-delay:240ms] sm:flex-row">
              {first && <ButtonLink href={`/modeles/${first.slug}`}>Essayer la démo {first.name}</ButtonLink>}
              <ButtonLink href="/contact?sujet=site" variant="outline">Site sur mesure</ButtonLink>
            </div>
          </div>
          {first?.preview && (
            <div className="animate-rise relative hidden [--rise-delay:200ms] sm:block">
              <BrowserFrame label={first.demoUrl?.replace(/^https?:\/\//, '')}>
                <div className="relative aspect-[16/10]">
                  <Image src={first.preview} alt={`Aperçu du modèle ${first.name} : accueil sur ordinateur et carte sur téléphone`} fill priority sizes="(min-width: 1024px) 600px, 100vw" className="object-cover object-left-top" />
                </div>
              </BrowserFrame>
            </div>
          )}
        </div>
      </section>

      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 pb-16 pt-4 sm:px-6 sm:pb-20">
          <ul data-reveal-stagger className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {templates.map(template => <li key={template.slug}><TemplateCard template={template} /></li>)}
          </ul>
        </div>
      </section>

      <Section tone="muted" eyebrow="Le principe" title={<>Votre site, <Highlight>nos données à jour</Highlight></>} intro="Le modèle est un site statique : il ne contient ni votre carte ni vos prix, il les lit dans RestauSpace.">
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
          <ul data-reveal-stagger className="grid gap-4 sm:grid-cols-3 lg:grid-cols-1 xl:grid-cols-3">
            {principles.map(item => (
              <li key={item.title} className="rounded-2xl border border-slate-200 bg-white p-5">
                <span className="grid size-10 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-brand-100"><item.icon className="size-5" aria-hidden="true" /></span>
                <h3 className="mt-4 font-semibold text-ink-900">{item.title}</h3>
                <p className="mt-1.5 text-sm leading-relaxed text-ink-600">{item.text}</p>
              </li>
            ))}
          </ul>
          <div className="rounded-2xl border border-brand-100 bg-white p-6 shadow-sm">
            <h3 className="font-semibold text-ink-900">Ce qu’il vous faut</h3>
            <ul className="mt-4 space-y-3">
              {requirements.map(item => (
                <li key={item} className="flex gap-2.5 text-sm leading-relaxed text-ink-700"><CircleCheck className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />{item}</li>
              ))}
            </ul>
            <div className="mt-6 flex flex-col gap-2 border-t border-slate-100 pt-5 text-sm">
              <Link href="/tarifs" className="inline-flex items-center gap-1.5 font-semibold text-brand-700 hover:underline">Voir les plans <ArrowRight className="size-4" aria-hidden="true" /></Link>
              <Link href="/developpeurs/guide-site-restaurant" className="inline-flex items-center gap-1.5 font-semibold text-brand-700 hover:underline">Guide technique du site <ArrowRight className="size-4" aria-hidden="true" /></Link>
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
              <h2 className="mt-4 max-w-xl text-3xl font-bold tracking-tight text-white sm:text-[2.2rem] sm:leading-tight">Aucun modèle ne vous ressemble{'\u00a0'}?</h2>
              <p className="mt-3 max-w-xl text-brand-100">Notre équipe réalise un site à votre image, branché sur RestauSpace, sur devis.</p>
            </div>
            <div className="flex shrink-0 flex-col gap-3 sm:w-64">
              <ButtonLink href="/contact?sujet=site" variant="light">Demander un devis</ButtonLink>
              <Link href="/faq" className="btn-motion inline-flex items-center justify-center rounded-xl border border-white/30 px-5 py-3 text-sm font-semibold text-white hover:bg-white/10">Questions fréquentes</Link>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
