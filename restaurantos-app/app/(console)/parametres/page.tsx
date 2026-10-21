'use client';
import {useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {Bell, CreditCard, Globe, Landmark, Printer, Store} from 'lucide-react';
import {cx} from '@/components/ui';
import {FiscalSettings} from './fiscal';
import {NotificationSettings} from './notifications';
import {OnlineSales} from './online';
import {PaymentMethods} from './payments';
import {Printers} from './printers';
import {RestaurantSettings} from './restaurant';

type Tab = 'restaurant' | 'paiements' | 'notifications' | 'vente-en-ligne' | 'imprimantes' | 'fiscalite';

const tabs: {value: Tab; label: string; icon: typeof Store}[] = [
  {value: 'restaurant', label: 'Restaurant', icon: Store},
  {value: 'paiements', label: 'Moyens de paiement', icon: CreditCard},
  {value: 'notifications', label: 'Notifications', icon: Bell},
  {value: 'vente-en-ligne', label: 'Vente en ligne', icon: Globe},
  {value: 'imprimantes', label: 'Imprimantes', icon: Printer},
  {value: 'fiscalite', label: 'Facture normalisée', icon: Landmark},
];

export default function SettingsPage() {
  const router = useRouter();
  const params = useSearchParams();
  const requested = params.get('onglet') as Tab | null;
  const [tab, setTab] = useState<Tab>(requested && tabs.some(item => item.value === requested) ? requested : 'restaurant');

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Paramètres</h1>
        <p className="mt-1 text-sm text-ink-500">Identité du restaurant, moyens de paiement, canaux d’envoi, vente en ligne, imprimantes et facture normalisée.</p>
      </div>
      <div role="tablist" aria-label="Paramètres" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map(({value, label, icon: Icon}) => (
          <button key={value} type="button" role="tab" aria-selected={tab === value}
            onClick={() => {
              setTab(value);
              router.replace(`/parametres?onglet=${value}`, {scroll: false});
            }}
            className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
            <Icon className="size-4" aria-hidden="true" />{label}
          </button>
        ))}
      </div>
      {tab === 'restaurant' && <RestaurantSettings />}
      {tab === 'paiements' && <PaymentMethods />}
      {tab === 'notifications' && <NotificationSettings />}
      {tab === 'vente-en-ligne' && <OnlineSales />}
      {tab === 'imprimantes' && <Printers />}
      {tab === 'fiscalite' && <FiscalSettings />}
    </div>
  );
}
