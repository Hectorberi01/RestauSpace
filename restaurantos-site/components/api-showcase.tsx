'use client';
import {Fragment, useState} from 'react';
import {Check, Copy} from 'lucide-react';

export interface ShowcaseSample {
  label: string;
  request: string;
}

/** Colore une ligne de JSON : clés, chaînes et nombres. */
function JsonLine({line}: {line: string}) {
  const parts = line.split(/("(?:[^"\\]|\\.)*"\s*:?|-?\b\d+(?:\.\d+)?\b|\btrue\b|\bfalse\b|\bnull\b)/g);
  return (
    <>
      {parts.map((part, index) => {
        if (/^".*":?$/.test(part.trim()) && part.trim().endsWith(':')) return <span key={index} className="text-pink-300">{part}</span>;
        if (/^"/.test(part)) return <span key={index} className="text-emerald-300">{part}</span>;
        if (/^-?\d/.test(part) || /^(true|false|null)$/.test(part)) return <span key={index} className="text-amber-300">{part}</span>;
        return <Fragment key={index}>{part}</Fragment>;
      })}
    </>
  );
}

function RequestLine({line}: {line: string}) {
  const parts = line.split(/("[^"]*"|'[^']*'|\$[A-Z_]+)/g);
  return (
    <>
      {parts.map((part, index) => {
        if (/^["']/.test(part)) return <span key={index} className="text-emerald-300">{part}</span>;
        if (/^\$/.test(part)) return <span key={index} className="text-sky-300">{part}</span>;
        return <Fragment key={index}>{part}</Fragment>;
      })}
    </>
  );
}

/** La fenêtre de code de l'accueil : une requête dans plusieurs langages, puis la réponse. */
export function ApiShowcase({samples, response}: {samples: ShowcaseSample[]; response: string}) {
  const [active, setActive] = useState(0);
  const [copied, setCopied] = useState(false);
  const current = samples[active];
  const request = current.request.split('\n');
  const answer = response.split('\n');
  const lines = [...request.map(line => ({kind: 'request' as const, line})), {kind: 'blank' as const, line: ''}, ...answer.map(line => ({kind: 'json' as const, line}))];
  return (
    <div className="overflow-hidden rounded-2xl bg-ink-900 shadow-2xl shadow-brand-900/30 ring-1 ring-white/10">
      <div className="flex items-center justify-between gap-2 border-b border-white/10 px-3 py-2.5">
        <div role="tablist" aria-label="Langage de l’exemple" className="flex gap-1 overflow-x-auto">
          {samples.map((sample, index) => (
            <button
              key={sample.label}
              type="button"
              role="tab"
              aria-selected={index === active}
              onClick={() => setActive(index)}
              className={`whitespace-nowrap rounded-lg px-3 py-1.5 text-xs font-semibold transition ${index === active ? 'bg-brand-600 text-white' : 'text-slate-400 hover:text-white'}`}
            >
              {sample.label}
            </button>
          ))}
        </div>
        <button
          type="button"
          onClick={() => void navigator.clipboard?.writeText(current.request).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          })}
          className="flex shrink-0 items-center gap-1.5 rounded-lg border border-white/15 px-2.5 py-1.5 text-xs font-medium text-slate-300 transition hover:bg-white/10 hover:text-white"
        >
          {copied ? <Check className="size-3.5" aria-hidden="true" /> : <Copy className="size-3.5" aria-hidden="true" />}
          {copied ? 'Copié' : 'Copier'}
        </button>
      </div>
      <pre className="overflow-x-auto py-4 text-[0.78rem] leading-relaxed text-slate-100" aria-label="Exemple de requête et de réponse">
        <code className="grid">
          {lines.map((item, index) => (
            <span key={index} className="grid grid-cols-[2.75rem_minmax(0,1fr)] pr-4">
              <span className="select-none pr-4 text-right text-slate-600">{index + 1}</span>
              <span className="whitespace-pre">{item.kind === 'json' ? <JsonLine line={item.line} /> : item.kind === 'request' ? <RequestLine line={item.line} /> : ' '}</span>
            </span>
          ))}
        </code>
      </pre>
    </div>
  );
}
