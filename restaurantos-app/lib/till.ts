import {API_URL, ApiError, api, del, post, put, setSession, type Session} from './api';

/**
 * Caisse partagée : l'appareil garde la clé remise à son enregistrement ; le personnel s'y identifie par son PIN et
 * reçoit une session à lui. La clé n'est jamais affichée ni envoyée ailleurs qu'à l'API (en-tête X-Till-Key).
 */
const KEY = 'restaurantos.till';

export interface TillDevice {
  key: string;
  tillId: string;
  name: string;
  tenantName: string;
  locationId: string;
  locationName: string;
  autoLockMinutes: number;
}

export function readTill(): TillDevice | null {
  try {
    const value = JSON.parse(window.localStorage.getItem(KEY) ?? 'null') as TillDevice | null;
    return value?.key && value.tillId ? value : null;
  } catch {
    return null;
  }
}

export function writeTill(device: TillDevice | null) {
  try {
    if (device) window.localStorage.setItem(KEY, JSON.stringify(device));
    else window.localStorage.removeItem(KEY);
  } catch {}
}

export interface TillOperator {
  staffMemberId: string;
  name: string;
  hasPin: boolean;
  pinLocked: boolean;
  clockedInSinceUtc: string | null;
}

export interface TillState {
  tillId: string;
  name: string;
  locationId: string;
  autoLockMinutes: number;
  operators: TillOperator[];
}

interface TillSession {
  tenantId: string;
  locationId: string;
  staffMemberId: string;
  name: string;
  userId: string;
  sessionId: string;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export interface ClockResult {
  staffMemberId: string;
  clockedIn: boolean;
  atUtc: string;
  worked: string | null;
}

async function tillFetch<T>(device: TillDevice, path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API_URL}/v1/till${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: {Accept: 'application/json', 'X-Till-Key': device.key, ...(body === undefined ? {} : {'Content-Type': 'application/json'})},
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: 'no-store',
  });
  if (!response.ok) {
    let code: string | undefined;
    let message = `${response.status} ${response.statusText}`;
    try {
      const problem = await response.json();
      code = typeof problem.code === 'string' ? problem.code : undefined;
      message = problem.detail || problem.title || message;
    } catch {}
    throw new ApiError(response.status, code ?? (response.status === 401 ? 'staff.till.invalid_key' : undefined), message);
  }
  return (await response.json()) as T;
}

export const tillState = (device: TillDevice) => tillFetch<TillState>(device, '/');

/** Ouvre la session du membre sur cet appareil (remplace toute session en cours). */
export async function unlockTill(device: TillDevice, staffMemberId: string, pin: string): Promise<Session> {
  const opened = await tillFetch<TillSession>(device, '/unlock', {staffMemberId, pin});
  const session: Session = {
    userId: opened.userId,
    sessionId: opened.sessionId,
    accessToken: opened.accessToken,
    accessTokenExpiresAtUtc: opened.accessTokenExpiresAtUtc,
    refreshToken: opened.refreshToken,
    refreshTokenExpiresAtUtc: opened.refreshTokenExpiresAtUtc,
    tenantId: opened.tenantId,
    operator: {staffMemberId: opened.staffMemberId, name: opened.name, tillId: device.tillId, locationId: opened.locationId},
  };
  setSession(session);
  return session;
}

export const clockOnTill = (device: TillDevice, staffMemberId: string, pin: string) =>
  tillFetch<ClockResult>(device, '/clock', {staffMemberId, pin});

// --- Console : gestion des caisses, PIN et pointages.

export interface Till {
  id: string;
  locationId: string;
  name: string;
  autoLockMinutes: number;
  createdAtUtc: string;
  lastUsedAtUtc: string | null;
  isActive: boolean;
}

export const listTills = () => api<Till[]>('/staff/tills');

export const registerTill = (locationId: string, name: string, autoLockMinutes: number) =>
  post<{id: string; key: string}>('/staff/tills', {locationId, name, autoLockMinutes});

export const changeTill = (id: string, name: string, autoLockMinutes: number) => put(`/staff/tills/${id}`, {name, autoLockMinutes});

export const revokeTill = (id: string) => del(`/staff/tills/${id}`);

export const setMyPin = (pin: string) => put('/staff/me/pin', {pin});

export const clearMyPin = () => del('/staff/me/pin');

export const resetMemberPin = (memberId: string) => del(`/staff/members/${memberId}/pin`);

export interface TimeEntry {
  id: string;
  staffMemberId: string;
  name: string | null;
  locationId: string;
  clockedInAtUtc: string;
  clockedOutAtUtc: string | null;
  closedAutomatically: boolean;
  workedMinutes: number;
}

export const listTimeEntries = (from: string, to: string, locationId?: string) =>
  api<TimeEntry[]>('/staff/time-entries', {query: {from, to, locationId}});

/** « 7 h 30 » */
export function hours(minutes: number): string {
  const total = Math.round(minutes);
  const h = Math.floor(total / 60);
  const m = total % 60;
  return h === 0 ? `${m} min` : `${h} h ${String(m).padStart(2, '0')}`;
}
