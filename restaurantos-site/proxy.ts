import {NextResponse, type NextRequest} from 'next/server';
import {isSensitivePath, noncePolicy} from '@/lib/csp';

/** CSP à nonce sur les pages sensibles uniquement ; les autres gardent celle de next.config.ts. */
export function proxy(request: NextRequest) {
  if (!isSensitivePath(request.nextUrl.pathname)) return NextResponse.next();

  const nonce = Buffer.from(crypto.randomUUID()).toString('base64');
  const policy = noncePolicy(nonce, process.env.NODE_ENV === 'development');

  // Next lit le nonce dans la CSP de la requête et l'ajoute à ses scripts.
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
