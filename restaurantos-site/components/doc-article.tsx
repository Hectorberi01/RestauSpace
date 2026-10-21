import type {ReactNode} from 'react';
import Link from 'next/link';
import {ArrowLeft, ArrowRight, ChevronRight, Clock} from 'lucide-react';
import type {LoadedDoc} from '@/lib/developer-docs';
import type {Block, Inline} from '@/lib/markdown';
import {DocContent} from './doc-content';
import {DocToc} from './doc-toc';

interface Neighbour {
  href: string;
  label: string;
}

const words = (items: Inline[]): number =>
  items.reduce((total, item) => total + (item.type === 'text' || item.type === 'code' ? item.value.split(/\s+/).filter(Boolean).length : words(item.children)), 0);

/** Temps de lecture approximatif (200 mots par minute, le code compte moitié). */
function readingMinutes(blocks: Block[]): number {
  let count = 0;
  for (const block of blocks) {
    if (block.type === 'paragraph' || block.type === 'quote' || block.type === 'heading') count += words(block.children);
    else if (block.type === 'list') count += block.items.reduce((total, item) => total + words(item.children), 0);
    else if (block.type === 'table') count += block.rows.flat().reduce((total, cell) => total + words(cell), 0);
    else if (block.type === 'code') count += block.block.code.split(/\s+/).length / 2;
    else if (block.type === 'codegroup') count += block.blocks[0].code.split(/\s+/).length / 2;
  }
  return Math.max(1, Math.round(count / 200));
}

/** Une page de la documentation : fil d'Ariane, titre, résumé, contenu, sommaire de la page et pages voisines. */
export function DocArticle({doc, group, previous, next, intro}: {doc: LoadedDoc; group?: string; previous?: Neighbour; next?: Neighbour; intro?: ReactNode}) {
  return (
    <div className="grid gap-10 xl:grid-cols-[minmax(0,1fr)_14rem]">
      <article className="min-w-0 max-w-3xl">
        <nav aria-label="Fil d’Ariane" className="flex flex-wrap items-center gap-1 text-xs font-medium text-ink-500">
          <Link href="/developpeurs/documentation" className="hover:text-brand-700">Documentation</Link>
          {group && <><ChevronRight className="size-3.5 text-slate-300" aria-hidden="true" /><span>{group}</span></>}
        </nav>
        <h1 className="mt-3 text-3xl font-bold tracking-tight text-ink-900 sm:text-[2.6rem] sm:leading-[1.1]">{doc.title}</h1>
        {doc.summary && <p className="mt-4 text-lg leading-relaxed text-ink-600">{doc.summary}</p>}
        <p className="mt-4 flex items-center gap-1.5 text-xs font-medium text-ink-500">
          <Clock className="size-3.5" aria-hidden="true" />{readingMinutes(doc.blocks)} min de lecture
        </p>
        {intro && <div className="mt-8">{intro}</div>}
        <div className="mt-10 border-t border-slate-100 pt-10">
          <DocContent blocks={doc.blocks} />
        </div>
        {(previous || next) && (
          <nav aria-label="Pages voisines" className="mt-16 grid gap-3 sm:grid-cols-2">
            {previous ? (
              <Link href={previous.href} className="group rounded-2xl border border-slate-200 p-5 transition hover:border-brand-300 hover:shadow-lg hover:shadow-brand-900/5">
                <span className="flex items-center gap-1 text-xs font-medium text-ink-500"><ArrowLeft className="size-3.5 transition group-hover:-translate-x-0.5" aria-hidden="true" />Précédent</span>
                <span className="mt-1.5 block font-semibold text-ink-900 group-hover:text-brand-700">{previous.label}</span>
              </Link>
            ) : <span />}
            {next && (
              <Link href={next.href} className="group rounded-2xl border border-slate-200 p-5 text-right transition hover:border-brand-300 hover:shadow-lg hover:shadow-brand-900/5">
                <span className="flex items-center justify-end gap-1 text-xs font-medium text-ink-500">Suivant<ArrowRight className="size-3.5 transition group-hover:translate-x-0.5" aria-hidden="true" /></span>
                <span className="mt-1.5 block font-semibold text-ink-900 group-hover:text-brand-700">{next.label}</span>
              </Link>
            )}
          </nav>
        )}
      </article>
      {doc.toc.length > 1 && (
        <aside className="hidden xl:block">
          <DocToc items={doc.toc} />
        </aside>
      )}
    </div>
  );
}
