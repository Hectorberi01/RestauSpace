'use client';
import {useState, type FormEvent} from 'react';
import Link from 'next/link';
import {describe} from '@/lib/api';
import {verifyEmail} from '@/lib/account';
import {AuthCard, Done, MissingToken, useLinkToken} from '@/components/account';
import {Alert, Button, Field, Input} from '@/components/ui';

export default function VerifyEmailPage() {
  const token = useLinkToken();
  const [password, setPassword] = useState('');
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!token) return;
    setBusy(true);
    setError(undefined);
    try {
      await verifyEmail(token, password);
      setDone(true);
    } catch (err) {
      setError(describe(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title="Confirmer votre adresse" subtitle={done ? undefined : 'Pour confirmer que cette adresse est bien la vôtre, saisissez le mot de passe actuel du compte.'}>
      {token === undefined ? null : done ? (
        <Done>Adresse confirmée.</Done>
      ) : token ? (
        <form onSubmit={submit} className="space-y-4">
          <Field label="Mot de passe">
            <Input type="password" autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} />
          </Field>
          {error && <Alert>{error}</Alert>}
          <Button type="submit" size="lg" busy={busy} className="w-full">Confirmer</Button>
          <p className="text-center text-sm">
            <Link href="/mot-de-passe-oublie" className="font-medium text-brand-700 hover:underline">Ce n’est pas votre compte ou mot de passe oublié ?</Link>
          </p>
        </form>
      ) : (
        <MissingToken />
      )}
    </AuthCard>
  );
}
