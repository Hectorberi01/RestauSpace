import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Spinner} from '@/components/order-ui';
import {Receipt} from './receipt';

export const metadata: Metadata = {title: 'Reçu', robots: {index: false, follow: false}};

/** Reçu d'une commande : le jeton signé vient de l'adresse (lien envoyé par le restaurant). */
export default function ReceiptPage() {
  return (
    <Suspense fallback={<Spinner />}>
      <Receipt />
    </Suspense>
  );
}
