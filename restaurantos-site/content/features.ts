export type FeatureIcon =
  | 'orders' | 'kitchen' | 'floor' | 'reservations' | 'menu' | 'stock' | 'customers' | 'loyalty' | 'marketing'
  | 'promotions' | 'payments' | 'notifications' | 'reports' | 'team' | 'locations' | 'integrations' | 'reviews'
  | 'online' | 'delivery';

/** Un visuel : une capture de la console (public/captures), cadrée sur la partie utile. */
export interface Visual {
  src: string;
  /** Point de cadrage (object-position), par exemple « 0% 40% ». */
  focus?: string;
  /** Agrandissement de la zone cadrée, pour les vignettes. */
  zoom?: number;
}

export interface Module {
  id: string;
  icon: FeatureIcon;
  title: string;
  /** Filtre de la page Fonctionnalités. */
  filter: string;
  badge: string;
  /** Une ligne, pour les vignettes de l'accueil. */
  short: string;
  summary: string;
  points: string[];
  visual: Visual;
  /** Cadrage serré pour les petites vignettes (accueil). */
  thumb: Visual;
  /** Photo facultative déposée par l'équipe dans public/photos/<id>.webp : elle remplace la capture dans les vignettes. */
  photo?: string;
  link?: {href: string; label: string};
  /** Ce qui dépend du plan, en une ligne. */
  plans?: string;
  /** Précision affichée sous la liste (par exemple une règle propre à un pays). */
  note?: string;
  group: 'service' | 'carte' | 'clients' | 'gestion';
}

export const modules: Module[] = [
  {
    id: 'commandes',
    icon: 'orders',
    title: 'Commandes',
    filter: 'Commandes',
    badge: 'Incontournable',
    short: 'Sur place, à emporter ou en livraison.',
    summary: 'Toutes vos commandes au même endroit, quel que soit le canal : comptoir, salle, site, application, borne ou plateforme de livraison.',
    points: [
      'Prise de commande sur place, à emporter et en livraison',
      'Commandes du site, de l’application, de la borne et des plateformes',
      'Suivi en direct : nouvelles, acceptées, en préparation, prêtes',
      'Options, variantes, notes par plat et remises tracées',
      'Prix TTC ou HT, taxes calculées ligne par ligne',
      'Tickets imprimés et facture normalisée',
    ],
    visual: {src: '/captures/commandes.webp', focus: '0% 62%', zoom: 1.23},
    thumb: {src: '/captures/commandes.webp', focus: '18% 62%', zoom: 2.6},
    group: 'service',
  },
  {
    id: 'cuisine',
    icon: 'kitchen',
    title: 'Cuisine',
    filter: 'Cuisine',
    badge: 'Indispensable',
    short: 'Écran cuisine et suivi en temps réel.',
    summary: 'Un écran clair pour chaque poste : chaque cuisinier voit ce qu’il doit préparer, dans l’ordre.',
    points: [
      'Tickets répartis par poste (grill, froid, bar…)',
      'Plat par plat : la commande passe « prête » quand tout est prêt',
      'Envoi par service : entrées d’abord, plats ensuite',
      'Alerte sur les commandes en retard',
      'Écran d’appel des commandes prêtes pour vos clients',
      'Mode plein écran pour tablette',
    ],
    visual: {src: '/captures/cuisine.webp', focus: '0% 38%', zoom: 1.23},
    thumb: {src: '/captures/cuisine.webp', focus: '14% 45%', zoom: 2.4},
    group: 'service',
  },
  {
    id: 'reservations',
    icon: 'reservations',
    title: 'Réservations',
    filter: 'Réservations',
    badge: 'Indispensable',
    short: 'Carnet, planning et réservation en ligne.',
    summary: 'Un carnet qui respecte vos horaires et votre capacité, alimenté aussi par votre site.',
    points: [
      'Réservation en ligne depuis votre site et votre application',
      'Carnet du jour en calendrier, en liste ou sur le plan de salle',
      'Pas de surréservation, même pour deux demandes simultanées',
      'Confirmations et rappels par SMS, WhatsApp ou e-mail',
      'Règles : délai minimum, durée, taille des groupes',
      'Arrivées, absences et annulations suivies',
    ],
    visual: {src: '/captures/reservations.webp', focus: '0% 100%', zoom: 1.16},
    thumb: {src: '/captures/reservations.webp', focus: '28% 86%', zoom: 2.4},
    group: 'service',
  },
  {
    id: 'menu',
    icon: 'menu',
    title: 'Menu & produits',
    filter: 'Menu & produits',
    badge: 'Incontournable',
    short: 'Créez et gérez votre carte facilement.',
    summary: 'Votre carte tenue à jour une seule fois, et affichée partout : caisse, site, application et borne.',
    points: [
      'Catégories, cartes et cartes par créneau (midi, soir)',
      'Variantes, options et suppléments',
      'Photos et allergènes des produits',
      'Prix par établissement et par canal de vente',
      'Rupture du jour en un geste',
      'Affichage sur la borne, le site et l’application',
    ],
    visual: {src: '/captures/menu.webp', focus: '0% 100%', zoom: 1.14},
    thumb: {src: '/captures/menu.webp', focus: '8% 96%', zoom: 1.7},
    group: 'carte',
  },
  {
    id: 'salle',
    icon: 'floor',
    title: 'Salle & tables',
    filter: 'Salle & tables',
    badge: 'Gestion',
    short: 'Plan de salle interactif et suivi en temps réel.',
    summary: 'Votre salle telle qu’elle est, avec l’état de chaque table pendant le service.',
    points: [
      'Plan dessiné : formes, tailles et orientation des tables',
      'Zones : salle, terrasse, étage',
      'Table libre, occupée, depuis combien de temps',
      'Déplacement et fusion de tables',
      'QR code par table pour commander depuis le téléphone',
      'Addition partagée entre plusieurs paiements',
    ],
    visual: {src: '/captures/salle.webp', focus: '8% 55%', zoom: 1.18},
    thumb: {src: '/captures/salle.webp', focus: '22% 52%', zoom: 2},
    group: 'service',
  },
  {
    id: 'stock',
    icon: 'stock',
    title: 'Stock',
    filter: 'Stock',
    badge: 'Gestion',
    short: 'Suivi des ingrédients et alertes de réapprovisionnement.',
    summary: 'Le stock se déduit tout seul des recettes à chaque préparation, et vous prévient avant la rupture.',
    points: [
      'Ingrédients, recettes et coût matière',
      'Déduction automatique à chaque plat préparé',
      'Alertes de stock bas',
      'Fournisseurs et bons de commande',
      'Inventaires, pertes et transferts entre établissements',
      'Valeur du stock à tout moment',
    ],
    visual: {src: '/captures/stock.webp', focus: '0% 80%', zoom: 1.18},
    thumb: {src: '/captures/stock.webp', focus: '4% 72%', zoom: 2.2},
    group: 'carte',
  },
  {
    id: 'personnel',
    icon: 'team',
    title: 'Personnel',
    filter: 'Personnel',
    badge: 'Gestion',
    short: 'Rôles, accès et pointage de votre équipe.',
    summary: 'Chacun accède à ce dont il a besoin, et à rien d’autre.',
    points: [
      'Invitation des membres par e-mail',
      'Rôles et permissions détaillés',
      'Affectation par établissement',
      'Pointage des arrivées et des départs',
      'Caisses partagées par l’équipe',
      'Journal des actions sensibles',
    ],
    visual: {src: '/captures/personnel.webp', focus: '0% 75%', zoom: 1.18},
    thumb: {src: '/captures/personnel.webp', focus: '6% 66%', zoom: 2.3},
    group: 'gestion',
  },
  {
    id: 'clients',
    icon: 'customers',
    title: 'Clients',
    filter: 'Clients',
    badge: 'Fidélisation',
    short: 'Historique, préférences et fidélité.',
    summary: 'Connaissez vos habitués et faites-les revenir, dans le respect de leurs données.',
    points: [
      'Fiches clients, étiquettes et notes',
      'Commandes et réservations rattachées',
      'Consentements par canal, avec leur historique',
      'Comptes clients en ligne, connexion par code',
      'Import de votre fichier existant',
      'Anonymisation à la demande du client',
    ],
    visual: {src: '/captures/clients.webp', focus: '0% 70%', zoom: 1.18},
    thumb: {src: '/captures/clients.webp', focus: '4% 56%', zoom: 2.4},
    plans: 'Import, étiquettes et segments à partir de Pro.',
    group: 'clients',
  },
  {
    id: 'marketing',
    icon: 'marketing',
    title: 'Marketing',
    filter: 'Marketing',
    badge: 'Croissance',
    short: 'Promotions, fidélité et campagnes clients.',
    summary: 'Attirez de nouveaux clients et faites revenir les habitués.',
    points: [
      'Campagnes WhatsApp, SMS et e-mail aux clients qui l’acceptent',
      'Promotions et codes promo, avec dates et limites',
      'Programme de fidélité : points et récompenses',
      'Cartes cadeaux et avoirs',
      'Fréquence d’envoi plafonnée pour ne pas lasser',
    ],
    visual: {src: '/captures/marketing.webp', focus: '0% 30%', zoom: 1.23},
    thumb: {src: '/captures/marketing.webp', focus: '4% 36%', zoom: 2.4},
    plans: 'Fidélité et codes promo dans tous les plans ; campagnes et cartes cadeaux à partir de Pro.',
    group: 'clients',
  },
  {
    id: 'rapports',
    icon: 'reports',
    title: 'Rapports',
    filter: 'Rapports',
    badge: 'Analyse',
    short: 'Analysez vos performances.',
    summary: 'Vos chiffres au fil du service, et des rapports prêts pour votre comptable.',
    points: [
      'Tableau de bord en temps réel',
      'Chiffre d’affaires, commandes, panier moyen, couverts',
      'Ventes, cuisine, paiements et réservations',
      'Rapprochement de caisse',
      'Exports Excel, PDF et CSV',
      'Rapports envoyés automatiquement par e-mail',
    ],
    visual: {src: '/captures/rapports.webp', focus: '0% 55%', zoom: 1.16},
    thumb: {src: '/captures/rapports.webp', focus: '4% 40%', zoom: 2.2},
    plans: 'Exports, rapprochement et rapports automatiques à partir de Pro ; rapports consolidés dans Multi-sites.',
    group: 'gestion',
  },
  {
    id: 'etablissements',
    icon: 'locations',
    title: 'Multi-établissements',
    filter: 'Autres',
    badge: 'Expansion',
    short: 'Plusieurs restaurants depuis un seul compte.',
    summary: 'Un compte, plusieurs adresses : chacune garde ses réglages, vous gardez la vue d’ensemble.',
    points: [
      'Horaires, fermetures, fuseau et capacité par établissement',
      'Prix par établissement',
      'Stock suivi séparément',
      'Rapports consolidés ou par établissement',
      'Droits de l’équipe par établissement',
    ],
    visual: {src: '/captures/etablissements.webp', focus: '0% 20%', zoom: 1.25},
    thumb: {src: '/captures/etablissements.webp', focus: '4% 28%', zoom: 2.3},
    plans: '2 établissements dans Pro, 5 dans Multi-sites, et d’autres en option à partir de Pro.',
    group: 'gestion',
  },
  {
    id: 'api',
    icon: 'integrations',
    title: 'API & développeurs',
    filter: 'Autres',
    badge: 'Intégration',
    short: 'Reliez vos propres outils grâce à notre API.',
    summary: 'Construisez votre site ou votre application, et reliez RestauSpace à vos autres logiciels.',
    points: [
      'API documentée pour votre site, votre application ou votre borne',
      'API d’intégration pour la caisse, la comptabilité, les tableaux de bord',
      'Webhooks signés : commandes, paiements, réservations, stock',
      'Clés d’API aux droits limités',
      'Clients prêts à l’emploi : TypeScript, Flutter, Swift, Kotlin',
    ],
    visual: {src: '/captures/integrations.webp', focus: '0% 30%', zoom: 1.23},
    thumb: {src: '/captures/integrations.webp', focus: '6% 34%', zoom: 2.4},
    plans: 'API et webhooks inclus dans Pro et Multi-sites, en option sur Essentiel.',
    link: {href: '/developpeurs', label: 'Espace développeurs'},
    group: 'gestion',
  },
  {
    id: 'caisse',
    icon: 'payments',
    title: 'Encaissement',
    filter: 'Autres',
    badge: 'Incontournable',
    short: 'Paiement en ligne, au comptoir et suivi des encaissements.',
    summary: 'Vos clients paient comme ils le souhaitent, et chaque paiement est rattaché à sa commande.',
    points: [
      'Paiement en ligne par vos prestataires : carte, Apple Pay, Google Pay',
      'Paiements au comptoir notés sur la commande : espèces, carte, titres-restaurant',
      'Remboursements partiels ou complets, toujours tracés',
      'Au Bénin et en zone UEMOA : caisse tactile, Mobile Money, clôture et tickets Z',
    ],
    note: 'En France, gardez votre caisse certifiée NF525 pour l’encaissement : RestauSpace s’utilise à côté, pour la commande en ligne, la cuisine, les réservations, le stock et l’équipe.',
    visual: {src: '/captures/caisse.webp', focus: '50% 30%', zoom: 1.14},
    thumb: {src: '/captures/caisse.webp', focus: '50% 35%', zoom: 1.8},
    group: 'gestion',
  },
  {
    id: 'vente-en-ligne',
    icon: 'online',
    title: 'Site, application et borne',
    filter: 'Autres',
    badge: 'Ventes',
    short: 'Vos clients commandent et réservent en ligne.',
    summary: 'Votre carte en ligne à vos couleurs : commande, paiement, suivi et réservation.',
    points: [
      'Site de commande à vos couleurs',
      'Application mobile et borne en libre-service',
      'Commande à table par QR code',
      'Paiement en ligne ou au comptoir',
      'Suivi de la commande en direct par le client',
    ],
    visual: {src: '/captures/hero-phone.webp', focus: '50% 0%'},
    thumb: {src: '/captures/hero-phone.webp', focus: '50% 40%', zoom: 1.2},
    plans: 'Site inclus dans Pro et Multi-sites, en option sur Essentiel ; application et borne en option.',
    group: 'service',
  },
  {
    id: 'livraison',
    icon: 'delivery',
    title: 'Livraison & retrait',
    filter: 'Autres',
    badge: 'Ventes',
    short: 'Zones, frais et livreurs.',
    summary: 'Livrez sans tableur : zones, frais, créneaux et livreurs.',
    points: [
      'Zones de livraison, frais et minimum de commande',
      'Créneaux et capacité par créneau',
      'Affectation des livreurs et suivi des courses',
      'Commandes des plateformes de livraison',
    ],
    visual: {src: '/captures/livraison.webp', focus: '0% 75%', zoom: 1.16},
    thumb: {src: '/captures/livraison.webp', focus: '20% 75%', zoom: 2.2},
    group: 'service',
  },
  {
    id: 'avis',
    icon: 'reviews',
    title: 'Avis et questionnaires',
    filter: 'Autres',
    badge: 'Fidélisation',
    short: 'Recueillez l’avis de vos clients.',
    summary: 'Écoutez vos clients après le repas, et affichez les meilleurs avis sur votre site.',
    points: [
      'Avis après chaque commande, en ligne ou sur tablette',
      'Alerte à l’équipe en cas de mauvaise note',
      'Questionnaires personnalisés',
      'Publication des avis choisis sur votre site',
    ],
    visual: {src: '/captures/avis.webp', focus: '0% 30%', zoom: 1.18},
    thumb: {src: '/captures/avis.webp', focus: '18% 40%', zoom: 2.4},
    group: 'clients',
  },
];

/** Les douze modules de l'accueil, dans l'ordre. */
export const homeModules = ['commandes', 'cuisine', 'reservations', 'menu', 'salle', 'stock', 'personnel', 'rapports', 'marketing', 'clients', 'etablissements', 'api']
  .map(id => modules.find(item => item.id === id)!);

export const filters = ['Commandes', 'Cuisine', 'Réservations', 'Menu & produits', 'Salle & tables', 'Stock', 'Personnel', 'Clients', 'Marketing', 'Rapports', 'Autres'];
