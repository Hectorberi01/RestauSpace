import type {Metadata} from 'next';
import {Booking} from './booking';

export const metadata: Metadata = {title: 'Réserver une table', robots: {index: false, follow: false}};

export default function BookingPage() {
  return <Booking />;
}
