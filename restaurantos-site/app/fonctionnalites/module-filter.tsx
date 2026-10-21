'use client';
import {useState, type ReactNode} from 'react';

/** Les cartes des modules, filtrées par thème. */
export function ModuleFilter({filters, cards}: {filters: string[]; cards: {id: string; filter: string; node: ReactNode}[]}) {
  const [current, setCurrent] = useState<string | null>(null);
  const shown = current ? cards.filter(card => card.filter === current) : cards;
  const chip = (active: boolean) =>
    `whitespace-nowrap rounded-xl px-4 py-2 text-sm font-semibold transition ${active ? 'bg-brand-600 text-white shadow-md shadow-brand-600/25' : 'border border-slate-200 bg-white text-ink-600 hover:border-brand-200 hover:text-brand-700'}`;
  return (
    <>
      <div className="sticky top-16 z-30 border-y border-slate-200/70 bg-white/90 backdrop-blur">
        <div role="toolbar" aria-label="Filtrer les modules" className="mx-auto flex max-w-7xl gap-2 overflow-x-auto px-4 py-3 sm:px-6">
          <button type="button" aria-pressed={current === null} className={chip(current === null)} onClick={() => setCurrent(null)}>Toutes</button>
          {filters.map(filter => (
            <button key={filter} type="button" aria-pressed={current === filter} className={chip(current === filter)} onClick={() => setCurrent(filter)}>{filter}</button>
          ))}
        </div>
      </div>
      <div key={current ?? 'toutes'} data-reveal-stagger className="mx-auto grid max-w-7xl gap-6 px-4 py-10 sm:px-6 lg:grid-cols-2">
        {shown.map(card => <div key={card.id} id={card.id} className="scroll-mt-36">{card.node}</div>)}
      </div>
    </>
  );
}
