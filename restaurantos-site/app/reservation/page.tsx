import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/order-ui';
import {ManageBooking} from './manage';

export const metadata: Metadata = {title: 'Ma réservation', robots: {index: false, follow: false}};

/** Réservation par son lien de gestion (jeton signé dans l'adresse). */
export default function ReservationPage() {
  return (
    <Suspense fallback={<Spinner />}>
      <ManageBooking />
    </Suspense>
  );
}
