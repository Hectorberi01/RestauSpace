import {connection} from 'next/server';

/** Layout des pages sensibles : rendu à chaque requête, pour recevoir le nonce CSP de proxy.ts. */
export default async function RequestTimeLayout({children}: {children: React.ReactNode}) {
  await connection();
  return children;
}
