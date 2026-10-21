import {NextResponse, type NextRequest} from 'next/server';

const apiOrigin = (() => {
  try {
    return new URL(process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5080').origin;
  } catch {
    return '';
  }
})();

const filesOrigins = (process.env.NEXT_PUBLIC_FILES_ORIGINS ?? '')
  .split(/[\s,]+/)
  .filter(origin => /^https?:\/\/[^\s;']+$/.test(origin))
  .join(' ');

export function proxy(request: NextRequest) {
  const nonce = Buffer.from(crypto.randomUUID()).toString('base64');
  const isDev = process.env.NODE_ENV === 'development';
  const policy = [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDev ? " 'unsafe-eval'" : ''}`,
    "style-src 'self' 'unsafe-inline'",
    // Logo du restaurant : une adresse https publique choisie par le restaurateur.
    `img-src 'self' blob: data: https: ${apiOrigin} ${filesOrigins}`.trim(),
    "font-src 'self' data:",
    `connect-src 'self' ${apiOrigin} ${filesOrigins}${isDev ? ' ws: wss:' : ''}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    ...(!isDev && apiOrigin.startsWith('https:') ? ['upgrade-insecure-requests'] : []),
  ].join('; ');

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set('x-nonce', nonce);
  requestHeaders.set('Content-Security-Policy', policy);

  const response = NextResponse.next({request: {headers: requestHeaders}});
  response.headers.set('Content-Security-Policy', policy);
  return response;
}

export const config = {
  matcher: [
    {
      source: '/((?!api|_next/static|_next/image|favicon.ico|favicon.svg).*)',
      missing: [
        {type: 'header', key: 'next-router-prefetch'},
        {type: 'header', key: 'purpose', value: 'prefetch'},
      ],
    },
  ],
};
