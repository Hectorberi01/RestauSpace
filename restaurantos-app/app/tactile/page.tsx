'use client';
import {useCallback, useEffect, useRef, useState, useSyncExternalStore} from 'react';
import {getSession, logout, onSessionChange} from '@/lib/api';
import {readTill, type TillDevice} from '@/lib/till';
import {ConsoleProvider} from '@/components/console';
import {TouchRegister} from '@/components/pos';
import {TillLockScreen} from '@/components/till-lock';
import {Spinner} from '@/components/ui';

/**
 * Caisse tactile plein écran. Sur un appareil enregistré comme caisse partagée, chacun l'ouvre par son PIN et elle se
 * verrouille seule après quelques minutes sans action ; ailleurs, elle s'ouvre avec la session de la console.
 */
export default function TouchTillPage() {
  const [device, setDevice] = useState<TillDevice | null | undefined>(undefined);

  useEffect(() => setDevice(readTill()), []);

  if (device === undefined) return <Spinner label="Ouverture de la caisse…" />;
  if (!device) {
    return (
      <ConsoleProvider>
        <TouchRegister />
      </ConsoleProvider>
    );
  }
  return <SharedTill device={device} />;
}

function SharedTill({device}: {device: TillDevice}) {
  const [unlocked, setUnlocked] = useState<{autoLockMinutes: number} | null | undefined>(undefined);
  const lastActivity = useRef(Date.now());
  // Session de caisse expirée (12 h) ou révoquée : retour à l'écran verrouillé, jamais à la page de connexion.
  const session = useSyncExternalStore(onSessionChange, getSession, () => null);
  const open = session?.operator?.tillId === device.tillId;

  useEffect(() => {
    // Une session déjà ouverte sur cette caisse (rechargement de la page) reste ouverte.
    const session = getSession();
    setUnlocked(session?.operator?.tillId === device.tillId ? {autoLockMinutes: device.autoLockMinutes || 5} : null);
  }, [device.tillId, device.autoLockMinutes]);

  const lock = useCallback(() => {
    setUnlocked(null);
    void logout();
  }, []);

  useEffect(() => {
    if (!unlocked) return;
    lastActivity.current = Date.now();
    const touch = () => (lastActivity.current = Date.now());
    const events = ['pointerdown', 'keydown', 'wheel'] as const;
    events.forEach(name => window.addEventListener(name, touch, {passive: true}));
    const timer = window.setInterval(() => {
      if (Date.now() - lastActivity.current > unlocked.autoLockMinutes * 60_000) lock();
    }, 10_000);
    return () => {
      events.forEach(name => window.removeEventListener(name, touch));
      window.clearInterval(timer);
    };
  }, [unlocked, lock]);

  useEffect(() => {
    if (unlocked && !open) setUnlocked(null);
  }, [unlocked, open]);

  if (unlocked === undefined) return <Spinner label="Ouverture de la caisse…" />;
  if (!unlocked || !open) return <TillLockScreen device={device} onUnlocked={state => setUnlocked({autoLockMinutes: state.autoLockMinutes})} />;
  return (
    <ConsoleProvider>
      <TouchRegister onLock={lock} />
    </ConsoleProvider>
  );
}
