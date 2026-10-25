'use client';
import {createContext, useCallback, useContext, useEffect, useState} from 'react';
import {usePathname} from 'next/navigation';
import {isConfigured} from '@/lib/config';
import {errorMessage, storefront, type Storefront, type StorefrontLocation} from '@/lib/storefront';
import {readLocationChoice, writeLocationChoice} from '@/lib/local';

interface SiteState {
  front: Storefront | null;
  error: string | null;
  /** L'établissement choisi (le premier par défaut). */
  location: StorefrontLocation | null;
  chooseLocation: (id: string) => void;
  retry: () => void;
}

const SiteContext = createContext<SiteState>({front: null, error: null, location: null, chooseLocation: () => {}, retry: () => {}});

export const useSite = () => useContext(SiteContext);

/** Charge une fois GET / (restaurant, établissements) pour toutes les pages. */
export function SiteProvider({children}: {children: React.ReactNode}) {
  const [front, setFront] = useState<Storefront | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [locationId, setLocationId] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const pathname = usePathname();

  useEffect(() => {
    if (!isConfigured()) {
      setError('Ce site n’est pas encore branché sur le restaurant : renseignez la clé publique dans site.config.json.');
      return;
    }
    const controller = new AbortController();
    setError(null);
    storefront<Storefront>('/', {signal: controller.signal})
      .then(result => {
        if (!result) return;
        setFront(result);
        const asked = new URLSearchParams(window.location.search).get('etablissement') ?? readLocationChoice();
        const found = result.locations.find(item => item.id === asked);
        if (found) writeLocationChoice(found.id);
        setLocationId(found?.id ?? result.locations[0]?.id ?? null);
      })
      .catch(reason => !controller.signal.aborted && setError(errorMessage(reason)));
    return () => controller.abort();
  }, [attempt]);

  // Le nom du restaurant vient de l'API : ajouté au titre de chaque page.
  useEffect(() => {
    if (!front) return;
    const {name, tagline} = front.restaurant;
    if (pathname === '/') document.title = tagline ? `${name} · ${tagline}` : name;
    else if (!document.title.includes(name)) document.title = `${document.title} · ${name}`;
  }, [front, pathname]);

  const chooseLocation = useCallback((id: string) => {
    setLocationId(id);
    writeLocationChoice(id);
  }, []);
  const retry = useCallback(() => setAttempt(value => value + 1), []);
  const location = front?.locations.find(item => item.id === locationId) ?? null;

  return <SiteContext.Provider value={{front, error, location, chooseLocation, retry}}>{children}</SiteContext.Provider>;
}
