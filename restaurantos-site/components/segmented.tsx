'use client';
import {useLayoutEffect, useRef, useState, type KeyboardEvent} from 'react';

/** Un sélecteur à onglets dont l'indicateur glisse sous l'option active ; les flèches du clavier changent d'option. */
export function Segmented<T extends string | boolean>({options, value, onChange, label, role = 'radio', controls, className = ''}: {
  options: {value: T; label: string}[];
  value: T;
  onChange: (value: T) => void;
  label: string;
  role?: 'radio' | 'tab';
  controls?: string;
  className?: string;
}) {
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const [box, setBox] = useState<{left: number; width: number} | null>(null);
  const index = Math.max(0, options.findIndex(option => option.value === value));

  useLayoutEffect(() => {
    const measure = () => {
      const button = buttons.current[index];
      if (button) setBox({left: button.offsetLeft, width: button.offsetWidth});
    };
    measure();
    const observer = new ResizeObserver(measure);
    buttons.current.forEach(button => button && observer.observe(button));
    return () => observer.disconnect();
  }, [index, options.length]);

  const onKey = (event: KeyboardEvent) => {
    const delta = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;
    if (!delta) return;
    event.preventDefault();
    const next = (index + delta + options.length) % options.length;
    onChange(options[next].value);
    buttons.current[next]?.focus();
  };

  return (
    <div role={role === 'tab' ? 'tablist' : 'radiogroup'} aria-label={label} onKeyDown={onKey} className={`relative inline-flex max-w-full rounded-xl bg-slate-100 p-1 ${className}`}>
      {box && <span aria-hidden="true" className="absolute inset-y-1 rounded-lg bg-white shadow-sm ring-1 ring-slate-200/60 transition-[left,width] duration-300 ease-[cubic-bezier(0.2,0.8,0.2,1)]" style={{left: box.left, width: box.width}} />}
      {options.map((option, i) => {
        const active = i === index;
        return (
          <button
            key={String(option.value)}
            ref={element => {
              buttons.current[i] = element;
            }}
            type="button"
            role={role}
            {...(role === 'tab' ? {'aria-selected': active, 'aria-controls': controls} : {'aria-checked': active})}
            tabIndex={active ? 0 : -1}
            onClick={() => onChange(option.value)}
            className={`relative z-10 whitespace-nowrap rounded-lg px-3 py-2 text-sm font-semibold transition-colors duration-200 sm:px-4 ${active ? 'text-ink-900' : 'text-ink-600 hover:text-ink-900'} ${box ? '' : active ? 'bg-white shadow-sm' : ''}`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
