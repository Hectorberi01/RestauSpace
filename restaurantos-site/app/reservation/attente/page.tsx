import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/order-ui';
import {WaitlistEntry} from './waiting';

export const metadata: Metadata = {title: 'Liste d’attente', robots: {index: false, follow: false}};

/** Inscription en liste d'attente par son lien (jeton signé dans l'adresse) : suivi et table proposée à accepter. */
export default function WaitlistPage() {
  return (
    <Suspense fallback={<Spinner />}>
      <WaitlistEntry />
    </Suspense>
  );
}
