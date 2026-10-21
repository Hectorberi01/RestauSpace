'use client';
import {useState, type FormEvent} from 'react';
import {describe} from '@/lib/api';
import {requestPasswordReset} from '@/lib/account';
import {AuthCard, BackToLogin} from '@/components/account';
import {Alert, Button, Field, Input} from '@/components/ui';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      await requestPasswordReset(email.trim());
      setSent(true);
    } catch (err) {
      setError(describe(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthCard title="Mot de passe oublié" subtitle="Indiquez l’adresse de votre compte : nous vous enverrons un lien pour choisir un nouveau mot de passe.">
      {sent ? (
        <Alert tone="blue">Si un compte existe pour cette adresse, un email vient de partir. Le lien est valable peu de temps et ne sert qu’une fois. Pensez à regarder dans les courriers indésirables.</Alert>
      ) : (
        <form onSubmit={submit} className="space-y-4">
          <Field label="Email">
            <Input type="email" autoComplete="username" required value={email} onChange={event => setEmail(event.target.value)} />
          </Field>
          {error && <Alert>{error}</Alert>}
          <Button type="submit" size="lg" busy={busy} className="w-full">Envoyer le lien</Button>
        </form>
      )}
      <BackToLogin />
    </AuthCard>
  );
}
