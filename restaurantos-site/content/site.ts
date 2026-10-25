// Coordonnées et informations légales : À COMPLÉTER avant la mise en ligne.
// Une valeur vide masque l'élément correspondant sur le site (bouton WhatsApp, téléphone, adresse...).
export const site = {
  name: 'RestauSpace',
  url: process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3002',
  adminUrl: process.env.NEXT_PUBLIC_ADMIN_URL ?? 'http://localhost:3000/admin',
  consoleUrl: process.env.NEXT_PUBLIC_CONSOLE_URL ?? 'http://localhost:3001/connexion',
  // Inscription en ligne dans la console : plan et pays y sont repris de l'adresse.
  signupUrl: process.env.NEXT_PUBLIC_SIGNUP_URL ?? 'http://localhost:3001/inscription',
  apiUrl: (process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5080').replace(/\/+$/, ''),
  tagline: 'Le logiciel de gestion qui fait tourner votre restaurant.',
  description:
    'Commandes, cuisine, salle, réservations, stocks, clients et fidélité dans un seul logiciel, pour les restaurants au Bénin, en zone UEMOA et en France.',
  contact: {
    email: process.env.NEXT_PUBLIC_CONTACT_EMAIL ?? '',
    whatsapp: process.env.NEXT_PUBLIC_CONTACT_WHATSAPP ?? '',
    phoneBenin: process.env.NEXT_PUBLIC_CONTACT_PHONE_BJ ?? '',
    phoneFrance: process.env.NEXT_PUBLIC_CONTACT_PHONE_FR ?? '',
  },
  // Adresse d'envoi d'un formulaire d'inscription à la lettre d'information (Brevo, Mailchimp...) : vide, le formulaire est masqué.
  newsletterUrl: process.env.NEXT_PUBLIC_NEWSLETTER_URL ?? '',
  // Réseaux sociaux : un lien vide masque l'icône.
  social: {
    linkedin: process.env.NEXT_PUBLIC_SOCIAL_LINKEDIN ?? '',
    facebook: process.env.NEXT_PUBLIC_SOCIAL_FACEBOOK ?? '',
    x: process.env.NEXT_PUBLIC_SOCIAL_X ?? '',
    youtube: process.env.NEXT_PUBLIC_SOCIAL_YOUTUBE ?? '',
    github: process.env.NEXT_PUBLIC_SOCIAL_GITHUB ?? '',
  },
  legal: {
    company: 'HBA Tech et Trade',
    form: 'entreprise individuelle',
    registration: 'RCCM Cotonou RB/ABC/26 A 142835',
    address: 'Godomey (Yénandjro), Abomey-Calavi, Atlantique, Bénin',
    publisher: 'Hector Adjakpa',
    host: 'OVH SAS, 2 rue Kellermann, 59100 Roubaix, France. Photos : Cloudflare, Inc. (R2), 101 Townsend Street, San Francisco, CA 94107, États-Unis',
  },
};

export const nav = [
  {href: '/', label: 'Accueil'},
  {href: '/fonctionnalites', label: 'Fonctionnalités'},
  {href: '/tarifs', label: 'Tarifs'},
  {href: '/site-sur-mesure', label: 'Site web'},
  {href: '/faq', label: 'Questions'},
  {href: '/developpeurs', label: 'Développeurs'},
  {href: '/contact', label: 'Contact'},
];

/** Le menu « Fonctionnalités » de l'en-tête : les modules regroupés par usage. */
export const featureMenu: {title: string; icon: 'service' | 'gestion' | 'croissance'; modules: string[]}[] = [
  {title: 'Service', icon: 'service', modules: ['commandes', 'reservations', 'menu', 'salle']},
  {title: 'Gestion', icon: 'gestion', modules: ['cuisine', 'stock', 'personnel', 'clients']},
  {title: 'Croissance', icon: 'croissance', modules: ['marketing', 'rapports', 'etablissements', 'api']},
];
