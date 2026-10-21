'use client';
import {useState} from 'react';
import {resetPassword} from '@/lib/account';
import {AuthCard, Done, MissingToken, NewPasswordForm, useLinkToken} from '@/components/account';

export default function ResetPasswordPage() {
  const token = useLinkToken();
  const [done, setDone] = useState(false);

  return (
    <AuthCard title="Nouveau mot de passe" subtitle={done ? undefined : 'Choisissez le mot de passe de votre compte. Vos autres appareils seront déconnectés.'}>
      {token === undefined ? null : done ? (
        <Done>Mot de passe changé. Connectez-vous avec le nouveau.</Done>
      ) : token ? (
        <NewPasswordForm submitLabel="Enregistrer" onSubmit={async password => { await resetPassword(token, password); setDone(true); }} />
      ) : (
        <MissingToken />
      )}
    </AuthCard>
  );
}
