import {site} from './site';

// Modèles de site de commande : le restaurateur héberge le site, RestauSpace n'héberge que les démos.

export interface SiteTemplate {
  slug: string;
  name: string;
  /** Badge de style sur la carte du catalogue. */
  style: string;
  tagline: string;
  description: string;
  features: string[];
  /** Aperçu dans public/modeles ; absent pour un modèle à venir. */
  preview?: string;
  /** Démo en ligne ; absente tant que le modèle n'est pas disponible. */
  demoUrl?: string;
  status: 'disponible' | 'bientot';
}

const demo = (slug: string) => `${site.demoBaseUrl}/${slug}/`;

export const templates: SiteTemplate[] = [
  {
    slug: 'maquis',
    name: 'Maquis',
    style: 'Chaleureux',
    tagline: 'Couleurs terre et orange, motif wax, cartes de plats généreuses.',
    description:
      'Pensé pour les maquis, cantines et restaurants de cuisine locale : la carte d’abord, la commande en quelques gestes sur téléphone, la réservation et les avis sur la page d’accueil.',
    features: [
      'Carte par catégories, options des plats, plats épuisés grisés',
      'Sur place, à emporter ou en livraison, selon vos réglages',
      'Paiement en ligne ou au comptoir',
      'Suivi de la commande en direct',
      'Réservation de table, avec acompte si vous le demandez',
      'Compte client par code reçu par e-mail',
      'Couleurs, textes et sections réglés dans un seul fichier',
      'Site statique : s’héberge partout',
    ],
    preview: '/modeles/maquis.webp',
    demoUrl: demo('maquis'),
    status: 'disponible',
  },
  {
    slug: 'fast-food',
    name: 'Fast-food',
    style: 'Rapide',
    tagline: 'Menus, formules et commande express, pour la vente à emporter.',
    description: 'Un parcours de commande court, pensé pour le comptoir et la vente à emporter.',
    features: [],
    status: 'bientot',
  },
  {
    slug: 'restaurant',
    name: 'Restaurant',
    style: 'Élégant',
    tagline: 'Une vitrine sobre qui met la réservation et la carte en avant.',
    description: 'Pour les restaurants à table : réservation, carte du moment et avis.',
    features: [],
    status: 'bientot',
  },
];

export const templateBySlug = (slug: string) => templates.find(item => item.slug === slug);

/** Étapes du parcours guidé ; `path` ouvre la page correspondante de la démo. */
export const demoSteps: {title: string; text: string; path?: string}[] = [
  {title: 'Choisissez un plat', text: 'Ouvrez la carte, choisissez le mode (sur place, à emporter, livraison) et ajoutez un plat avec ses options.', path: 'carte/'},
  {title: 'Passez commande', text: 'Ouvrez le panier : récapitulatif, créneau, coordonnées et code promo, comme sur un vrai site.'},
  {title: 'Payez (paiement simulé)', text: 'Choisissez le paiement en ligne : la page de paiement est simulée, aucun débit n’a lieu.'},
  {title: 'Suivez la préparation', text: 'La page de suivi avance seule : commande acceptée, en préparation, puis prête.'},
  {title: 'Réservez une table', text: 'Choisissez une date, un créneau et le nombre de personnes.', path: 'reserver/'},
  {title: 'Créez un compte', text: 'Connectez-vous avec le code reçu par e-mail et retrouvez vos commandes passées.', path: 'compte/'},
];

/** Ce que le site affiche depuis la console, sans redéploiement. */
export const liveUpdates = [
  'La carte, les prix et les photos',
  'Les plats épuisés ou indisponibles',
  'Les horaires et les fermetures exceptionnelles',
  'Le nom, le logo, l’adresse et les établissements',
  'Les avis de vos clients',
  'Le statut de chaque commande',
];

export const requirements = [
  'L’API RestauSpace : incluse dans Pro et Multi-sites, option « API et webhooks » sur Essentiel.',
  'Une application de vente « Web » créée dans la console : elle fournit la clé publique du site.',
  'Un hébergement de fichiers statiques (Cloudflare Pages, Netlify, Vercel ou tout autre).',
];
