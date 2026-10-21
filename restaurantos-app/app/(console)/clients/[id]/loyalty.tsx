'use client';
import {useState} from 'react';
import {Star} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {accountStatusLabel, accountStatusTone, findAccountOfCustomer, openAccount} from '@/lib/marketing';
import type {Customer} from '@/lib/customers';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Spinner, useAction} from '@/components/ui';
import {AccountDialog} from '../../marketing/loyalty';

export function CustomerLoyalty({customer}: {customer: Customer}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [open, setOpen] = useState(false);
  const {data: account, error, loading, reload} = useResource(signal => findAccountOfCustomer(customer.id, signal), [customer.id]);

  if (error) return <Alert>{error}</Alert>;
  if (loading && account === undefined) return <Spinner />;
  if (!account) {
    return (
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <p className="flex-1 text-ink-500">Pas encore de compte fidélité.</p>
        {can('loyalty.operate') && !customer.isAnonymized && (
          <Button size="sm" variant="secondary" busy={busy === 'open'} onClick={() => run('open', async () => {
            await openAccount(customer.id);
            await reload();
          }, 'Compte fidélité ouvert.')}><Star className="size-4" aria-hidden="true" />Inscrire au programme</Button>
        )}
      </div>
    );
  }
  return (
    <div className="flex flex-wrap items-center gap-3">
      <div className="flex-1">
        <p className="text-2xl font-bold tracking-tight">{new Intl.NumberFormat('fr-FR').format(account.balance)} <span className="text-base font-medium text-ink-500">points</span></p>
      </div>
      {account.status !== 'Active' && <Badge tone={accountStatusTone[account.status]}>{accountStatusLabel[account.status]}</Badge>}
      <Button size="sm" variant="secondary" onClick={() => setOpen(true)}>Historique et ajustements</Button>
      {open && <AccountDialog account={account} name={customer.name} onClose={() => setOpen(false)} onChanged={reload} />}
    </div>
  );
}
