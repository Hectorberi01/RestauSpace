import type {Metadata, Viewport} from 'next';
import '@fontsource-variable/inter';
import '@fontsource/lora/500.css';
import '@fontsource/lora/700.css';
import '@fontsource/lora/600-italic.css';
import '@fontsource/lora/700-italic.css';
import {config} from '@/lib/config';
import {SiteProvider} from '@/components/site-provider';
import {Header} from '@/components/header';
import {Footer} from '@/components/footer';
import {TopBar} from '@/components/top-bar';
import './globals.css';

export const metadata: Metadata = {
  title: {default: 'Commander en ligne', template: '%s'},
  description: 'Commandez en ligne, à emporter ou en livraison, et réservez votre table.',
};

export const viewport: Viewport = {themeColor: config.theme.background};

const theme = {
  '--primary': config.theme.primary,
  '--accent': config.theme.accent,
  '--dark': config.theme.dark,
  '--background': config.theme.background,
} as React.CSSProperties;

export default function RootLayout({children}: {children: React.ReactNode}) {
  return (
    <html lang="fr" style={theme}>
      <body className="flex min-h-screen flex-col overflow-x-clip">
        <a href="#contenu" className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[80] focus:rounded-lg focus:bg-white focus:px-4 focus:py-2 focus:shadow">
          Aller au contenu
        </a>
        <SiteProvider>
          <TopBar>
            <Header />
          </TopBar>
          <main id="contenu" className="flex-1">{children}</main>
          <Footer />
        </SiteProvider>
      </body>
    </html>
  );
}
