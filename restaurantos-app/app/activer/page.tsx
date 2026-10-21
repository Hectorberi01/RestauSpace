'use client';
import {useState} from 'react';
import {acceptInvitation} from '@/lib/account';
import {AuthCard, Done, MissingToken, NewPasswordForm, useLinkToken} from '@/components/account';

export default function AcceptInvitationPage() {
  const token = useLinkToken();
  const [done, setDone] = useState(false);

  return (
    <AuthCard title="Activer votre compte" subtitle={done ? undefined : 'Vous avez été invité à rejoindre un restaurant. Choisissez votre mot de passe pour activer le compte.'}>
      {token === undefined ? null : done ? (
        <Done>Compte activé. Connectez-vous avec votre adresse et ce mot de passe.</Done>
      ) : token ? (
        <NewPasswordForm submitLabel="Activer le compte" onSubmit={async password => { await acceptInvitation(token, password); setDone(true); }} />
      ) : (
        <MissingToken />
      )}
    </AuthCard>
  );
}
