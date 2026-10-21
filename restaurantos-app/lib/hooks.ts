'use client';
import {useCallback, useEffect, useRef, useState} from 'react';
import {describe} from './api';

export interface Resource<T> {
  data: T | undefined;
  error: string | undefined;
  loading: boolean;
  reload: () => Promise<void>;
}

/**
 * Charge une ressource et la recharge toutes les `intervalMs` (0 : jamais) tant que l'onglet est visible :
 * l'API n'a pas de temps réel, l'écran cuisine et les commandes vivent donc par interrogation régulière.
 */
export function useResource<T>(load: (signal: AbortSignal) => Promise<T>, deps: unknown[], intervalMs = 0): Resource<T> {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  const loader = useRef(load);
  loader.current = load;
  const controller = useRef<AbortController | null>(null);

  const reload = useCallback(async () => {
    controller.current?.abort();
    const current = new AbortController();
    controller.current = current;
    try {
      const next = await loader.current(current.signal);
      if (current.signal.aborted) return;
      setData(next);
      setError(undefined);
    } catch (err) {
      if (current.signal.aborted || (err instanceof DOMException && err.name === 'AbortError')) return;
      setError(describe(err));
    } finally {
      if (!current.signal.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    setData(undefined);
    void reload();
    return () => controller.current?.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    if (!intervalMs) return;
    const tick = () => {
      if (document.visibilityState === 'visible') void reload();
    };
    const timer = window.setInterval(tick, intervalMs);
    document.addEventListener('visibilitychange', tick);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', tick);
    };
  }, [intervalMs, reload]);

  return {data, error, loading, reload};
}

/** Horloge partagée par les écrans qui affichent des durées (« depuis 4 min »). */
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}

/** Vrai quand la requête média correspond (faux au premier rendu, avant hydratation). */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(false);
  useEffect(() => {
    const media = window.matchMedia(query);
    const update = () => setMatches(media.matches);
    update();
    media.addEventListener('change', update);
    return () => media.removeEventListener('change', update);
  }, [query]);
  return matches;
}
