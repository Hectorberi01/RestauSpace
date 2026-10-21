import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {TableOrdering} from './table-ordering';

type Props = {params: Promise<{token: string}>};

const TOKEN = /^[A-Za-z0-9_-]{16,64}$/;

export const metadata: Metadata = {title: 'Commander à table', robots: {index: false, follow: false}};

export default async function TablePage({params}: Props) {
  const {token} = await params;
  if (!TOKEN.test(token)) notFound();
  return <TableOrdering token={token} />;
}
