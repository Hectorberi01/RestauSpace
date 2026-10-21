'use client';
import {useEffect, useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {Archive, Bell, CheckCheck, Settings2} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {alertTypeLabel, archive, channelLabel, listInbox, loadPreferences, markRead, setAlertType, setPreference, type Channel, type InboxItem} from '@/lib/inbox';
import {Alert, Badge, Button, Card, Empty, Spinner, cx, useAction} from '@/components/ui';

type Tab = 'boite' | 'preferences';
type View = 'unread' | 'all' | 'archived';

const stamp = (iso: string) => {
  const date = new Date(iso);
  const today = new Date();
  const sameDay = date.toDateString() === today.toDateString();
  return new Intl.DateTimeFormat('fr-FR', sameDay ? {hour: '2-digit', minute: '2-digit'} : {day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit'}).format(date);
};

function Inbox() {
  const {busy, run} = useAction();
  const [view, setView] = useState<View>('unread');
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<InboxItem[]>([]);
  useEffect(() => setPage(1), [view]);
  const {data, error, reload} = useResource(signal => listInbox(view === 'archived', page, signal), [view, page], 60_000);
  useEffect(() => {
    if (data) setItems(data.items);
  }, [data]);
  const shown = items.filter(item => (view === 'unread' ? item.state === 'Unread' : view === 'archived' ? item.state === 'Archived' : item.state !== 'Archived'));
  const unread = items.filter(item => item.state === 'Unread');

  const read = async (item: InboxItem) => {
    if (item.state !== 'Unread') return;
    setItems(current => current.map(entry => (entry.id === item.id ? {...entry, state: 'Read'} : entry)));
    await markRead(item.id).catch(() => undefined);
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex rounded-xl bg-slate-100 p-1" role="tablist" aria-label="Filtrer">
          {([['unread', 'Non lues'], ['all', 'Toutes'], ['archived', 'Archivées']] as [View, string][]).map(([value, label]) => (
            <button key={value} type="button" role="tab" aria-selected={view === value} onClick={() => setView(value)}
              className={cx('rounded-lg px-3 py-1.5 text-sm font-semibold', view === value ? 'bg-white shadow-sm' : 'text-ink-500 hover:text-ink-800')}>
              {label}{value === 'unread' && unread.length > 0 ? ` (${unread.length}${data?.hasMore ? '+' : ''})` : ''}
            </button>
          ))}
        </div>
        {unread.length > 0 && view !== 'archived' && (
          <Button size="sm" variant="ghost" className="ml-auto" busy={busy === 'all'} onClick={() => run('all', async () => {
            for (const item of unread) await markRead(item.id);
            await reload();
          }, 'Notifications marquées comme lues.')}><CheckCheck className="size-4" aria-hidden="true" />Tout marquer comme lu</Button>
        )}
      </div>

      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : shown.length === 0 ? (
        <Card className="p-6">
          <Empty icon={<Bell className="size-8" />} title={view === 'unread' ? 'Tout est lu' : view === 'archived' ? 'Aucune notification archivée' : 'Aucune notification'}>
            {view === 'unread' ? 'Les nouvelles alertes apparaîtront ici et sur la cloche.' : null}
          </Empty>
        </Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100">
            {shown.map(item => (
              <li key={item.id} className={cx('flex items-start gap-3 px-4 py-3', item.state === 'Unread' && 'bg-brand-50/40')}>
                <span className={cx('mt-1.5 size-2.5 shrink-0 rounded-full', item.state === 'Unread' ? 'bg-brand-600' : 'bg-transparent')} aria-hidden="true" />
                <button type="button" onClick={() => void read(item)} className="min-w-0 flex-1 text-left">
                  <span className="flex flex-wrap items-center gap-2">
                    <span className={cx('text-sm', item.state === 'Unread' ? 'font-semibold' : 'font-medium text-ink-700')}>{item.title ?? 'Notification'}</span>
                    {(item.priority === 'Critical' || item.priority === 'High') && <Badge tone={item.priority === 'Critical' ? 'red' : 'amber'}>{item.priority === 'Critical' ? 'Urgent' : 'Important'}</Badge>}
                  </span>
                  <span className="mt-0.5 block text-sm whitespace-pre-line text-ink-600">{item.body}</span>
                  <span className="mt-1 block text-xs text-ink-500">{stamp(item.createdAtUtc)}{item.state === 'Unread' ? ' · cliquer pour marquer comme lue' : ''}</span>
                </button>
                {item.state !== 'Archived' && (
                  <Button size="sm" variant="ghost" aria-label="Archiver" title="Archiver" busy={busy === item.id} onClick={() => run(item.id, async () => {
                    await archive(item.id);
                    setItems(current => current.map(entry => (entry.id === item.id ? {...entry, state: 'Archived'} : entry)));
                  })}><Archive className="size-4" /></Button>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
      {data && (page > 1 || data.hasMore) && (
        <div className="flex justify-end gap-2">
          <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récentes</Button>
          <Button size="sm" variant="secondary" disabled={!data.hasMore} onClick={() => setPage(page + 1)}>Plus anciennes</Button>
        </div>
      )}
    </div>
  );
}

const channels: Channel[] = ['InApp', 'Email', 'Push', 'Sms', 'WhatsApp'];

function Preferences() {
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => loadPreferences(signal), []);
  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;
  const choice = (channel: Channel) => data.choices.find(item => item.purpose === 'Transactional' && item.channel === channel);

  const alerts = (data.alerts ?? []).filter(alert => alertTypeLabel[alert.type]);

  return (
    <div className="space-y-6">
      <Card className="p-5">
        <h2 className="font-semibold">Comment être prévenu</h2>
        <p className="mb-4 text-sm text-ink-500">Alertes de service : nouvelles commandes et réservations, incidents de paiement ou de webhook, sécurité du compte. Vos choix ne concernent que vous.</p>
        <ul className="divide-y divide-slate-100">
          {channels.map(channel => {
            const current = choice(channel);
            if (!current) return null;
            return (
              <li key={channel} className="flex items-center gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium">{channelLabel[channel]}</p>
                  {channel === 'Push' && <p className="text-xs text-ink-500">Sur les téléphones où l’application mobile est installée.</p>}
                  {channel === 'InApp' && <p className="text-xs text-ink-500">La cloche en haut de la console.</p>}
                </div>
                <Switch on={current.isAllowed} label={channelLabel[channel]} busy={busy === channel} onToggle={() => run(channel, async () => {
                  await setPreference('Transactional', channel, !current.isAllowed);
                  await reload();
                }, 'Préférence enregistrée.')} />
              </li>
            );
          })}
        </ul>
      </Card>

      {alerts.length > 0 && (
        <Card className="p-5">
          <h2 className="font-semibold">Quelles alertes recevoir</h2>
          <p className="mb-4 text-sm text-ink-500">Une alerte coupée ne vous parvient plus, sur aucun canal. Les messages de sécurité du compte restent toujours envoyés.</p>
          <ul className="divide-y divide-slate-100">
            {alerts.map(alert => {
              const label = alertTypeLabel[alert.type];
              return (
                <li key={alert.type} className="flex items-center gap-3 py-3">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium">{label.title}</p>
                    <p className="text-xs text-ink-500">{label.hint}</p>
                  </div>
                  <Switch on={alert.isEnabled} label={label.title} busy={busy === alert.type} onToggle={() => run(alert.type, async () => {
                    await setAlertType(alert.type, !alert.isEnabled);
                    await reload();
                  }, 'Préférence enregistrée.')} />
                </li>
              );
            })}
          </ul>
        </Card>
      )}
    </div>
  );
}

function Switch({on, label, busy, onToggle}: {on: boolean; label: string; busy: boolean; onToggle: () => void}) {
  return (
    <button type="button" role="switch" aria-checked={on} aria-label={`${label} : ${on ? 'désactiver' : 'activer'}`} disabled={busy} onClick={onToggle}
      className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', on ? 'bg-brand-600' : 'bg-slate-300')}>
      <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', on ? 'left-[1.375rem]' : 'left-0.5')} />
    </button>
  );
}

export default function NotificationsPage() {
  const router = useRouter();
  const params = useSearchParams();
  const [tab, setTab] = useState<Tab>(params.get('onglet') === 'preferences' ? 'preferences' : 'boite');
  const tabs: {value: Tab; label: string; icon: typeof Bell}[] = [
    {value: 'boite', label: 'Boîte de réception', icon: Bell},
    {value: 'preferences', label: 'Préférences', icon: Settings2},
  ];
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Notifications</h1>
        <p className="mt-1 text-sm text-ink-500">Vos alertes et la façon dont vous les recevez.</p>
      </div>
      <div role="tablist" aria-label="Notifications" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map(({value, label, icon: Icon}) => (
          <button key={value} type="button" role="tab" aria-selected={tab === value}
            onClick={() => {
              setTab(value);
              router.replace(`/notifications?onglet=${value}`, {scroll: false});
            }}
            className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
            <Icon className="size-4" aria-hidden="true" />{label}
          </button>
        ))}
      </div>
      {tab === 'boite' ? <Inbox /> : <Preferences />}
    </div>
  );
}
