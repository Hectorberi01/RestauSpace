import type {NextConfig} from 'next';

// Chemin de publication : vide pour un site à la racine, ou un sous-dossier.
const basePath = (process.env.TEMPLATE_BASE_PATH ?? '').replace(/\/+$/, '');

const nextConfig: NextConfig = {
  output: 'export',
  trailingSlash: true,
  images: {unoptimized: true},
  basePath,
  env: {NEXT_PUBLIC_BASE_PATH: basePath},
  poweredByHeader: false,
  devIndicators: false,
};

export default nextConfig;
