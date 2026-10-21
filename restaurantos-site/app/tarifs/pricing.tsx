'use client';
import {useEffect, useState, type CSSProperties} from 'react';
import Link from 'next/link';
import {site} from '@/content/site';
import {ArrowRight, Check, Crown, Gift, Layers, Minus, Send, Settings2, Store, UsersRound, type LucideIcon} from 'lucide-react';
import {AnimatedPrice} from '@/components/animated-price';
import {Segmented} from '@/components/segmented';
import {featureGroups} from '@/content/features';
import {markets, price, quota, quotaRows, tiers, trialDays, type Market, type Tier} from '@/content/plans';

const planIcons: Record<string, {icon: LucideIcon; tone: string}> = {
  Découverte: {icon: Send, tone: 'bg-sky-50 text-sky-600 ring-sky-100'},
  Essentiel: {icon: Store, tone: 'bg-violet-50 text-violet-600 ring-violet-100'},
  Pro: {icon: Crown, tone: 'bg-amber-50 text-amber-600 ring-amber-100'},
  Groupe: {icon: UsersRound, tone: 'bg-brand-50 text-brand-600 ring-brand-100'},
};

function PlanIcon({name, size = 'size-12'}: {name: string; size?: string}) {
  const {icon: Icon, tone} = planIcons[name] ?? planIcons.Essentiel;
  return <span className={`grid ${size} shrink-0 place-items-center rounded-2xl ring-1 ${tone}`}><Icon className="size-[45%]" aria-hidden="true" /></span>;
}

function perMonth(amount: number, currency: string) {
  return currency === 'XOF' ? Math.round(amount / 12) : Math.round((amount / 12) * 100) / 100;
}

export function Pricing() {
  const [market, setMarket] = useState<Market>('fr');
  const [yearly, setYearly] = useState(false);
  const [selected, setSelected] = useState('Pro');

  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get('pays');
    if (requested === 'fr' || requested === 'bj') setMarket(requested);
  }, []);

  const list = tiers(market);
  const currency = markets[market].currency;
  const ht = market === 'fr' ? ' HT' : '';
  const planOf = (tier: Tier) => (yearly && tier.yearly ? tier.yearly : tier.monthly);
  const link = (tier: Tier) => `${site.signupUrl}?plan=${encodeURIComponent(planOf(tier).name)}&pays=${market}`;

  return (
    <>
      <div className="flex flex-col items-center gap-4 sm:flex-row sm:justify-between">
        <Segmented label="Pays" value={market} onChange={setMarket} options={(Object.keys(markets) as Market[]).map(key => ({value: key, label: markets[key].label}))} />
        <div className="flex items-center gap-2">
          <Segmented label="Facturation" value={yearly} onChange={setYearly} options={[{value: false, label: 'Mensuel'}, {value: true, label: 'Annuel'}]} />
          <span className="rounded-full bg-emerald-50 px-2.5 py-1 text-xs font-semibold text-emerald-700 ring-1 ring-emerald-100">2 mois offerts</span>
        </div>
      </div>
      <p className="mt-4 text-center text-sm text-ink-500 sm:text-left">{markets[market].taxNote} {trialDays} jours d’essai sur chaque plan. Cliquez sur un plan pour le repérer dans le comparatif.</p>

      <div key={market} role="radiogroup" aria-label="Plans" className="mt-10 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
        {list.map((tier, index) => {
          const plan = planOf(tier);
          const free = tier.monthly.price === 0;
          const active = tier.name === selected;
          return (
            <div
              key={tier.name}
              onClick={() => setSelected(tier.name)}
              style={{'--rise-delay': `${index * 60}ms`} as CSSProperties}
              className={`card-lift animate-rise relative flex min-w-0 cursor-pointer flex-col rounded-3xl border bg-white p-6 ${active ? 'border-brand-500 shadow-2xl shadow-brand-900/15 ring-1 ring-brand-500' : 'border-slate-200 shadow-sm hover:border-brand-200 hover:shadow-lg hover:shadow-brand-900/5'}`}
            >
              {tier.highlighted && <span className="absolute -top-3 left-6 rounded-full bg-brand-600 px-3 py-1 text-xs font-semibold text-white shadow-md shadow-brand-600/30">Le plus complet</span>}
              <div className="flex items-start justify-between gap-3">
                <PlanIcon name={tier.name} />
                <button
                  type="button"
                  role="radio"
                  aria-checked={active}
                  aria-label={`Repérer le plan ${tier.name}`}
                  onClick={event => {
                    event.stopPropagation();
                    setSelected(tier.name);
                  }}
                  className={`grid size-6 place-items-center rounded-full border-2 transition ${active ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-300 hover:border-brand-400'}`}
                >
                  {active && <Check className="animate-pop size-3.5" strokeWidth={3} aria-hidden="true" />}
                </button>
              </div>
              <h3 className="mt-4 text-xl font-bold text-ink-900">{tier.name}</h3>
              <p className="mt-1 min-h-10 text-sm text-ink-500">{tier.audience}</p>
              <div className="mt-5">
                {free ? (
                  <p className="text-3xl font-bold tracking-tight text-ink-900">Gratuit</p>
                ) : (
                  <AnimatedPrice amount={plan.price} currency={currency} className="block whitespace-nowrap text-3xl font-bold tracking-tight text-ink-900" />
                )}
                <p className="mt-1 text-sm text-ink-500">{free ? 'Sans engagement' : `${ht.trim() ? 'HT ' : ''}par ${yearly ? 'an' : 'mois'}`}</p>
                {yearly && !free && <p className="animate-rise text-sm text-ink-500">soit {price(perMonth(plan.price, currency), currency)}{ht} par mois</p>}
              </div>
              <ul className="mt-5 flex-1 space-y-2.5 border-t border-slate-100 pt-5 text-sm">
                {quotaRows.slice(0, 3).map(row => (
                  <li key={row.code} className="flex items-center justify-between gap-3">
                    <span className="flex items-center gap-2 text-ink-600"><Check className="size-4 shrink-0 text-brand-600" aria-hidden="true" />{row.label}</span>
                    <strong className="whitespace-nowrap text-ink-900">{quota(plan, row.code)}</strong>
                  </li>
                ))}
              </ul>
              <Link
                href={link(tier)}
                onClick={event => event.stopPropagation()}
                className={`btn-motion group mt-6 inline-flex items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-semibold ${tier.highlighted ? 'bg-brand-600 text-white shadow-lg shadow-brand-600/30 hover:bg-brand-700' : 'bg-slate-100 text-ink-900 hover:bg-brand-50 hover:text-brand-700'}`}
              >
                {free ? 'Commencer gratuitement' : `Choisir ${tier.name}`}
                <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
              </Link>
              {!free && <p className="mt-2 text-center text-xs text-ink-500">{trialDays} jours d’essai gratuit</p>}
            </div>
          );
        })}
      </div>

      <div data-reveal className="mt-8 flex flex-col gap-5 rounded-2xl border border-brand-100 bg-gradient-to-r from-brand-50 via-white to-brand-50/60 p-5 sm:p-6 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex items-center gap-4">
          <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-white text-brand-600 shadow-md shadow-brand-900/10 ring-1 ring-brand-100"><Gift className="size-6" aria-hidden="true" /></span>
          <div>
            <p className="font-semibold text-ink-900">{trialDays} jours d’essai gratuit sur tous les plans</p>
            <p className="text-sm text-ink-600">Vous travaillez en conditions réelles avant de payer quoi que ce soit.</p>
          </div>
        </div>
        <ul className="flex flex-wrap gap-x-6 gap-y-2 text-sm text-ink-700">
          {['Tous les modules inclus', 'Changement de plan à tout moment', 'Accompagnement au démarrage'].map(item => (
            <li key={item} className="flex items-center gap-2"><span className="grid size-5 place-items-center rounded-full bg-brand-600 text-white"><Check className="size-3" strokeWidth={3} aria-hidden="true" /></span>{item}</li>
          ))}
        </ul>
      </div>

      <section data-reveal aria-labelledby="comparatif" className="mt-14 overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-sm">
        <div className="p-6 sm:p-8">
          <h2 id="comparatif" className="text-2xl font-bold tracking-tight text-ink-900">Comparer les plans en détail</h2>
          <p className="mt-1 text-ink-600">Tous les modules sont inclus dans tous les plans. Les différences portent sur les volumes d’utilisation{yearly ? ' (par mois, en formule annuelle)' : ''}.</p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[820px] border-t border-slate-100 text-left text-sm">
            <caption className="sr-only">Comparaison des plans</caption>
            <thead>
              <tr>
                <th scope="col" className="w-[30%] px-6 py-4"><span className="sr-only">Critère</span></th>
                {list.map(tier => {
                  const active = tier.name === selected;
                  return (
                    <th key={tier.name} scope="col" className={`px-4 py-4 align-middle transition-colors duration-300 ${active ? 'bg-brand-50' : ''}`}>
                      <button type="button" onClick={() => setSelected(tier.name)} className="flex items-center gap-3 text-left">
                        <PlanIcon name={tier.name} size="size-10" />
                        <span>
                          <span className={`block font-semibold ${active ? 'text-brand-700' : 'text-ink-900'}`}>{tier.name}</span>
                          <span className="block text-xs font-normal text-ink-500">{tier.monthly.price === 0 ? 'Gratuit' : `${price(planOf(tier).price, currency)}${ht} / ${yearly ? 'an' : 'mois'}`}</span>
                        </span>
                      </button>
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {quotaRows.map((row, index) => (
                <tr key={row.code} className="border-t border-slate-100">
                  {index === 0 ? (
                    <th scope="row" className="px-6 py-3 font-normal text-ink-600">
                      <span className="mb-2 flex items-center gap-2 font-semibold text-ink-900"><Layers className="size-4 text-brand-600" aria-hidden="true" />Volumes par mois</span>
                      {row.label}
                    </th>
                  ) : (
                    <th scope="row" className="px-6 py-3 font-normal text-ink-600">{row.label}</th>
                  )}
                  {list.map(tier => {
                    const value = quota(planOf(tier), row.code);
                    return (
                      <td key={tier.name} className={`whitespace-nowrap px-4 py-3 align-bottom transition-colors duration-300 ${tier.name === selected ? 'bg-brand-50/70' : ''}`}>
                        {value === '—' ? <Minus className="size-4 text-slate-300" aria-label="Non inclus" /> : <span className={value === 'Illimité' ? 'font-semibold text-ink-900' : 'text-ink-800'}>{value}</span>}
                      </td>
                    );
                  })}
                </tr>
              ))}
              {featureGroups.map((group, index) => (
                <tr key={group.id} className="border-t border-slate-100">
                  <th scope="row" className="px-6 py-3 font-normal text-ink-600">
                    {index === 0 && <span className="mb-2 flex items-center gap-2 font-semibold text-ink-900"><Settings2 className="size-4 text-brand-600" aria-hidden="true" />Fonctionnalités</span>}
                    {group.title} <span className="text-xs text-ink-500">({group.features.map(item => item.title.toLowerCase()).join(', ')})</span>
                  </th>
                  {list.map(tier => (
                    <td key={tier.name} className={`px-4 py-3 align-bottom transition-colors duration-300 ${tier.name === selected ? 'bg-brand-50/70' : ''}`}>
                      <Check className="size-4 text-brand-600" aria-label="Inclus" />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}
