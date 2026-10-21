'use client';
import {useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {ClipboardList, MessageSquareText} from 'lucide-react';
import {useConsole} from '@/components/console';
import {Alert, cx} from '@/components/ui';
import {Feedbacks} from './feedbacks';
import {Surveys} from './surveys';

type Tab = 'avis' | 'questionnaires';

const tabs: {value: Tab; label: string; icon: typeof ClipboardList}[] = [
  {value: 'avis', label: 'Avis clients', icon: MessageSquareText},
  {value: 'questionnaires', label: 'Questionnaires', icon: ClipboardList},
];

export default function ReviewsPage() {
  const {can} = useConsole();
  const router = useRouter();
  const params = useSearchParams();
  const requested = params.get('onglet') as Tab | null;
  const [tab, setTab] = useState<Tab>(tabs.some(item => item.value === requested) ? requested! : 'avis');

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Avis</h1>
        <p className="mt-1 text-sm text-ink-500">Ce que vos clients disent de vous, et les questionnaires de satisfaction.</p>
      </div>
      {!can('reviews.view') ? <Alert tone="blue">Vous n’avez pas accès aux avis (permission « Voir les avis »).</Alert> : (
        <>
          <div role="tablist" aria-label="Avis" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
            {tabs.map(({value, label, icon: Icon}) => (
              <button key={value} type="button" role="tab" aria-selected={tab === value}
                onClick={() => {
                  setTab(value);
                  router.replace(`/avis?onglet=${value}`, {scroll: false});
                }}
                className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                <Icon className="size-4" aria-hidden="true" />{label}
              </button>
            ))}
          </div>
          {tab === 'avis' && <Feedbacks />}
          {tab === 'questionnaires' && <Surveys />}
        </>
      )}
    </div>
  );
}
