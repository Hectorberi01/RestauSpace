'use client';
import {useState} from 'react';
import {WithLocation} from '@/components/console';
import {Breadcrumb} from '@/components/ui';
import {Menus} from './menus';
import {Options} from './options';
import {Products} from './products';
import {Taxes} from './taxes';

type View = 'products' | 'menus' | 'options' | 'taxes';

const titles: Record<Exclude<View, 'products'>, {title: string; description: string}> = {
  menus: {title: 'Cartes & catégories', description: 'Chaque carte est publiée pour un établissement ; ses sections sont les catégories de vos produits.'},
  options: {title: 'Groupes d’options', description: 'Suppléments et choix proposés à la commande : accompagnements, sauces, cuissons…'},
  taxes: {title: 'Taxes', description: 'Taux appliqués aux produits.'},
};

export default function MenuPage() {
  const [view, setView] = useState<View>('products');
  return (
    <WithLocation>
      {location =>
        view === 'products' ? (
          <Products location={location} onManage={setView} />
        ) : (
          <div className="space-y-6">
            <div>
              <Breadcrumb items={[{label: 'Tableau de bord', href: '/tableau-de-bord'}, {label: 'Menu & produits', onClick: () => setView('products')}, {label: titles[view].title}]} />
              <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight text-ink-900">{titles[view].title}</h1>
              <p className="mt-1 text-sm text-ink-500">{titles[view].description}</p>
            </div>
            {view === 'menus' && <Menus location={location} />}
            {view === 'options' && <Options />}
            {view === 'taxes' && <Taxes />}
          </div>
        )
      }
    </WithLocation>
  );
}
