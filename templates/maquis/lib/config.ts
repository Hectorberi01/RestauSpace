import raw from '@/site.config.json';

// Configuration du site : site.config.json, remplacé par les variables NEXT_PUBLIC_* si elles sont définies au build.

export type SectionName = 'hero' | 'featured' | 'booking' | 'hours' | 'reviews';

interface RawConfig {
  apiUrl?: string;
  publishableKey?: string;
  demo?: boolean;
  theme?: Partial<Record<'primary' | 'accent' | 'dark' | 'background', string>>;
  texts?: Partial<Record<'heroTitle' | 'heroAccent' | 'heroText' | 'featuredTitle' | 'featuredText', string>>;
  sections?: string[];
  featured?: {section?: string | null; count?: number};
  links?: Partial<Record<'instagram' | 'facebook' | 'whatsapp', string | null>>;
  legal?: Partial<Record<'company' | 'registration' | 'address' | 'publicationDirector' | 'host', string | null>>;
}

const file = raw as RawConfig;
const HEX = /^#[0-9a-f]{6}$/i;
const color = (value: string | undefined, fallback: string) => (value && HEX.test(value) ? value : fallback);
const known: SectionName[] = ['hero', 'featured', 'booking', 'hours', 'reviews'];

const envDemo = process.env.NEXT_PUBLIC_DEMO;

export const config = {
  apiUrl: (process.env.NEXT_PUBLIC_API_URL || file.apiUrl || '').replace(/\/+$/, ''),
  publishableKey: process.env.NEXT_PUBLIC_STOREFRONT_KEY || file.publishableKey || '',
  demo: envDemo !== undefined && envDemo !== '' ? envDemo === '1' || envDemo === 'true' : file.demo === true,
  turnstileSiteKey: process.env.NEXT_PUBLIC_TURNSTILE_SITE_KEY || null,
  basePath: process.env.NEXT_PUBLIC_BASE_PATH ?? '',
  theme: {
    primary: color(file.theme?.primary, '#b4441a'),
    accent: color(file.theme?.accent, '#f4b23c'),
    dark: color(file.theme?.dark, '#7a2e12'),
    background: color(file.theme?.background, '#fbf3e6'),
  },
  texts: {
    heroTitle: file.texts?.heroTitle ?? '',
    heroAccent: file.texts?.heroAccent ?? '',
    heroText: file.texts?.heroText ?? '',
    featuredTitle: file.texts?.featuredTitle || 'Nos incontournables',
    featuredText: file.texts?.featuredText ?? '',
  },
  sections: (file.sections ?? known).filter((name): name is SectionName => known.includes(name as SectionName)),
  featured: {section: file.featured?.section ?? null, count: Math.min(12, Math.max(1, file.featured?.count ?? 4))},
  links: {
    instagram: file.links?.instagram ?? null,
    facebook: file.links?.facebook ?? null,
    whatsapp: file.links?.whatsapp ?? null,
  },
  legal: {
    company: file.legal?.company ?? null,
    registration: file.legal?.registration ?? null,
    address: file.legal?.address ?? null,
    publicationDirector: file.legal?.publicationDirector ?? null,
    host: file.legal?.host ?? null,
  },
};

/** La clé d'exemple ou une clé vide : le site n'est pas encore branché. */
export const isConfigured = () => config.apiUrl !== '' && /^pk_(live|test)_[A-Za-z0-9]+$/.test(config.publishableKey);

export const hasSection = (name: SectionName) => config.sections.includes(name);

/** Adresse absolue d'une page du site (pages de retour du paiement). */
export const absoluteUrl = (path: string) => `${window.location.origin}${config.basePath}${path}`;
