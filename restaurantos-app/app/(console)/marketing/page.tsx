'use client';
import {useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {Gift, Megaphone, Star, Tag} from 'lucide-react';
import {useConsole} from '@/components/console';
import {Alert, cx} from '@/components/ui';
import {PlanBadge} from '@/components/plan';
import {Feature} from '@/lib/plan';
import {Campaigns} from './campaigns';
import {GiftCards} from './gift-cards';
import {Loyalty} from './loyalty';
import {Promotions} from './promotions';

type Tab = 'campagnes' | 'promotions' | 'fidelite' | 'cartes-cadeaux';

export default function MarketingPage() {
  const {can} = useConsole();
  const router = useRouter();
  const params = useSearchParams();
  const tabs = [
    {value: 'campagnes' as Tab, label: 'Campagnes', icon: Megaphone, allowed: can('notifications.view'), feature: Feature.MarketingCampaigns},
    {value: 'promotions' as Tab, label: 'Promotions', icon: Tag, allowed: can('promotions.view'), feature: undefined as string | undefined},
    {value: 'fidelite' as Tab, label: 'Fidélité', icon: Star, allowed: can('loyalty.view'), feature: Feature.Loyalty},
    {value: 'cartes-cadeaux' as Tab, label: 'Cartes cadeaux', icon: Gift, allowed: can('loyalty.view'), feature: Feature.MarketingGiftCards},
  ].filter(item => item.allowed);
  const requested = params.get('onglet') as Tab | null;
  const [tab, setTab] = useState<Tab | undefined>(tabs.find(item => item.value === requested)?.value ?? tabs[0]?.value);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Marketing</h1>
        <p className="mt-1 text-sm text-ink-500">Campagnes WhatsApp, SMS et e-mail, promotions et codes promo, programme de fidélité, cartes cadeaux et avoirs.</p>
      </div>
      {tabs.length === 0 ? (
        <Alert tone="blue">Vous n’avez accès ni aux campagnes, ni aux promotions, ni à la fidélité. Demandez la permission à un gérant.</Alert>
      ) : (
        <>
          <div role="tablist" aria-label="Marketing" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
            {tabs.map(({value, label, icon: Icon, feature}) => (
              <button key={value} type="button" role="tab" aria-selected={tab === value}
                onClick={() => {
                  setTab(value);
                  router.replace(`/marketing?onglet=${value}`, {scroll: false});
                }}
                className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                <Icon className="size-4" aria-hidden="true" />{label}{feature && <PlanBadge feature={feature} />}
              </button>
            ))}
          </div>
          {tab === 'campagnes' && <Campaigns />}
          {tab === 'promotions' && <Promotions />}
          {tab === 'fidelite' && <Loyalty />}
          {tab === 'cartes-cadeaux' && <GiftCards />}
        </>
      )}
    </div>
  );
}
