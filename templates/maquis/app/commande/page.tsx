import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/ui';
import {Tracking} from './tracking';

export const metadata: Metadata = {title: 'Suivi de commande', robots: {index: false, follow: false}};

/** Suivi d'une commande : le jeton vient de l'adresse (?jeton=…), page de retour du paiement comprise. */
export default function TrackingPage() {
  return (
    <Suspense fallback={<Spinner />}>
      <Tracking />
    </Suspense>
  );
}
