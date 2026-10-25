import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/ui';
import {Account} from './account';

export const metadata: Metadata = {title: 'Mon compte', robots: {index: false, follow: false}};

export default function Page() {
  return (
    <Suspense fallback={<Spinner />}>
      <Account />
    </Suspense>
  );
}
