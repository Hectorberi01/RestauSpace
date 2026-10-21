'use client';
import {usePathname} from 'next/navigation';

const ownPages = ['/menu/', '/commander', '/commande', '/borne', '/compte', '/table/', '/questionnaire/', '/reserver', '/reservation', '/recu'];

/** En-tête et pied du site vitrine : masqués sur le menu public, la commande en ligne (et à table), le compte client, la borne, les questionnaires et le reçu. */
export function MarketingOnly({children}: {children: React.ReactNode}) {
  const path = usePathname();
  return ownPages.some(prefix => path.startsWith(prefix)) ? null : children;
}
