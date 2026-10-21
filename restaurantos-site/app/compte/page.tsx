import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/order-ui';
import {Account} from './account';

export const metadata: Metadata = {title: 'Mon compte', robots: {index: false, follow: false}};

/** Compte client : connexion par code, points de fidélité et commandes passées. */
export default function AccountPage() {
  return (
    <Suspense fallback={<Spinner />}>
      <Account />
    </Suspense>
  );
}
