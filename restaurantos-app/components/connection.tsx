'use client';
import {useCallback, useEffect, useState} from 'react';
import Link from 'next/link';
import {CloudOff, RefreshCw} from 'lucide-react';
import {listSales, syncAll} from '@/lib/offline';
import {cx} from './ui';

/** État du réseau vu par le navigateur, mis à jour aux événements online/offline. */
export function useOnline(): boolean {
  const [online, setOnline] = useState(true);
  useEffect(() => {
    const update = () => setOnline(navigator.onLine);
    update();
    window.addEventListener('online', update);
    window.addEventListener('offline', update);
    return () => {
      window.removeEventListener('online', update);
      window.removeEventListener('offline', update);
    };
  }, []);
  return online;
}

/** Ventes hors ligne restant à envoyer ; renvoyées au retour du réseau et toutes les 30 secondes. */
export function useOfflineQueue() {
  const online = useOnline();
  const [pending, setPending] = useState(0);
  const [failed, setFailed] = useState(0);
  const [syncing, setSyncing] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const sales = await listSales();
      setPending(sales.filter(sale => sale.status === 'pending').length);
      setFailed(sales.filter(sale => sale.status === 'failed').length);
    } catch {
      // IndexedDB indisponible (navigation privée) : pas de file locale.
    }
  }, []);

  const sync = useCallback(async () => {
    if (!navigator.onLine) return;
    setSyncing(true);
    try {
      await syncAll();
    } finally {
      setSyncing(false);
      await refresh();
    }
  }, [refresh]);

  useEffect(() => {
    void refresh();
    const changed = () => void refresh();
    window.addEventListener('restaurantos:offline-sales', changed);
    return () => window.removeEventListener('restaurantos:offline-sales', changed);
  }, [refresh]);

  useEffect(() => {
    if (!online) return;
    void sync();
    const timer = window.setInterval(() => void sync(), 30_000);
    return () => window.clearInterval(timer);
  }, [online, sync]);

  return {online, pending, failed, syncing, sync, refresh};
}

/** Signale aux écrans abonnés qu'une vente locale a changé. */
export const notifyOfflineSales = () => window.dispatchEvent(new Event('restaurantos:offline-sales'));

/** Bandeau de la barre du haut : hors ligne, ventes à envoyer, ventes en échec. Rien quand tout va bien. */
export function ConnectionStatus() {
  const {online, pending, failed, syncing, sync} = useOfflineQueue();
  if (online && pending === 0 && failed === 0) return null;
  return (
    <Link
      href="/caisse/vente"
      className={cx('hidden items-center gap-2 rounded-xl px-3 py-2 text-sm font-semibold sm:flex', online ? 'bg-amber-50 text-amber-800' : 'bg-red-50 text-red-800')}
      onClick={() => void sync()}
    >
      {online ? <RefreshCw className={cx('size-4', syncing && 'animate-spin')} aria-hidden="true" /> : <CloudOff className="size-4" aria-hidden="true" />}
      {!online ? 'Hors ligne' : syncing ? 'Envoi…' : 'En ligne'}
      {pending > 0 && <span>· {pending} vente{pending > 1 ? 's' : ''} à envoyer</span>}
      {failed > 0 && <span>· {failed} en échec</span>}
    </Link>
  );
}
