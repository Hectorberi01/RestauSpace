'use client';
import {useEffect, useId, useRef} from 'react';
import Link from 'next/link';
import {Star, X} from 'lucide-react';

export const cx = (...names: (string | false | null | undefined)[]) => names.filter(Boolean).join(' ');

const variants = {
  primary: 'bg-primary text-white shadow-[0_8px_20px_color-mix(in_srgb,var(--primary)_25%,transparent)] hover:brightness-110',
  ghost: 'border-[1.5px] border-line bg-white text-ink hover:border-primary/50',
  sun: 'bg-accent text-ink hover:brightness-105',
  danger: 'border border-red-200 bg-white text-red-700 hover:bg-red-50',
};

export const buttonClass = (variant: keyof typeof variants = 'primary', className?: string) =>
  cx('inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-5 py-3 text-[15px] font-semibold transition disabled:cursor-not-allowed disabled:opacity-50', variants[variant], className);

export function Button({variant = 'primary', busy, className, children, ...props}: React.ButtonHTMLAttributes<HTMLButtonElement> & {variant?: keyof typeof variants; busy?: boolean}) {
  return (
    <button type="button" {...props} disabled={props.disabled || busy} className={buttonClass(variant, className)}>
      {busy && <span className="size-4 animate-spin rounded-full border-2 border-current/30 border-t-current" aria-hidden="true" />}
      {children}
    </button>
  );
}

export function ButtonLink({variant = 'primary', className, ...props}: React.ComponentProps<typeof Link> & {variant?: keyof typeof variants}) {
  return <Link {...props} className={buttonClass(variant, className)} />;
}

/** Panneau modal : bas d'écran sur mobile, centré sur grand écran. Échap et clic à côté le ferment. */
export function Sheet({onClose, title, children, footer}: {onClose: () => void; title: string; children: React.ReactNode; footer?: React.ReactNode}) {
  const panel = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    panel.current?.focus();
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = '';
      previous?.focus();
    };
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-[70] flex items-end justify-center bg-ink/55 sm:items-center sm:p-6" onMouseDown={event => event.target === event.currentTarget && onClose()}>
      <div ref={panel} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby={titleId}
        className="flex max-h-[92vh] w-full flex-col overflow-hidden rounded-t-3xl bg-cream shadow-xl outline-none sm:max-w-lg sm:rounded-3xl">
        <div className="flex items-center justify-between gap-3 border-b border-line px-5 py-4">
          <h2 id={titleId} className="truncate font-serif text-xl font-bold">{title}</h2>
          <button type="button" onClick={onClose} aria-label="Fermer" className="grid size-10 place-items-center rounded-full hover:bg-sand">
            <X className="size-5" />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="border-t border-line bg-white px-5 py-4">{footer}</div>}
      </div>
    </div>
  );
}

export function Notice({tone = 'info', children}: {tone?: 'info' | 'error' | 'success' | 'warning'; children: React.ReactNode}) {
  const tones = {
    info: 'border-line bg-white text-ink',
    error: 'border-red-200 bg-red-50 text-red-800',
    success: 'border-emerald-200 bg-emerald-50 text-emerald-900',
    warning: 'border-amber-200 bg-amber-50 text-amber-900',
  };
  return <div role={tone === 'error' ? 'alert' : 'status'} className={cx('rounded-xl border px-4 py-3 text-sm leading-relaxed', tones[tone])}>{children}</div>;
}

export function Spinner({label = 'Chargement…'}: {label?: string}) {
  return (
    <div className="flex items-center justify-center gap-3 py-16 text-sm text-muted" role="status">
      <span className="size-5 animate-spin rounded-full border-2 border-line border-t-primary" aria-hidden="true" />
      {label}
    </div>
  );
}

export function Skeleton({className}: {className?: string}) {
  return <span aria-hidden="true" className={cx('block animate-pulse rounded-lg bg-sand/80', className)} />;
}

export function Stars({rating, className}: {rating: number; className?: string}) {
  return (
    <span className={cx('inline-flex gap-0.5 text-accent', className)} role="img" aria-label={`${rating.toLocaleString('fr-FR', {maximumFractionDigits: 1})} sur 5`}>
      {[1, 2, 3, 4, 5].map(value => (
        <Star key={value} className={cx('size-[1em]', value <= Math.round(rating) ? 'fill-current' : 'opacity-35')} aria-hidden="true" />
      ))}
    </span>
  );
}

/** Mise en page des pages intérieures. */
export function PageShell({title, eyebrow, children, wide}: {title: string; eyebrow?: React.ReactNode; children: React.ReactNode; wide?: boolean}) {
  return (
    <div className={cx('mx-auto w-full px-4 py-8 sm:px-6 sm:py-12', wide ? 'max-w-6xl' : 'max-w-2xl')}>
      {eyebrow && <p className="text-sm font-semibold text-primary">{eyebrow}</p>}
      <h1 className="mt-1 font-serif text-3xl font-bold tracking-tight sm:text-4xl">{title}</h1>
      <div className="mt-6">{children}</div>
    </div>
  );
}

export const inputClass = 'w-full rounded-xl border border-line bg-white px-3.5 py-3 text-[15px] placeholder:text-muted/80 focus:border-primary focus:outline-none';

export const choiceClass = (on: boolean, extra?: string | false) =>
  cx('rounded-xl border bg-white text-sm transition', on ? 'border-primary ring-1 ring-primary' : 'border-line hover:border-primary/40', extra);
