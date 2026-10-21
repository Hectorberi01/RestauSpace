'use client';
import {useEffect, useState} from 'react';
import {useRouter} from 'next/navigation';
import {ChevronRight, LogOut, Store} from 'lucide-react';
import {api, describe, logout, post, selectTenant} from '@/lib/api';
import type {Membership} from '@/lib/types';
import {useSession} from '@/components/console';
import {Logo} from '@/components/shell';
import {Alert, Badge, Button, Empty, Spinner} from '@/components/ui';

export default function RestaurantsPage() {
  const router = useRouter();
  const session = useSession();
  const [memberships, setMemberships] = useState<Membership[]>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState<string>();

  const userId = session?.userId;
  useEffect(() => {
    if (session === null) router.replace('/connexion');
  }, [session, router]);

  useEffect(() => {
    if (!userId) return;
    api<Membership[]>('/me/memberships')
      .then(setMemberships)
      .catch(err => setError(describe(err)));
  }, [userId]);

  const open = async (membership: Membership) => {
    setBusy(membership.tenantId);
    setError(undefined);
    try {
      if (membership.status === 'Invited') await post(`/me/memberships/${membership.tenantId}/accept`);
      await selectTenant(membership.tenantId);
      router.replace('/tableau-de-bord');
    } catch (err) {
      setError(describe(err));
      setBusy(undefined);
    }
  };

  return (
    <div className="min-h-dvh bg-gradient-to-b from-brand-50 to-slate-50 px-4 py-10">
      <div className="mx-auto max-w-lg">
        <div className="flex items-center justify-between">
          <Logo />
          <Button
            variant="ghost"
            size="sm"
            onClick={async () => {
              await logout();
              router.replace('/connexion');
            }}>
            <LogOut className="size-4" aria-hidden="true" />
            Se déconnecter
          </Button>
        </div>
        <h1 className="mt-10 text-2xl font-bold tracking-tight">Vos restaurants</h1>
        <p className="mt-1 text-sm text-ink-500">Choisissez le restaurant dans lequel vous travaillez.</p>
        <div className="mt-6 space-y-3">
          {error && <Alert>{error}</Alert>}
          {!memberships && !error && <Spinner />}
          {memberships?.length === 0 && (
            <Empty icon={<Store className="size-8" />} title="Aucun restaurant">
              Votre compte n’est rattaché à aucun restaurant. Demandez une invitation au gérant, ou contactez-nous pour ouvrir le vôtre avec ce compte.
            </Empty>
          )}
          {memberships?.map(membership => {
            const suspended = membership.status === 'Suspended';
            return (
              <button
                key={membership.tenantId}
                type="button"
                disabled={suspended || !!busy}
                onClick={() => open(membership)}
                className="flex w-full items-center gap-4 rounded-2xl border border-slate-200 bg-white p-5 text-left transition hover:border-brand-300 hover:shadow-md disabled:cursor-not-allowed disabled:opacity-60">
                <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600">
                  <Store className="size-5" aria-hidden="true" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-semibold">{membership.tenantName}</span>
                  <span className="mt-1 block">
                    {membership.status === 'Invited' && <Badge tone="blue">Invitation à accepter</Badge>}
                    {suspended && <Badge tone="red">Accès suspendu</Badge>}
                    {membership.status === 'Active' && <Badge tone="green">Membre</Badge>}
                  </span>
                </span>
                {busy === membership.tenantId ? <span className="text-sm text-ink-500">Ouverture…</span> : <ChevronRight className="size-5 text-ink-500" aria-hidden="true" />}
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}
