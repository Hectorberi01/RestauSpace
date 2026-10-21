'use client';
import {useEffect, useState, type FormEvent} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {LogIn, ShieldCheck} from 'lucide-react';
import {ApiError, describe, login, loginWithCode} from '@/lib/api';
import {resendVerification} from '@/lib/account';
import {useSession} from '@/components/console';
import {Logo} from '@/components/shell';
import {Alert, Button, Field, Input} from '@/components/ui';

export default function LoginPage() {
  const router = useRouter();
  const session = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [challenge, setChallenge] = useState<string | null>(null);
  const [code, setCode] = useState('');
  const [unverified, setUnverified] = useState(false);
  const [resent, setResent] = useState(false);

  useEffect(() => {
    if (session) router.replace(session.tenantId ? '/tableau-de-bord' : '/restaurants');
  }, [session, router]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    setUnverified(false);
    setResent(false);
    try {
      if (challenge) {
        await loginWithCode(challenge, code.trim());
      } else {
        const next = await login(email.trim(), password);
        if (next) {
          setChallenge(next);
          setPassword('');
        }
      }
    } catch (err) {
      if (challenge && err instanceof ApiError && err.code === 'identity.two_factor.challenge_invalid') {
        setChallenge(null);
        setCode('');
        setError('Le délai est dépassé : saisissez de nouveau votre mot de passe.');
      } else if (challenge && err instanceof ApiError && err.status === 401) {
        setError('Code incorrect.');
      } else {
        setUnverified(err instanceof ApiError && err.code === 'identity.user.email_not_verified');
        setError(err instanceof ApiError && err.status === 401 ? 'Email ou mot de passe incorrect.' : describe(err));
      }
    } finally {
      setBusy(false);
    }
  };

  if (challenge) {
    return (
      <div className="grid min-h-dvh place-items-center bg-gradient-to-b from-brand-50 to-slate-50 px-4 py-10">
        <form onSubmit={submit} className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-7 shadow-xl shadow-brand-900/5">
          <Logo />
          <h1 className="mt-6 flex items-center gap-2 text-2xl font-bold tracking-tight"><ShieldCheck className="size-6 text-brand-600" aria-hidden="true" />Validation</h1>
          <p className="mt-1 text-sm text-ink-500">Saisissez le code à 6 chiffres de votre application d’authentification, ou un code de secours.</p>
          <div className="mt-6 space-y-4">
            <Field label="Code">
              <Input autoFocus inputMode="text" autoComplete="one-time-code" required maxLength={12} value={code} onChange={event => setCode(event.target.value)} placeholder="123456" />
            </Field>
            {error && <Alert>{error}</Alert>}
            <Button type="submit" size="lg" busy={busy} className="w-full">Valider</Button>
            <button type="button" className="w-full text-center text-sm font-medium text-brand-700 hover:underline" onClick={() => {
              setChallenge(null);
              setCode('');
              setError(undefined);
            }}>Revenir au mot de passe</button>
          </div>
        </form>
      </div>
    );
  }

  return (
    <div className="grid min-h-dvh place-items-center bg-gradient-to-b from-brand-50 to-slate-50 px-4 py-10">
      <form onSubmit={submit} className="w-full max-w-sm rounded-2xl border border-slate-200 bg-white p-7 shadow-xl shadow-brand-900/5">
        <Logo />
        <h1 className="mt-6 text-2xl font-bold tracking-tight">Connexion</h1>
        <p className="mt-1 text-sm text-ink-500">Accédez à la console de votre restaurant.</p>
        <div className="mt-6 space-y-4">
          <Field label="Email">
            <Input type="email" autoComplete="username" required value={email} onChange={event => setEmail(event.target.value)} />
          </Field>
          <Field label="Mot de passe">
            <Input type="password" autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} />
          </Field>
          <p className="-mt-2 text-right text-sm">
            <Link href="/mot-de-passe-oublie" className="font-medium text-brand-700 hover:underline">Mot de passe oublié ?</Link>
          </p>
          {error && <Alert tone={unverified ? 'amber' : 'red'}>{error}</Alert>}
          {unverified && (
            <button type="button" disabled={resent} className="-mt-2 text-sm font-medium text-brand-700 hover:underline disabled:text-ink-500 disabled:no-underline" onClick={async () => {
              await resendVerification(email.trim()).catch(() => undefined);
              setResent(true);
            }}>{resent ? 'Email renvoyé : vérifiez votre boîte mail.' : 'Renvoyer l’email de confirmation'}</button>
          )}
          <Button type="submit" size="lg" busy={busy} className="w-full">
            {!busy && <LogIn className="size-4" aria-hidden="true" />}
            Se connecter
          </Button>
        </div>
        <p className="mt-6 text-center text-sm text-ink-600">Pas encore de compte ? <Link href="/inscription" className="font-semibold text-brand-700 hover:underline">Créer mon restaurant</Link></p>
        <p className="mt-4 text-xs text-ink-500">Vous avez reçu une invitation ? Ouvrez le lien « Activer votre compte » de l’email pour choisir votre mot de passe, puis revenez ici.</p>
      </form>
    </div>
  );
}
