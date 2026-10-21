'use client';
import {useState} from 'react';
import {Hash} from 'lucide-react';
import {api} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {clearMyPin, setMyPin} from '@/lib/till';
import type {MyPermissions} from '@/lib/types';
import {Button, Card, Field, Input, Modal, useAction} from '@/components/ui';

/** PIN de caisse du membre dans ce restaurant : il ouvre la caisse partagée et sert au pointage. */
export function PinCard() {
  const {busy, run} = useAction();
  const me = useResource(signal => api<MyPermissions>('/staff/me/permissions', {signal}), []);
  const [editing, setEditing] = useState(false);
  const [pin, setPin] = useState('');
  const [confirm, setConfirm] = useState('');
  const hasPin = me.data?.hasPin ?? false;
  const valid = /^\d{4,6}$/.test(pin) && pin === confirm;

  return (
    <Card className="p-5">
      <h2 className="flex items-center gap-2.5 font-semibold"><Hash className="size-5 text-ink-500" aria-hidden="true" />PIN de caisse</h2>
      <p className="mt-3 text-sm text-ink-500">
        {hasPin ? 'Votre PIN ouvre la caisse partagée de ce restaurant et sert à pointer vos arrivées et départs.' : 'Choisissez un PIN de 4 à 6 chiffres pour ouvrir la caisse partagée et pointer.'}
      </p>
      <div className="mt-3 flex gap-2">
        <Button size="sm" variant="secondary" disabled={!me.data} onClick={() => { setPin(''); setConfirm(''); setEditing(true); }}>{hasPin ? 'Changer le PIN' : 'Choisir un PIN'}</Button>
        {hasPin && <Button size="sm" variant="ghost" busy={busy === 'clear'} onClick={() => void run('clear', async () => { await clearMyPin(); await me.reload(); }, 'PIN retiré.')}>Retirer</Button>}
      </div>
      <Modal open={editing} onClose={() => setEditing(false)} title="PIN de caisse" footer={
        <>
          <Button variant="secondary" onClick={() => setEditing(false)}>Annuler</Button>
          <Button type="submit" form="pin-form" busy={busy === 'save'} disabled={!valid}>Enregistrer</Button>
        </>
      }>
        <form id="pin-form" className="space-y-4" onSubmit={event => {
          event.preventDefault();
          if (!valid) return;
          void run('save', async () => {
            await setMyPin(pin);
            setEditing(false);
            await me.reload();
          }, 'PIN enregistré.');
        }}>
          <Field label="Nouveau PIN" hint="4 à 6 chiffres, ni répétés (0000) ni qui se suivent (1234).">
            <Input type="password" inputMode="numeric" autoComplete="off" maxLength={6} value={pin} onChange={event => setPin(event.target.value.replace(/\D/g, ''))} />
          </Field>
          <Field label="Confirmer le PIN">
            <Input type="password" inputMode="numeric" autoComplete="off" maxLength={6} value={confirm} onChange={event => setConfirm(event.target.value.replace(/\D/g, ''))} />
          </Field>
          {confirm.length >= 4 && pin !== confirm && <p className="text-sm text-red-700">Les deux PIN ne correspondent pas.</p>}
        </form>
      </Modal>
    </Card>
  );
}
