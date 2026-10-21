'use client';
import {useEffect, useState} from 'react';
import {CheckCircle2, Info, X, XCircle} from 'lucide-react';

type Kind = 'success' | 'error' | 'info';

interface Item {
  id: number;
  message: string;
  kind: Kind;
  leaving: boolean;
}

const event = 'restaurantos:toast';

/** Affiche une notification éphémère en bas de l'écran. */
export function toast(message: string, kind: Kind = 'success') {
  window.dispatchEvent(new CustomEvent(event, {detail: {message, kind}}));
}

const styles: Record<Kind, {icon: typeof Info; tone: string}> = {
  success: {icon: CheckCircle2, tone: 'text-emerald-600'},
  error: {icon: XCircle, tone: 'text-red-600'},
  info: {icon: Info, tone: 'text-brand-600'},
};

export function Toaster() {
  const [items, setItems] = useState<Item[]>([]);

  useEffect(() => {
    let next = 0;
    const timers = new Set<ReturnType<typeof setTimeout>>();
    const later = (fn: () => void, ms: number) => {
      const timer = setTimeout(() => {
        timers.delete(timer);
        fn();
      }, ms);
      timers.add(timer);
    };
    const onToast = (e: Event) => {
      const {message, kind} = (e as CustomEvent<{message: string; kind: Kind}>).detail;
      const id = ++next;
      setItems(list => [...list.slice(-2), {id, message, kind, leaving: false}]);
      later(() => setItems(list => list.map(item => (item.id === id ? {...item, leaving: true} : item))), 4200);
      later(() => setItems(list => list.filter(item => item.id !== id)), 4500);
    };
    window.addEventListener(event, onToast);
    return () => {
      window.removeEventListener(event, onToast);
      timers.forEach(clearTimeout);
    };
  }, []);

  const dismiss = (id: number) => {
    setItems(list => list.map(item => (item.id === id ? {...item, leaving: true} : item)));
    setTimeout(() => setItems(list => list.filter(item => item.id !== id)), 300);
  };

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-5 z-[60] flex flex-col items-center gap-2 px-4" role="status" aria-live="polite">
      {items.map(item => {
        const {icon: Icon, tone} = styles[item.kind];
        return (
          <div key={item.id} className={`pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-2xl bg-white/95 px-4 py-3 text-sm text-ink-800 shadow-2xl shadow-ink-900/15 ring-1 ring-slate-200 backdrop-blur ${item.leaving ? 'animate-toast-out' : 'animate-toast-in'}`}>
            <Icon className={`mt-0.5 size-5 shrink-0 ${tone}`} aria-hidden="true" />
            <p className="flex-1 leading-snug">{item.message}</p>
            <button type="button" onClick={() => dismiss(item.id)} aria-label="Fermer la notification" className="-mr-1 rounded-lg p-1 text-ink-500 transition hover:bg-slate-100 hover:text-ink-900">
              <X className="size-4" aria-hidden="true" />
            </button>
          </div>
        );
      })}
    </div>
  );
}
