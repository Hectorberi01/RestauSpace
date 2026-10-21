import type {MetadataRoute} from 'next';

/** Console installable sur l'écran d'accueil (téléphone, tablette de caisse) ; elle s'ouvre sur la vente rapide. */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: 'RestauSpace — Console',
    short_name: 'RestauSpace',
    description: 'Console de gestion du restaurant : caisse, commandes, cuisine.',
    start_url: '/caisse/vente',
    scope: '/',
    display: 'standalone',
    orientation: 'any',
    background_color: '#f8fafc',
    theme_color: '#6b3ee6',
    lang: 'fr',
    icons: [
      {src: '/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any'},
      {src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any'},
      {src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable'},
    ],
  };
}
