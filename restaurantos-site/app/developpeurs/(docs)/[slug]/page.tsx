import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {DocArticle} from '@/components/doc-article';
import {docPages, loadDoc} from '@/lib/developer-docs';

type Props = {params: Promise<{slug: string}>};

export const dynamicParams = false;

export function generateStaticParams() {
  return docPages.map(page => ({slug: page.slug}));
}

export async function generateMetadata({params}: Props): Promise<Metadata> {
  const {slug} = await params;
  const page = docPages.find(item => item.slug === slug);
  if (!page) return {};
  const doc = loadDoc(page.file);
  return {title: `${doc.title} · Développeurs`, description: doc.summary, alternates: {canonical: `/developpeurs/${page.slug}`}};
}

export default async function DeveloperPage({params}: Props) {
  const {slug} = await params;
  const index = docPages.findIndex(item => item.slug === slug);
  if (index < 0) notFound();
  const previous = docPages[index - 1];
  const next = docPages[index + 1];
  return (
    <DocArticle
      doc={loadDoc(docPages[index].file)}
      group={docPages[index].group}
      previous={previous ? {href: `/developpeurs/${previous.slug}`, label: previous.label} : {href: '/developpeurs/documentation', label: 'Sommaire'}}
      next={next ? {href: `/developpeurs/${next.slug}`, label: next.label} : undefined}
    />
  );
}
