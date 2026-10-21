import type {Metadata} from 'next';
import {Locations} from './locations';

export const metadata: Metadata = {title: 'Commander en ligne', robots: {index: false, follow: false}};

export default function OrderHomePage() {
  return <Locations />;
}
