'use client';
import {useId, useState} from 'react';
import {Plus} from 'lucide-react';

/** Questions dépliables : la hauteur de la réponse s'anime, son texte reste dans la page pour les moteurs de recherche. */
export function Accordion({items, variant = 'cards', defaultOpen = null}: {items: {q: string; a: string}[]; variant?: 'cards' | 'list'; defaultOpen?: number | null}) {
  const [open, setOpen] = useState<number | null>(defaultOpen);
  const id = useId();
  return (
    <ul className={variant === 'cards' ? 'space-y-3' : 'divide-y divide-slate-200 rounded-2xl border border-slate-200 bg-white'}>
      {items.map((item, index) => {
        const expanded = open === index;
        return (
          <li key={item.q} className={variant === 'cards' ? `overflow-hidden rounded-xl border transition duration-300 ${expanded ? 'border-brand-200 bg-brand-50/40 shadow-md shadow-brand-900/5' : 'border-slate-200 bg-white hover:border-brand-200'}` : ''}>
            <h3>
              <button
                type="button"
                id={`${id}-q${index}`}
                aria-expanded={expanded}
                aria-controls={`${id}-a${index}`}
                onClick={() => setOpen(expanded ? null : index)}
                className={`flex w-full items-center justify-between gap-4 px-5 py-4 text-left font-semibold text-ink-900 ${variant === 'cards' ? 'text-sm' : ''}`}
              >
                {item.q}
                <span className={`grid size-7 shrink-0 place-items-center rounded-full transition duration-300 ${expanded ? 'rotate-45 bg-brand-600 text-white' : 'bg-slate-100 text-ink-500'}`}>
                  <Plus className="size-4" aria-hidden="true" />
                </span>
              </button>
            </h3>
            <div
              id={`${id}-a${index}`}
              role="region"
              aria-labelledby={`${id}-q${index}`}
              className={`grid transition-[grid-template-rows,opacity] duration-300 ease-out ${expanded ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0'}`}
            >
              <div className="overflow-hidden" inert={!expanded}>
                <p className={`px-5 pb-4 leading-relaxed text-ink-600 ${variant === 'cards' ? 'text-sm' : ''}`}>{item.a}</p>
              </div>
            </div>
          </li>
        );
      })}
    </ul>
  );
}
