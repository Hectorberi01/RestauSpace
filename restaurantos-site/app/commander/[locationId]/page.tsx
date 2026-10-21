import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {Ordering} from './ordering';

type Props = {params: Promise<{locationId: string}>};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const metadata: Metadata = {title: 'Commander en ligne', robots: {index: false, follow: false}};

export default async function OrderPage({params}: Props) {
  const {locationId} = await params;
  if (!GUID.test(locationId)) notFound();
  return <Ordering locationId={locationId} />;
}
