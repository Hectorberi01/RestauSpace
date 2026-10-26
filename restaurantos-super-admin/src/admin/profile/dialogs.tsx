'use client';
import {useEffect, useState, type FormEvent, type ReactNode} from 'react';
import QRCode from 'qrcode';
import {Copy, Download, Eye, EyeOff} from 'lucide-react';
import {toast} from 'sonner';
import {Button} from '@/components/ui/button';
import {Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle} from '@/components/ui/dialog';
import {describe} from '../../services/api/client';
import {
  changeEmail, changePassword, confirmTwoFactor, disableTwoFactor, normalizePhone, startTwoFactor, timeZones, updatePreferences, updateProfile,
  type MyProfile,
} from '../../services/api/profile';

type Done = () => Promise<void>;

/** Lance une action, affiche son erreur, et rend l'état d'attente. */
function useRun() {
  const [busy, setBusy] = useState(false);
  const run = async (action: () => Promise<void>, success?: string) => {
    setBusy(true);
    try {
      await action();
      if (success) toast.success(success);
    } catch (error) {
      toast.error(describe(error));
    } finally {
      setBusy(false);
    }
  };
  return {busy, run};
}

function Shell({open, onClose, title, description, children, footer, onSubmit}: {
  open: boolean; onClose: () => void; title: string; description: string; children: ReactNode; footer: ReactNode; onSubmit?: (event: FormEvent) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={value => !value && onClose()}>
      <DialogContent className="form-dialog">
        <DialogHeader><DialogTitle>{title}</DialogTitle><DialogDescription>{description}</DialogDescription></DialogHeader>
        {onSubmit ? <form onSubmit={onSubmit}>{children}<DialogFooter>{footer}</DialogFooter></form> : <>{children}<DialogFooter>{footer}</DialogFooter></>}
      </DialogContent>
    </Dialog>
  );
}

export function PasswordInput({value, onChange, autoComplete, label}: {value: string; onChange: (value: string) => void; autoComplete: string; label: string}) {
  const [visible, setVisible] = useState(false);
  return (
    <span className="password-input">
      <input type={visible ? 'text' : 'password'} required autoComplete={autoComplete} value={value} onChange={event => onChange(event.target.value)} aria-label={label} />
      <button type="button" onClick={() => setVisible(current => !current)} aria-label={visible ? `Masquer ${label}` : `Afficher ${label}`}>
        {visible ? <EyeOff size={16} /> : <Eye size={16} />}
      </button>
    </span>
  );
}

export function ProfileDialog({me, open, onClose, onDone}: {me: MyProfile; open: boolean; onClose: () => void; onDone: Done}) {
  const {busy, run} = useRun();
  const [values, setValues] = useState({firstName: '', lastName: '', phone: '', city: ''});
  useEffect(() => {
    if (open) setValues({firstName: me.firstName ?? '', lastName: me.lastName ?? '', phone: me.phone ?? '', city: me.city ?? ''});
  }, [open, me]);
  const set = (key: keyof typeof values) => (event: {target: {value: string}}) => setValues(current => ({...current, [key]: event.target.value}));

  return (
    <Shell open={open} onClose={onClose} title="Informations personnelles" description="Votre nom apparaît dans l’en-tête et le journal d’audit."
      onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          const phone = normalizePhone(values.phone);
          if (phone === undefined) throw new Error('Numéro invalide : utilisez le format international (+33…, +229…).');
          await updateProfile({firstName: values.firstName.trim() || null, lastName: values.lastName.trim() || null, phone, city: values.city.trim() || null});
          await onDone();
          onClose();
        }, 'Profil enregistré.');
      }}
      footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy}>Enregistrer</Button></>}>
      <div className="form-fields">
        <label>Prénom<input maxLength={60} autoComplete="given-name" value={values.firstName} onChange={set('firstName')} /></label>
        <label>Nom<input maxLength={60} autoComplete="family-name" value={values.lastName} onChange={set('lastName')} /></label>
        <label>Téléphone<input type="tel" autoComplete="tel" placeholder="+33 6 12 34 56 78" value={values.phone} onChange={set('phone')} /></label>
        <label>Ville<input maxLength={80} autoComplete="address-level2" value={values.city} onChange={set('city')} /></label>
      </div>
    </Shell>
  );
}

export function PreferencesDialog({me, open, onClose, onDone}: {me: MyProfile; open: boolean; onClose: () => void; onDone: Done}) {
  const {busy, run} = useRun();
  const [language, setLanguage] = useState('fr');
  const [timeZone, setTimeZone] = useState('Europe/Paris');
  const [news, setNews] = useState(false);
  useEffect(() => {
    if (!open) return;
    setLanguage(me.language);
    setTimeZone(me.timeZone);
    setNews(me.productNewsEmails);
  }, [open, me]);
  const zones = timeZones.some(zone => zone.id === me.timeZone) ? timeZones : [{id: me.timeZone, label: me.timeZone}, ...timeZones];

  return (
    <Shell open={open} onClose={onClose} title="Préférences" description="Langue, fuseau horaire et emails d’actualités."
      onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          await updatePreferences({language, timeZone, productNewsEmails: news});
          await onDone();
          onClose();
        }, 'Préférences enregistrées.');
      }}
      footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy}>Enregistrer</Button></>}>
      <div className="form-fields">
        <label>Langue<select value={language} onChange={event => setLanguage(event.target.value)}><option value="fr">Français</option><option value="en">English</option></select></label>
        <label>Fuseau horaire<select value={timeZone} onChange={event => setTimeZone(event.target.value)}>{zones.map(zone => <option key={zone.id} value={zone.id}>{zone.label}</option>)}</select></label>
      </div>
      <label className="settings-toggle">
        <span><strong>Emails d’actualités</strong><small className="muted">Nouveautés de la plateforme. Les emails de sécurité sont toujours envoyés.</small></span>
        <input type="checkbox" checked={news} onChange={event => setNews(event.target.checked)} />
      </label>
    </Shell>
  );
}

export function PasswordDialog({open, onClose, onDone}: {open: boolean; onClose: () => void; onDone: Done}) {
  const {busy, run} = useRun();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  useEffect(() => {
    if (!open) return;
    setCurrent('');
    setNext('');
    setConfirm('');
  }, [open]);
  const mismatch = confirm.length > 0 && confirm !== next;

  return (
    <Shell open={open} onClose={onClose} title="Changer le mot de passe" description="Vos autres appareils seront déconnectés ; celui-ci reste connecté."
      onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          await changePassword(current, next);
          await onDone();
          onClose();
        }, 'Mot de passe modifié. Vos autres appareils ont été déconnectés.');
      }}
      footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy || mismatch || next.length < 12}>Changer le mot de passe</Button></>}>
      <div className="form-fields single">
        <label>Mot de passe actuel<PasswordInput label="le mot de passe actuel" autoComplete="current-password" value={current} onChange={setCurrent} /></label>
        <label>Nouveau mot de passe<PasswordInput label="le nouveau mot de passe" autoComplete="new-password" value={next} onChange={setNext} /><small className="muted">12 caractères au moins.</small></label>
        <label>Confirmation<PasswordInput label="la confirmation" autoComplete="new-password" value={confirm} onChange={setConfirm} />{mismatch && <small className="field-error">Les deux mots de passe ne correspondent pas.</small>}</label>
      </div>
    </Shell>
  );
}

export function EmailDialog({me, open, onClose, onDone}: {me: MyProfile; open: boolean; onClose: () => void; onDone: Done}) {
  const {busy, run} = useRun();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  useEffect(() => {
    if (!open) return;
    setEmail(me.email);
    setPassword('');
  }, [open, me.email]);

  return (
    <Shell open={open} onClose={onClose} title="Changer d’adresse email" description="Un lien de vérification sera envoyé à la nouvelle adresse."
      onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          await changeEmail(email.trim(), password);
          await onDone();
          onClose();
        }, 'Adresse modifiée. Un lien de vérification vous a été envoyé.');
      }}
      footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy || email.trim().toLowerCase() === me.email.toLowerCase()}>Changer l’adresse</Button></>}>
      <div className="form-fields single">
        <label>Nouvelle adresse<input type="email" required autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} /></label>
        <label>Mot de passe actuel<PasswordInput label="le mot de passe" autoComplete="current-password" value={password} onChange={setPassword} /></label>
      </div>
    </Shell>
  );
}

type Step = {kind: 'password'} | {kind: 'scan'; secret: string; uri: string} | {kind: 'codes'; codes: string[]};

/** Mot de passe, puis QR code et premier code, puis codes de secours affichés une seule fois. */
export function EnableTwoFactorDialog({open, email, onClose, onDone}: {open: boolean; email: string; onClose: () => void; onDone: Done}) {
  const {busy, run} = useRun();
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

  const finish = () => void onDone().then(onClose);
  const download = (codes: string[]) => {
    const blob = new Blob([`Codes de secours RestauSpace (${email})\nChaque code ne sert qu'une fois.\n\n${codes.join('\n')}\n`], {type: 'text/plain'});
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = 'codes-de-secours-restauspace-admin.txt';
    link.click();
    URL.revokeObjectURL(link.href);
  };

  if (step.kind === 'codes') {
    return (
      <Shell open={open} onClose={finish} title="Codes de secours" description="Gardez-les en lieu sûr : chacun ouvre une connexion une seule fois. Ils ne seront plus affichés."
        footer={<>
          <Button type="button" variant="outline" onClick={() => void navigator.clipboard?.writeText(step.codes.join('\n')).then(() => toast.success('Codes copiés.'))}><Copy size={15} />Copier</Button>
          <Button type="button" variant="outline" onClick={() => download(step.codes)}><Download size={15} />Télécharger</Button>
          <Button type="button" onClick={finish}>J’ai gardé mes codes</Button>
        </>}>
        <ul className="recovery-codes">{step.codes.map(item => <li key={item}>{item}</li>)}</ul>
      </Shell>
    );
  }

  if (step.kind === 'scan') {
    return (
      <Shell open={open} onClose={onClose} title="Scannez le QR code" description="Avec votre application d’authentification, puis saisissez le code affiché."
        onSubmit={event => {
          event.preventDefault();
          void run(async () => {
            const result = await confirmTwoFactor(code.trim());
            setStep({kind: 'codes', codes: result.codes});
          });
        }}
        footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy || code.replace(/\D/g, '').length !== 6}>Activer</Button></>}>
        <div className="qr-box" aria-label="QR code à scanner" dangerouslySetInnerHTML={{__html: svg}} />
        <p className="muted qr-key">Impossible de scanner ? Clé : <code>{step.secret.match(/.{1,4}/g)?.join(' ')}</code></p>
        <div className="form-fields single">
          <label>Code à 6 chiffres<input autoFocus inputMode="numeric" autoComplete="one-time-code" maxLength={7} placeholder="123456" value={code} onChange={event => setCode(event.target.value)} /></label>
        </div>
      </Shell>
    );
  }

  return (
    <Shell open={open} onClose={onClose} title="Validation en deux étapes" description="À chaque connexion, un code de votre application d’authentification sera demandé en plus du mot de passe."
      onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          const setup = await startTwoFactor(password);
          setPassword('');
          setStep({kind: 'scan', secret: setup.secret, uri: setup.otpAuthUri});
        });
      }}
      footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" disabled={busy || !password}>Continuer</Button></>}>
      <div className="form-fields single">
        <label>Mot de passe actuel<PasswordInput label="le mot de passe actuel" autoComplete="current-password" value={password} onChange={setPassword} /></label>
      </div>
    </Shell>
  );
}

export function DisableTwoFactorDialog({open, onClose, onDone}: {open: boolean; onClose: () => void; onDone: Done}) {
  const {busy, run} = useRun();
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  useEffect(() => {
    if (!open) return;
    setPassword('');
    setCode('');
  }, [open]);

  return (
    <Shell open={open} onClose={onClose} title="Désactiver la validation en deux étapes" description="Déconseillé pour un compte d’administration de la plateforme."
      onSubmit={event => {
        event.preventDefault();
        void run(async () => {
          await disableTwoFactor(password, code.trim());
          await onDone();
          onClose();
        }, 'Validation en deux étapes désactivée. Vos autres appareils ont été déconnectés.');
      }}
      footer={<><Button type="button" variant="outline" onClick={onClose}>Annuler</Button><Button type="submit" variant="destructive" disabled={busy || !password || !code.trim()}>Désactiver</Button></>}>
      <div className="form-fields single">
        <label>Mot de passe actuel<PasswordInput label="le mot de passe actuel" autoComplete="current-password" value={password} onChange={setPassword} /></label>
        <label>Code de l’application ou code de secours<input autoComplete="one-time-code" maxLength={12} value={code} onChange={event => setCode(event.target.value)} /></label>
      </div>
    </Shell>
  );
}
