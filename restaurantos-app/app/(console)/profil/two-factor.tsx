'use client';
import {useEffect, useState} from 'react';
import QRCode from 'qrcode';
import {Copy, Download, ShieldCheck} from 'lucide-react';
import {post} from '@/lib/api';
import type {Me} from '@/lib/profile';
import {Alert, Badge, Button, Card, Field, Input, Modal, useAction, useToast} from '@/components/ui';
import {PasswordInput} from './dialogs';

type Step = {kind: 'password'} | {kind: 'scan'; secret: string; uri: string} | {kind: 'codes'; codes: string[]};

/** Activation : mot de passe, puis QR code à scanner et premier code, puis codes de secours à garder. */
function EnableDialog({open, email, onClose, onDone}: {open: boolean; email: string; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const toast = useToast();
  const [step, setStep] = useState<Step>({kind: 'password'});
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [svg, setSvg] = useState('');

  useEffect(() => {
    if (!open) return;
    setStep({kind: 'password'});
    setPassword('');
    setCode('');
    setSvg('');
  }, [open]);

  useEffect(() => {
    if (step.kind !== 'scan') return;
    let active = true;
    void QRCode.toString(step.uri, {type: 'svg', errorCorrectionLevel: 'M', margin: 1}).then(value => active && setSvg(value));
    return () => {
      active = false;
    };
  }, [step]);

  const download = (codes: string[]) => {
    const blob = new Blob([`Codes de secours RestauSpace (${email})\nChaque code ne sert qu'une fois.\n\n${codes.join('\n')}\n`], {type: 'text/plain'});
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'codes-de-secours-restauspace.txt';
    link.click();
    URL.revokeObjectURL(link.href);
  };

  return (
    <Modal open={open} onClose={step.kind === 'codes' ? () => void onDone().then(onClose) : onClose} title="Validation en deux étapes">
      {step.kind === 'password' && (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('setup', async () => {
            const setup = await post<{secret: string; otpAuthUri: string}>('/me/two-factor/setup', {password});
            setPassword('');
            setStep({kind: 'scan', secret: setup.secret, uri: setup.otpAuthUri});
          });
        }}>
          <p className="text-sm text-ink-600">À chaque connexion, en plus du mot de passe, un code à 6 chiffres de votre application d’authentification (Google Authenticator, Microsoft Authenticator, 2FAS…) vous sera demandé.</p>
          <Field label="Mot de passe actuel"><PasswordInput id="two-factor-password" label="le mot de passe actuel" autoComplete="current-password" value={password} onChange={setPassword} /></Field>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>Annuler</Button>
            <Button type="submit" busy={busy === 'setup'} disabled={!password}>Continuer</Button>
          </div>
        </form>
      )}
      {step.kind === 'scan' && (
        <form className="space-y-4" onSubmit={event => {
          event.preventDefault();
          void run('confirm', async () => {
            const result = await post<{codes: string[]}>('/me/two-factor/confirm', {code: code.trim()});
            setStep({kind: 'codes', codes: result.codes});
          });
        }}>
          <p className="text-sm text-ink-600">Scannez ce QR code avec votre application, puis saisissez le code qu’elle affiche.</p>
          <div className="mx-auto size-48 rounded-xl border border-slate-200 bg-white p-2" aria-label="QR code à scanner" dangerouslySetInnerHTML={{__html: svg}} />
          <p className="text-center text-xs text-ink-500">Impossible de scanner ? Saisissez cette clé : <code className="font-mono text-ink-800 break-all select-all">{step.secret.match(/.{1,4}/g)?.join(' ')}</code></p>
          <Field label="Code à 6 chiffres"><Input autoFocus inputMode="numeric" autoComplete="one-time-code" maxLength={7} value={code} onChange={event => setCode(event.target.value)} placeholder="123456" /></Field>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose}>Annuler</Button>
            <Button type="submit" busy={busy === 'confirm'} disabled={code.replace(/\D/g, '').length !== 6}>Activer</Button>
          </div>
        </form>
      )}
      {step.kind === 'codes' && (
        <div className="space-y-4">
          <Alert tone="amber">Gardez ces codes de secours en lieu sûr : chacun ouvre une connexion une seule fois si vous perdez votre téléphone. Ils ne seront plus affichés.</Alert>
          <ul className="grid grid-cols-2 gap-2 rounded-xl bg-slate-50 p-4 font-mono text-sm">
            {step.codes.map(item => <li key={item}>{item}</li>)}
          </ul>
          <div className="flex flex-wrap justify-end gap-2">
            <Button variant="secondary" onClick={() => {
              void navigator.clipboard?.writeText(step.codes.join('\n')).then(() => toast('success', 'Codes copiés.'));
            }}><Copy className="size-4" aria-hidden="true" />Copier</Button>
            <Button variant="secondary" onClick={() => download(step.codes)}><Download className="size-4" aria-hidden="true" />Télécharger</Button>
            <Button onClick={() => void onDone().then(onClose)}>J’ai gardé mes codes</Button>
          </div>
        </div>
      )}
    </Modal>
  );
}

function DisableDialog({open, onClose, onDone}: {open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  useEffect(() => {
    if (!open) return;
    setPassword('');
    setCode('');
  }, [open]);
  return (
    <Modal open={open} onClose={onClose} title="Désactiver la validation en deux étapes">
      <form className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('disable', async () => {
          await post('/me/two-factor/disable', {password, code: code.trim()});
          await onDone();
          onClose();
        }, 'Validation en deux étapes désactivée. Vos autres appareils ont été déconnectés.');
      }}>
        <Field label="Mot de passe actuel"><PasswordInput id="two-factor-off-password" label="le mot de passe actuel" autoComplete="current-password" value={password} onChange={setPassword} /></Field>
        <Field label="Code de l’application ou code de secours"><Input inputMode="text" autoComplete="one-time-code" maxLength={12} value={code} onChange={event => setCode(event.target.value)} /></Field>
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>Annuler</Button>
          <Button type="submit" variant="danger" busy={busy === 'disable'} disabled={!password || !code.trim()}>Désactiver</Button>
        </div>
      </form>
    </Modal>
  );
}

export function TwoFactorCard({me, onChanged}: {me: Me; onChanged: () => Promise<void>}) {
  const [dialog, setDialog] = useState<'enable' | 'disable' | null>(null);
  const enabled = !!me.twoFactorEnabled;
  return (
    <Card className="p-5">
      <h2 className="flex items-center gap-2.5 font-semibold">
        <ShieldCheck className="size-5 text-ink-500" aria-hidden="true" />Validation en deux étapes
        <Badge tone={enabled ? 'green' : 'neutral'}>{enabled ? 'Activée' : 'Désactivée'}</Badge>
      </h2>
      <p className="mt-3 text-sm text-ink-500">
        {enabled
          ? `Un code de votre application d’authentification est demandé à chaque connexion. Codes de secours restants : ${me.recoveryCodesLeft ?? 0}.`
          : 'Protégez votre compte : même avec votre mot de passe, personne ne peut se connecter sans votre téléphone. Recommandé pour les propriétaires et les gérants.'}
      </p>
      {enabled && (me.recoveryCodesLeft ?? 0) <= 2 && <div className="mt-3"><Alert tone="amber">Il vous reste peu de codes de secours : désactivez puis réactivez la validation pour en obtenir de nouveaux.</Alert></div>}
      <Button variant={enabled ? 'secondary' : 'primary'} size="sm" className="mt-3" onClick={() => setDialog(enabled ? 'disable' : 'enable')}>
        {enabled ? 'Désactiver' : 'Activer'}
      </Button>
      <EnableDialog open={dialog === 'enable'} email={me.email} onClose={() => setDialog(null)} onDone={onChanged} />
      <DisableDialog open={dialog === 'disable'} onClose={() => setDialog(null)} onDone={onChanged} />
    </Card>
  );
}
