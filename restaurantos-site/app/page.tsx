import Link from 'next/link';
import {site} from '@/content/site';
import Image from 'next/image';
import type {CSSProperties} from 'react';
import {ArrowRight, BarChart3, CalendarDays, Check, ChefHat, CircleCheck, Headset, LayoutGrid, Play, Rocket, ShieldCheck, Star} from 'lucide-react';
import {CountUp} from '@/components/count-up';
import {ButtonLink, CallToAction, Highlight, Pill, Section} from '@/components/section';
import {HeroDevices, ModuleIcon} from '@/components/visual';
import {homeModules, modules} from '@/content/features';
import {stats, testimonials} from '@/content/proof';
import {price, quota, tiers, trial} from '@/content/plans';

const highlights = [
  {icon: LayoutGrid, title: `${modules.length} modules`, count: true, text: 'Tous inclus, dans chaque plan.'},
  {icon: Rocket, title: `${trial} d’essai`, count: true, text: 'En service réel, avant de payer.'},
  {icon: ShieldCheck, title: 'Données protégées', count: false, text: 'Chaque restaurant isolé, droits par employé.'},
  {icon: Headset, title: 'Accompagnement', count: false, text: 'Une équipe qui vous répond, du démarrage au quotidien.'},
];

const steps = [
  {title: 'Demandez une démo', text: 'Nous découvrons votre façon de travailler et vous montrons RestauSpace.'},
  {title: 'Configurez votre restaurant', text: 'Menu, tables, équipe et moyens de paiement, avec notre aide.'},
  {title: 'Recevez vos commandes', text: `Vous travaillez en conditions réelles pendant ${trial} avant de payer.`},
];

function Proof() {
  if (stats.length === 0 && testimonials.length === 0) return null;
  const quote = testimonials[0];
  return (
    <Section tone="muted" title="Ils nous font confiance" intro="Des restaurants de toutes tailles utilisent RestauSpace au quotidien.">
      <div className="grid items-center gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
        {stats.length > 0 && (
          <dl className="grid grid-cols-2 gap-6 sm:grid-cols-4 lg:grid-cols-2 xl:grid-cols-4">
            {stats.map(stat => (
              <div key={stat.label}>
                <dt className="text-sm text-ink-500">{stat.label}</dt>
                <dd className="mt-1 text-2xl font-bold text-ink-900"><CountUp value={stat.value} /></dd>
              </div>
            ))}
          </dl>
        )}
        {quote && (
          <figure className="flex gap-5 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            {quote.photo && <Image src={`/photos/${quote.photo}`} alt="" width={160} height={160} className="hidden size-28 shrink-0 rounded-xl object-cover sm:block" />}
            <div>
              <blockquote className="text-ink-700">« {quote.quote} »</blockquote>
              <figcaption className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                <span className="font-semibold text-ink-900">{quote.name}</span>
                <span className="text-ink-500">{quote.role} · {quote.place}</span>
                {quote.rating && (
                  <span className="flex gap-0.5" aria-label={`${quote.rating} sur 5`}>
                    {[1, 2, 3, 4, 5].map(i => <Star key={i} className={`size-4 ${i <= quote.rating! ? 'fill-amber-400 text-amber-400' : 'text-slate-200'}`} aria-hidden="true" />)}
                  </span>
                )}
              </figcaption>
            </div>
          </figure>
        )}
      </div>
    </Section>
  );
}

export default function Home() {
  const plans = tiers('fr').filter(tier => tier.monthly.price > 0);
  return (
    <>
      <section className="relative -mt-16 overflow-hidden bg-cover bg-center" style={{backgroundImage: 'url(/backgrounds/hero.webp)'}}>
        <div className="relative mx-auto grid max-w-7xl items-center gap-14 px-4 pb-32 pt-28 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] lg:pb-36 lg:pt-32">
          <div>
            <div className="animate-rise">
              <p className="inline-flex items-center gap-2 rounded-full bg-white/70 px-3 py-1 text-xs font-semibold text-brand-700 shadow-sm ring-1 ring-brand-200/70 backdrop-blur">
                <span className="size-1.5 rounded-full bg-brand-600" aria-hidden="true" />Tout pour votre restaurant
              </p>
            </div>
            <h1 style={{'--rise-delay': '80ms'} as CSSProperties} className="animate-rise mt-6 text-4xl font-bold leading-[1.06] tracking-tight text-ink-900 sm:text-5xl lg:text-[2.8rem] xl:text-[3.3rem]">
              Gérez votre restaurant <span className="block"><Highlight>plus simplement</Highlight></span>
            </h1>
            <p style={{'--rise-delay': '160ms'} as CSSProperties} className="animate-rise mt-6 max-w-xl text-lg leading-relaxed text-ink-600">
              RestauSpace réunit vos commandes, votre carte, vos réservations, la cuisine, les stocks et votre équipe dans un seul logiciel.
              Concentrez-vous sur ce qui compte vraiment : vos clients.
            </p>
            <div style={{'--rise-delay': '240ms'} as CSSProperties} className="animate-rise mt-8 flex flex-col gap-3 sm:flex-row">
              <ButtonLink href="/contact" demo className="px-7 py-3.5">Demander une démo</ButtonLink>
              <Link href="/decouvrir" className="btn-motion group inline-flex items-center justify-center gap-2.5 rounded-xl border border-slate-200 bg-white/90 px-6 py-3.5 text-sm font-semibold text-ink-900 shadow-sm backdrop-blur hover:border-brand-200 hover:bg-white">
                <span className="grid size-6 place-items-center rounded-full bg-brand-600 text-white transition group-hover:scale-110"><Play className="ml-0.5 size-3 fill-current" aria-hidden="true" /></span>
                Voir la visite guidée
              </Link>
            </div>
            <ul style={{'--rise-delay': '320ms'} as CSSProperties} className="animate-rise mt-8 flex flex-wrap gap-x-6 gap-y-2 text-sm text-ink-700">
              {[`${trial} d’essai`, 'Rien à installer', 'Tous les modules inclus'].map(item => (
                <li key={item} className="flex items-center gap-2"><CircleCheck className="size-5 fill-brand-600 text-white" aria-hidden="true" />{item}</li>
              ))}
            </ul>
          </div>
          <div style={{'--rise-delay': '200ms'} as CSSProperties} className="animate-rise">
            <HeroDevices tiles={[
              <BarChart3 key="chart" className="size-7 text-brand-600" />,
              <CalendarDays key="calendar" className="size-7 text-brand-600" />,
              <ChefHat key="chef" className="size-7 text-orange-500" />,
            ]} />
          </div>
        </div>
      </section>

      <section className="relative z-10 -mt-20 px-4 sm:px-6">
        <ul data-reveal-stagger className="mx-auto grid max-w-7xl gap-6 rounded-3xl bg-white/90 p-6 shadow-2xl shadow-brand-900/10 ring-1 ring-slate-200/70 backdrop-blur sm:grid-cols-2 sm:p-8 lg:grid-cols-4 lg:divide-x lg:divide-slate-200/70 lg:gap-0">
          {highlights.map(item => (
            <li key={item.title} className="flex items-start gap-4 lg:px-6 lg:first:pl-0 lg:last:pr-0">
              <span className="grid size-14 shrink-0 place-items-center rounded-2xl bg-brand-50 text-brand-600 ring-1 ring-brand-100">
                <item.icon className="size-6" aria-hidden="true" />
              </span>
              <div>
                <p className="text-xl font-bold tracking-tight text-ink-900">{item.count ? <CountUp value={item.title} /> : item.title}</p>
                <p className="mt-0.5 text-sm leading-snug text-ink-500">{item.text}</p>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 sm:py-20">
          <div data-reveal className="grid gap-6 lg:grid-cols-2 lg:items-end">
            <div>
              <Pill>Fonctionnalités principales</Pill>
              <h2 className="mt-4 text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.4rem] sm:leading-tight">
                Tout ce dont vous avez besoin <span className="block"><Highlight>dans un seul logiciel</Highlight></span>
              </h2>
            </div>
            <p className="text-lg leading-relaxed text-ink-600 lg:max-w-lg lg:justify-self-end">
              Une solution complète et simple à utiliser, adaptée à tous les types de restaurants : sur place, à emporter ou en livraison.
            </p>
          </div>
          <ul data-reveal-stagger className="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {homeModules.map(module => (
              <li key={module.id}>
                <Link
                  href={module.link?.href ?? `/fonctionnalites#${module.id}`}
                  className="card-lift group flex h-full items-start gap-4 rounded-2xl border border-slate-200 bg-white p-5 hover:border-brand-200 hover:shadow-xl hover:shadow-brand-900/5"
                >
                  <ModuleIcon icon={module.icon} size="size-14" />
                  <span className="min-w-0">
                    <span className="block font-semibold text-ink-900 group-hover:text-brand-700">{module.title}</span>
                    <span className="mt-1 block text-sm leading-relaxed text-ink-500">{module.short}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
          <div className="mt-10 text-center">
            <ButtonLink href="/fonctionnalites" variant="outline">Voir les {modules.length} modules</ButtonLink>
          </div>
        </div>
      </section>

      <Section tone="muted" eyebrow="Comment ça marche ?" title="Démarrez en 3 étapes simples">
        <ol data-reveal-stagger className="grid gap-8 md:grid-cols-3">
          {steps.map((step, index) => (
            <li key={step.title} className="relative flex gap-4">
              <span className="grid size-12 shrink-0 place-items-center rounded-full bg-brand-600 text-lg font-bold text-white shadow-lg shadow-brand-600/30">{index + 1}</span>
              <div>
                <h3 className="font-semibold text-ink-900">{step.title}</h3>
                <p className="mt-1 text-sm leading-relaxed text-ink-500">{step.text}</p>
              </div>
              {index < steps.length - 1 && <ArrowRight className="absolute -right-6 top-3 hidden size-6 text-brand-300 md:block" aria-hidden="true" />}
            </li>
          ))}
        </ol>
      </Section>

      <Proof />

      <Section
        title="Des tarifs adaptés à vos besoins"
        intro="Choisissez le plan qui correspond à votre activité. Tous les modules sont inclus dans chaque plan, avec les mises à jour."
        action={{href: '/tarifs', label: 'Voir les tarifs'}}
      >
        <div data-reveal-stagger className="grid gap-6 md:grid-cols-3">
          {plans.map(tier => (
            <article
              key={tier.name}
              className={`card-lift relative flex flex-col rounded-2xl bg-white p-6 hover:shadow-xl hover:shadow-brand-900/10 ${tier.highlighted ? 'border-2 border-brand-600 shadow-xl shadow-brand-900/10' : 'border border-slate-200'}`}
            >
              {tier.highlighted && <span className="absolute -top-3 left-1/2 -translate-x-1/2 rounded-full bg-brand-600 px-3 py-1 text-xs font-semibold text-white">Le plus populaire</span>}
              <h3 className="text-lg font-semibold text-ink-900">{tier.name}</h3>
              <p className="text-sm text-ink-500">{tier.audience}</p>
              <p className="mt-5">
                <span className="text-3xl font-bold tracking-tight text-ink-900">{price(tier.monthly.price, 'EUR')}</span>
                <span className="text-ink-500"> / mois HT</span>
              </p>
              <ul className="mt-5 flex-1 space-y-2.5 text-sm text-ink-700">
                <li className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />Établissements : {quota(tier.monthly, 'locations.count')}</li>
                <li className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />Membres de l’équipe : {quota(tier.monthly, 'staff.count')}</li>
                <li className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />Commandes par mois : {quota(tier.monthly, 'orders.monthly')}</li>
                <li className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />Tous les modules inclus</li>
              </ul>
              <Link
                href={`${site.signupUrl}?plan=${encodeURIComponent(tier.name)}&pays=fr`}
                className={`btn-motion mt-6 rounded-xl px-4 py-2.5 text-center text-sm font-semibold ${tier.highlighted ? 'bg-brand-600 text-white hover:bg-brand-700' : 'border border-brand-200 text-brand-700 hover:bg-brand-50'}`}
              >
                Commencer
              </Link>
            </article>
          ))}
        </div>
        <p className="mt-6 text-sm text-ink-500"><Link href="/simulateur" className="font-semibold text-brand-700 hover:underline">Simulez votre prix</Link> selon votre activité. Prix en euros hors taxes. Les tarifs en francs CFA pour le Bénin et la zone UEMOA sont sur la page <Link href="/tarifs?pays=bj" className="font-semibold text-brand-700 hover:underline">Tarifs</Link>.</p>
      </Section>

      <CallToAction />
    </>
  );
}
