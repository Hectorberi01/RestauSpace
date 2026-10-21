'use client';
import {useEffect, useRef} from 'react';
import {X} from 'lucide-react';
import {usePathname} from 'next/navigation';
import {languages, setLang, tr, useLang} from '@/lib/i18n';

export const cx = (...names: (string | false | null | undefined)[]) => names.filter(Boolean).join(' ');

/** Bouton principal aux couleurs de l'enseigne (variable CSS --accent posée par la page). */
export function AccentButton({children, className, busy, ...props}: React.ButtonHTMLAttributes<HTMLButtonElement> & {busy?: boolean}) {
  return (
    <button
      {...props}
      disabled={props.disabled || busy}
      className={cx(
        'inline-flex items-center justify-center gap-2 rounded-xl bg-[var(--accent)] px-5 py-3 text-sm font-semibold text-white shadow-sm transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}>
      {busy && <span className="size-4 animate-spin rounded-full border-2 border-white/40 border-t-white" aria-hidden="true" />}
      {children}
    </button>
  );
}

/** Panneau modal : bas d'écran sur mobile, centré sur grand écran. Échap et clic à côté le ferment. */
export function Sheet({open, onClose, title, children, footer}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  const panel = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
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
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/50 sm:items-center sm:p-6" onMouseDown={event => event.target === event.currentTarget && onClose()}>
      <div ref={panel} tabIndex={-1} role="dialog" aria-modal="true" aria-label={title}
        className="flex max-h-[92vh] w-full flex-col overflow-hidden rounded-t-3xl bg-white shadow-xl outline-none sm:max-w-lg sm:rounded-3xl">
        <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-5 py-4">
          <h2 className="truncate text-lg font-bold">{title}</h2>
          <button type="button" onClick={onClose} aria-label={tr('Fermer')} className="grid size-9 place-items-center rounded-full text-ink-600 hover:bg-slate-100">
            <X className="size-5" />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="border-t border-slate-100 px-5 py-4">{footer}</div>}
      </div>
    </div>
  );
}

export function Notice({tone = 'info', children}: {tone?: 'info' | 'error' | 'success' | 'warning'; children: React.ReactNode}) {
  const tones = {
    info: 'border-sky-200 bg-sky-50 text-sky-900',
    error: 'border-red-200 bg-red-50 text-red-800',
    success: 'border-emerald-200 bg-emerald-50 text-emerald-900',
    warning: 'border-amber-200 bg-amber-50 text-amber-900',
  };
  return <div role={tone === 'error' ? 'alert' : 'status'} className={cx('rounded-xl border px-4 py-3 text-sm', tones[tone])}>{children}</div>;
}

export function Spinner({label = tr('Chargement…')}: {label?: string}) {
  return (
    <div className="flex items-center justify-center gap-3 py-16 text-sm text-ink-500" role="status">
      <span className="size-5 animate-spin rounded-full border-2 border-slate-300 border-t-[var(--accent,#5b3df5)]" aria-hidden="true" />
      {label}
    </div>
  );
}

/** FR | EN : la langue des pages des clients, gardée sur l'appareil. */
export function LanguageSwitch({onAccent = false, className}: {onAccent?: boolean; className?: string}) {
  const lang = useLang();
  return (
    <div role="group" aria-label={tr('Langue')} className={cx('inline-flex shrink-0 rounded-full p-0.5 text-xs font-semibold', onAccent ? 'bg-white/15 backdrop-blur' : 'bg-slate-100', className)}>
      {languages.map(item => (
        <button key={item.value} type="button" lang={item.value} aria-pressed={lang === item.value} title={item.name} onClick={() => setLang(item.value)}
          className={cx('rounded-full px-2.5 py-1 transition', lang === item.value
            ? onAccent ? 'bg-white text-slate-900' : 'bg-white text-ink-900 shadow-sm'
            : onAccent ? 'text-white/85 hover:text-white' : 'text-ink-500 hover:text-ink-800')}>
          {item.label}
        </button>
      ))}
    </div>
  );
}

const barPages = ['/commander', '/commande', '/compte', '/reserver', '/reservation', '/questionnaire/', '/desinscription', '/recu'];

/** Barre de langue en haut des pages des clients qui n'ont pas d'en-tête à elles (pas la commande d'un établissement ni la borne). */
export function LanguageBar() {
  const path = usePathname();
  if (!barPages.some(prefix => path === prefix || path.startsWith(prefix.endsWith('/') ? prefix : `${prefix}/`) || path === prefix)) return null;
  if (/^\/commander\/[^/]+/.test(path)) return null;
  return (
    <div className="mx-auto flex w-full max-w-5xl justify-end px-4 pt-3">
      <LanguageSwitch />
    </div>
  );
}
