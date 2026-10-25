'use client';
import {useCallback, useSyncExternalStore} from 'react';
import type {CartLine, ServiceMode} from './storefront';

// Ce que le navigateur garde : panier (un par établissement), mode choisi, commandes et réservations passées ici.

const listeners = new Set<() => void>();
const notify = () => listeners.forEach(listener => listener());
const subscribe = (listener: () => void) => {
  listeners.add(listener);
  const onStorage = () => listener();
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', onStorage);
  };
};

function read(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, value);
  } catch {}
  notify();
}

function parseList<T>(raw: string | null, valid: (item: T) => boolean): T[] {
  try {
    const value = raw ? JSON.parse(raw) : [];
    return Array.isArray(value) ? value.filter(valid) : [];
  } catch {
    return [];
  }
}

// ---- Panier.

const cartKey = (locationId: string) => `maquis.cart.${locationId}`;
const EMPTY: CartLine[] = [];
const cartCache = new Map<string, {raw: string | null; lines: CartLine[]}>();

export function readCart(locationId: string): CartLine[] {
  const raw = read(cartKey(locationId));
  const cached = cartCache.get(locationId);
  if (cached && cached.raw === raw) return cached.lines;
  const lines = parseList<CartLine>(raw, line => typeof line?.productId === 'string' && line.quantity > 0);
  cartCache.set(locationId, {raw, lines});
  return lines;
}

export const writeCart = (locationId: string, lines: CartLine[]) => write(cartKey(locationId), lines.length ? JSON.stringify(lines) : null);

/** Le panier d'un établissement, partagé entre les composants de la page. */
export function useCart(locationId: string | null) {
  const lines = useSyncExternalStore(subscribe, () => (locationId ? readCart(locationId) : EMPTY), () => EMPTY);
  const set = useCallback((next: CartLine[]) => locationId && writeCart(locationId, next), [locationId]);
  return [lines, set] as const;
}

// ---- Établissement et mode choisis.

export const readLocationChoice = () => read('maquis.location');
export const writeLocationChoice = (id: string) => write('maquis.location', id);

export function readModeChoice(): {mode: ServiceMode | null; zoneId: string | null} {
  const mode = read('maquis.mode');
  return {mode: mode === 'DineIn' || mode === 'Takeaway' || mode === 'Delivery' ? mode : null, zoneId: read('maquis.zone')};
}

export function writeModeChoice(mode: ServiceMode, zoneId?: string | null) {
  write('maquis.mode', mode);
  if (zoneId !== undefined) write('maquis.zone', zoneId);
}

export function useModeChoice() {
  const mode = useSyncExternalStore(subscribe, () => read('maquis.mode'), () => null);
  const zoneId = useSyncExternalStore(subscribe, () => read('maquis.zone'), () => null);
  return {mode: mode === 'DineIn' || mode === 'Takeaway' || mode === 'Delivery' ? mode as ServiceMode : null, zoneId};
}

// ---- Commandes et réservations passées sur ce navigateur.

export interface RememberedOrder {
  token: string;
  number: string;
  at: string;
}

export const rememberedOrders = () =>
  parseList<RememberedOrder>(read('maquis.orders'), item => typeof item?.token === 'string' && typeof item.number === 'string');

export const rememberOrder = (token: string, number: string) =>
  write('maquis.orders', JSON.stringify([{token, number, at: new Date().toISOString()}, ...rememberedOrders().filter(item => item.token !== token)].slice(0, 10)));

export const rememberedBookings = () => parseList<string>(read('maquis.bookings'), item => typeof item === 'string');

export const rememberBooking = (token: string) =>
  write('maquis.bookings', JSON.stringify([token, ...rememberedBookings().filter(item => item !== token)].slice(0, 10)));
