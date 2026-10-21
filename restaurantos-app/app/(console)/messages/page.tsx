'use client';
import {useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {FileText, History as HistoryIcon} from 'lucide-react';
import {useConsole} from '@/components/console';
import {cx} from '@/components/ui';
import {History} from './history';
import {Templates} from './templates';

type Tab = 'modeles' | 'historique';

export default function MessagesPage() {
  const {can} = useConsole();
  const router = useRouter();
  const params = useSearchParams();
  const history = can('notifications.view');
  const customerId = params.get('client') ?? undefined;
  const [tab, setTab] = useState<Tab>(history && (params.get('onglet') === 'historique' || customerId) ? 'historique' : 'modeles');
  const tabs: {value: Tab; label: string; icon: typeof FileText}[] = [
    {value: 'modeles', label: 'Modèles', icon: FileText},
    ...(history ? [{value: 'historique' as Tab, label: 'Historique des envois', icon: HistoryIcon}] : []),
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Messages clients</h1>
        <p className="mt-1 text-sm text-ink-500">Les e-mails, SMS et WhatsApp envoyés à vos clients : leur texte et leur suivi.</p>
      </div>
      <div role="tablist" aria-label="Messages clients" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map(({value, label, icon: Icon}) => (
          <button key={value} type="button" role="tab" aria-selected={tab === value}
            onClick={() => {
              setTab(value);
              router.replace(`/messages?onglet=${value}`, {scroll: false});
            }}
            className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
            <Icon className="size-4" aria-hidden="true" />{label}
          </button>
        ))}
      </div>
      {tab === 'historique' && history ? <History customerId={customerId} /> : <Templates />}
    </div>
  );
}
