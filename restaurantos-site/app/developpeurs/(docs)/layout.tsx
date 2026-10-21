import type {ReactNode} from 'react';
import Link from 'next/link';
import {ArrowLeft, Code2} from 'lucide-react';
import {DocSearch, type SearchEntry} from '@/components/doc-search';
import {DocsNav} from '@/components/docs-nav';
import {docGroups, docPages, loadDoc} from '@/lib/developer-docs';

function searchIndex(): SearchEntry[] {
  const pages = [{href: '/developpeurs/documentation', file: 'README.md', label: 'Sommaire'}, ...docPages.map(page => ({href: `/developpeurs/${page.slug}`, file: page.file, label: page.label}))];
  return pages.flatMap(page => {
    const doc = loadDoc(page.file);
    return [
      {href: page.href, title: doc.title, page: page.label, section: false},
      ...doc.toc.map(item => ({href: `${page.href}#${item.id}`, title: item.text, page: doc.title, section: true})),
    ];
  });
}

export default function DocumentationLayout({children}: {children: ReactNode}) {
  const groups = docGroups.map(group => ({
    group,
    pages: docPages.filter(page => page.group === group).map(page => ({slug: page.slug, label: page.label})),
  }));
  return (
    <div className="bg-white">
      <div className="border-b border-slate-200/80 bg-gradient-to-b from-brand-50/70 to-white">
        <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-4 sm:px-6 md:flex-row md:items-center md:justify-between">
          <div className="flex items-center gap-3">
            <span className="grid size-10 place-items-center rounded-xl bg-gradient-to-br from-ink-900 to-brand-700 text-white shadow-sm"><Code2 className="size-5" aria-hidden="true" /></span>
            <div>
              <p className="font-semibold text-ink-900">Documentation développeurs</p>
              <Link href="/developpeurs" className="inline-flex items-center gap-1 text-xs font-medium text-ink-500 hover:text-brand-700">
                <ArrowLeft className="size-3" aria-hidden="true" />Espace développeurs
              </Link>
            </div>
          </div>
          <DocSearch entries={searchIndex()} />
        </div>
      </div>
      <div className="mx-auto grid max-w-7xl gap-8 px-4 py-8 sm:px-6 lg:grid-cols-[16rem_minmax(0,1fr)] lg:gap-12 lg:py-10">
        <aside className="lg:border-r lg:border-slate-100 lg:pr-6">
          <DocsNav groups={groups} />
        </aside>
        <div className="min-w-0">{children}</div>
      </div>
    </div>
  );
}
