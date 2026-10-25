import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/ui';
import {Menu} from './menu';

export const metadata: Metadata = {title: 'La carte'};

export default function MenuPage() {
  return (
    <Suspense fallback={<Spinner />}>
      <Menu />
    </Suspense>
  );
}
