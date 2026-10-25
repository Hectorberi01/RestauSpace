'use client';
import {createContext, useCallback, useContext, useEffect, useRef, useState, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes} from 'react';
import {AlertCircle, ArrowDownRight, ArrowRight, ArrowUpRight, CheckCircle2, ChevronLeft, ChevronRight, Loader2, Minus, X} from 'lucide-react';
import Link from 'next/link';
import type {Tone} from '@/lib/format';
import {describe, PLAN_FEATURE_REQUIRED_MESSAGE} from '@/lib/api';

export function cx(...classes: (string | false | null | undefined)[]) {
  return classes.filter(Boolean).join(' ');
}

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'success';
type Size = 'sm' | 'md' | 'lg';

const variants: Record<Variant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700 shadow-sm',
  secondary: 'border border-slate-200 bg-white text-ink-800 shadow-[0_1px_2px_rgba(15,23,42,0.04)] hover:border-slate-300 hover:bg-slate-50',
  ghost: 'text-ink-600 hover:bg-slate-100 hover:text-ink-900',
  danger: 'bg-red-600 text-white hover:bg-red-700 shadow-sm',
  success: 'bg-emerald-600 text-white hover:bg-emerald-700 shadow-sm',
};

const sizes: Record<Size, string> = {
  sm: 'h-8 gap-1.5 rounded-lg px-2.5 text-sm',
  md: 'h-10 gap-2 rounded-xl px-4 text-sm',
  lg: 'h-12 gap-2 rounded-xl px-5 text-base',
};

export function Button({variant = 'primary', size = 'md', busy, className, children, disabled, ...props}: ButtonHTMLAttributes<HTMLButtonElement> & {variant?: Variant; size?: Size; busy?: boolean}) {
  return (
    <button
      type="button"
      {...props}
      disabled={disabled || busy}
      className={cx('inline-flex shrink-0 items-center justify-center font-semibold whitespace-nowrap transition disabled:cursor-not-allowed disabled:opacity-50', variants[variant], sizes[size], className)}>
      {busy && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
      {children}
    </button>
  );
}

const control = 'w-full rounded-xl border border-slate-200 bg-white px-3 text-sm text-ink-900 shadow-[0_1px_2px_rgba(15,23,42,0.03)] transition placeholder:text-ink-500 hover:border-slate-300 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 disabled:bg-slate-50';

export function Input({className, ...props}: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={cx(control, 'h-10', className)} />;
}

export function Select({className, children, ...props}: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select {...props} className={cx(control, 'h-10 pr-8', className)}>
      {children}
    </select>
  );
}

export function Textarea({className, ...props}: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={cx(control, 'min-h-20 py-2', className)} />;
}

export function Field({label, hint, children, className}: {label: string; hint?: string; children: ReactNode; className?: string}) {
  return (
    <label className={cx('block', className)}>
      <span className="mb-1.5 block text-sm font-medium text-ink-800">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-xs text-ink-500">{hint}</span>}
    </label>
  );
}

const tones: Record<Tone, string> = {
  neutral: 'bg-slate-100 text-ink-600',
  brand: 'bg-brand-100 text-brand-700',
  blue: 'bg-sky-100 text-sky-800',
  amber: 'bg-amber-100 text-amber-800',
  green: 'bg-emerald-100 text-emerald-800',
  red: 'bg-red-100 text-red-700',
};

export function Badge({tone = 'neutral', children, className}: {tone?: Tone; children: ReactNode; className?: string}) {
  return <span className={cx('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold whitespace-nowrap', tones[tone], className)}>{children}</span>;
}

/** Ombre commune des surfaces de la console. */
export const surfaceShadow = 'shadow-[0_1px_2px_rgba(15,23,42,0.04)]';

/** Style du titre de page, le même sur tous les écrans. */
export const pageTitle = 'text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900';

export function Card({children, className}: {children: ReactNode; className?: string}) {
  return <div className={cx('rounded-2xl border border-slate-200 bg-white', surfaceShadow, className)}>{children}</div>;
}

export function PageHeader({title, description, actions, eyebrow}: {title: string; description?: ReactNode; actions?: ReactNode; eyebrow?: ReactNode}) {
  return (
    <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        {eyebrow && <p className="mb-1 text-sm font-medium text-ink-500">{eyebrow}</p>}
        <h1 className={pageTitle}>{title}</h1>
        {description && <p className="mt-1 text-sm text-ink-500">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

/** Fil d'Ariane : la flèche remonte d'un niveau, le dernier élément est la page courante. */
export function Breadcrumb({items}: {items: {label: string; href?: string; onClick?: () => void}[]}) {
  const parent = items.length > 1 ? items[items.length - 2] : undefined;
  const link = 'rounded-md text-ink-500 transition hover:text-ink-900';
  const back = parent?.href ? (
    <Link href={parent.href} aria-label={`Retour : ${parent.label}`} className="grid size-7 place-items-center rounded-lg border border-slate-200 bg-white text-ink-600 hover:bg-slate-50"><ChevronLeft className="size-4" /></Link>
  ) : parent?.onClick ? (
    <button type="button" onClick={parent.onClick} aria-label={`Retour : ${parent.label}`} className="grid size-7 place-items-center rounded-lg border border-slate-200 bg-white text-ink-600 hover:bg-slate-50"><ChevronLeft className="size-4" /></button>
  ) : null;
  return (
    <nav aria-label="Fil d’Ariane" className="mb-2 flex items-center gap-2 text-sm">
      {back}
      <ol className="flex min-w-0 items-center gap-1.5">
        {items.map((item, index) => {
          const last = index === items.length - 1;
          return (
            <li key={item.label} className="flex min-w-0 items-center gap-1.5">
              {index > 0 && <ChevronRight className="size-3.5 shrink-0 text-ink-500" aria-hidden="true" />}
              {last ? <span aria-current="page" className="truncate font-medium text-ink-800">{item.label}</span>
                : item.href ? <Link href={item.href} className={link}>{item.label}</Link>
                  : <button type="button" onClick={item.onClick} className={link}>{item.label}</button>}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/** Un bloc de contenu : titre, sous-titre facultatif, action à droite. */
export function Panel({title, subtitle, action, children, className, bodyClassName}: {
  title: ReactNode;
  subtitle?: ReactNode;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
  bodyClassName?: string;
}) {
  return (
    <Card className={cx('flex min-w-0 flex-col p-5', className)}>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-[0.95rem] font-semibold text-ink-900">{title}</h2>
          {subtitle && <p className="mt-0.5 text-xs text-ink-500">{subtitle}</p>}
        </div>
        {action}
      </div>
      <div className={cx('min-w-0 flex-1', bodyClassName)}>{children}</div>
    </Card>
  );
}

export function SeeAll({href, label = 'Voir tout'}: {href: string; label?: string}) {
  return (
    <Link href={href} className="group inline-flex shrink-0 items-center gap-1 text-xs font-semibold text-brand-700 hover:text-brand-600">
      {label}
      <ArrowRight className="size-3.5 transition group-hover:translate-x-0.5" aria-hidden="true" />
    </Link>
  );
}

/** Variation par rapport à une période de référence ; `better` dit si une hausse est une bonne nouvelle. */
export function Delta({value, better = 'up', label}: {value: number | null; better?: 'up' | 'down' | 'neutral'; label?: string}) {
  if (value === null || !Number.isFinite(value)) return <span className="text-xs text-ink-500">{label ? `— ${label}` : '—'}</span>;
  const rounded = Math.round(value * 100);
  const flat = rounded === 0;
  const good = flat || better === 'neutral' ? null : (value > 0) === (better === 'up');
  const Icon = flat ? Minus : value > 0 ? ArrowUpRight : ArrowDownRight;
  return (
    <span className="inline-flex flex-wrap items-center gap-x-1.5 gap-y-0.5 text-xs">
      <span className={cx(
        'inline-flex shrink-0 items-center gap-0.5 rounded-md px-1.5 py-0.5 font-semibold whitespace-nowrap tabular-nums',
        good === null ? 'bg-slate-100 text-ink-600' : good ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-700',
      )}>
        <Icon className="size-3" aria-hidden="true" />
        {rounded > 0 ? '+' : ''}{rounded} %
      </span>
      {label && <span className="text-ink-500">{label}</span>}
    </span>
  );
}

const accents = {
  neutral: 'bg-slate-100 text-ink-600',
  brand: 'bg-brand-100 text-brand-600',
  amber: 'bg-amber-100 text-amber-600',
  red: 'bg-red-100 text-red-600',
  blue: 'bg-sky-100 text-sky-600',
  green: 'bg-emerald-100 text-emerald-600',
  orange: 'bg-orange-100 text-orange-600',
};

export type Accent = keyof typeof accents;

/** Pastille d'indicateur déduite d'une classe de couleur Tailwind (« bg-sky-50 text-sky-600 » donne « blue »). */
export function accentOf(tone?: string): Accent | undefined {
  const color = tone?.match(/bg-([a-z]+)-/)?.[1];
  if (!color) return undefined;
  return ({violet: 'brand', brand: 'brand', emerald: 'green', green: 'green', orange: 'orange', rose: 'red', red: 'red', sky: 'blue', blue: 'blue', amber: 'amber'} as Record<string, Accent>)[color];
}

/**
 * Tuile d'indicateur, la même sur tous les écrans : pastille d'icône colorée, libellé, valeur, puis une ligne
 * (variation ou précision) et, en option, une courbe. `accent` colore la pastille ; ambre et rouge signalent un état.
 */
export function StatCard({icon, label, value, hint, delta, chart, accent = 'brand', onClick, active, className}: {
  icon?: ReactNode;
  label: ReactNode;
  value: ReactNode;
  hint?: ReactNode;
  delta?: ReactNode;
  chart?: ReactNode;
  accent?: Accent;
  onClick?: () => void;
  active?: boolean;
  className?: string;
}) {
  const body = (
    <>
      <span className="flex min-w-0 flex-col items-start gap-2.5 sm:flex-row sm:gap-4">
        {icon && <span className={cx('grid size-10 shrink-0 place-items-center rounded-xl sm:size-12 [&_svg]:size-5 sm:[&_svg]:size-6', accents[accent])} aria-hidden="true">{icon}</span>}
        <span className="block min-w-0 flex-1">
          <span className="line-clamp-2 block text-sm leading-snug font-medium text-ink-600 sm:truncate">{label}</span>
          <span className="mt-1 block truncate text-lg leading-tight font-bold tracking-tight text-ink-900 tabular-nums min-[420px]:text-xl sm:text-[1.6rem]">{value}</span>
          {(delta || hint) && <span className="mt-1 flex min-h-5 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-ink-500">{delta}{hint && <span className="line-clamp-2 min-w-0">{hint}</span>}</span>}
        </span>
      </span>
      {chart && <span className="-mx-1 mt-auto block [&_svg]:h-10 [&_svg]:w-full">{chart}</span>}
    </>
  );
  const classes = cx('flex min-w-0 flex-col gap-3 rounded-2xl border bg-white p-4 text-left sm:p-5', surfaceShadow, className);
  return onClick ? (
    <button type="button" onClick={onClick} aria-pressed={active}
      className={cx(classes, 'transition hover:border-brand-300', active ? 'border-brand-400 ring-2 ring-brand-100' : 'border-slate-200')}>
      {body}
    </button>
  ) : (
    <div className={cx(classes, 'border-slate-200')}>{body}</div>
  );
}

export function Skeleton({className}: {className?: string}) {
  return <div className={cx('animate-pulse rounded-lg bg-slate-200/70', className)} />;
}

/** Ajoute le lien vers Abonnement au message « fonction non incluse ». */
function planLink(content: ReactNode, className: string): ReactNode {
  if (content !== PLAN_FEATURE_REQUIRED_MESSAGE) return content;
  return <>{content} <Link href="/abonnement" className={className}>Voir les plans</Link></>;
}

export function Alert({children, tone = 'red'}: {children: ReactNode; tone?: 'red' | 'amber' | 'blue'}) {
  const style = {red: 'border-red-200 bg-red-50 text-red-800', amber: 'border-amber-200 bg-amber-50 text-amber-900', blue: 'border-sky-200 bg-sky-50 text-sky-900'}[tone];
  return (
    <div role={tone === 'red' ? 'alert' : 'status'} className={cx('flex gap-2 rounded-xl border px-4 py-3 text-sm', style)}>
      <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div className="min-w-0">{planLink(children, 'font-semibold underline underline-offset-2')}</div>
    </div>
  );
}

export function Empty({icon, title, children}: {icon?: ReactNode; title: string; children?: ReactNode}) {
  return (
    <div className="flex flex-col items-center rounded-2xl border border-dashed border-slate-300 bg-white/60 px-6 py-12 text-center">
      {icon && <div className="mb-3 grid size-12 place-items-center rounded-xl bg-slate-100 text-ink-500 [&_svg]:size-6">{icon}</div>}
      <p className="font-semibold text-ink-900">{title}</p>
      {children && <div className="mt-2 max-w-md text-sm text-ink-500">{children}</div>}
    </div>
  );
}

export function Spinner({label = 'Chargement…'}: {label?: string}) {
  return (
    <div className="flex items-center justify-center gap-2 py-12 text-sm text-ink-500" role="status">
      <Loader2 className="size-5 animate-spin" aria-hidden="true" />
      {label}
    </div>
  );
}

export function Tabs<T extends string>({value, onChange, items, label}: {value: T; onChange: (value: T) => void; items: {value: T; label: ReactNode}[]; label: string}) {
  return (
    <div role="tablist" aria-label={label} className="mb-5 flex max-w-full gap-1 overflow-x-auto rounded-xl bg-slate-100 p-1">
      {items.map(item => (
        <button
          key={item.value}
          type="button"
          role="tab"
          aria-selected={value === item.value}
          onClick={() => onChange(item.value)}
          className={cx('rounded-lg px-3 py-1.5 text-sm font-semibold whitespace-nowrap transition', value === item.value ? 'bg-white text-ink-900 shadow-sm' : 'text-ink-600 hover:text-ink-900')}>
          {item.label}
        </button>
      ))}
    </div>
  );
}

export function Modal({open, onClose, title, children, footer, wide}: {open: boolean; onClose: () => void; title: string; children: ReactNode; footer?: ReactNode; wide?: boolean}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      onClose={event => {
        if (event.target === event.currentTarget) onClose();
      }}
      onCancel={event => {
        if (event.target !== event.currentTarget) return;
        event.preventDefault();
        onClose();
      }}
      className={cx('m-auto max-h-[90dvh] w-[calc(100%-2rem)] overflow-hidden rounded-2xl p-0 text-ink-900 shadow-2xl backdrop:bg-ink-900/40', wide ? 'max-w-3xl' : 'max-w-lg')}>
      {open && (
        <div className="flex max-h-[90dvh] flex-col">
          <div className="flex items-center justify-between gap-4 border-b border-slate-100 px-5 py-4">
            <h2 className="text-lg font-semibold">{title}</h2>
            <button type="button" onClick={onClose} className="rounded-lg p-1.5 text-ink-500 hover:bg-slate-100" aria-label="Fermer">
              <X className="size-5" />
            </button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>
          {footer && <div className="flex flex-wrap justify-end gap-2 border-t border-slate-100 px-5 py-3">{footer}</div>}
        </div>
      )}
    </dialog>
  );
}

interface Toast {
  id: number;
  tone: 'success' | 'error';
  text: string;
}

const ToastContext = createContext<(tone: Toast['tone'], text: string) => void>(() => {});

export function ToastProvider({children}: {children: ReactNode}) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const push = useCallback((tone: Toast['tone'], text: string) => {
    const id = Date.now() + Math.random();
    setToasts(list => [...list.slice(-3), {id, tone, text}]);
    window.setTimeout(() => setToasts(list => list.filter(toast => toast.id !== id)), tone === 'error' ? 7000 : 3500);
  }, []);
  return (
    <ToastContext.Provider value={push}>
      {children}
      <div className="pointer-events-none fixed inset-x-0 bottom-4 z-50 flex flex-col items-center gap-2 px-4" aria-live="polite">
        {toasts.map(toast => (
          <div key={toast.id} className={cx('pointer-events-auto flex max-w-md items-start gap-2 rounded-xl px-4 py-3 text-sm font-medium text-white shadow-lg', toast.tone === 'error' ? 'bg-red-700' : 'bg-ink-900')}>
            {toast.tone === 'error' ? <AlertCircle className="mt-0.5 size-4 shrink-0" /> : <CheckCircle2 className="mt-0.5 size-4 shrink-0" />}
            <span>{planLink(toast.text, 'font-semibold underline underline-offset-2')}</span>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}

/** Exécute une action de l'API : un seul bouton occupé à la fois, l'erreur en toast, un message de succès facultatif. */
export function useAction() {
  const toast = useToast();
  const [busy, setBusy] = useState<string | null>(null);
  const run = useCallback(
    async (key: string, action: () => Promise<unknown>, success?: string) => {
      setBusy(key);
      try {
        await action();
        if (success) toast('success', success);
        return true;
      } catch (error) {
        toast('error', describe(error));
        return false;
      } finally {
        setBusy(null);
      }
    },
    [toast],
  );
  return {busy, run};
}
