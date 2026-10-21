'use client';
import {useEffect, useId, useState} from 'react';
import {Check, Copy} from 'lucide-react';
import {highlight} from '@/lib/highlight';
import {toast} from './toast';

export interface CodeSample {
  lang: string;
  code: string;
}

const labels: Record<string, string> = {
  bash: 'curl',
  sh: 'Shell',
  ts: 'TypeScript',
  js: 'JavaScript',
  dart: 'Flutter (Dart)',
  swift: 'Swift',
  kotlin: 'Kotlin',
  json: 'JSON',
  http: 'HTTP',
  text: 'Texte',
};

export const languageLabel = (lang: string) => labels[lang] ?? lang;

const storageKey = 'developpeurs.lang';
const eventName = 'developpeurs-lang';

function CopyButton({code}: {code: string}) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={() => {
        void navigator.clipboard?.writeText(code).then(() => {
          setCopied(true);
          toast('Code copié dans le presse-papiers.');
          setTimeout(() => setCopied(false), 1500);
        }, () => toast('Impossible de copier le code : sélectionnez-le à la main.', 'error'));
      }}
      className="btn-motion flex shrink-0 items-center gap-1.5 rounded-lg border border-white/10 px-2.5 py-1 text-xs font-medium text-slate-300 hover:bg-white/10 hover:text-white"
    >
      {copied ? <Check className="animate-pop size-3.5 text-emerald-400" aria-hidden="true" /> : <Copy className="size-3.5" aria-hidden="true" />}
      {copied ? 'Copié' : 'Copier'}
    </button>
  );
}

function Pre({code, lang}: {code: string; lang: string}) {
  return (
    <pre className="overflow-x-auto px-5 py-4 font-mono text-[0.8rem] leading-[1.7] text-slate-100">
      <code>{highlight(code, lang)}</code>
    </pre>
  );
}

/** Un bloc de code seul, avec son langage et un bouton « Copier ». */
export function CodeBlock({sample}: {sample: CodeSample}) {
  return (
    <div className="my-6 overflow-hidden rounded-2xl bg-[#0b1020] shadow-lg shadow-ink-900/10 ring-1 ring-white/5">
      <div className="flex items-center justify-between gap-2 border-b border-white/[0.07] bg-white/[0.03] px-3 py-2">
        <span className="flex items-center gap-2 pl-1 text-xs font-semibold text-slate-400">
          <span className="flex gap-1" aria-hidden="true"><span className="size-2 rounded-full bg-slate-600" /><span className="size-2 rounded-full bg-slate-600" /><span className="size-2 rounded-full bg-slate-600" /></span>
          <span className="ml-1">{languageLabel(sample.lang)}</span>
        </span>
        <CopyButton code={sample.code} />
      </div>
      <Pre code={sample.code} lang={sample.lang} />
    </div>
  );
}

/** Le même exemple dans plusieurs langages ; le langage choisi est gardé pour tous les exemples de la documentation. */
export function CodeTabs({samples}: {samples: CodeSample[]}) {
  const id = useId();
  const [active, setActive] = useState(0);

  useEffect(() => {
    const follow = (lang: string | null) => {
      const index = lang ? samples.findIndex(sample => sample.lang === lang) : -1;
      if (index >= 0) setActive(index);
    };
    try {
      follow(window.localStorage.getItem(storageKey));
    } catch {
      // Stockage indisponible : premier onglet.
    }
    const listener = (event: Event) => follow((event as CustomEvent<string>).detail);
    window.addEventListener(eventName, listener);
    return () => window.removeEventListener(eventName, listener);
  }, [samples]);

  const choose = (index: number) => {
    setActive(index);
    const lang = samples[index].lang;
    try {
      window.localStorage.setItem(storageKey, lang);
    } catch {
      // Le choix vaut pour cette page.
    }
    window.dispatchEvent(new CustomEvent(eventName, {detail: lang}));
  };

  const current = samples[active] ?? samples[0];
  return (
    <div className="my-6 overflow-hidden rounded-2xl bg-[#0b1020] shadow-lg shadow-ink-900/10 ring-1 ring-white/5">
      <div className="flex items-center justify-between gap-2 border-b border-white/[0.07] bg-white/[0.03] px-2 py-2">
        <div role="tablist" aria-label="Langage de l’exemple" className="flex gap-1 overflow-x-auto">
          {samples.map((sample, index) => (
            <button
              key={sample.lang}
              id={`${id}-tab-${index}`}
              type="button"
              role="tab"
              aria-selected={index === active}
              aria-controls={`${id}-panel`}
              onClick={() => choose(index)}
              onKeyDown={event => {
                if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
                  event.preventDefault();
                  const next = (index + (event.key === 'ArrowRight' ? 1 : samples.length - 1)) % samples.length;
                  choose(next);
                  document.getElementById(`${id}-tab-${next}`)?.focus();
                }
              }}
              tabIndex={index === active ? 0 : -1}
              className={`whitespace-nowrap rounded-lg px-3 py-1.5 text-xs font-semibold transition ${index === active ? 'bg-brand-600 text-white shadow-sm' : 'text-slate-400 hover:bg-white/5 hover:text-slate-100'}`}
            >
              {languageLabel(sample.lang)}
            </button>
          ))}
        </div>
        <CopyButton code={current.code} />
      </div>
      <div id={`${id}-panel`} role="tabpanel" aria-labelledby={`${id}-tab-${active}`}>
        <Pre code={current.code} lang={current.lang} />
      </div>
    </div>
  );
}
