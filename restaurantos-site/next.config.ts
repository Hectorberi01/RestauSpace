import type {NextConfig} from 'next';
import {nonSensitiveSource, staticPolicy} from './lib/csp';

const securityHeaders = [
  {key: 'X-Frame-Options', value: 'DENY'},
  {key: 'X-Content-Type-Options', value: 'nosniff'},
  {key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin'},
  {key: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=(), payment=(), usb=()'},
  // Paiements par redirection (window.location.assign), jamais par fenêtre surgissante : compatible.
  {key: 'Cross-Origin-Opener-Policy', value: 'same-origin'},
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
    const rules = [{source: '/:path*', headers: securityHeaders}];
    // Pages sensibles exclues : proxy.ts y pose une CSP à nonce (une seule CSP par réponse).
    if (process.env.NODE_ENV === 'production') {
      rules.push({source: nonSensitiveSource, headers: [{key: 'Content-Security-Policy', value: staticPolicy()}]});
    }
    return rules;
  },
};

export default nextConfig;
