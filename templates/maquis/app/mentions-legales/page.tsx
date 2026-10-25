import type {Metadata} from 'next';
import {Legal} from './legal';

export const metadata: Metadata = {title: 'Mentions légales et confidentialité'};

export default function LegalPage() {
  return <Legal />;
}
