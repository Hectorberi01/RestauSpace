'use client';
import {useEffect, useRef, useState, type ReactNode} from 'react';
import {
  Activity as ActivityIcon, Building2, Camera, CheckCircle2, ChevronRight, Clock, Globe, KeyRound, Laptop, LogIn, Mail, MapPin, Monitor, Newspaper,
  Pencil, Phone, Settings2, ShieldCheck, Smartphone, Trash2, User, Users, Crown, CalendarDays, Languages,
} from 'lucide-react';
import {api, type Page} from '@/lib/api';
import {dateTime, money} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  activityLabel, activityModule, fullName, initialsOf, languageLabel, loadActivity, loadAvatar, loadMe, loadSessions, removeAvatar, revokeSession,
  uploadAvatar, utcOffset, type Me, type MySession,
} from '@/lib/profile';
import {useConsole} from '@/components/console';
import {accentOf, Alert, Badge, Button, Card, Spinner, StatCard, cx, useAction} from '@/components/ui';
import {EmailDialog, PasswordDialog, PreferencesDialog, ProfileDialog} from './dialogs';
import {EraseAccount, SignOutEverywhere} from './danger';
import {PinCard} from './pin';
import {TwoFactorCard} from './two-factor';

interface Subscription {
  subscription: {status: string} | null;
  plan: {name: string; price: number; currencyCode: string; billingInterval: 'Monthly' | 'Yearly'} | null;
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

/** +22997001122 -> +229 97 00 11 22 ; +33612345678 -> +33 6 12 34 56 78. */
function formatPhone(phone: string) {
  const match = /^\+(33|32|41|1|\d{3})(\d+)$/.exec(phone);
  if (!match) return phone;
  const [, country, rest] = match;
  const head = rest.length % 2 === 1 ? `${rest[0]} ` : '';
  const pairs = (rest.length % 2 === 1 ? rest.slice(1) : rest).match(/\d{2}/g)?.join(' ') ?? rest;
  return `+${country} ${head}${pairs}`;
}

function longDate(iso: string) {
  return new Date(iso).toLocaleDateString('fr-FR', {day: 'numeric', month: 'long', year: 'numeric'});
}

function Avatar({me, url, size}: {me: Me; url: string | null; size: 'lg' | 'md'}) {
  const dimension = size === 'lg' ? 'size-28' : 'size-20';
  return url ? (
    // eslint-disable-next-line @next/next/no-img-element
    <img src={url} alt="" className={cx(dimension, 'rounded-full object-cover ring-4 ring-white shadow')} />
  ) : (
    <span className={cx(dimension, 'grid place-items-center rounded-full bg-gradient-to-br from-brand-500 to-emerald-700 font-bold text-white ring-4 ring-white shadow', size === 'lg' ? 'text-3xl' : 'text-2xl')} aria-hidden="true">
      {initialsOf(me)}
    </span>
  );
}

function SectionCard({icon, title, action, children, className}: {icon: ReactNode; title: string; action?: ReactNode; children: ReactNode; className?: string}) {
  return (
    <Card className={cx('p-5', className)}>
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="flex items-center gap-2.5 font-semibold"><span className="text-ink-500" aria-hidden="true">{icon}</span>{title}</h2>
        {action}
      </div>
      {children}
    </Card>
  );
}

function Stat({icon, tone, label, value, hint, children}: {icon: ReactNode; tone?: string; label: string; value: ReactNode; hint?: string; children?: ReactNode}) {
  return <StatCard icon={icon} label={label} value={value} hint={hint} chart={children} accent={accentOf(tone)} />;
}

function InfoRow({icon, label, children}: {icon: ReactNode; label: string; children: ReactNode}) {
  return (
    <div className="grid grid-cols-[1.25rem_6rem_minmax(0,1fr)] items-center gap-3 sm:grid-cols-[1.25rem_7.5rem_minmax(0,1fr)] py-2.5 text-sm">
      <span className="text-ink-500" aria-hidden="true">{icon}</span>
      <dt className="text-ink-500">{label}</dt>
      <dd className="min-w-0 truncate">{children}</dd>
    </div>
  );
}

function deviceIcon(label: string | null) {
  if (label && /iPhone|Android|mobile/i.test(label)) return <Smartphone className="size-4" />;
  if (label && /iPad/i.test(label)) return <Monitor className="size-4" />;
  return <Laptop className="size-4" />;
}

function Sessions({sessions, onChanged}: {sessions: MySession[]; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const list = [...sessions].sort((a, b) => Number(b.isCurrent) - Number(a.isCurrent) || b.createdAtUtc.localeCompare(a.createdAtUtc));
  return (
    <ul className="divide-y divide-slate-100">
      {list.map(session => (
        <li key={session.id} className="flex items-center gap-3 py-2.5">
          <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true">{deviceIcon(session.deviceLabel)}</span>
          <div className="min-w-0 flex-1 text-sm">
            <p className="truncate font-medium">
              {session.deviceLabel ?? 'Appareil inconnu'}
              {session.isCurrent && <Badge tone="green" className="ml-2">Cet appareil</Badge>}
            </p>
            <p className="text-xs text-ink-500">Active {ago(session.createdAtUtc)}</p>
          </div>
          {!session.isCurrent && (
            <Button size="sm" variant="ghost" className="text-red-600" busy={busy === session.id} onClick={() => run(session.id, async () => {
              await revokeSession(session.id);
              await onChanged();
            }, 'Appareil déconnecté.')}>Déconnecter</Button>
          )}
        </li>
      ))}
    </ul>
  );
}

const moduleIcon: Record<string, ReactNode> = {
  identity: <LogIn className="size-4" />,
  catalog: <Pencil className="size-4" />,
  staff: <Users className="size-4" />,
};

const moduleTone: Record<string, string> = {
  identity: 'bg-sky-50 text-sky-600',
  catalog: 'bg-violet-50 text-violet-600',
  staff: 'bg-amber-50 text-amber-600',
  orders: 'bg-emerald-50 text-emerald-600',
};

export default function ProfilePage() {
  const {tenant, isOwner, can} = useConsole();
  const {busy, run} = useAction();
  const fileInput = useRef<HTMLInputElement>(null);
  const [editing, setEditing] = useState<'profile' | 'preferences' | 'password' | 'email' | null>(null);
  const [avatar, setAvatar] = useState<string | null>(null);

  const data = useResource(async signal => {
    const [me, sessions, activity, memberships, members, billing] = await Promise.all([
      loadMe(signal),
      loadSessions(signal),
      loadActivity(signal),
      api<{status: string}[]>('/me/memberships', {signal}).catch(() => null),
      can('staff.manage')
        ? api<Page<{status: string}>>('/staff/members', {query: {pageSize: 100}, signal}).then(page => page.items).catch(() => null)
        : Promise.resolve(null),
      can('billing.view') ? api<Subscription>('/billing/subscription', {signal}).catch(() => null) : Promise.resolve(null),
    ]);
    return {me, sessions, activity, memberships, members, billing};
  }, []);

  const avatarVersion = data.data?.me.avatarUpdatedAtUtc ?? null;
  useEffect(() => {
    if (!avatarVersion) {
      setAvatar(null);
      return;
    }
    const controller = new AbortController();
    let url: string | null = null;
    loadAvatar(controller.signal)
      .then(result => {
        url = result;
        setAvatar(result);
      })
      .catch(() => setAvatar(null));
    return () => {
      controller.abort();
      if (url) URL.revokeObjectURL(url);
    };
  }, [avatarVersion]);

  if (data.error) return <Alert>{data.error}</Alert>;
  if (!data.data) return <Spinner label="Chargement de votre profil…" />;
  const {me, sessions, activity, memberships, members, billing} = data.data;
  const name = fullName(me);
  const role = isOwner ? 'Propriétaire' : 'Membre de l’équipe';
  const restaurants = memberships?.filter(membership => membership.status === 'Active').length ?? null;
  const devices = new Set(sessions.map(session => session.deviceLabel ?? session.id)).size;
  const lastActivity = sessions.reduce<string | null>((latest, session) => (!latest || session.createdAtUtc > latest ? session.createdAtUtc : latest), null);

  const pickPhoto = () => fileInput.current?.click();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Mon profil</h1>
        <p className="mt-1 text-sm text-ink-500">Gérez vos informations personnelles et vos préférences.</p>
      </div>

      <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" tabIndex={-1} aria-hidden="true"
        onChange={event => {
          const file = event.target.files?.[0];
          event.target.value = '';
          if (file) void run('avatar', async () => {
            await uploadAvatar(file);
            await data.reload();
          }, 'Photo enregistrée.');
        }} />

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.35fr)_repeat(3,minmax(0,1fr))]">
        <Card className="flex flex-col gap-5 p-5 sm:flex-row sm:items-center">
          <div className="relative shrink-0 self-start">
            <Avatar me={me} url={avatar} size="lg" />
            <button type="button" onClick={pickPhoto} aria-label="Changer la photo de profil"
              className="absolute right-0 bottom-1 grid size-9 place-items-center rounded-full bg-brand-600 text-white ring-4 ring-white hover:bg-brand-700">
              <Pencil className="size-4" aria-hidden="true" />
            </button>
          </div>
          <div className="min-w-0 space-y-1.5">
            <p className="truncate text-xl font-bold">{name ?? me.email.split('@')[0]}</p>
            <p className="text-sm text-ink-500">{role}</p>
            <p className="flex items-center gap-2 text-sm"><Mail className="size-4 shrink-0 text-ink-500" aria-hidden="true" /><span className="truncate">{me.email}</span></p>
            {me.phone && <p className="flex items-center gap-2 text-sm"><Phone className="size-4 shrink-0 text-ink-500" aria-hidden="true" />{formatPhone(me.phone)}</p>}
            {me.city && <p className="flex items-center gap-2 text-sm"><MapPin className="size-4 shrink-0 text-ink-500" aria-hidden="true" />{me.city}</p>}
            {me.isEmailVerified ? (
              <Badge tone="green" className="mt-1"><CheckCircle2 className="mr-1 inline size-3.5" aria-hidden="true" />Compte vérifié</Badge>
            ) : (
              <Badge tone="amber" className="mt-1">Adresse email à vérifier</Badge>
            )}
          </div>
        </Card>
        <Stat icon={<Building2 className="size-6" />} tone="bg-emerald-50 text-emerald-600" label="Restaurants" value={restaurants ?? '—'} hint={tenant.branding.displayName || tenant.name} />
        {members ? (
          <Stat icon={<Users className="size-6" />} tone="bg-violet-50 text-violet-600" label="Collaborateurs" value={members.filter(member => member.status === 'Active').length} hint="En équipe" />
        ) : (
          <Stat icon={<ShieldCheck className="size-6" />} tone="bg-violet-50 text-violet-600" label="Votre rôle" value={isOwner ? 'Propriétaire' : 'Équipe'} hint={tenant.branding.displayName || tenant.name} />
        )}
        <Stat icon={<Crown className="size-6" />} tone="bg-sky-50 text-sky-600" label="Abonnement" value={billing?.plan?.name ?? (billing ? 'Aucun' : '—')}
          hint={billing?.plan && billing.plan.price > 0 ? `${money(billing.plan.price, billing.plan.currencyCode)} / ${billing.plan.billingInterval === 'Yearly' ? 'an' : 'mois'}` : billing?.subscription?.status === 'Trialing' ? 'Essai en cours' : undefined} />
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <SectionCard icon={<User className="size-5" />} title="Informations personnelles" action={<Button size="sm" variant="secondary" onClick={() => setEditing('profile')}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>}>
          <dl className="divide-y divide-slate-100">
            <InfoRow icon={<User className="size-4" />} label="Nom complet">{name ?? <span className="text-ink-500">Non renseigné</span>}</InfoRow>
            <InfoRow icon={<Mail className="size-4" />} label="Email">
              <span className="flex items-center gap-2">
                <span className="truncate">{me.email}</span>
                <button type="button" className="shrink-0 text-xs font-semibold text-brand-700 hover:underline" onClick={() => setEditing('email')}>Changer</button>
              </span>
            </InfoRow>
            <InfoRow icon={<Phone className="size-4" />} label="Téléphone">{(me.phone && formatPhone(me.phone)) ?? <span className="text-ink-500">Non renseigné</span>}</InfoRow>
            <InfoRow icon={<MapPin className="size-4" />} label="Ville">{me.city ?? <span className="text-ink-500">Non renseignée</span>}</InfoRow>
            <InfoRow icon={<Languages className="size-4" />} label="Langue">{languageLabel[me.language] ?? me.language}</InfoRow>
            <InfoRow icon={<Clock className="size-4" />} label="Fuseau horaire">{me.timeZone} {utcOffset(me.timeZone) && <span className="text-ink-500">({utcOffset(me.timeZone)})</span>}</InfoRow>
          </dl>
        </SectionCard>

        <div className="space-y-4">
          <SectionCard icon={<Camera className="size-5" />} title="Photo de profil">
            <div className="flex flex-wrap items-center gap-4">
              <Avatar me={me} url={avatar} size="md" />
              <div className="flex flex-col gap-2">
                <Button variant="secondary" size="sm" busy={busy === 'avatar'} onClick={pickPhoto}><Camera className="size-4" aria-hidden="true" />{me.avatarUpdatedAtUtc ? 'Changer la photo' : 'Ajouter une photo'}</Button>
                {me.avatarUpdatedAtUtc && (
                  <Button variant="ghost" size="sm" className="text-red-600" busy={busy === 'remove-avatar'} onClick={() => run('remove-avatar', async () => {
                    await removeAvatar();
                    await data.reload();
                  }, 'Photo supprimée.')}><Trash2 className="size-4" aria-hidden="true" />Supprimer</Button>
                )}
              </div>
            </div>
            <p className="mt-3 text-xs text-ink-500">JPEG, PNG ou WebP. La photo est recadrée en carré.</p>
          </SectionCard>

          <SectionCard icon={<KeyRound className="size-5" />} title="Mot de passe">
            <p className="text-sm text-ink-500">
              {me.passwordChangedAtUtc ? `Dernière modification : ${longDate(me.passwordChangedAtUtc)}` : `Défini à la création du compte, le ${longDate(me.createdAtUtc)}`}
            </p>
            <Button variant="secondary" size="sm" className="mt-3" onClick={() => setEditing('password')}><KeyRound className="size-4" aria-hidden="true" />Changer le mot de passe</Button>
          </SectionCard>

          <TwoFactorCard me={me} onChanged={data.reload} />

          <PinCard />
        </div>

        <div className="space-y-4">
          <SectionCard icon={<Settings2 className="size-5" />} title="Préférences" action={<Button size="sm" variant="secondary" onClick={() => setEditing('preferences')}>Modifier</Button>}>
            <ul className="divide-y divide-slate-100 text-sm">
              <li className="flex items-center justify-between gap-3 py-2.5"><span className="flex items-center gap-3"><Globe className="size-4 text-ink-500" aria-hidden="true" />Langue de l’interface</span><span className="text-ink-600">{languageLabel[me.language] ?? me.language}</span></li>
              <li className="flex items-center justify-between gap-3 py-2.5"><span className="flex items-center gap-3"><Clock className="size-4 text-ink-500" aria-hidden="true" />Fuseau horaire</span><span className="truncate text-ink-600">{me.timeZone}</span></li>
              <li className="flex items-center justify-between gap-3 py-2.5"><span className="flex items-center gap-3"><Newspaper className="size-4 text-ink-500" aria-hidden="true" />Emails d’actualités</span><span className="text-ink-600">{me.productNewsEmails ? 'Activés' : 'Désactivés'}</span></li>
            </ul>
          </SectionCard>

          <SectionCard icon={<ShieldCheck className="size-5" />} title="Appareils connectés" action={<SignOutEverywhere />}>
            <Sessions sessions={sessions} onChanged={data.reload} />
          </SectionCard>
        </div>
      </div>

      <div className="grid grid-cols-1 items-start gap-4 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <SectionCard icon={<ActivityIcon className="size-5" />} title="Votre compte en chiffres">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            {[
              {label: 'Membre depuis', value: new Date(me.createdAtUtc).toLocaleDateString('fr-FR', {month: 'short', year: 'numeric'}), hint: longDate(me.createdAtUtc)},
              {label: 'Dernière activité', value: lastActivity ? ago(lastActivity) : '—', hint: lastActivity ? dateTime(lastActivity) : undefined},
              {label: 'Appareils', value: String(devices), hint: [...new Set(sessions.map(session => session.deviceLabel).filter(Boolean))].slice(0, 2).join(', ') || undefined},
              {label: 'Sessions actives', value: String(sessions.length), hint: 'En ligne'},
            ].map(item => (
              <div key={item.label} className="rounded-xl bg-slate-50 p-4">
                <p className="text-xs text-ink-500">{item.label}</p>
                <p className="mt-1 text-xl font-bold">{item.value}</p>
                {item.hint && <p className="mt-0.5 truncate text-xs text-ink-500">{item.hint}</p>}
              </div>
            ))}
          </div>
        </SectionCard>

        <SectionCard icon={<CalendarDays className="size-5" />} title="Activité récente">
          {activity.length === 0 ? (
            <p className="text-sm text-ink-500">Aucune activité enregistrée pour l’instant.</p>
          ) : (
            <ol className="space-y-3">
              {activity.map(entry => {
                const module = activityModule(entry.action);
                return (
                  <li key={entry.id} className="flex items-start gap-3">
                    <span className={cx('grid size-8 shrink-0 place-items-center rounded-lg', moduleTone[module] ?? 'bg-slate-100 text-ink-600')} aria-hidden="true">
                      {moduleIcon[module] ?? <ChevronRight className="size-4" />}
                    </span>
                    <div className="min-w-0 text-sm">
                      <p className="truncate font-medium">{activityLabel(entry.action)}</p>
                      <p className="text-xs text-ink-500">{dateTime(entry.occurredAtUtc)}</p>
                    </div>
                  </li>
                );
              })}
            </ol>
          )}
        </SectionCard>
      </div>

      <EraseAccount />

      <ProfileDialog me={me} open={editing === 'profile'} onClose={() => setEditing(null)} onDone={data.reload} />
      <PreferencesDialog me={me} open={editing === 'preferences'} onClose={() => setEditing(null)} onDone={data.reload} />
      <PasswordDialog open={editing === 'password'} onClose={() => setEditing(null)} onDone={data.reload} />
      <EmailDialog me={me} open={editing === 'email'} onClose={() => setEditing(null)} onDone={data.reload} />
    </div>
  );
}
