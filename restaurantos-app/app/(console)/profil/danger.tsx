'use client';
import {useState} from 'react';
import {useRouter} from 'next/navigation';
import {LogOut, UserX} from 'lucide-react';
import {del, logout, post} from '@/lib/api';
import {Alert, Button, Field, Input, Modal, useAction} from '@/components/ui';

/** Déconnecte toutes les sessions, celle-ci comprise, puis renvoie à la connexion. */
export function SignOutEverywhere() {
  const router = useRouter();
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button size="sm" variant="ghost" className="text-red-600" onClick={() => setOpen(true)}><LogOut className="size-4" aria-hidden="true" />Se déconnecter partout</Button>
      <Modal open={open} onClose={() => setOpen(false)} title="Se déconnecter partout" footer={
        <>
          <Button variant="secondary" onClick={() => setOpen(false)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'all'} onClick={() => run('all', async () => {
            await del('/me/sessions');
            await logout();
            router.replace('/connexion');
          })}>Tout déconnecter</Button>
        </>
      }>
        <p className="text-sm text-ink-700">Tous vos appareils sont déconnectés, celui-ci compris : il faudra vous reconnecter partout. Utile après la perte d’un téléphone ou si vous pensez que quelqu’un utilise votre compte (changez aussi votre mot de passe).</p>
      </Modal>
    </>
  );
}

const WORD = 'SUPPRIMER';

/** Effacement définitif du compte (RGPD) : mot de passe actuel et confirmation écrite. */
export function EraseAccount() {
  const router = useRouter();
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [word, setWord] = useState('');
  return (
    <section className="rounded-2xl border border-red-200 bg-red-50/40 p-5">
      <h2 className="font-semibold text-red-800">Supprimer mon compte</h2>
      <p className="mt-1 text-sm text-ink-700">Vos données personnelles (email, nom, téléphone, photo, sessions) sont effacées définitivement. L’historique du restaurant (commandes, journal) reste, sans votre nom.</p>
      <Button variant="danger" className="mt-3" onClick={() => {
        setPassword('');
        setWord('');
        setOpen(true);
      }}><UserX className="size-4" aria-hidden="true" />Supprimer mon compte</Button>
      <Modal open={open} onClose={() => setOpen(false)} title="Supprimer définitivement mon compte" footer={
        <>
          <Button variant="secondary" onClick={() => setOpen(false)}>Annuler</Button>
          <Button type="submit" form="erase-form" variant="danger" busy={busy === 'erase'} disabled={!password || word.trim().toUpperCase() !== WORD}>Supprimer définitivement</Button>
        </>
      }>
        <form id="erase-form" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('erase', async () => {
            await post('/me/erase', {currentPassword: password});
            await logout();
            router.replace('/connexion');
          });
        }}>
          <Alert>C’est irréversible. Le seul propriétaire d’un restaurant ne peut pas supprimer son compte : nommez d’abord un autre propriétaire dans Personnel.</Alert>
          <Field label="Mot de passe actuel"><Input type="password" autoComplete="current-password" required value={password} onChange={event => setPassword(event.target.value)} /></Field>
          <Field label={`Tapez ${WORD} pour confirmer`}><Input value={word} onChange={event => setWord(event.target.value)} autoComplete="off" /></Field>
        </form>
      </Modal>
    </section>
  );
}
