import Link from 'next/link';
import {ArrowRight, ArrowUp, Globe, Headset, Mail, MapPin, ShieldCheck} from 'lucide-react';
import {nav, site} from '@/content/site';
import {Brand} from './brand';
import {DemoLink} from './demo-modal';

const social: {key: keyof typeof site.social; label: string; path: string}[] = [
  {key: 'linkedin', label: 'LinkedIn', path: 'M20.447 20.452h-3.554v-5.569c0-1.328-.027-3.037-1.852-3.037-1.853 0-2.136 1.445-2.136 2.939v5.667H9.351V9h3.414v1.561h.046c.477-.9 1.637-1.85 3.37-1.85 3.601 0 4.267 2.37 4.267 5.455v6.286zM5.337 7.433a2.062 2.062 0 1 1 0-4.125 2.062 2.062 0 0 1 0 4.125zM7.119 20.452H3.555V9h3.564v11.452zM22.225 0H1.771C.792 0 0 .774 0 1.729v20.542C0 23.227.792 24 1.771 24h20.451C23.2 24 24 23.227 24 22.271V1.729C24 .774 23.2 0 22.222 0h.003z'},
  {key: 'facebook', label: 'Facebook', path: 'M9.101 23.691v-7.98H6.627v-3.667h2.474v-1.58c0-4.085 1.848-5.978 5.858-5.978.401 0 .955.042 1.468.103a8.68 8.68 0 0 1 1.141.195v3.325a8.623 8.623 0 0 0-.653-.036c-.26-.006-.506-.009-.733-.009-.707 0-1.259.096-1.675.309a1.686 1.686 0 0 0-.679.622c-.258.42-.374.995-.374 1.752v1.297h3.919l-.386 2.103-.287 1.564h-3.246v8.245C19.396 23.238 24 18.179 24 12.044c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.628 3.874 10.35 9.101 11.647z'},
  {key: 'x', label: 'X', path: 'M18.901 1.153h3.68l-8.04 9.19L24 22.846h-7.406l-5.8-7.584-6.638 7.584H.474l8.6-9.83L0 1.154h7.594l5.243 6.932zM17.61 20.644h2.039L6.486 3.24H4.298z'},
  {key: 'youtube', label: 'YouTube', path: 'M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z'},
  {key: 'github', label: 'GitHub', path: 'M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61-.546-1.385-1.335-1.755-1.335-1.755-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12'},
];

const assurances = [
  {icon: ShieldCheck, title: 'Sécurisé', text: 'Vos données protégées'},
  {icon: Globe, title: '100 % en ligne', text: 'Rien à installer'},
  {icon: Headset, title: 'Accompagnement', text: 'Une équipe qui vous répond'},
];

const product = [...nav.slice(0, 3), {href: '/decouvrir', label: 'Visite guidée'}, {href: '/simulateur', label: 'Simulateur de prix'}, ...nav.slice(3)];

function FooterLink({href, children}: {href: string; children: React.ReactNode}) {
  return (
    <Link href={href} className="group inline-flex items-center gap-2.5 text-ink-600 transition-colors hover:text-brand-700">
      <ArrowRight className="size-3.5 text-ink-400 transition-all group-hover:translate-x-0.5 group-hover:text-brand-600" aria-hidden="true" />
      {children}
    </Link>
  );
}

export function Footer() {
  const links = social.filter(item => site.social[item.key]);
  return (
    <footer className="relative overflow-hidden border-t border-slate-200/70 bg-gradient-to-b from-white to-brand-50/60">
      <div className="pointer-events-none absolute -right-32 -top-32 size-96 rounded-full bg-brand-200/40 blur-3xl" aria-hidden="true" />
      <div className="pointer-events-none absolute -bottom-40 -left-32 size-96 rounded-full bg-violet-200/40 blur-3xl" aria-hidden="true" />
      <div className="relative mx-auto grid max-w-7xl gap-12 px-4 pb-10 pt-16 sm:px-6 md:grid-cols-2 lg:grid-cols-[minmax(0,1.9fr)_minmax(0,0.8fr)_minmax(0,0.8fr)_minmax(0,1.2fr)]">
        <div>
          <Brand />
          <p className="mt-5 max-w-md leading-relaxed text-ink-600">{site.description}</p>
          <ul className="mt-8 flex flex-wrap gap-x-7 gap-y-4">
            {assurances.map(item => (
              <li key={item.title} className="flex items-start gap-2.5">
                <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-brand-100"><item.icon className="size-4.5" aria-hidden="true" /></span>
                <span>
                  <span className="block whitespace-nowrap text-sm font-semibold text-ink-900">{item.title}</span>
                  <span className="block whitespace-nowrap text-xs text-ink-500">{item.text}</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
        <div>
          <h2 className="font-semibold text-ink-900">Produit</h2>
          <ul className="mt-5 space-y-3 text-sm">
            {product.map(item => <li key={item.href}><FooterLink href={item.href}>{item.label}</FooterLink></li>)}
          </ul>
        </div>
        <div>
          <h2 className="font-semibold text-ink-900">Informations</h2>
          <ul className="mt-5 space-y-3 text-sm">
            <li><FooterLink href="/mentions-legales">Mentions légales</FooterLink></li>
            <li><FooterLink href="/confidentialite">Confidentialité</FooterLink></li>
            <li><FooterLink href="/developpeurs/documentation">Documentation</FooterLink></li>
            {site.contact.email && <li><a href={`mailto:${site.contact.email}`} className="inline-flex items-center gap-2.5 text-ink-600 hover:text-brand-700"><Mail className="size-3.5" aria-hidden="true" />{site.contact.email}</a></li>}
          </ul>
        </div>
        <div>
          {site.newsletterUrl ? (
            <>
              <h2 className="font-semibold text-ink-900">Restez informé</h2>
              <p className="mt-4 text-sm leading-relaxed text-ink-600">Recevez nos nouveautés et nos mises à jour directement dans votre boîte mail.</p>
              <form action={site.newsletterUrl} method="post" target="_blank" className="mt-5 flex rounded-2xl bg-white p-1.5 shadow-lg shadow-brand-900/5 ring-1 ring-slate-200 focus-within:ring-4 focus-within:ring-brand-100">
                <label className="flex min-w-0 flex-1 items-center gap-2.5 pl-3">
                  <Mail className="size-4 shrink-0 text-ink-400" aria-hidden="true" />
                  <span className="sr-only">Votre adresse email</span>
                  <input type="email" name="email" required autoComplete="email" placeholder="Votre adresse email" className="min-w-0 flex-1 bg-transparent py-2 text-sm text-ink-900 outline-none placeholder:text-ink-400" />
                </label>
                <button type="submit" aria-label="S’inscrire" className="btn-motion grid size-11 shrink-0 place-items-center rounded-xl bg-brand-600 text-white shadow-md shadow-brand-600/30 hover:bg-brand-700">
                  <ArrowRight className="size-5" aria-hidden="true" />
                </button>
              </form>
            </>
          ) : (
            <>
              <h2 className="font-semibold text-ink-900">Parlons de votre restaurant</h2>
              <p className="mt-4 text-sm leading-relaxed text-ink-600">Une démonstration avec votre propre menu, puis l’essai en service réel.</p>
              <DemoLink className="btn-motion group mt-5 inline-flex items-center gap-2 rounded-xl bg-brand-600 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-brand-600/25 hover:bg-brand-700">
                Demander une démo <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
              </DemoLink>
            </>
          )}
          {links.length > 0 && (
            <>
              <h2 className="mt-8 font-semibold text-ink-900">Suivez-nous</h2>
              <ul className="mt-4 flex flex-wrap gap-2.5">
                {links.map(item => (
                  <li key={item.key}>
                    <a href={site.social[item.key]} target="_blank" rel="noopener noreferrer" aria-label={item.label} className="btn-motion grid size-11 place-items-center rounded-full bg-white text-ink-700 shadow-sm ring-1 ring-slate-200 hover:text-brand-600 hover:ring-brand-200">
                      <svg viewBox="0 0 24 24" className="size-4.5" fill="currentColor" aria-hidden="true"><path d={item.path} /></svg>
                    </a>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      </div>
      <div className="relative mx-auto max-w-7xl px-4 sm:px-6">
        <div className="flex flex-col gap-4 border-t border-slate-200/80 py-6 text-sm text-ink-500 sm:flex-row sm:items-center sm:justify-between">
          <span>© {new Date().getFullYear()} {site.name} · {site.legal.company}. Tous droits réservés.</span>
          <div className="flex flex-wrap items-center gap-x-5 gap-y-3">
            <span className="flex items-center gap-1.5 whitespace-nowrap"><MapPin className="size-4 text-brand-600" aria-hidden="true" />France · Bénin · zone UEMOA</span>
            <span className="hidden h-6 w-px bg-slate-200 sm:block" aria-hidden="true" />
            <a href="#" className="btn-motion inline-flex items-center gap-1.5 whitespace-nowrap rounded-full bg-brand-50 px-4 py-2 text-xs font-semibold text-brand-700 ring-1 ring-brand-100 hover:bg-brand-100">
              <ArrowUp className="size-3.5" aria-hidden="true" /> Retour en haut
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}
