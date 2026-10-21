import type {NextConfig} from 'next';

const securityHeaders = [
  {key: 'X-Frame-Options', value: 'DENY'},
  {key: 'X-Content-Type-Options', value: 'nosniff'},
  {key: 'Referrer-Policy', value: 'no-referrer'},
  {key: 'Cross-Origin-Opener-Policy', value: 'same-origin'},
  {key: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()'},
];

if (process.env.NODE_ENV === 'production') {
  securityHeaders.push({key: 'Strict-Transport-Security', value: 'max-age=63072000; includeSubDomains'});
}

const nextConfig: NextConfig = {
  // Image Docker : serveur Node autonome (.next/standalone), sans node_modules complet.
  output: 'standalone',
  poweredByHeader: false,
  devIndicators: false,
  async headers() {
    return [{source: '/:path*', headers: securityHeaders}];
  },
};

export default nextConfig;
