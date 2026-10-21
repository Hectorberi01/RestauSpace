'use client';
import {Suspense, useEffect, useRef, useState, type MouseEvent, type ReactNode} from 'react';
import Link from 'next/link';
import {usePathname} from 'next/navigation';
import {X} from 'lucide-react';
import {ContactForm} from '@/app/contact/contact-form';
import {trialDays} from '@/content/plans';
import {FormSkeleton} from './skeleton';

const event = 'restaurantos:demo';

/** Un lien « Demander une démo » : ouvre la fenêtre de demande ; sans JavaScript, il mène à la page Contact. */
export function DemoLink({children, className, onClick}: {children: ReactNode; className?: string; onClick?: () => void}) {
  const path = usePathname();
  const open = (e: MouseEvent<HTMLAnchorElement>) => {
    onClick?.();
    if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
    e.preventDefault();
    if (path === '/contact') {
      document.getElementById('formulaire')?.scrollIntoView({behavior: 'smooth', block: 'start'});
      return;
    }
    window.dispatchEvent(new Event(event));
  };
  return <Link href="/contact" onClick={open} className={className}>{children}</Link>;
}

export function DemoModal() {
  const dialog = useRef<HTMLDialogElement>(null);
  const [mounted, setMounted] = useState(false);
  const [closing, setClosing] = useState(false);
  const path = usePathname();

  useEffect(() => {
    const open = () => {
      setMounted(true);
      setClosing(false);
      requestAnimationFrame(() => dialog.current?.showModal());
    };
    window.addEventListener(event, open);
    return () => window.removeEventListener(event, open);
  }, []);

  useEffect(() => {
    if (dialog.current?.open) dialog.current.close();
  }, [path]);

  const close = () => {
    if (!dialog.current?.open || closing) return;
    setClosing(true);
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    setTimeout(() => {
      dialog.current?.close();
      setClosing(false);
    }, reduced ? 0 : 200);
  };

  if (!mounted) return null;
  return (
    <dialog
      ref={dialog}
      aria-labelledby="demo-title"
      onCancel={event => {
        event.preventDefault();
        close();
      }}
      onClick={event => {
        if (event.target === dialog.current) close();
      }}
      className={`demo-dialog m-auto max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-xl overflow-y-auto rounded-3xl bg-white p-0 text-ink-900 shadow-2xl shadow-ink-900/30 ${closing ? 'is-closing' : ''}`}
    >
      <div className="sticky top-0 z-10 flex items-start justify-between gap-4 border-b border-slate-100 bg-white/95 px-6 py-5 backdrop-blur sm:px-7">
        <div>
          <h2 id="demo-title" className="text-xl font-bold">Demander une démo</h2>
          <p className="mt-1 text-sm text-ink-500">Nous vous montrons RestauSpace avec votre menu, puis {trialDays} jours d’essai.</p>
        </div>
        <button type="button" onClick={close} aria-label="Fermer" className="rounded-xl p-2 text-ink-500 transition hover:rotate-90 hover:bg-slate-100 hover:text-ink-900">
          <X className="size-5" aria-hidden="true" />
        </button>
      </div>
      <div className="px-6 py-6 sm:px-7">
        <Suspense fallback={<FormSkeleton />}><ContactForm initialSubjectIndex={0} /></Suspense>
      </div>
    </dialog>
  );
}
