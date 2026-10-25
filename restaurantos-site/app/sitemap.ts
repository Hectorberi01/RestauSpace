import type {MetadataRoute} from 'next';
import {site} from '@/content/site';
import {docPages} from '@/lib/developer-docs';

export default function sitemap(): MetadataRoute.Sitemap {
  const pages = ['', '/fonctionnalites', '/decouvrir', '/tarifs', '/site-sur-mesure', '/simulateur', '/faq', '/contact', '/confidentialite', '/developpeurs', '/developpeurs/documentation', ...docPages.map(page => `/developpeurs/${page.slug}`)];
  return pages.map(path => ({
    url: `${site.url}${path}`,
    changeFrequency: 'monthly',
    priority: path === '' ? 1 : 0.7,
  }));
}
