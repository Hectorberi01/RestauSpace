'use client';
import {useEffect, useRef, useState, type FormEvent, type ReactNode} from 'react';
import Link from 'next/link';
import {CheckCircle2} from 'lucide-react';
import {describe} from '@/lib/api';
import {PASSWORD_MAX, PASSWORD_MIN} from '@/lib/account';
import {Logo} from '@/components/shell';
import {Alert, Button, Field, Input} from '@/components/ui';

export function AuthCard({title, subtitle, children}: {title: string; subtitle?: ReactNode; children: ReactNode}) {
  return (
    <div className="grid min-h-dvh place-items-center bg-gradient-to-b from-brand-50 to-slate-50 px-4 py-10">
      <div className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-7 shadow-xl shadow-brand-900/5">
        <Logo />
        <h1 className="mt-6 text-2xl font-bold tracking-tight">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-ink-500">{subtitle}</p>}
        <div className="mt-6">{children}</div>
      </div>
    </div>
  );
}

/**
 * Lit le jeton du lien une seule fois puis le retire de l'adresse : il ne reste ni dans l'historique ni dans un
 * éventuel en-tête Referer.
 */
export function useLinkToken(): string | null | undefined {
  const [token, setToken] = useState<string | null>();
  // Conservé d'un passage à l'autre de l'effet : le second ne trouve plus le jeton dans l'adresse.
  const read = useRef<string | null>(undefined);
  useEffect(() => {
    if (read.current === undefined) {
      const url = new URL(window.location.href);
      const value = url.searchParams.get('token');
      read.current = value && value.trim() ? value.trim() : null;
      if (value !== null) {
        url.searchParams.delete('token');
        window.history.replaceState(null, '', url.pathname + url.search + url.hash);
      }
    }
    setToken(read.current);
  }, []);
  return token;
}

export function BackToLogin({label = 'Retour à la connexion'}: {label?: string}) {
  return (
    <p className="mt-6 text-center text-sm">
      <Link href="/connexion" className="font-medium text-brand-700 hover:underline">{label}</Link>
    </p>
  );
}

export function Done({children, action = 'Se connecter'}: {children: ReactNode; action?: string}) {
  return (
    <div className="space-y-5">
      <div className="flex gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-900" role="status">
        <CheckCircle2 className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <div>{children}</div>
      </div>
      <Link href="/connexion" className="flex h-11 w-full items-center justify-center rounded-xl bg-brand-600 text-sm font-semibold text-white hover:bg-brand-700">
        {action}
      </Link>
    </div>
  );
}

export function MissingToken() {
  return (
    <>
      <Alert>Ce lien est incomplet. Ouvrez-le de nouveau depuis l’email reçu, ou demandez-en un nouveau.</Alert>
      <BackToLogin />
    </>
  );
}

/** Formulaire « nouveau mot de passe » : saisie, confirmation, longueur contrôlée avant l'envoi. */
export function NewPasswordForm({submitLabel, onSubmit}: {submitLabel: string; onSubmit: (password: string) => Promise<void>}) {
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (password.length < PASSWORD_MIN) return setError(`Le mot de passe doit contenir au moins ${PASSWORD_MIN} caractères.`);
    if (password !== confirm) return setError('Les deux mots de passe ne sont pas identiques.');
    setBusy(true);
    setError(undefined);
    try {
      await onSubmit(password);
    } catch (err) {
      setError(describe(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-4">
      <Field label="Nouveau mot de passe" hint={`${PASSWORD_MIN} caractères au moins.`}>
        <Input type="password" autoComplete="new-password" required minLength={PASSWORD_MIN} maxLength={PASSWORD_MAX} value={password} onChange={event => setPassword(event.target.value)} />
      </Field>
      <Field label="Confirmez le mot de passe">
        <Input type="password" autoComplete="new-password" required maxLength={PASSWORD_MAX} value={confirm} onChange={event => setConfirm(event.target.value)} />
      </Field>
      {error && <Alert>{error}</Alert>}
      <Button type="submit" size="lg" busy={busy} className="w-full">{submitLabel}</Button>
    </form>
  );
}
