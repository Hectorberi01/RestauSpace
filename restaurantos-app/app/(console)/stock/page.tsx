'use client';
import {useState} from 'react';
import {useRouter, useSearchParams} from 'next/navigation';
import {Boxes, ChefHat, ClipboardList, Truck, Wheat} from 'lucide-react';
import {useConsole} from '@/components/console';
import {cx} from '@/components/ui';
import {Ingredients, Recipes} from './catalog';
import {StockItems} from './items';
import {PurchaseOrders, Suppliers} from './purchasing';

type Tab = 'stock' | 'commandes' | 'fournisseurs' | 'ingredients' | 'recettes';

const tabs: {value: Tab; label: string; icon: typeof Boxes}[] = [
  {value: 'stock', label: 'Stock', icon: Boxes},
  {value: 'commandes', label: 'Bons de commande', icon: ClipboardList},
  {value: 'fournisseurs', label: 'Fournisseurs', icon: Truck},
  {value: 'ingredients', label: 'Ingrédients', icon: Wheat},
  {value: 'recettes', label: 'Recettes', icon: ChefHat},
];

export default function StockPage() {
  const {location} = useConsole();
  const router = useRouter();
  const params = useSearchParams();
  const requested = params.get('onglet') as Tab | null;
  const [tab, setTab] = useState<Tab>(tabs.some(item => item.value === requested) ? requested! : 'stock');

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">Stock</h1>
        <p className="mt-1 text-sm text-ink-500">{location ? `${location.name} · ` : ''}niveaux, achats, pertes, inventaires, recettes et coût matière.</p>
      </div>
      <div role="tablist" aria-label="Stock" className="-mx-1 flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map(({value, label, icon: Icon}) => (
          <button key={value} type="button" role="tab" aria-selected={tab === value}
            onClick={() => {
              setTab(value);
              router.replace(`/stock?onglet=${value}`, {scroll: false});
            }}
            className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', tab === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
            <Icon className="size-4" aria-hidden="true" />{label}
          </button>
        ))}
      </div>
      {tab === 'stock' && <StockItems />}
      {tab === 'commandes' && <PurchaseOrders />}
      {tab === 'fournisseurs' && <Suppliers />}
      {tab === 'ingredients' && <Ingredients />}
      {tab === 'recettes' && <Recipes />}
    </div>
  );
}
