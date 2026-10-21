import type {NextConfig} from 'next';

const apiOrigin = (() => {
  try {
    return new URL(process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5080').origin;
  } catch {
    return '';
  }
})();

const csp = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com",
  'frame-src https://challenges.cloudflare.com',
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: https:",
  "font-src 'self'",
  `connect-src 'self' ${apiOrigin}`.trim(),
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ');

const securityHeaders = [
  {key: 'X-Frame-Options', value: 'DENY'},
  {key: 'X-Content-Type-Options', value: 'nosniff'},
  {key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin'},
  {key: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=(), payment=(), usb=()'},
];

if (process.env.NODE_ENV === 'production') {
  securityHeaders.push({key: 'Content-Security-Policy', value: csp});
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
