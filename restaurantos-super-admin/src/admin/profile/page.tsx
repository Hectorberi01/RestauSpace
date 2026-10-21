'use client';
import {useCallback, useEffect, useRef, useState, type ReactNode} from 'react';
import {
  Activity, CalendarDays, Camera, CheckCircle2, KeyRound, Laptop, LogOut, Mail, MapPin, Monitor, Pencil, Phone, Settings2, ShieldCheck, Smartphone, Trash2, User,
} from 'lucide-react';
import {toast} from 'sonner';
import {Button} from '@/components/ui/button';
import {ConfirmDialog} from '../../components/feedback/dialogs';
import {ErrorState, LoadingSkeleton, PageHeader, StatCard} from '../../components/ui/shared';
import {describe} from '../../services/api/client';
import {logout} from '../../services/api/auth';
import {
  activityLabel, formatPhone, fullName, languageLabel, loadActivity, loadAvatar, loadProfile, loadSessions, profileChanged, removeAvatar, revokeAllSessions, revokeSession,
  uploadAvatar, type MyActivity, type MyProfile, type MySession,
} from '../../services/api/profile';
import {date, dateTime} from '../../utils/format';
import {DisableTwoFactorDialog, EmailDialog, EnableTwoFactorDialog, PasswordDialog, PreferencesDialog, ProfileDialog} from './dialogs';

type Editing = 'profile' | 'preferences' | 'password' | 'email' | 'enable-2fa' | 'disable-2fa' | null;

interface Data {
  me: MyProfile;
  sessions: MySession[];
  activity: MyActivity[];
}

function initialsOf(me: MyProfile) {
  const name = fullName(me) ?? me.email.split('@')[0].replace(/[._-]+/g, ' ');
  return name.split(' ').filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase() || '?';
}

function ago(iso: string) {
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) return 'à l’instant';
  if (minutes < 60) return `il y a ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `il y a ${hours} h`;
  const days = Math.round(hours / 24);
  return days === 1 ? 'hier' : `il y a ${days} jours`;
}

function deviceIcon(label: string | null) {
  if (label && /iPhone|Android|mobile/i.test(label)) return <Smartphone size={17} />;
  if (label && /iPad/i.test(label)) return <Monitor size={17} />;
  return <Laptop size={17} />;
}

function Panel({icon, title, subtitle, action, children}: {icon: ReactNode; title: string; subtitle?: string; action?: ReactNode; children: ReactNode}) {
  return (
    <section className="panel">
      <div className="panel-heading">
        <div><h2 className="profile-title">{icon}{title}</h2>{subtitle && <p>{subtitle}</p>}</div>
        {action}
      </div>
      {children}
    </section>
  );
}

function Avatar({me, url, large}: {me: MyProfile; url: string | null; large?: boolean}) {
  return url
    // eslint-disable-next-line @next/next/no-img-element
    ? <img src={url} alt="" className={`profile-avatar ${large ? 'large' : ''}`} />
    : <span className={`profile-avatar ${large ? 'large' : ''}`} aria-hidden="true">{initialsOf(me)}</span>;
}

export default function ProfilePage() {
  const [data, setData] = useState<Data | null>(null);
  const [failed, setFailed] = useState(false);
  const [editing, setEditing] = useState<Editing>(null);
  const [avatar, setAvatar] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const reload = useCallback(async () => {
    try {
      const [me, sessions] = await Promise.all([loadProfile(), loadSessions()]);
      const activity = await loadActivity(me.id);
      setData({me, sessions, activity});
      setFailed(false);
    } catch {
      setFailed(true);
    }
  }, []);

  const changed = useCallback(async () => {
    await reload();
    profileChanged();
  }, [reload]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const avatarVersion = data?.me.avatarUpdatedAtUtc ?? null;
  useEffect(() => {
    if (!avatarVersion) {
      setAvatar(null);
      return;
    }
    const controller = new AbortController();
    let url: string | null = null;
    loadAvatar(controller.signal).then(result => {
      url = result;
      setAvatar(result);
    }).catch(() => setAvatar(null));
    return () => {
      controller.abort();
      if (url) URL.revokeObjectURL(url);
    };
  }, [avatarVersion]);

  const act = async (key: string, action: () => Promise<void>, success: string) => {
    setBusy(key);
    try {
      await action();
      toast.success(success);
    } catch (error) {
      toast.error(describe(error));
    } finally {
      setBusy(null);
    }
  };

  if (failed && !data) return <ErrorState retry={() => void reload()} />;
  if (!data) return <LoadingSkeleton />;
  const {me, sessions, activity} = data;
  const name = fullName(me);
  const twoFactor = !!me.twoFactorEnabled;
  const ordered = [...sessions].sort((a, b) => Number(b.isCurrent) - Number(a.isCurrent) || b.createdAtUtc.localeCompare(a.createdAtUtc));
  const pickPhoto = () => fileInput.current?.click();

  return (
    <>
      <PageHeader title="Mon profil" subtitle="Votre compte d’administration : identité, sécurité et appareils connectés." />

      <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" tabIndex={-1} aria-hidden="true"
        onChange={event => {
          const file = event.target.files?.[0];
          event.target.value = '';
          if (file) void act('avatar', async () => {
            await uploadAvatar(file);
            await changed();
          }, 'Photo enregistrée.');
        }} />

      <div className="profile-top">
        <section className="panel profile-identity">
          <div className="profile-photo">
            <Avatar me={me} url={avatar} large />
            <button type="button" onClick={pickPhoto} aria-label="Changer la photo de profil"><Pencil size={15} /></button>
          </div>
          <div className="profile-summary">
            <strong>{name ?? me.email.split('@')[0]}</strong>
            <span className="muted">Super administrateur de la plateforme</span>
            <span><Mail size={15} />{me.email}</span>
            {me.phone && <span><Phone size={15} />{formatPhone(me.phone)}</span>}
            {me.city && <span><MapPin size={15} />{me.city}</span>}
            {me.isEmailVerified
              ? <span className="status green"><CheckCircle2 size={12} />Adresse vérifiée</span>
              : <span className="status amber">Adresse email à vérifier</span>}
          </div>
        </section>
        <StatCard label="Membre depuis" value={date(me.createdAtUtc)} caption="Création du compte" icon={CalendarDays} />
        <StatCard label="Sessions actives" value={String(sessions.length)} caption={sessions.length > 1 ? 'Sur plusieurs appareils' : 'Cet appareil uniquement'} icon={Laptop} />
        <StatCard label="Deux étapes" value={twoFactor ? 'Activée' : 'Désactivée'} caption={twoFactor ? `${me.recoveryCodesLeft ?? 0} codes de secours` : 'Recommandée pour un administrateur'} icon={ShieldCheck} negative={!twoFactor} />
      </div>

      {!twoFactor && (
        <div className="notice">
          Votre compte donne accès à tous les restaurants de la plateforme : activez la validation en deux étapes.{' '}
          <button className="text-link" onClick={() => setEditing('enable-2fa')}>Activer maintenant</button>
        </div>
      )}

      <div className="profile-grid">
        <Panel icon={<User size={18} />} title="Informations personnelles" subtitle="Visibles par les autres administrateurs."
          action={<Button size="sm" variant="outline" onClick={() => setEditing('profile')}><Pencil size={14} />Modifier</Button>}>
          <dl className="info-list">
            <div><dt>Nom complet</dt><dd>{name ?? <span className="muted">Non renseigné</span>}</dd></div>
            <div><dt>Email</dt><dd>{me.email} <button className="text-link" onClick={() => setEditing('email')}>Changer</button></dd></div>
            <div><dt>Téléphone</dt><dd>{me.phone ? formatPhone(me.phone) : <span className="muted">Non renseigné</span>}</dd></div>
            <div><dt>Ville</dt><dd>{me.city ?? <span className="muted">Non renseignée</span>}</dd></div>
          </dl>
          <div className="profile-photo-row">
            <Avatar me={me} url={avatar} />
            <Button size="sm" variant="outline" disabled={busy === 'avatar'} onClick={pickPhoto}><Camera size={14} />{me.avatarUpdatedAtUtc ? 'Changer la photo' : 'Ajouter une photo'}</Button>
            {me.avatarUpdatedAtUtc && (
              <Button size="sm" variant="ghost" className="danger-text" disabled={busy === 'remove-avatar'} onClick={() => void act('remove-avatar', async () => {
                await removeAvatar();
                await changed();
              }, 'Photo supprimée.')}><Trash2 size={14} />Supprimer</Button>
            )}
            <small className="muted">JPEG, PNG ou WebP, recadrée en carré.</small>
          </div>
        </Panel>

        <Panel icon={<KeyRound size={18} />} title="Sécurité" subtitle="Mot de passe et validation en deux étapes.">
          <div className="settings-toggle">
            <span>
              <strong>Mot de passe</strong>
              <small className="muted">{me.passwordChangedAtUtc ? `Modifié le ${date(me.passwordChangedAtUtc)}` : `Défini le ${date(me.createdAtUtc)}`}</small>
            </span>
            <Button size="sm" variant="outline" onClick={() => setEditing('password')}>Changer</Button>
          </div>
          <div className="settings-toggle">
            <span>
              <strong>Validation en deux étapes <span className={`status ${twoFactor ? 'green' : 'gray'}`}>{twoFactor ? 'Activée' : 'Désactivée'}</span></strong>
              <small className="muted">
                {twoFactor
                  ? `Un code de l’application d’authentification est demandé à chaque connexion. Codes de secours restants : ${me.recoveryCodesLeft ?? 0}.`
                  : 'Un code de votre téléphone en plus du mot de passe à chaque connexion.'}
              </small>
            </span>
            <Button size="sm" variant={twoFactor ? 'outline' : 'default'} onClick={() => setEditing(twoFactor ? 'disable-2fa' : 'enable-2fa')}>{twoFactor ? 'Désactiver' : 'Activer'}</Button>
          </div>
          {twoFactor && (me.recoveryCodesLeft ?? 0) <= 2 && (
            <p className="notice compact-notice">Il vous reste peu de codes de secours : désactivez puis réactivez la validation pour en obtenir de nouveaux.</p>
          )}
          <div className="settings-toggle">
            <span>
              <strong>Préférences</strong>
              <small className="muted">{languageLabel[me.language] ?? me.language} · {me.timeZone} · actualités {me.productNewsEmails ? 'activées' : 'désactivées'}</small>
            </span>
            <Button size="sm" variant="outline" onClick={() => setEditing('preferences')}><Settings2 size={14} />Modifier</Button>
          </div>
        </Panel>

        <Panel icon={<Laptop size={18} />} title="Appareils connectés" subtitle="Les sessions ouvertes sur votre compte."
          action={sessions.length > 1 ? (
            <ConfirmDialog title="Déconnecter tous les appareils ?" description="Toutes vos sessions seront fermées, y compris celle-ci." danger
              onConfirm={() => void act('all', async () => {
                await revokeAllSessions();
                await logout();
              }, 'Tous les appareils ont été déconnectés.')}>
              <Button size="sm" variant="outline"><LogOut size={14} />Tout déconnecter</Button>
            </ConfirmDialog>
          ) : undefined}>
          <ul className="profile-sessions">
            {ordered.map(session => (
              <li key={session.id}>
                <span className="profile-device" aria-hidden="true">{deviceIcon(session.deviceLabel)}</span>
                <span className="profile-session-text">
                  <strong>{session.deviceLabel ?? 'Appareil inconnu'}{session.isCurrent && <span className="status green">Cet appareil</span>}</strong>
                  <small className="muted">Ouverte {ago(session.createdAtUtc)} · expire le {dateTime(session.expiresAtUtc)}</small>
                </span>
                {!session.isCurrent && (
                  <Button size="sm" variant="ghost" className="danger-text" disabled={busy === session.id} onClick={() => void act(session.id, async () => {
                    await revokeSession(session.id);
                    await reload();
                  }, 'Appareil déconnecté.')}>Déconnecter</Button>
                )}
              </li>
            ))}
          </ul>
        </Panel>

        <Panel icon={<Activity size={18} />} title="Activité récente" subtitle="Vos dernières actions enregistrées au journal d’audit.">
          {activity.length === 0
            ? <p className="muted">Aucune activité enregistrée pour l’instant.</p>
            : (
              <ol className="profile-activity">
                {activity.map(entry => (
                  <li key={entry.id}><strong>{activityLabel(entry.action)}</strong><small className="muted">{dateTime(entry.occurredAtUtc)}</small></li>
                ))}
              </ol>
            )}
        </Panel>
      </div>

      <ProfileDialog me={me} open={editing === 'profile'} onClose={() => setEditing(null)} onDone={changed} />
      <PreferencesDialog me={me} open={editing === 'preferences'} onClose={() => setEditing(null)} onDone={changed} />
      <PasswordDialog open={editing === 'password'} onClose={() => setEditing(null)} onDone={reload} />
      <EmailDialog me={me} open={editing === 'email'} onClose={() => setEditing(null)} onDone={changed} />
      <EnableTwoFactorDialog open={editing === 'enable-2fa'} email={me.email} onClose={() => setEditing(null)} onDone={reload} />
      <DisableTwoFactorDialog open={editing === 'disable-2fa'} onClose={() => setEditing(null)} onDone={reload} />
    </>
  );
}
