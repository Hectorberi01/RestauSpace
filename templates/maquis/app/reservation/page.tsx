import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/ui';
import {ManageBooking} from './manage';

export const metadata: Metadata = {title: 'Ma réservation', robots: {index: false, follow: false}};

export default function Page() {
  return (
    <Suspense fallback={<Spinner />}>
      <ManageBooking />
    </Suspense>
  );
}
