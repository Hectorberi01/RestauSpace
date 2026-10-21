'use client';
import {useEffect, useState} from 'react';

/** « Sur cette page » : la section lue est mise en valeur pendant le défilement. */
export function DocToc({items}: {items: {id: string; text: string}[]}) {
  const [current, setCurrent] = useState(items[0]?.id ?? '');
  useEffect(() => {
    const headings = items.map(item => document.getElementById(item.id)).filter((element): element is HTMLElement => element !== null);
    const update = () => {
      const passed = headings.filter(element => element.getBoundingClientRect().top < 140);
      setCurrent((passed.at(-1) ?? headings[0])?.id ?? '');
    };
    update();
    window.addEventListener('scroll', update, {passive: true});
    return () => window.removeEventListener('scroll', update);
  }, [items]);
  return (
    <nav aria-label="Sur cette page" className="sticky top-24 max-h-[calc(100vh-7rem)] overflow-y-auto [scrollbar-width:thin]">
      <p className="text-[0.7rem] font-bold uppercase tracking-[0.12em] text-ink-500">Sur cette page</p>
      <ul className="mt-3 space-y-1 border-l border-slate-200 text-[0.82rem]">
        {items.map(item => (
          <li key={item.id}>
            <a
              href={`#${item.id}`}
              aria-current={current === item.id ? 'location' : undefined}
              className={`-ml-px block border-l-2 py-1 pl-3 leading-snug transition ${current === item.id ? 'border-brand-600 font-medium text-brand-700' : 'border-transparent text-ink-500 hover:border-slate-300 hover:text-ink-900'}`}
            >
              {item.text}
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}
