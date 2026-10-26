'use client';
import {useEffect, useState} from 'react';
import {Eye, EyeOff} from 'lucide-react';
import {normalizePhone} from '@/lib/reservations';
import {changeEmail, changePassword, timeZones, updatePreferences, updateProfile, type Me} from '@/lib/profile';
import {Button, Field, Input, Modal, Select, useAction} from '@/components/ui';

export function PasswordInput({id, value, onChange, autoComplete, label}: {id: string; value: string; onChange: (value: string) => void; autoComplete: string; label: string}) {
  const [visible, setVisible] = useState(false);
  return (
    <div className="relative">
      <Input id={id} type={visible ? 'text' : 'password'} required autoComplete={autoComplete} value={value} onChange={event => onChange(event.target.value)} className="pr-11" />
      <button type="button" onClick={() => setVisible(current => !current)} aria-label={visible ? `Masquer ${label}` : `Afficher ${label}`}
        className="absolute top-1/2 right-2 grid size-8 -translate-y-1/2 place-items-center rounded-lg text-ink-500 hover:bg-slate-100">
        {visible ? <EyeOff className="size-4" aria-hidden="true" /> : <Eye className="size-4" aria-hidden="true" />}
      </button>
    </div>
  );
}

export function ProfileDialog({me, open, onClose, onDone}: {me: Me; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [phone, setPhone] = useState('');
  const [city, setCity] = useState('');
  useEffect(() => {
    if (!open) return;
    setFirstName(me.firstName ?? '');
    setLastName(me.lastName ?? '');
    setPhone(me.phone ?? '');
    setCity(me.city ?? '');
  }, [open, me]);

  return (
    <Modal open={open} onClose={onClose} title="Informations personnelles" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="profile-form" busy={busy === 'profile'}>Enregistrer</Button>
      </>
    }>
      <form id="profile-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('profile', async () => {
          const normalized = normalizePhone(phone);
          if (normalized === undefined) throw new Error('Numéro de téléphone invalide : utilisez le format international (+33…, +229…).');
          await updateProfile({firstName: firstName.trim() || null, lastName: lastName.trim() || null, phone: normalized, city: city.trim() || null});
          await onDone();
          onClose();
        }, 'Profil enregistré.');
      }}>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Prénom"><Input maxLength={60} autoComplete="given-name" value={firstName} onChange={event => setFirstName(event.target.value)} /></Field>
          <Field label="Nom"><Input maxLength={60} autoComplete="family-name" value={lastName} onChange={event => setLastName(event.target.value)} /></Field>
        </div>
        <Field label="Téléphone" hint="Format international, par exemple +33 6 12 34 56 78."><Input type="tel" autoComplete="tel" value={phone} onChange={event => setPhone(event.target.value)} /></Field>
        <Field label="Ville"><Input maxLength={80} autoComplete="address-level2" value={city} onChange={event => setCity(event.target.value)} /></Field>
      </form>
    </Modal>
  );
}

export function PreferencesDialog({me, open, onClose, onDone}: {me: Me; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
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
    <Modal open={open} onClose={onClose} title="Préférences" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="preferences-form" busy={busy === 'preferences'}>Enregistrer</Button>
      </>
    }>
      <form id="preferences-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('preferences', async () => {
          await updatePreferences({language, timeZone, productNewsEmails: news});
          await onDone();
          onClose();
        }, 'Préférences enregistrées.');
      }}>
        <Field label="Langue de l’interface" hint="L’anglais arrive bientôt : la console reste en français en attendant.">
          <Select value={language} onChange={event => setLanguage(event.target.value)}>
            <option value="fr">Français</option>
            <option value="en">English</option>
          </Select>
        </Field>
        <Field label="Fuseau horaire" hint="Utilisé pour vos emails et vos exports.">
          <Select value={timeZone} onChange={event => setTimeZone(event.target.value)}>
            {zones.map(zone => <option key={zone.id} value={zone.id}>{zone.label}</option>)}
          </Select>
        </Field>
        <label className="flex cursor-pointer items-start gap-3 rounded-xl border border-slate-200 p-3">
          <input type="checkbox" className="mt-0.5 size-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500" checked={news} onChange={event => setNews(event.target.checked)} />
          <span className="text-sm">
            <span className="block font-medium">Emails d’actualités</span>
            <span className="text-ink-500">Nouveautés et conseils de la plateforme, quelques fois par mois. Les emails de sécurité sont toujours envoyés.</span>
          </span>
        </label>
      </form>
    </Modal>
  );
}

export function PasswordDialog({open, onClose, onDone}: {open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
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
    <Modal open={open} onClose={onClose} title="Changer le mot de passe" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="password-form" busy={busy === 'password'} disabled={mismatch || next.length < 12}>Changer le mot de passe</Button>
      </>
    }>
      <form id="password-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('password', async () => {
          await changePassword(current, next);
          await onDone();
          onClose();
        }, 'Mot de passe modifié. Vos autres appareils ont été déconnectés.');
      }}>
        <Field label="Mot de passe actuel"><PasswordInput id="current-password" label="le mot de passe actuel" autoComplete="current-password" value={current} onChange={setCurrent} /></Field>
        <Field label="Nouveau mot de passe" hint="12 caractères au moins. Évitez un mot de passe utilisé ailleurs.">
          <PasswordInput id="new-password" label="le nouveau mot de passe" autoComplete="new-password" value={next} onChange={setNext} />
        </Field>
        <Field label="Confirmation" hint={mismatch ? 'Les deux mots de passe ne correspondent pas.' : undefined}>
          <PasswordInput id="confirm-password" label="la confirmation" autoComplete="new-password" value={confirm} onChange={setConfirm} />
        </Field>
        <p className="text-xs text-ink-500">Vos autres appareils seront déconnectés ; celui-ci reste connecté.</p>
      </form>
    </Modal>
  );
}

export function EmailDialog({me, open, onClose, onDone}: {me: Me; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  useEffect(() => {
    if (!open) return;
    setEmail(me.email);
    setPassword('');
  }, [open, me.email]);

  return (
    <Modal open={open} onClose={onClose} title="Changer d’adresse email" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="email-form" busy={busy === 'email'} disabled={email.trim().toLowerCase() === me.email.toLowerCase()}>Changer l’adresse</Button>
      </>
    }>
      <form id="email-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('email', async () => {
          await changeEmail(email.trim(), password);
          await onDone();
          onClose();
        }, 'Adresse modifiée. Un lien de vérification vous a été envoyé.');
      }}>
        <Field label="Nouvelle adresse"><Input type="email" required autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} /></Field>
        <Field label="Mot de passe actuel"><PasswordInput id="email-password" label="le mot de passe" autoComplete="current-password" value={password} onChange={setPassword} /></Field>
        <p className="text-xs text-ink-500">Vous recevrez un lien de vérification à la nouvelle adresse.</p>
      </form>
    </Modal>
  );
}
