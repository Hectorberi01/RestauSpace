import type {Metadata, Viewport} from 'next';
import {Kiosk} from './kiosk';

export const metadata: Metadata = {
  title: 'Borne de commande',
  robots: {index: false, follow: false},
  manifest: '/borne.webmanifest',
  appleWebApp: {capable: true, title: 'Borne', statusBarStyle: 'black-translucent'},
};

export const viewport: Viewport = {themeColor: '#0f172a', width: 'device-width', initialScale: 1, maximumScale: 1, userScalable: false};

export default function KioskPage() {
  return <Kiosk />;
}
