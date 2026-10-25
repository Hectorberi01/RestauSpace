import type {Metadata} from 'next';
import {Checkout} from './checkout';

export const metadata: Metadata = {title: 'Votre commande', robots: {index: false, follow: false}};

export default function CheckoutPage() {
  return <Checkout />;
}
