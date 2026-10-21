'use client';
import {createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {all, api, describe, getSession, onSessionChange, type Session} from '@/lib/api';
import type {LocationSummary, MyPermissions, Tenant} from '@/lib/types';
import {Building2} from 'lucide-react';
import {Alert, Button, Spinner} from './ui';

const LOCATION_KEY = 'restaurantos.app.location';

export function useSession(): Session | null | undefined {
  return useSyncExternalStore(onSessionChange, getSession, () => undefined);
}

interface ConsoleState {
  tenant: Tenant;
  permissions: Set<string>;
  can: (permission: string) => boolean;
  locations: LocationSummary[];
  location: LocationSummary | undefined;
  setLocationId: (id: string) => void;
  reloadLocations: () => Promise<void>;
  reloadTenant: () => Promise<void>;
  currency: string;
  email: string;
  isOwner: boolean;
}

const ConsoleContext = createContext<ConsoleState | null>(null);

export function useConsole(): ConsoleState {
  const state = useContext(ConsoleContext);
  if (!state) throw new Error('useConsole hors de ConsoleProvider');
  return state;
}

function readLocation(tenantId: string): string | undefined {
  try {
    return window.localStorage.getItem(`${LOCATION_KEY}.${tenantId}`) ?? undefined;
  } catch {
    return undefined;
  }
}

function writeLocation(tenantId: string, id: string) {
  try {
    window.localStorage.setItem(`${LOCATION_KEY}.${tenantId}`, id);
  } catch {}
}

type Snapshot = {tenant: Tenant; permissions: MyPermissions; locations: LocationSummary[]; email: string};

const SNAPSHOT_KEY = 'restaurantos.console';

/** Dernier état chargé du restaurant, pour rouvrir la console sans réseau (caisse hors ligne). */
function readSnapshot(tenantId: string): Snapshot | null {
  try {
    const value = JSON.parse(window.localStorage.getItem(`${SNAPSHOT_KEY}.${tenantId}`) ?? 'null') as Snapshot | null;
    return value?.tenant?.id === tenantId ? value : null;
  } catch {
    return null;
  }
}

function writeSnapshot(tenantId: string, snapshot: Snapshot) {
  try {
    window.localStorage.setItem(`${SNAPSHOT_KEY}.${tenantId}`, JSON.stringify(snapshot));
  } catch {}
}

export function ConsoleProvider({children}: {children: ReactNode}) {
  const router = useRouter();
  const session = useSession();
  const tenantId = session?.tenantId;
  const [state, setState] = useState<{tenant: Tenant; permissions: MyPermissions; locations: LocationSummary[]; email: string} | null>(null);
  const [locationId, setLocationIdState] = useState<string>();
  const [error, setError] = useState<string>();
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (session === null) router.replace('/connexion');
    else if (session && !session.tenantId) router.replace('/restaurants');
  }, [session, router]);

  useEffect(() => {
    if (!tenantId) return;
    let active = true;
    setError(undefined);
    Promise.all([
      api<Tenant>('/tenant'),
      api<MyPermissions>('/staff/me/permissions'),
      all<LocationSummary>('/locations'),
      api<{email: string}>('/me'),
    ])
      .then(([tenant, permissions, locations, me]) => ({tenant, permissions, locations, email: me.email}))
      .then(snapshot => {
        writeSnapshot(tenantId, snapshot);
        return snapshot;
      })
      .catch(err => {
        // Sans réseau (fetch rejeté), la console repart du dernier état connu : la vente rapide reste utilisable.
        const cached = err instanceof TypeError ? readSnapshot(tenantId) : null;
        if (cached) return cached;
        throw err;
      })
      .then(snapshot => {
        if (!active) return;
        setState(snapshot);
        const stored = readLocation(tenantId);
        const usable = snapshot.locations.filter(location => location.status !== 'Archived');
        setLocationIdState(usable.find(location => location.id === stored)?.id ?? usable.find(location => location.status === 'Active')?.id ?? usable[0]?.id);
      })
      .catch(err => {
        if (active) setError(describe(err));
      });
    return () => {
      active = false;
    };
  }, [tenantId, attempt]);

  const reloadLocations = useCallback(async () => {
    const locations = await all<LocationSummary>('/locations');
    setState(current => (current ? {...current, locations} : current));
    setLocationIdState(current => current ?? locations.find(location => location.status !== 'Archived')?.id);
  }, []);

  const reloadTenant = useCallback(async () => {
    const tenant = await api<Tenant>('/tenant');
    setState(current => (current ? {...current, tenant} : current));
  }, []);

  const setLocationId = useCallback(
    (id: string) => {
      setLocationIdState(id);
      if (tenantId) writeLocation(tenantId, id);
    },
    [tenantId],
  );

  const value = useMemo<ConsoleState | null>(() => {
    if (!state) return null;
    const permissions = new Set(state.permissions.permissions);
    return {
      tenant: state.tenant,
      permissions,
      can: permission => permissions.has(permission),
      locations: state.locations.filter(location => location.status !== 'Archived'),
      location: state.locations.find(location => location.id === locationId),
      setLocationId,
      reloadLocations,
      reloadTenant,
      currency: state.tenant.defaultCurrencyCode,
      email: state.email,
      isOwner: permissions.has('staff.manage') && permissions.has('roles.manage'),
    };
  }, [state, locationId, setLocationId, reloadLocations, reloadTenant]);

  if (error) {
    return (
      <div className="mx-auto max-w-md p-6 pt-24">
        <Alert>{error}</Alert>
        <div className="mt-4 flex gap-2">
          <Button onClick={() => setAttempt(n => n + 1)}>Réessayer</Button>
          <Button variant="secondary" onClick={() => router.replace('/restaurants')}>Changer de restaurant</Button>
        </div>
      </div>
    );
  }
  if (!value || !session?.tenantId) return <Spinner label="Ouverture du restaurant…" />;
  return <ConsoleContext.Provider value={value}>{children}</ConsoleContext.Provider>;
}

/** Affiche les écrans qui travaillent sur un établissement seulement quand il y en a un de sélectionné. */
export function WithLocation({children}: {children: (location: LocationSummary) => ReactNode}) {
  const {location, can} = useConsole();
  if (!location) {
    return (
      <div className="mx-auto max-w-lg pt-16 text-center">
        <span className="mx-auto grid size-14 place-items-center rounded-2xl bg-brand-50 text-brand-600"><Building2 className="size-7" aria-hidden="true" /></span>
        <h1 className="mt-5 text-xl font-bold">Aucun établissement</h1>
        <p className="mt-2 text-ink-500">Chaque écran travaille sur un établissement : son adresse, ses horaires, sa carte et ses tables.</p>
        {can('locations.manage') ? (
          <Link href="/etablissements" className="mt-6 inline-flex h-11 items-center rounded-xl bg-brand-600 px-5 text-sm font-semibold text-white hover:bg-brand-700">Créer un établissement</Link>
        ) : (
          <p className="mt-4 text-sm text-ink-500">Demandez au gérant de créer ou d’ouvrir un établissement.</p>
        )}
      </div>
    );
  }
  return <>{children(location)}</>;
}
