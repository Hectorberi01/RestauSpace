import type {Metadata, Viewport} from 'next';
import {connection} from 'next/server';
import '@fontsource-variable/inter';
import {ServiceWorker} from '@/components/service-worker';
import {ToastProvider} from '@/components/ui';
import './globals.css';

export const metadata: Metadata = {
  title: {default: 'RestauSpace', template: '%s · RestauSpace'},
  description: 'Console de gestion du restaurant.',
  icons: {icon: '/favicon.svg', apple: '/icon-192.png'},
  robots: {index: false, follow: false},
};

export const viewport: Viewport = {themeColor: '#6b3ee6'};

export default async function RootLayout({children}: {children: React.ReactNode}) {
  await connection();
  return (
    <html lang="fr">
      <body className="min-h-dvh font-sans">
        <ToastProvider>{children}</ToastProvider>
        <ServiceWorker />
      </body>
    </html>
  );
}
