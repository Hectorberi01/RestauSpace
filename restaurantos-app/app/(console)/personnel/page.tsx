'use client';
import {useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import {Building2, Check, Clock3, Hourglass, KeyRound, Mail, MoreHorizontal, Plus, Search, ShieldCheck, Tablet, Trash2, UserCheck, UserX, Users} from 'lucide-react';
import {resetMemberPin} from '@/lib/till';
import {TimeSheet, Tills} from './tills';
import {api} from '@/lib/api';
import {dateTime} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {
  assign, createRole, deleteRole, grant, groupOf, invite, listMembers, listRoles, permissionCatalog, permissionLabel, reinstate, remove, renameRole, resendInvitation, revoke, roleName,
  setTenantRole, statusLabel, statusTone, suspend, unassign, type Role, type StaffMember, type StaffStatus,
} from '@/lib/staff';
import {useConsole} from '@/components/console';
import {accentOf, Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, StatCard, cx, useAction} from '@/components/ui';

type Tab = 'members' | 'roles' | 'tills' | 'time';

function initials(value: string | null) {
  const name = (value ?? '?').split('@')[0].replace(/[._-]+/g, ' ');
  return name.split(' ').filter(Boolean).slice(0, 2).map(part => part[0]).join('').toUpperCase() || '?';
}

function Kpi({icon, tone, label, value}: {icon: ReactNode; tone?: string; label: string; value: number}) {
  return <StatCard icon={icon} label={label} value={value} accent={accentOf(tone)} />;
}

function InviteDialog({open, roles, onClose, onDone}: {open: boolean; roles: Role[]; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [email, setEmail] = useState('');
  const [roleId, setRoleId] = useState('');
  useEffect(() => {
    if (open) {
      setEmail('');
      setRoleId(roles.find(role => role.isSystem && role.name === 'Manager')?.id ?? '');
    }
  }, [open, roles]);
  return (
    <Modal open={open} onClose={onClose} title="Inviter un membre" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="invite-form" busy={busy === 'invite'}>Envoyer l’invitation</Button>
      </>
    }>
      <form id="invite-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('invite', async () => {
          await invite(email.trim(), roleId || null);
          await onDone();
          onClose();
        }, 'Invitation envoyée par email.');
      }}>
        <p className="text-sm text-ink-600">La personne reçoit un email pour créer son mot de passe et rejoindre l’équipe.</p>
        <Field label="Email"><Input type="email" required autoComplete="off" value={email} onChange={event => setEmail(event.target.value)} placeholder="prenom@exemple.com" /></Field>
        <Field label="Rôle dans tout le restaurant" hint="Laissez vide pour donner un rôle seulement dans certains établissements.">
          <Select value={roleId} onChange={event => setRoleId(event.target.value)}>
            <option value="">Aucun (par établissement)</option>
            {roles.map(role => <option key={role.id} value={role.id}>{roleName(role)}</option>)}
          </Select>
        </Field>
      </form>
    </Modal>
  );
}

function MemberDialog({member, roles, onClose, onDone}: {member: StaffMember | null; roles: Role[]; onClose: () => void; onDone: () => Promise<void>}) {
  const {locations} = useConsole();
  const {busy, run} = useAction();
  const [roleId, setRoleId] = useState('');
  const [locationId, setLocationId] = useState('');
  const [locationRoleId, setLocationRoleId] = useState('');
  useEffect(() => {
    if (!member) return;
    setRoleId(member.tenantWideRoleId ?? '');
    setLocationId('');
    setLocationRoleId(roles.find(role => !role.isSystem)?.id ?? roles[0]?.id ?? '');
  }, [member, roles]);
  if (!member) return <Modal open={false} onClose={onClose} title="">{null}</Modal>;
  const roleOf = (id: string) => roles.find(role => role.id === id);
  const free = locations.filter(location => !member.assignments.some(assignment => assignment.locationId === location.id));
  const editable = member.status !== 'Left';

  return (
    <Modal open={!!member} onClose={onClose} title={member.email ?? 'Membre'}>
      <div className="space-y-6">
        <div className="flex flex-wrap items-center gap-2 text-sm text-ink-600">
          <Badge tone={statusTone[member.status]}>{statusLabel[member.status]}</Badge>
          Invité le {dateTime(member.invitedAtUtc)}
        </div>

        <section>
          <h3 className="mb-2 font-semibold">Rôle dans tout le restaurant</h3>
          <div className="flex flex-wrap items-end gap-2">
            <Select aria-label="Rôle" className="min-w-48 flex-1" value={roleId} disabled={!editable} onChange={event => setRoleId(event.target.value)}>
              <option value="">Aucun (par établissement)</option>
              {roles.map(role => <option key={role.id} value={role.id}>{roleName(role)}</option>)}
            </Select>
            <Button variant="secondary" disabled={!editable || roleId === (member.tenantWideRoleId ?? '')} busy={busy === 'role'} onClick={() => run('role', async () => {
              await setTenantRole(member.id, roleId || null);
              await onDone();
            }, 'Rôle enregistré.')}>Enregistrer</Button>
          </div>
        </section>

        <section>
          <h3 className="mb-1 font-semibold">Établissements</h3>
          <p className="mb-2 text-sm text-ink-500">Un rôle donné dans un établissement remplace le rôle général pour cet établissement.</p>
          <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {member.assignments.length === 0 && <li className="px-4 py-3 text-sm text-ink-500">Aucune affectation particulière.</li>}
            {member.assignments.map(assignment => (
              <li key={assignment.locationId} className="flex items-center justify-between gap-2 px-4 py-2.5 text-sm">
                <span className="min-w-0">
                  <span className="block truncate font-medium">{locations.find(location => location.id === assignment.locationId)?.name ?? 'Établissement archivé'}</span>
                  <span className="text-xs text-ink-500">{roleName(roleOf(assignment.roleId))}</span>
                </span>
                {editable && (
                  <Button size="sm" variant="ghost" className="text-red-600" busy={busy === `u-${assignment.locationId}`} onClick={() => run(`u-${assignment.locationId}`, async () => {
                    await unassign(member.id, assignment.locationId);
                    await onDone();
                  }, 'Affectation retirée.')}>Retirer</Button>
                )}
              </li>
            ))}
          </ul>
          {editable && free.length > 0 && (
            <div className="mt-3 grid gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]">
              <Select aria-label="Établissement" value={locationId} onChange={event => setLocationId(event.target.value)}>
                <option value="">Choisir un établissement…</option>
                {free.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
              </Select>
              <Select aria-label="Rôle dans l’établissement" value={locationRoleId} onChange={event => setLocationRoleId(event.target.value)}>
                {roles.map(role => <option key={role.id} value={role.id}>{roleName(role)}</option>)}
              </Select>
              <Button disabled={!locationId || !locationRoleId} busy={busy === 'assign'} onClick={() => run('assign', async () => {
                await assign(member.id, locationId, locationRoleId);
                setLocationId('');
                await onDone();
              }, 'Membre affecté.')}><Plus className="size-4" aria-hidden="true" />Affecter</Button>
            </div>
          )}
        </section>
      </div>
    </Modal>
  );
}

function RowMenu({member, me, onEdit, onChanged}: {member: StaffMember; me: string | undefined; onEdit: () => void; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => !ref.current?.contains(event.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const self = member.id === me;
  const item = 'block w-full rounded-lg px-3 py-2 text-left text-sm hover:bg-slate-50 disabled:opacity-50';
  return (
    <div ref={ref} className="relative" onClick={event => event.stopPropagation()}>
      <button type="button" aria-haspopup="menu" aria-expanded={open} aria-label={`Actions pour ${member.email ?? 'ce membre'}`} disabled={!!busy} onClick={() => setOpen(value => !value)} className="grid size-8 place-items-center rounded-lg text-ink-500 hover:bg-slate-100">
        <MoreHorizontal className="size-4" />
      </button>
      {open && (
        <div role="menu" className="absolute top-9 right-0 z-20 w-56 rounded-xl border border-slate-200 bg-white p-1 shadow-lg">
          <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); onEdit(); }}>Rôle et établissements</button>
          {member.status === 'Invited' && (
            <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); void run('resend', async () => { await resendInvitation(member.id); await onChanged(); }, 'Invitation renvoyée.'); }}>Renvoyer l’invitation</button>
          )}
          {member.status === 'Active' && !self && (
            <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); void run('suspend', async () => { await suspend(member.id); await onChanged(); }, 'Accès suspendu.'); }}>Suspendre l’accès</button>
          )}
          {member.status === 'Suspended' && (
            <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); void run('reinstate', async () => { await reinstate(member.id); await onChanged(); }, 'Accès rétabli.'); }}>Rétablir l’accès</button>
          )}
          {member.hasPin && (
            <button type="button" role="menuitem" className={item} onClick={() => { setOpen(false); void run('pin', async () => { await resetMemberPin(member.id); await onChanged(); }, 'PIN effacé : le membre en choisira un nouveau dans Mon profil.'); }}>Effacer le PIN de caisse</button>
          )}
          {member.status !== 'Left' && !self && (
            <button type="button" role="menuitem" className={cx(item, 'text-red-600')} onClick={() => { setOpen(false); setConfirm(true); }}>Retirer de l’équipe</button>
          )}
        </div>
      )}
      <Modal open={confirm} onClose={() => setConfirm(false)} title="Retirer de l’équipe" footer={
        <>
          <Button variant="secondary" onClick={() => setConfirm(false)}>Annuler</Button>
          <Button variant="danger" busy={busy === 'remove'} onClick={() => void run('remove', async () => {
            await remove(member.id);
            setConfirm(false);
            await onChanged();
          }, 'Membre retiré.')}>Retirer</Button>
        </>
      }>
        <p className="text-sm text-ink-600">{member.email ?? 'Ce membre'} perdra l’accès à ce restaurant. Son compte reste valable pour d’autres restaurants.</p>
      </Modal>
    </div>
  );
}

function Members({members, roles, me, onChanged}: {members: StaffMember[]; roles: Role[]; me: string | undefined; onChanged: () => Promise<void>}) {
  const {locations} = useConsole();
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState<'all' | StaffStatus>('all');
  const [editing, setEditing] = useState<string | null>(null);
  const needle = query.trim().toLowerCase();
  const list = members
    .filter(member => (status === 'all' ? member.status !== 'Left' : member.status === status))
    .filter(member => !needle || (member.email ?? '').toLowerCase().includes(needle))
    .sort((a, b) => (a.email ?? '').localeCompare(b.email ?? ''));
  const roleOf = (id: string | null) => roles.find(role => role.id === id);
  const current = members.find(member => member.id === editing) ?? null;

  return (
    <>
      <div className="mb-4 flex flex-col gap-3 sm:flex-row">
        <label className="relative block flex-1">
          <span className="sr-only">Rechercher un membre</span>
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-500" aria-hidden="true" />
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Rechercher par email…" className="h-10 w-full rounded-xl border border-slate-200 bg-white pr-3 pl-9 text-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none" />
        </label>
        <Select aria-label="Statut" className="sm:w-56" value={status} onChange={event => setStatus(event.target.value as typeof status)}>
          <option value="all">Tous (hors partis)</option>
          <option value="Active">Actifs</option>
          <option value="Invited">Invitations en attente</option>
          <option value="Suspended">Suspendus</option>
          <option value="Left">Partis</option>
        </Select>
      </div>
      {list.length === 0 ? (
        <Empty icon={<Users className="size-8" />} title="Aucun membre pour ces filtres" />
      ) : (
        <div className="relative overflow-x-auto">
          <table className="w-full min-w-[720px] text-left text-sm">
            <thead className="border-b border-slate-200 text-xs text-ink-500">
              <tr>
                <th scope="col" className="px-3 py-2 font-medium">Membre</th>
                <th scope="col" className="px-3 py-2 font-medium">Rôle</th>
                <th scope="col" className="px-3 py-2 font-medium">Établissements</th>
                <th scope="col" className="px-3 py-2 font-medium">Statut</th>
                <th scope="col" className="px-3 py-2"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {list.map(member => (
                <tr key={member.id} className="cursor-pointer hover:bg-slate-50" onClick={() => setEditing(member.id)}>
                  <td className="px-3 py-3">
                    <span className="flex items-center gap-3">
                      <span className="grid size-10 shrink-0 place-items-center rounded-full bg-brand-50 text-sm font-semibold text-brand-700">{initials(member.email)}</span>
                      <span className="min-w-0">
                        <span className="block truncate font-semibold">{member.email ?? 'Compte supprimé'}{member.id === me && <span className="ml-2 text-xs font-normal text-ink-500">(vous)</span>}</span>
                        <span className="text-xs text-ink-500">Depuis le {dateTime(member.invitedAtUtc)}</span>
                      </span>
                    </span>
                  </td>
                  <td className="px-3 py-3">{member.tenantWideRoleId ? roleName(roleOf(member.tenantWideRoleId)) : <span className="text-ink-500">Par établissement</span>}</td>
                  <td className="px-3 py-3">
                    {member.assignments.length === 0 ? (
                      <span className="text-ink-500">{member.tenantWideRoleId ? 'Tous' : '—'}</span>
                    ) : (
                      <span className="flex flex-wrap gap-1">
                        {member.assignments.map(assignment => (
                          <Badge key={assignment.locationId} tone="blue">{locations.find(location => location.id === assignment.locationId)?.name ?? 'Archivé'} · {roleName(roleOf(assignment.roleId))}</Badge>
                        ))}
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-3"><Badge tone={statusTone[member.status]}>{statusLabel[member.status]}</Badge></td>
                  <td className="px-3 py-3"><RowMenu member={member} me={me} onEdit={() => setEditing(member.id)} onChanged={onChanged} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <MemberDialog member={current} roles={roles} onClose={() => setEditing(null)} onDone={onChanged} />
    </>
  );
}

function Roles({roles, members, onChanged}: {roles: Role[]; members: StaffMember[]; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const manage = can('roles.manage');
  const catalog = useResource(signal => (manage ? permissionCatalog(signal) : Promise.resolve(null)), [manage]);
  const {busy, run} = useAction();
  const [selected, setSelected] = useState<string | null>(null);
  const [newName, setNewName] = useState('');
  const [rename, setRename] = useState('');
  const [deleting, setDeleting] = useState(false);
  const role = roles.find(item => item.id === selected) ?? roles[0];
  useEffect(() => setRename(role ? role.name : ''), [role?.id, role?.name]);

  const codes = useMemo(() => [...new Set([...(catalog.data ?? []), ...(role?.permissions ?? [])])].filter(code => permissionLabel[code] || role?.permissions.includes(code)), [catalog.data, role]);
  const groups = useMemo(() => {
    const map = new Map<string, string[]>();
    codes.forEach(code => map.set(groupOf(code), [...(map.get(groupOf(code)) ?? []), code]));
    return [...map.entries()];
  }, [codes]);
  const usage = (id: string) => members.filter(member => member.status !== 'Left' && (member.tenantWideRoleId === id || member.assignments.some(assignment => assignment.roleId === id))).length;

  if (!role) return <Spinner />;
  const editable = manage && !role.isSystem;

  return (
    <div className="grid gap-6 lg:grid-cols-[18rem_minmax(0,1fr)]">
      <div className="space-y-3">
        <ul className="space-y-1">
          {roles.map(item => (
            <li key={item.id}>
              <button type="button" onClick={() => setSelected(item.id)} className={cx('flex w-full items-center justify-between gap-2 rounded-xl px-3 py-2.5 text-left text-sm', item.id === role.id ? 'bg-brand-50 text-brand-800' : 'hover:bg-slate-50')}>
                <span className="min-w-0">
                  <span className="block truncate font-semibold">{roleName(item)}</span>
                  <span className="text-xs text-ink-500">{item.permissions.length} permission{item.permissions.length > 1 ? 's' : ''} · {usage(item.id)} membre{usage(item.id) > 1 ? 's' : ''}</span>
                </span>
                {item.isSystem && <ShieldCheck className="size-4 shrink-0 text-ink-500" aria-label="Rôle système" />}
              </button>
            </li>
          ))}
        </ul>
        {manage && (
          <form className="flex gap-2" onSubmit={event => {
            event.preventDefault();
            void run('create', async () => {
              const created = await createRole(newName.trim());
              setNewName('');
              await onChanged();
              setSelected(created.id);
            }, 'Rôle créé.');
          }}>
            <Input required maxLength={60} value={newName} onChange={event => setNewName(event.target.value)} placeholder="Serveur, Caissier…" aria-label="Nom du nouveau rôle" />
            <Button type="submit" busy={busy === 'create'}><Plus className="size-4" aria-hidden="true" /><span className="sr-only sm:not-sr-only">Créer</span></Button>
          </form>
        )}
      </div>

      <div className="min-w-0">
        <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
          {editable ? (
            <form className="flex flex-1 items-end gap-2" onSubmit={event => {
              event.preventDefault();
              void run('rename', async () => { await renameRole(role.id, rename.trim()); await onChanged(); }, 'Rôle renommé.');
            }}>
              <Field label="Nom du rôle" className="max-w-sm flex-1"><Input required maxLength={60} value={rename} onChange={event => setRename(event.target.value)} /></Field>
              <Button type="submit" variant="secondary" disabled={rename.trim() === role.name} busy={busy === 'rename'}>Renommer</Button>
              <Button type="button" variant="secondary" onClick={() => setDeleting(true)} title={usage(role.id) > 0 ? 'Retirez d’abord ce rôle aux membres qui l’ont.' : undefined}
                disabled={usage(role.id) > 0} className="text-red-600"><Trash2 className="size-4" aria-hidden="true" />Supprimer</Button>
            </form>
          ) : (
            <div>
              <h2 className="text-lg font-bold">{roleName(role)}</h2>
              {role.isSystem && <p className="text-sm text-ink-500">Rôle système : ses permissions sont fixées par la plateforme.</p>}
            </div>
          )}
        </div>
        {editable && usage(role.id) > 0 && (
          <p className="mb-3 text-sm text-ink-500">Attribué à {usage(role.id)} membre{usage(role.id) > 1 ? 's' : ''} : retirez-le-leur pour pouvoir le supprimer.</p>
        )}
        <Modal open={deleting} onClose={() => setDeleting(false)} title="Supprimer le rôle" footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(false)}>Annuler</Button>
            <Button variant="danger" busy={busy === 'delete'} onClick={() => void run('delete', async () => {
              await deleteRole(role.id);
              setDeleting(false);
              setSelected(null);
              await onChanged();
            }, 'Rôle supprimé.')}>Supprimer</Button>
          </>
        }>
          <p className="text-sm text-ink-600">Le rôle « {role.name} » et ses permissions seront supprimés. Aucun membre ne l’a.</p>
        </Modal>
        {!manage && <Alert tone="blue">Réservé aux membres qui gèrent les rôles : la liste montre les permissions accordées.</Alert>}
        <div className="mt-2 grid gap-4 xl:grid-cols-2">
          {(manage ? groups : role.permissions.reduce<[string, string[]][]>((acc, code) => {
            const name = groupOf(code);
            const entry = acc.find(([key]) => key === name);
            if (entry) entry[1].push(code);
            else acc.push([name, [code]]);
            return acc;
          }, [])).map(([group, list]) => (
            <section key={group} className="rounded-xl border border-slate-200 p-4">
              <h3 className="mb-2 text-sm font-semibold">{group}</h3>
              <ul className="space-y-1.5">
                {list.map(code => {
                  const granted = role.permissions.includes(code);
                  return (
                    <li key={code}>
                      <label className={cx('flex items-center gap-3 rounded-lg px-1 py-1 text-sm', editable && 'cursor-pointer hover:bg-slate-50')}>
                        <input
                          type="checkbox"
                          className="size-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
                          checked={granted}
                          disabled={!editable || !!busy}
                          onChange={() => void run(code, async () => {
                            if (granted) await revoke(role.id, code);
                            else await grant(role.id, code);
                            await onChanged();
                          })}
                        />
                        <span className="min-w-0 flex-1">{permissionLabel[code] ?? code}</span>
                        {!editable && granted && <Check className="size-4 text-emerald-600" aria-hidden="true" />}
                      </label>
                    </li>
                  );
                })}
              </ul>
            </section>
          ))}
        </div>
      </div>
    </div>
  );
}

export default function StaffPage() {
  const {can} = useConsole();
  const allowed = can('staff.manage');
  const [tab, setTab] = useState<Tab>('members');
  const [inviting, setInviting] = useState(false);
  const data = useResource(
    async signal => {
      if (!allowed) return null;
      const [members, roles, me] = await Promise.all([listMembers(), listRoles(signal), api<{staffMemberId: string}>('/staff/me/permissions', {signal})]);
      return {members, roles, me: me.staffMemberId};
    },
    [allowed],
  );

  if (!allowed) {
    return <Empty icon={<Users className="size-8" />} title="Gestion du personnel non disponible">Réservé aux membres qui gèrent le personnel.</Empty>;
  }

  const members = data.data?.members ?? [];
  const roles = data.data?.roles ?? [];
  const count = (status: StaffStatus) => members.filter(member => member.status === status).length;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Personnel</h1>
          <p className="mt-1 text-sm text-ink-500">Invitez votre équipe, attribuez des rôles et choisissez qui peut faire quoi.</p>
        </div>
        <Button onClick={() => setInviting(true)} disabled={!data.data}><Mail className="size-4" aria-hidden="true" />Inviter un membre</Button>
      </div>

      {data.error && <Alert>{data.error}</Alert>}
      {!data.data && !data.error && <Spinner label="Chargement de l’équipe…" />}
      {data.data && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
            <Kpi icon={<UserCheck className="size-6" />} tone="bg-emerald-50 text-emerald-600" label="Membres actifs" value={count('Active')} />
            <Kpi icon={<Hourglass className="size-6" />} tone="bg-amber-50 text-amber-600" label="Invitations en attente" value={count('Invited')} />
            <Kpi icon={<UserX className="size-6" />} tone="bg-rose-50 text-rose-500" label="Accès suspendus" value={count('Suspended')} />
            <Kpi icon={<KeyRound className="size-6" />} tone="bg-violet-50 text-violet-600" label="Rôles" value={roles.length} />
          </div>

          <Card className="p-4 sm:p-5">
            <div role="tablist" aria-label="Personnel" className="-mx-1 mb-4 flex gap-1 overflow-x-auto border-b border-slate-200">
              {([['members', 'Membres', Users], ['roles', 'Rôles & permissions', ShieldCheck], ['tills', 'Caisses partagées', Tablet], ['time', 'Pointages', Clock3]] as const).map(([value, label, Icon]) => (
                <button key={value} type="button" role="tab" aria-selected={tab === value} onClick={() => setTab(value)}
                  className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                  <Icon className="size-4" aria-hidden="true" />{label}
                </button>
              ))}
            </div>
            {tab === 'members' ? (
              <Members members={members} roles={roles} me={data.data.me} onChanged={data.reload} />
            ) : tab === 'roles' ? (
              <Roles roles={roles} members={members} onChanged={data.reload} />
            ) : tab === 'tills' ? (
              <Tills />
            ) : (
              <TimeSheet />
            )}
          </Card>
          <p className="flex items-center gap-2 text-xs text-ink-500"><Building2 className="size-3.5" aria-hidden="true" />Un rôle « dans tout le restaurant » s’applique à chaque établissement, sauf affectation particulière.</p>
        </>
      )}
      <InviteDialog open={inviting} roles={roles} onClose={() => setInviting(false)} onDone={data.reload} />
    </div>
  );
}
