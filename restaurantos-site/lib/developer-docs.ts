import fs from 'node:fs';
import path from 'node:path';
import {site} from '@/content/site';
import {parse, plain, slugify, type Block} from './markdown';

/**
 * La documentation développeurs : les fichiers Markdown de `docs/developpeurs` (copiés dans `content/developpeurs` par
 * `pnpm sync:docs`), lus au moment de la construction. `{{API_URL}}` y devient l'adresse de l'API de ce site.
 */
export interface DocPage {
  slug: string;
  file: string;
  group: string;
  label: string;
}

export const docPages: DocPage[] = [
  {slug: 'prise-en-main', file: '01-prise-en-main.md', group: 'Démarrer', label: 'Prise en main'},
  {slug: 'conventions', file: '02-conventions.md', group: 'Démarrer', label: 'Conventions communes'},
  {slug: 'guide-site-restaurant', file: '14-guide-site-restaurant.md', group: 'Démarrer', label: 'Construire le site d’un restaurant'},
  {slug: 'vente-catalogue', file: '03-vente-catalogue.md', group: 'Vente en ligne', label: 'Restaurant et carte'},
  {slug: 'vente-commande', file: '04-vente-commande.md', group: 'Vente en ligne', label: 'Panier et commande'},
  {slug: 'vente-paiement', file: '05-vente-paiement.md', group: 'Vente en ligne', label: 'Paiement en ligne'},
  {slug: 'vente-comptes-clients', file: '06-vente-comptes-clients.md', group: 'Vente en ligne', label: 'Comptes clients'},
  {slug: 'vente-reservations-questionnaires', file: '07-vente-reservations-questionnaires.md', group: 'Vente en ligne', label: 'Réservations et avis'},
  {slug: 'vente-borne', file: '08-vente-borne.md', group: 'Vente en ligne', label: 'Borne de commande'},
  {slug: 'integrations-cles', file: '09-integrations-cles.md', group: 'Intégration', label: 'Clés d’API'},
  {slug: 'integrations-ressources', file: '10-integrations-ressources.md', group: 'Intégration', label: 'Ressources'},
  {slug: 'webhooks', file: '11-webhooks.md', group: 'Intégration', label: 'Webhooks'},
  {slug: 'reference-erreurs', file: '12-reference-erreurs.md', group: 'Référence', label: 'Codes d’erreur et routes'},
  {slug: 'exemples-clients', file: '13-exemples-clients.md', group: 'Référence', label: 'Clients prêts à l’emploi'},
];

export const docGroups = [...new Set(docPages.map(page => page.group))];

const directory = path.join(process.cwd(), 'content', 'developpeurs');

/** Les liens entre fichiers Markdown deviennent des liens du site. */
export function docHref(href: string): string {
  if (/^(https?:|mailto:)/.test(href)) return href;
  const [file, hash] = href.split('#');
  const anchor = hash ? `#${slugify(decodeURIComponent(hash))}` : '';
  if (href.startsWith('#')) return anchor;
  if (file === 'README.md' || file === '') return `/developpeurs/documentation${anchor}`;
  if (file.endsWith('.json')) return `/developpeurs/${file}`;
  const page = docPages.find(item => item.file === file);
  return page ? `/developpeurs/${page.slug}${anchor}` : href;
}

export interface LoadedDoc {
  title: string;
  summary: string;
  blocks: Block[];
  toc: {id: string; text: string}[];
}

export function loadDoc(file: string): LoadedDoc {
  const source = fs.readFileSync(path.join(directory, file), 'utf8').replaceAll('{{API_URL}}', site.apiUrl);
  const blocks = parse(source);
  const titleIndex = blocks.findIndex(block => block.type === 'heading' && block.level === 1);
  const title = titleIndex >= 0 ? (blocks[titleIndex] as Extract<Block, {type: 'heading'}>).text : 'Documentation';
  const quoteIndex = blocks.findIndex(block => block.type === 'quote');
  const summary = quoteIndex >= 0 ? plain((blocks[quoteIndex] as Extract<Block, {type: 'quote'}>).children) : '';
  const body = blocks.filter((_, index) => index !== titleIndex && index !== quoteIndex);
  const toc = body.flatMap(block => (block.type === 'heading' && block.level === 2 ? [{id: block.id, text: block.text}] : []));
  return {title, summary, blocks: body, toc};
}

/** Chiffres de la page d'accueil des développeurs, comptés dans la documentation elle-même. */
export function docFigures() {
  const read = (file: string) => fs.readFileSync(path.join(directory, file), 'utf8');
  const section = (source: string, title: string) => {
    const start = source.indexOf(title);
    if (start < 0) return '';
    const end = source.indexOf('\n## ', start + title.length);
    return source.slice(start, end < 0 ? undefined : end);
  };
  const routes = section(read('12-reference-erreurs.md'), '## Routes de la vente en ligne')
    .split('\n').filter(line => /^\| (GET|POST|PUT|DELETE) \|/.test(line)).length;
  const events = section(read('11-webhooks.md'), '## Événements')
    .split('\n').filter(line => /^\| `[a-z]/.test(line)).length;
  return {routes, events, languages: 5, pages: docPages.length + 1};
}
