import type {Metadata} from 'next';
import Link from 'next/link';
import {Suspense} from 'react';
import {ArrowRight, CalendarCheck, CircleHelp, Code2, Mail, MapPin, MessageCircle, Phone, type LucideIcon} from 'lucide-react';
import {Accordion} from '@/components/accordion';
import {ButtonLink, CallToAction, Highlight, Pill} from '@/components/section';
import {faq} from '@/content/faq';
import {markets, trial} from '@/content/plans';
import {site} from '@/content/site';
import {FormSkeleton} from '@/components/skeleton';
import {ContactForm} from './contact-form';

export const metadata: Metadata = {
  title: 'Contact et démonstration',
  description: 'Une question sur RestauSpace ? Écrivez-nous ou demandez une démonstration pour votre restaurant, au Bénin, en zone UEMOA ou en France.',
  alternates: {canonical: '/contact'},
};

interface Channel {
  icon: LucideIcon;
  tone: string;
  title: string;
  text: string;
  action: string;
  href: string;
  external?: boolean;
}

function channels(): Channel[] {
  const {email, phoneBenin, phoneFrance, whatsapp} = site.contact;
  const tel = (phone: string) => `tel:${phone.replace(/\s/g, '')}`;
  const list: (Channel | false)[] = [
    !!email && {icon: Mail, tone: 'bg-violet-50 text-violet-600 ring-violet-100', title: 'Par email', text: 'Pour toute question', action: email, href: `mailto:${email}`},
    !!phoneBenin && {icon: Phone, tone: 'bg-emerald-50 text-emerald-600 ring-emerald-100', title: 'Par téléphone', text: 'Bénin et zone UEMOA', action: phoneBenin, href: tel(phoneBenin)},
    !!phoneFrance && {icon: Phone, tone: 'bg-emerald-50 text-emerald-600 ring-emerald-100', title: 'Par téléphone', text: 'France', action: phoneFrance, href: tel(phoneFrance)},
    !!whatsapp && {icon: MessageCircle, tone: 'bg-sky-50 text-sky-600 ring-sky-100', title: 'Sur WhatsApp', text: 'Écrivez-nous directement', action: 'Ouvrir WhatsApp', href: `https://wa.me/${whatsapp.replace(/\D/g, '')}`, external: true},
    {icon: CalendarCheck, tone: 'bg-orange-50 text-orange-600 ring-orange-100', title: 'Demander une démo', text: `Puis ${trial} d’essai`, action: 'Remplir le formulaire', href: '#formulaire'},
    {icon: CircleHelp, tone: 'bg-sky-50 text-sky-600 ring-sky-100', title: 'Questions fréquentes', text: 'Les réponses courantes', action: 'Voir la FAQ', href: '/faq'},
    {icon: Code2, tone: 'bg-pink-50 text-pink-600 ring-pink-100', title: 'Intégration et API', text: 'Pour vos développeurs', action: 'Espace développeurs', href: '/developpeurs'},
  ];
  return list.filter((item): item is Channel => !!item).slice(0, 4);
}

const steps = [
  'Nous vous recontactons pour répondre à votre question ou fixer la démonstration.',
  'Nous créons votre compte et configurons votre restaurant avec vous.',
  `Vous testez pendant ${trial}, puis vous choisissez votre plan.`,
];

const featured = ['Comment commencer ?', 'Faut-il installer un logiciel ?', 'Puis-je importer mon menu existant ?', 'Quelle différence entre les plans ?', 'Dans quelle devise suis-je facturé ?'];

export default function Contact() {
  const questions = faq.flatMap(group => group.questions).filter(item => featured.includes(item.q));
  const address = site.legal.address;
  const list = channels();
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/30 to-white">
        <div className="pointer-events-none absolute -right-32 -top-20 size-[34rem] rounded-full bg-brand-200/40 blur-3xl" aria-hidden="true" />
        <div className="pointer-events-none absolute -left-40 bottom-0 size-96 rounded-full bg-sky-100/60 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto grid max-w-7xl items-start gap-12 px-4 pb-16 pt-12 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,30rem)] lg:pb-20 lg:pt-16">
          <div>
            <Pill>Contact</Pill>
            <h1 className="mt-5 text-4xl font-bold leading-[1.08] tracking-tight text-ink-900 sm:text-5xl lg:text-[3.1rem]">
              Une question ?<br /><Highlight>Notre équipe est là pour vous.</Highlight>
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-ink-600">
              Que vous ayez une question sur nos fonctionnalités, nos tarifs, une intégration ou que vous souhaitiez une démonstration, nous sommes à votre écoute.
            </p>
            <ul className={`mt-10 grid gap-3 sm:grid-cols-2 ${list.length === 4 ? 'xl:grid-cols-4' : list.length === 3 ? 'xl:grid-cols-3' : ''}`}>
              {list.map(item => (
                <li key={`${item.title}-${item.text}`}>
                  <a
                    href={item.href}
                    {...(item.external ? {target: '_blank', rel: 'noopener noreferrer'} : {})}
                    className="card-lift group flex h-full flex-col rounded-2xl border border-slate-200/80 bg-white/90 p-4 shadow-sm backdrop-blur hover:border-brand-200 hover:shadow-lg hover:shadow-brand-900/5"
                  >
                    <span className={`grid size-10 place-items-center rounded-xl ring-1 ${item.tone}`}><item.icon className="size-5" aria-hidden="true" /></span>
                    <span className="mt-4 font-semibold text-ink-900">{item.title}</span>
                    <span className="mt-0.5 text-[0.8rem] text-ink-500">{item.text}</span>
                    <span className="mt-auto flex items-center gap-1 pt-3 [overflow-wrap:anywhere] text-[0.8rem] font-semibold text-brand-700">
                      {item.action}
                      <ArrowRight className="size-3.5 shrink-0 transition group-hover:translate-x-0.5" aria-hidden="true" />
                    </span>
                  </a>
                </li>
              ))}
            </ul>
            <div className="mt-10 rounded-2xl border border-brand-100 bg-white/70 p-5 backdrop-blur">
              <h2 className="text-sm font-semibold text-ink-900">Ce qui se passe ensuite</h2>
              <ol className="mt-4 grid gap-4 sm:grid-cols-3">
                {steps.map((step, index) => (
                  <li key={step} className="flex gap-3 text-sm leading-snug text-ink-600">
                    <span className="grid size-7 shrink-0 place-items-center rounded-full bg-brand-600 text-xs font-bold text-white shadow-md shadow-brand-600/30">{index + 1}</span>
                    {step}
                  </li>
                ))}
              </ol>
            </div>
          </div>
          <div id="formulaire" className="scroll-mt-24 rounded-3xl bg-white p-6 shadow-2xl shadow-brand-900/10 ring-1 ring-slate-200/80 sm:p-7">
            <Pill>Nous écrire</Pill>
            <h2 className="mt-3 text-xl font-bold text-ink-900">Envoyez-nous un message</h2>
            <p className="mt-1 text-sm text-ink-500">Remplissez le formulaire : nous vous répondons rapidement.</p>
            <div className="mt-6">
              <Suspense fallback={<FormSkeleton />}><ContactForm /></Suspense>
            </div>
          </div>
        </div>
      </section>

      <section className="bg-white">
        <div className="mx-auto grid max-w-7xl gap-12 px-4 pb-20 pt-4 sm:px-6 lg:grid-cols-2">
          <div>
            <h2 className="text-2xl font-bold tracking-tight text-ink-900">Où nous travaillons</h2>
            <p className="mt-2 max-w-lg text-ink-600">RestauSpace accompagne les restaurants en France, au Bénin et en zone UEMOA, avec des prix dans la devise de chaque pays.</p>
            <div className="relative mt-6 overflow-hidden rounded-2xl border border-slate-200 bg-gradient-to-br from-sky-50 via-white to-brand-50 p-6">
              <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle,rgb(148_163_184/0.35)_1px,transparent_1px)] [background-size:18px_18px]" aria-hidden="true" />
              <ul className="relative grid gap-4 sm:grid-cols-2">
                {(['fr', 'bj'] as const).map(market => (
                  <li key={market} className="rounded-2xl bg-white/95 p-5 shadow-lg shadow-brand-900/5 ring-1 ring-slate-200/80">
                    <span className="grid size-10 place-items-center rounded-full bg-brand-600 text-white shadow-lg shadow-brand-600/30"><MapPin className="size-5" aria-hidden="true" /></span>
                    <p className="mt-4 font-semibold text-ink-900">{markets[market].label}</p>
                    <p className="mt-1 text-sm text-ink-500">{markets[market].taxNote}</p>
                    <Link href={`/tarifs?pays=${market}`} className="mt-3 inline-flex items-center gap-1 text-sm font-semibold text-brand-700 hover:underline">
                      Voir les tarifs <ArrowRight className="size-3.5" aria-hidden="true" />
                    </Link>
                  </li>
                ))}
              </ul>
              {address && (
                <div className="relative mt-4 flex items-start gap-3 rounded-2xl bg-white/95 p-5 ring-1 ring-slate-200/80">
                  <MapPin className="mt-0.5 size-5 shrink-0 text-brand-600" aria-hidden="true" />
                  <div>
                    <p className="font-semibold text-ink-900">{site.legal.company}</p>
                    <p className="text-sm text-ink-500">{address}</p>
                    <a href={`https://www.openstreetmap.org/search?query=${encodeURIComponent(address)}`} target="_blank" rel="noopener noreferrer" className="mt-2 inline-flex items-center gap-1 text-sm font-semibold text-brand-700 hover:underline">
                      Obtenir l’itinéraire <ArrowRight className="size-3.5" aria-hidden="true" />
                    </a>
                  </div>
                </div>
              )}
            </div>
          </div>

          <div>
            <div className="flex flex-wrap items-end justify-between gap-4">
              <div>
                <Pill>FAQ</Pill>
                <h2 className="mt-3 text-2xl font-bold tracking-tight text-ink-900">Questions fréquentes</h2>
                <p className="mt-2 text-ink-600">Trouvez rapidement les réponses aux questions courantes.</p>
              </div>
              <ButtonLink href="/faq" variant="outline" className="!py-2">Voir toutes les questions</ButtonLink>
            </div>
            <div className="mt-6"><Accordion items={questions} /></div>
          </div>
        </div>
      </section>

      <CallToAction title="Prêt à digitaliser votre restaurant ?" intro="Parlez-nous de votre projet : une démonstration avec votre menu, puis un mois d’essai en service réel." />
    </>
  );
}
