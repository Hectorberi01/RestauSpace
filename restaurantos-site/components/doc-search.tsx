'use client';
import {useEffect, useMemo, useRef, useState} from 'react';
import {useRouter} from 'next/navigation';
import {CornerDownLeft, FileText, Hash, Search} from 'lucide-react';

export interface SearchEntry {
  href: string;
  title: string;
  page: string;
  section: boolean;
}

const fold = (text: string) => text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');

/** Recherche dans les titres et les sections de la documentation ; Ctrl+K (⌘K) pour y aller. */
export function DocSearch({entries}: {entries: SearchEntry[]}) {
  const router = useRouter();
  const input = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState(0);
  const [mac, setMac] = useState(false);

  useEffect(() => {
    setMac(/Mac|iPhone|iPad/.test(navigator.platform));
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        input.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const results = useMemo(() => {
    const words = fold(query).split(/\s+/).filter(Boolean);
    if (words.length === 0) return [];
    return entries
      .map(entry => {
        const haystack = fold(`${entry.title} ${entry.page}`);
        if (!words.every(word => haystack.includes(word))) return null;
        const score = (fold(entry.title).startsWith(words[0]) ? 0 : 1) + (entry.section ? 1 : 0);
        return {entry, score};
      })
      .filter((item): item is {entry: SearchEntry; score: number} => item !== null)
      .sort((a, b) => a.score - b.score)
      .slice(0, 8)
      .map(item => item.entry);
  }, [entries, query]);

  useEffect(() => setSelected(0), [query]);

  const go = (entry: SearchEntry | undefined) => {
    if (!entry) return;
    setOpen(false);
    setQuery('');
    input.current?.blur();
    router.push(entry.href);
  };

  return (
    <div className="relative w-full max-w-md">
      <label className="flex h-10 items-center gap-2 rounded-xl border border-slate-200 bg-white px-3 text-sm shadow-sm transition focus-within:border-brand-400 focus-within:ring-4 focus-within:ring-brand-100">
        <Search className="size-4 shrink-0 text-ink-500" aria-hidden="true" />
        <input
          ref={input}
          type="search"
          value={query}
          onChange={event => {
            setQuery(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onKeyDown={event => {
            if (event.key === 'ArrowDown') {
              event.preventDefault();
              setSelected(index => Math.min(results.length - 1, index + 1));
            } else if (event.key === 'ArrowUp') {
              event.preventDefault();
              setSelected(index => Math.max(0, index - 1));
            } else if (event.key === 'Enter') {
              event.preventDefault();
              go(results[selected]);
            } else if (event.key === 'Escape') {
              setOpen(false);
              input.current?.blur();
            }
          }}
          placeholder="Rechercher dans la documentation…"
          aria-label="Rechercher dans la documentation"
          aria-expanded={open && results.length > 0}
          aria-controls="resultats-documentation"
          role="combobox"
          className="min-w-0 flex-1 bg-transparent text-ink-900 placeholder:text-ink-500 focus:outline-none [&::-webkit-search-cancel-button]:hidden"
        />
        <kbd className="hidden rounded-md border border-slate-200 bg-slate-50 px-1.5 py-0.5 font-sans text-[0.7rem] font-semibold text-ink-500 sm:block">{mac ? '⌘' : 'Ctrl'} K</kbd>
      </label>
      {open && query.trim() !== '' && (
        <ul id="resultats-documentation" role="listbox" className="absolute inset-x-0 top-12 z-50 max-h-96 overflow-y-auto rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl shadow-ink-900/10">
          {results.length === 0 && <li className="px-3 py-6 text-center text-sm text-ink-500">Aucun résultat pour « {query} ».</li>}
          {results.map((entry, index) => (
            <li key={entry.href} role="option" aria-selected={index === selected}>
              <button
                type="button"
                onMouseDown={event => event.preventDefault()}
                onMouseEnter={() => setSelected(index)}
                onClick={() => go(entry)}
                className={`flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm ${index === selected ? 'bg-brand-50' : ''}`}
              >
                {entry.section
                  ? <Hash className="size-4 shrink-0 text-ink-500" aria-hidden="true" />
                  : <FileText className="size-4 shrink-0 text-brand-600" aria-hidden="true" />}
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium text-ink-900">{entry.title}</span>
                  {entry.section && <span className="block truncate text-xs text-ink-500">{entry.page}</span>}
                </span>
                {index === selected && <CornerDownLeft className="size-3.5 text-ink-500" aria-hidden="true" />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
