import type {ReactNode} from 'react';
import Link from 'next/link';
import {Hash, Info} from 'lucide-react';
import {docHref} from '@/lib/developer-docs';
import type {Block, Inline} from '@/lib/markdown';
import {CodeBlock, CodeTabs} from './code-tabs';

function Inlines({items}: {items: Inline[]}): ReactNode {
  return items.map((item, index) => {
    switch (item.type) {
      case 'text':
        return item.value;
      case 'code':
        return <code key={index} className="rounded-md border border-slate-200/80 bg-slate-50 px-1.5 py-0.5 font-mono text-[0.82em] text-brand-700 [overflow-wrap:anywhere]">{item.value}</code>;
      case 'strong':
        return <strong key={index} className="font-semibold text-ink-900"><Inlines items={item.children} /></strong>;
      case 'em':
        return <em key={index}><Inlines items={item.children} /></em>;
      case 'link': {
        const href = docHref(item.href);
        const className = 'font-medium text-brand-700 underline decoration-brand-200 underline-offset-2 hover:decoration-brand-600';
        return /^https?:/.test(href) || href.endsWith('.json')
          ? <a key={index} href={href} className={className}><Inlines items={item.children} /></a>
          : <Link key={index} href={href} className={className}><Inlines items={item.children} /></Link>;
      }
    }
  });
}

const methods: Record<string, string> = {
  GET: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
  POST: 'bg-sky-50 text-sky-700 ring-sky-200',
  PUT: 'bg-amber-50 text-amber-700 ring-amber-200',
  DELETE: 'bg-red-50 text-red-700 ring-red-200',
};

/** Une cellule « GET », « POST »… devient une pastille de couleur. */
function Cell({items}: {items: Inline[]}) {
  const text = items.length === 1 && items[0].type === 'text' ? items[0].value.trim() : null;
  const verbs = text?.split(/\s*\/\s*/) ?? [];
  if (text && verbs.every(verb => methods[verb])) {
    return (
      <span className="flex flex-wrap gap-1">
        {verbs.map(verb => <span key={verb} className={`rounded-md px-1.5 py-0.5 font-mono text-[0.7rem] font-bold ring-1 ring-inset ${methods[verb]}`}>{verb}</span>)}
      </span>
    );
  }
  return <Inlines items={items} />;
}

function Heading({level, id, children}: {level: number; id: string; children: ReactNode}) {
  const anchor = (
    <a href={`#${id}`} className="ml-2 inline-flex align-middle text-slate-300 opacity-0 transition group-hover:opacity-100 hover:text-brand-600" aria-label="Lien vers cette section">
      <Hash className="size-4" aria-hidden="true" />
    </a>
  );
  if (level <= 2) {
    return <h2 id={id} className="group mt-14 scroll-mt-28 text-[1.6rem] font-bold leading-tight tracking-tight text-ink-900 first:mt-0">{children}{anchor}</h2>;
  }
  if (level === 3) {
    return <h3 id={id} className="group mt-9 scroll-mt-28 text-lg font-semibold text-ink-900">{children}{anchor}</h3>;
  }
  return <h4 id={id} className="group mt-6 scroll-mt-28 font-semibold text-ink-800">{children}{anchor}</h4>;
}

export function DocContent({blocks}: {blocks: Block[]}) {
  return (
    <div className="text-[0.95rem] leading-7 text-ink-600 [&_h2+p]:mt-3 [&_h3+p]:mt-2 [&_h2_code]:border-0 [&_h2_code]:bg-brand-50 [&_h2_code]:px-2 [&_h2_code]:text-[0.72em] [&_h2_code]:text-brand-700 [&_h3_code]:border-0 [&_h3_code]:text-[0.8em]">
      {blocks.map((block, index) => {
        switch (block.type) {
          case 'heading':
            return <Heading key={index} level={block.level} id={block.id}><Inlines items={block.children} /></Heading>;
          case 'paragraph':
            return <p key={index} className="my-4"><Inlines items={block.children} /></p>;
          case 'quote':
            return (
              <div key={index} className="my-6 flex gap-3 rounded-xl border border-brand-100 bg-gradient-to-r from-brand-50 to-white px-4 py-3.5 text-ink-800">
                <Info className="mt-1 size-4 shrink-0 text-brand-600" aria-hidden="true" />
                <p><Inlines items={block.children} /></p>
              </div>
            );
          case 'list': {
            const Tag = block.ordered ? 'ol' : 'ul';
            const tasks = block.items.some(item => item.checked !== null);
            return (
              <Tag key={index} className={`my-4 space-y-2 ${tasks ? 'list-none pl-0' : block.ordered ? 'list-decimal pl-6 marker:font-semibold marker:text-brand-600' : 'list-disc pl-6 marker:text-brand-400'}`}>
                {block.items.map((item, position) => (
                  <li key={position} className={tasks ? 'flex items-start gap-2.5' : 'pl-1'}>
                    {tasks && (
                      <span className="mt-1 grid size-4 shrink-0 place-items-center rounded border border-slate-300 bg-white" aria-hidden="true" />
                    )}
                    <span><Inlines items={item.children} /></span>
                  </li>
                ))}
              </Tag>
            );
          }
          case 'table':
            return (
              <div key={index} className="my-6 overflow-x-auto rounded-xl border border-slate-200 shadow-sm shadow-slate-900/[0.03]">
                <table className="w-full border-collapse text-left text-sm [&_td:not(:last-child)_code]:whitespace-nowrap [&_td_code]:border-0 [&_td_code]:bg-slate-100/80 [&_td:last-child]:min-w-[14rem]">
                  <thead className="bg-slate-50/80 text-xs uppercase tracking-wide text-ink-500">
                    <tr>
                      {block.head.map((cell, position) => (
                        <th key={position} scope="col" className="whitespace-nowrap border-b border-slate-200 px-4 py-2.5 font-semibold"><Inlines items={cell} /></th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {block.rows.map((row, position) => (
                      <tr key={position} className="align-top transition-colors hover:bg-slate-50/70">
                        {row.map((cell, column) => (
                          <td key={column} className={`px-4 py-2.5 ${column === 0 ? 'text-ink-800' : ''}`}><Cell items={cell} /></td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            );
          case 'code':
            return <CodeBlock key={index} sample={block.block} />;
          case 'codegroup':
            return <CodeTabs key={index} samples={block.blocks} />;
        }
      })}
    </div>
  );
}
