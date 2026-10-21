import {api} from './api';

// Parcours de compte sans session : les réponses ne révèlent jamais si une adresse existe.
export const requestPasswordReset = (email: string) =>
  api<void>('/auth/forgot-password', {method: 'POST', body: {email}, anonymous: true});

export const resetPassword = (token: string, newPassword: string) =>
  api<void>('/auth/reset-password', {method: 'POST', body: {token, newPassword}, anonymous: true});

export const acceptInvitation = (token: string, password: string) =>
  api<void>('/auth/accept-invitation', {method: 'POST', body: {token, password}, anonymous: true});

export const verifyEmail = (token: string, password: string) =>
  api<void>('/auth/verify-email', {method: 'POST', body: {token, password}, anonymous: true});

export const resendVerification = (email: string) =>
  api<void>('/auth/resend-verification', {method: 'POST', body: {email}, anonymous: true});

export const PASSWORD_MIN = 8;
export const PASSWORD_MAX = 128;

export interface SignUpCountry {
  code: string;
  name: string;
  currencyCode: string;
  timeZoneId: string;
}

export interface SignUpPlan {
  id: string;
  name: string;
  price: number;
  currencyCode: string;
  billingInterval: 'Monthly' | 'Yearly';
  entitlements: {featureCode: string; enabled: boolean; limit: number | null}[];
}

export interface SignUpOptions {
  countries: SignUpCountry[];
  plans: SignUpPlan[];
  trialDays: number;
}

export interface SignUpInput {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  restaurantName: string;
  country: string;
  planId: string;
  acceptTerms: boolean;
  website: string;
}

// Inscription en libre-service : pays, plans et essai proposés, puis création du compte et du restaurant.
export const loadSignUpOptions = (signal?: AbortSignal) =>
  api<SignUpOptions>('/public/sign-up', {anonymous: true, signal});

export const signUp = (input: SignUpInput) =>
  api<{trialDays: number}>('/public/sign-up', {method: 'POST', body: input, anonymous: true});
