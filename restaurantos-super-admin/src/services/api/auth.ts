import {api, getSession, setSession, type Session} from './client';

export interface Me {
  id: string;
  email: string;
  status: string;
}

/** Le mot de passe ; renvoie le jeton de seconde étape si le compte a la validation en deux étapes, sinon null. */
export async function login(email: string, password: string): Promise<string | null> {
  const result = await api<Session | {twoFactorRequired: true; challenge: string}>('/auth/login', {method: 'POST', body: {email, password}, anonymous: true});
  if ('twoFactorRequired' in result) return result.challenge;
  setSession(result);
  return null;
}

/** Le code de l'application d'authentification (ou un code de secours) après le mot de passe. */
export async function loginWithCode(challenge: string, code: string): Promise<void> {
  setSession(await api<Session>('/auth/login/two-factor', {method: 'POST', body: {challenge, code}, anonymous: true}));
}

export async function logout(): Promise<void> {
  const session = getSession();
  setSession(null);
  if (!session) return;
  try {
    await api<void>('/auth/logout', {method: 'POST', body: {refreshToken: session.refreshToken}, anonymous: true});
  } catch {}
}

export function me(): Promise<Me> {
  return api<Me>('/me');
}
