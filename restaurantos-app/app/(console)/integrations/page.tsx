'use client';
import {useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {KeyRound, Webhook} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {loadCatalog} from '@/lib/integrations';
import {developerDocsUrl} from '@/lib/storefront';
import {useConsole} from '@/components/console';
import {Alert, Spinner, cx} from '@/components/ui';
import {ApiKeys} from './keys';
import {Webhooks} from './webhooks';

type Tab = 'cles' | 'webhooks';

const tabs: {value: Tab; label: string; icon: typeof KeyRound}[] = [
  {value: 'cles', label: 'Clés d’API', icon: KeyRound},
  {value: 'webhooks', label: 'Webhooks', icon: Webhook},
];

export default function IntegrationsPage() {
  const {can} = useConsole();
  const allowed = can('integrations.view');
  const router = useRouter();
  const params = useSearchParams();
  const requested = params.get('onglet') as Tab | null;
  const [tab, setTab] = useState<Tab>(tabs.some(item => item.value === requested) ? requested! : 'cles');
  const catalog = useResource(signal => (allowed ? loadCatalog(signal) : Promise.resolve(null)), [allowed]);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Intégrations</h1>
        <p className="mt-1 text-sm text-ink-500">
          Connectez vos logiciels (caisse, site, comptabilité) à RestauSpace.{' '}
          <a href={developerDocsUrl} target="_blank" rel="noopener" className="font-medium text-brand-700 hover:underline">Documentation développeurs</a>
        </p>
      </div>
      {!allowed ? <Alert tone="blue">Vous n’avez pas accès aux intégrations (permission « Voir les intégrations »).</Alert> : catalog.error ? <Alert>{catalog.error}</Alert> : !catalog.data ? <Spinner /> : (
        <>
          <div role="tablist" aria-label="Intégrations" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
            {tabs.map(({value, label, icon: Icon}) => (
              <button key={value} type="button" role="tab" aria-selected={tab === value}
                onClick={() => {
                  setTab(value);
                  router.replace(`/integrations?onglet=${value}`, {scroll: false});
                }}
                className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                <Icon className="size-4" aria-hidden="true" />{label}
              </button>
            ))}
          </div>
          {tab === 'cles' && <ApiKeys catalog={catalog.data} />}
          {tab === 'webhooks' && <Webhooks catalog={catalog.data} />}
        </>
      )}
    </div>
  );
}
