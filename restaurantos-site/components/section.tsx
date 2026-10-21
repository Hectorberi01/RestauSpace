import type {CSSProperties, ReactNode} from 'react';
import Link from 'next/link';
import {ArrowRight} from 'lucide-react';
import {DemoLink} from './demo-modal';

export function Eyebrow({children}: {children: ReactNode}) {
  return <p className="text-xs font-bold uppercase tracking-[0.14em] text-brand-600">{children}</p>;
}

export function Pill({children}: {children: ReactNode}) {
  return <p className="inline-flex items-center gap-2 rounded-full bg-brand-100/70 px-3 py-1 text-xs font-semibold text-brand-700 ring-1 ring-brand-200/70">{children}</p>;
}

/** Le mot mis en valeur d'un titre, dans le dégradé de la marque. */
export function Highlight({children}: {children: ReactNode}) {
  return <span className="bg-gradient-to-r from-brand-600 to-violet-500 bg-clip-text text-transparent">{children}</span>;
}

export function Section({id, eyebrow, title, intro, action, children, tone = 'white'}: {
  id?: string;
  eyebrow?: string;
  title: ReactNode;
  intro?: string;
  action?: {href: string; label: string};
  children: ReactNode;
  tone?: 'white' | 'muted';
}) {
  return (
    <section id={id} className={tone === 'muted' ? 'bg-slate-50/80' : 'bg-white'}>
      <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 sm:py-20">
        <div data-reveal className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
          <div className="max-w-2xl">
            {eyebrow && <Eyebrow>{eyebrow}</Eyebrow>}
            <h2 className="mt-2 text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.2rem] sm:leading-tight">{title}</h2>
            {intro && <p className="mt-3 text-lg leading-relaxed text-ink-600">{intro}</p>}
          </div>
          {action && <ButtonLink href={action.href} variant="outline" className="self-start sm:self-auto">{action.label}</ButtonLink>}
        </div>
        <div data-reveal className="mt-10">{children}</div>
      </div>
    </section>
  );
}

export function ButtonLink({href, children, variant = 'primary', className = '', demo = false}: {
  href: string;
  demo?: boolean;
  children: ReactNode;
  variant?: 'primary' | 'outline' | 'light' | 'ghost-light';
  className?: string;
}) {
  const styles = {
    primary: 'bg-brand-600 text-white shadow-lg shadow-brand-600/25 hover:bg-brand-700',
    outline: 'border border-brand-200 bg-white text-brand-700 hover:border-brand-400 hover:bg-brand-50',
    light: 'bg-white text-brand-700 shadow-lg shadow-black/10 hover:bg-brand-50',
    'ghost-light': 'border border-white/40 text-white hover:bg-white/10',
  };
  const classes = `btn-motion group inline-flex items-center justify-center gap-2 rounded-xl px-5 py-3 text-sm font-semibold ${styles[variant]} ${className}`;
  const content = (
    <>
      {children}
      {variant !== 'ghost-light' && <ArrowRight className="size-4 transition-transform duration-200 group-hover:translate-x-0.5" aria-hidden="true" />}
    </>
  );
  return demo ? <DemoLink className={classes}>{content}</DemoLink> : <Link href={href} className={classes}>{content}</Link>;
}

export function PageHero({eyebrow, title, intro}: {eyebrow: string; title: ReactNode; intro: string}) {
  return (
    <section className="relative overflow-hidden bg-gradient-to-b from-brand-50 via-brand-50/40 to-white">
      <div className="mx-auto max-w-7xl px-4 pb-12 pt-14 sm:px-6 sm:pt-20">
        <div className="animate-rise"><Pill>{eyebrow}</Pill></div>
        <h1 style={{'--rise-delay': '80ms'} as CSSProperties} className="animate-rise mt-5 max-w-3xl text-4xl font-bold tracking-tight text-ink-900 sm:text-5xl">{title}</h1>
        <p style={{'--rise-delay': '160ms'} as CSSProperties} className="animate-rise mt-5 max-w-2xl text-lg leading-relaxed text-ink-600">{intro}</p>
      </div>
    </section>
  );
}

export function CallToAction({title = 'Prêt à simplifier la gestion de votre restaurant ?', intro = 'Une démonstration avec votre propre menu, puis 14 jours d’essai en service réel.'}: {title?: string; intro?: string}) {
  return (
    <section className="relative overflow-hidden bg-gradient-to-r from-brand-700 via-brand-600 to-violet-600">
      <div className="pointer-events-none absolute -right-24 -top-24 size-80 rounded-full bg-white/10 blur-3xl" aria-hidden="true" />
      <div className="pointer-events-none absolute -bottom-32 left-1/3 size-80 rounded-full bg-fuchsia-400/20 blur-3xl" aria-hidden="true" />
      <div data-reveal className="relative mx-auto flex max-w-7xl flex-col gap-6 px-4 py-12 sm:px-6 md:flex-row md:items-center md:justify-between">
        <div>
          <h2 className="text-2xl font-bold tracking-tight text-white sm:text-3xl">{title}</h2>
          <p className="mt-2 text-brand-100">{intro}</p>
        </div>
        <div className="flex flex-col gap-3 sm:flex-row">
          <ButtonLink href="/contact" variant="light" demo>Demander une démo</ButtonLink>
          <ButtonLink href="/tarifs" variant="ghost-light">Voir les tarifs</ButtonLink>
        </div>
      </div>
    </section>
  );
}
