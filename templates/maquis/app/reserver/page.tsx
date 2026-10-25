import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/ui';
import {Booking} from './booking';

export const metadata: Metadata = {title: 'Réserver une table'};

export default function Page() {
  return (
    <Suspense fallback={<Spinner />}>
      <Booking />
    </Suspense>
  );
}
