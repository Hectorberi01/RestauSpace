import type {Metadata} from 'next';
import Link from 'next/link';
import {
  ArrowRight, BarChart3, BookOpenText, Boxes, Braces, CalendarCheck, ChefHat, CircleCheck, FileCode2, FlaskConical, LayoutGrid,
  ListTree, ReceiptText, UsersRound, UtensilsCrossed, Webhook,
} from 'lucide-react';
import {ApiShowcase} from '@/components/api-showcase';
import {ButtonLink, Highlight, Pill} from '@/components/section';
import {BrowserFrame, Capture} from '@/components/visual';
import {site} from '@/content/site';
import {docFigures} from '@/lib/developer-docs';

export const metadata: Metadata = {
  title: 'Développeurs',
  description: 'Intégrez RestauSpace à vos applications : API de vente en ligne pour le site, l’application ou la borne d’un restaurant, API d’intégration et webhooks pour vos outils.',
  alternates: {canonical: '/developpeurs'},
};

const response = `{
  "number": "A-1042",
  "status": "Preparing",
  "mode": "Delivery",
  "total": 34.9,
  "currencyCode": "EUR",
  "paymentState": "Paid",
  "estimatedReadyAtUtc": "2026-10-14T12:35:00+00:00",
  "delivery": {"zoneName": "Centre-ville", "status": "Assigned"}
}`;

const resources = [
  {icon: ReceiptText, title: 'Commandes', text: 'Créer, lire et faire avancer les commandes, des plateformes comme de votre caisse.', href: '/developpeurs/integrations-ressources#commandes', tone: 'bg-violet-50 text-violet-600'},
  {icon: ChefHat, title: 'Cuisine', text: 'Suivre les tickets et être prévenu quand une commande est prête.', href: '/developpeurs/integrations-ressources#cuisine', tone: 'bg-orange-50 text-orange-600'},
  {icon: CalendarCheck, title: 'Réservations', text: 'Lire l’agenda, vérifier une disponibilité, créer et faire avancer une réservation.', href: '/developpeurs/integrations-ressources#reservations', tone: 'bg-sky-50 text-sky-600'},
  {icon: UtensilsCrossed, title: 'Menu & produits', text: 'Lire la carte, gérer les prix et les ruptures, afficher la carte en ligne.', href: '/developpeurs/integrations-ressources#carte', tone: 'bg-emerald-50 text-emerald-600'},
  {icon: LayoutGrid, title: 'Salle & tables', text: 'Consulter les tables et faire commander à table par QR code.', href: '/developpeurs/vente-catalogue#menu-a-table-qr-code', tone: 'bg-rose-50 text-rose-600'},
  {icon: Boxes, title: 'Stock', text: 'Suivre les niveaux, les mouvements et enregistrer réceptions et pertes.', href: '/developpeurs/integrations-ressources#stocks', tone: 'bg-indigo-50 text-indigo-600'},
  {icon: UsersRound, title: 'Clients', text: 'Fiches, consentements, comptes clients en ligne et fidélité.', href: '/developpeurs/integrations-ressources#clients', tone: 'bg-cyan-50 text-cyan-600'},
  {icon: BarChart3, title: 'Rapports', text: 'Exécuter un rapport, télécharger un export, alimenter vos tableaux de bord.', href: '/developpeurs/integrations-ressources#rapports-et-exports', tone: 'bg-amber-50 text-amber-600'},
];

const documentation = [
  {icon: BookOpenText, title: 'Guides pas à pas', text: 'De la première clé au site complet d’un restaurant.', href: '/developpeurs/guide-site-restaurant'},
  {icon: ListTree, title: 'Référence de l’API', text: 'Toutes les routes, les champs, les codes d’erreur et leurs messages.', href: '/developpeurs/reference-erreurs'},
  {icon: FlaskConical, title: 'Restaurant de test', text: 'Tout est réel, commandes, cuisine et webhooks, sauf l’argent.', href: '/developpeurs/prise-en-main#5-tester-sans-argent-reel'},
  {icon: FileCode2, title: 'Clients prêts à l’emploi', text: 'TypeScript, Flutter, Swift et Kotlin, à copier dans votre projet.', href: '/developpeurs/exemples-clients'},
];

export default function DevelopersHome() {
  const figures = docFigures();
  const samples = [
    {label: 'cURL', request: `curl ${site.apiUrl}/v1/storefront/orders/$TOKEN \\\n  -H "X-Storefront-Key: $KEY" \\\n  -H "Accept: application/json"`},
    {label: 'TypeScript', request: `const storefront = new Storefront('${site.apiUrl}', key);\nconst order = await storefront.track(trackingToken);`},
    {label: 'Flutter', request: `final storefront = Storefront('${site.apiUrl}', key);\nfinal order = await storefront.track(trackingToken);`},
    {label: 'Kotlin', request: `val storefront = Storefront("${site.apiUrl}", key)\nval order = storefront.track(trackingToken)`},
  ];
  const stats = [
    {icon: Braces, value: '2 API', label: 'Vente en ligne et intégration'},
    {icon: ListTree, value: String(figures.routes), label: 'Routes pour votre site ou votre application'},
    {icon: Webhook, value: String(figures.events), label: 'Événements de webhook signés'},
    {icon: FileCode2, value: String(figures.languages), label: 'Langages d’exemples'},
  ];
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/40 to-white">
        <div className="pointer-events-none absolute -right-32 top-0 size-[34rem] rounded-full bg-brand-200/40 blur-3xl" aria-hidden="true" />
        <div className="relative mx-auto grid max-w-7xl items-center gap-12 px-4 pb-16 pt-12 sm:px-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.05fr)] lg:pb-20 lg:pt-16">
          <div>
            <Pill>API &amp; intégrations</Pill>
            <h1 className="mt-5 text-4xl font-bold leading-[1.08] tracking-tight text-ink-900 sm:text-5xl">
              Intégrez <Highlight>RestauSpace</Highlight> à vos applications
            </h1>
            <p className="mt-5 max-w-xl text-lg leading-relaxed text-ink-600">
              Une API REST documentée pour construire le site, l’application ou la borne d’un restaurant, et relier
              RestauSpace à sa caisse, sa comptabilité et ses tableaux de bord.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <ButtonLink href="/developpeurs/documentation">Voir la documentation</ButtonLink>
              <Link href="/developpeurs/prise-en-main#2-creer-une-application-de-vente-cle-publique" className="inline-flex items-center justify-center rounded-xl border border-ink-900/80 bg-white px-5 py-3 text-sm font-semibold text-ink-900 transition hover:bg-slate-50">
                Obtenir une clé d’API
              </Link>
            </div>
            <ul className="mt-8 space-y-2.5 text-sm text-ink-700">
              {['Clés aux droits limités, restaurant toujours déduit de la clé', 'Documentation complète, en français', 'Webhooks signés et idempotence sur chaque paiement'].map(item => (
                <li key={item} className="flex items-center gap-2.5"><CircleCheck className="size-5 shrink-0 fill-brand-600 text-white" aria-hidden="true" />{item}</li>
              ))}
            </ul>
          </div>
          <div className="relative pb-4 sm:pb-20 lg:pr-6">
            <ApiShowcase samples={samples} response={response} />
            <a
              href={`${site.apiUrl}/openapi/storefront.json`}
              className="absolute bottom-0 right-0 hidden w-60 rounded-2xl border border-slate-200 bg-white p-4 shadow-xl shadow-brand-900/10 transition hover:border-brand-300 sm:block lg:-right-2"
            >
              <span className="flex items-center gap-2 text-sm font-semibold text-ink-900">
                <span className="size-2.5 rounded-full bg-emerald-500" aria-hidden="true" />Description OpenAPI
              </span>
              <span className="mt-1 block text-xs leading-relaxed text-ink-500">Publique : générez votre client ou importez-la dans Postman.</span>
              <span className="mt-2 block truncate font-mono text-[0.7rem] text-brand-700">/openapi/storefront.json</span>
            </a>
          </div>
        </div>
      </section>

      <section className="border-y border-slate-200/70 bg-white">
        <dl className="mx-auto grid max-w-7xl gap-6 px-4 py-8 sm:grid-cols-2 sm:px-6 lg:grid-cols-4">
          {stats.map(stat => (
            <div key={stat.label} className="flex items-center gap-4">
              <span className="grid size-12 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-brand-100"><stat.icon className="size-5" aria-hidden="true" /></span>
              <div>
                <dd className="text-2xl font-bold tracking-tight text-ink-900">{stat.value}</dd>
                <dt className="text-sm text-ink-500">{stat.label}</dt>
              </div>
            </div>
          ))}
        </dl>
      </section>

      <section className="bg-white">
        <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 sm:py-20">
          <p className="text-sm font-semibold text-brand-600">Fonctionnalités de l’API</p>
          <h2 className="mt-2 text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.2rem]">Tout ce dont vous avez besoin</h2>
          <p className="mt-3 max-w-2xl text-lg text-ink-600">L’API d’intégration donne accès aux modules de RestauSpace, avec les seuls droits que le restaurant accorde à votre clé.</p>
          <ul className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {resources.map(item => (
              <li key={item.title}>
                <Link href={item.href} className="flex h-full gap-4 rounded-2xl border border-slate-200 p-5 transition hover:-translate-y-0.5 hover:border-brand-200 hover:shadow-lg hover:shadow-brand-900/5">
                  <span className={`grid size-11 shrink-0 place-items-center rounded-xl ${item.tone}`}><item.icon className="size-5" aria-hidden="true" /></span>
                  <span>
                    <span className="block font-semibold text-ink-900">{item.title}</span>
                    <span className="mt-1 block text-sm leading-relaxed text-ink-500">{item.text}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section className="bg-slate-50/80">
        <div className="mx-auto grid max-w-7xl items-center gap-12 px-4 py-16 sm:px-6 sm:py-20 lg:grid-cols-[minmax(0,0.85fr)_minmax(0,1.15fr)]">
          <div>
            <h2 className="text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.2rem] sm:leading-tight">Une documentation claire et complète</h2>
            <p className="mt-3 text-lg leading-relaxed text-ink-600">
              {figures.pages} pages de guides, d’exemples et de référence pour intégrer l’API rapidement, avec des exemples
              en curl, TypeScript, Flutter, Swift et Kotlin.
            </p>
            <ul className="mt-8 space-y-5">
              {documentation.map(item => (
                <li key={item.title}>
                  <Link href={item.href} className="group flex gap-4">
                    <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-white text-brand-600 shadow-sm ring-1 ring-slate-200"><item.icon className="size-5" aria-hidden="true" /></span>
                    <span>
                      <span className="flex items-center gap-1 font-semibold text-ink-900">{item.title}<ArrowRight className="size-4 opacity-0 transition group-hover:translate-x-0.5 group-hover:opacity-100" aria-hidden="true" /></span>
                      <span className="mt-0.5 block text-sm text-ink-500">{item.text}</span>
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
            <ButtonLink href="/developpeurs/documentation" className="mt-8">Accéder à la documentation</ButtonLink>
          </div>
          <BrowserFrame label="restaurantos.app/developpeurs">
            <Capture
              visual={{src: '/captures/docs.webp', focus: '0% 0%'}}
              alt="La documentation développeurs : sommaire, prise en main et exemples de code"
              sizes="(min-width: 1024px) 700px, 100vw"
              className="aspect-[16/10]"
            />
          </BrowserFrame>
        </div>
      </section>

      <section className="bg-white px-2 py-2 sm:px-4 sm:py-4">
        <div className="relative mx-auto max-w-[90rem] overflow-hidden rounded-3xl bg-gradient-to-br from-ink-900 via-[#1e1b4b] to-brand-700">
          <div className="pointer-events-none absolute -right-20 -top-20 size-96 rounded-full bg-brand-500/30 blur-3xl" aria-hidden="true" />
          <div className="relative mx-auto flex max-w-7xl flex-col gap-8 px-6 py-14 sm:px-10 lg:flex-row lg:items-center lg:justify-between">
            <div className="max-w-2xl">
              <p className="inline-block rounded-full bg-brand-600 px-3 py-1 text-xs font-semibold text-white">Besoin d’un accompagnement ?</p>
              <h2 className="mt-4 text-3xl font-bold tracking-tight text-white sm:text-4xl">Nous aidons les développeurs à <span className="text-brand-200">réussir</span></h2>
              <p className="mt-3 text-lg text-slate-300">
                Un restaurant de test, une question sur une route ou un cas particulier : écrivez-nous, nous vous répondons.
              </p>
            </div>
            <div className="flex flex-col gap-3 sm:flex-row">
              <ButtonLink href="/contact" variant="light">Contacter l’équipe</ButtonLink>
              <ButtonLink href="/developpeurs/documentation" variant="ghost-light">Voir la documentation</ButtonLink>
            </div>
          </div>
        </div>
      </section>
    </>
  );
}
