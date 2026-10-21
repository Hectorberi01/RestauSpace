'use client';
import {api, put} from '@/lib/api';
import {useResource} from '@/lib/hooks';
import {channelLabel, type MessageChannel} from '@/lib/messages';
import {Alert, Spinner, cx, useAction} from '@/components/ui';

interface Choice {
  purpose: 'Transactional' | 'Marketing';
  channel: MessageChannel;
  isAllowed: boolean;
  changedAtUtc: string | null;
  source: string | null;
}

const channels: MessageChannel[] = ['Email', 'Sms', 'WhatsApp'];
const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));

/**
 * Les messages de service (confirmations, rappels, commande prête) que le client accepte, canal par canal. Le marketing
 * relève des consentements, juste au-dessus : il n'est pas réglé ici.
 */
export function ServiceMessages({customerId, editable}: {customerId: string; editable: boolean}) {
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(
    signal => api<{owner: string; choices: Choice[]}>(`/notifications/preferences/customers/${customerId}`, {signal}),
    [customerId],
  );
  if (error) return <Alert>{error}</Alert>;
  if (!data) return <Spinner />;

  return (
    <ul className="divide-y divide-slate-100">
      {channels.map(channel => {
        const choice = data.choices.find(item => item.purpose === 'Transactional' && item.channel === channel);
        const allowed = choice?.isAllowed ?? true;
        return (
          <li key={channel} className="flex items-center gap-3 py-3">
            <div className="min-w-0 flex-1 text-sm">
              <p className="font-medium">{channelLabel[channel]}</p>
              <p className="text-xs text-ink-500">
                {allowed ? 'Reçoit les confirmations et rappels' : 'Ne reçoit plus les messages de service'}
                {choice?.changedAtUtc && ` · modifié le ${day(choice.changedAtUtc)}`}
              </p>
            </div>
            <button type="button" role="switch" aria-checked={allowed} aria-label={`Messages de service par ${channelLabel[channel]} : ${allowed ? 'désactiver' : 'activer'}`}
              disabled={!editable || busy === channel}
              onClick={() => run(channel, async () => {
                await put(`/notifications/preferences/customers/${customerId}`, {purpose: 'Transactional', channel, isAllowed: !allowed, source: 'console'});
                await reload();
              }, allowed ? 'Le client ne recevra plus ces messages sur ce canal.' : 'Messages de service réactivés.')}
              className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', allowed ? 'bg-brand-600' : 'bg-slate-300')}>
              <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', allowed ? 'left-[1.375rem]' : 'left-0.5')} />
            </button>
          </li>
        );
      })}
    </ul>
  );
}
