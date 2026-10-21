import type {Metadata, Viewport} from 'next';
import '@fontsource-variable/inter';
import {Header} from '@/components/header';
import {Footer} from '@/components/footer';
import {MarketingOnly} from '@/components/marketing-only';
import {Motion} from '@/components/motion';
import {DemoModal} from '@/components/demo-modal';
import {Toaster} from '@/components/toast';
import {LanguageBar} from '@/components/order-ui';
import {site} from '@/content/site';
import './globals.css';

export const metadata: Metadata = {
  metadataBase: new URL(site.url),
  title: {default: `${site.name} · Logiciel de gestion pour restaurants`, template: `%s · ${site.name}`},
  description: site.description,
  icons: {icon: '/favicon.svg'},
  openGraph: {type: 'website', locale: 'fr_FR', siteName: site.name, title: site.name, description: site.description},
  alternates: {canonical: '/'},
};

export const viewport: Viewport = {themeColor: '#5b3df5'};

export default function RootLayout({children}: {children: React.ReactNode}) {
  return (
    <html lang="fr">
      <body className="flex min-h-screen flex-col font-sans">
        <a href="#contenu" className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-white focus:px-4 focus:py-2 focus:shadow">
          Aller au contenu
        </a>
        <MarketingOnly><Header /></MarketingOnly>
        <LanguageBar />
        <main id="contenu" className="flex-1">{children}</main>
        <MarketingOnly><Footer /><Motion /><DemoModal /></MarketingOnly>
        <Toaster />
      </body>
    </html>
  );
}
