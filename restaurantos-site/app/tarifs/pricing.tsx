'use client';
import {Fragment, useEffect, useState, type CSSProperties} from 'react';
import Link from 'next/link';
import {site} from '@/content/site';
import {ArrowRight, Building2, Check, Crown, Gift, Layers, Minus, Plus, Settings2, Store, type LucideIcon} from 'lucide-react';
import {AnimatedPrice} from '@/components/animated-price';
import {Segmented} from '@/components/segmented';
import {addOns, compareSections, markets, orders, price, quota, tiers, trial, type Cell, type Market, type Tier} from '@/content/plans';

const planIcons: Record<string, {icon: LucideIcon; tone: string}> = {
  Essentiel: {icon: Store, tone: 'bg-violet-50 text-violet-600 ring-violet-100'},
  Pro: {icon: Crown, tone: 'bg-amber-50 text-amber-600 ring-amber-100'},
  'Multi-sites': {icon: Building2, tone: 'bg-brand-50 text-brand-600 ring-brand-100'},
};

const sectionIcons: Record<string, LucideIcon> = {Volumes: Layers, Fonctionnalités: Settings2};

function PlanIcon({name, size = 'size-12'}: {name: string; size?: string}) {
  const {icon: Icon, tone} = planIcons[name] ?? planIcons.Essentiel;
  return <span className={`grid ${size} shrink-0 place-items-center rounded-2xl ring-1 ${tone}`}><Icon className="size-[45%]" aria-hidden="true" /></span>;
}

function perMonth(amount: number, currency: string) {
  return currency === 'XOF' ? Math.round(amount / 12) : Math.round((amount / 12) * 100) / 100;
}

function CellValue({cell}: {cell: Cell}) {
  if (cell.kind === 'check') return <Check className="size-4 text-brand-600" aria-label="Inclus" />;
  if (cell.kind === 'dash') return <Minus className="size-4 text-slate-300" aria-label="Non inclus" />;
  return <span className={cell.strong ? 'font-semibold text-ink-900' : 'text-ink-800'}>{cell.text}</span>;
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
  const column = (tier: Tier) => (tier.name === selected ? 'bg-brand-50/70' : '');

  return (
    <>
      <div className="flex flex-col items-center gap-4 sm:flex-row sm:justify-between">
        <Segmented label="Pays" value={market} onChange={setMarket} options={(Object.keys(markets) as Market[]).map(key => ({value: key, label: markets[key].label}))} />
        <div className="flex items-center gap-2">
          <Segmented label="Facturation" value={yearly} onChange={setYearly} options={[{value: false, label: 'Mensuel'}, {value: true, label: 'Annuel'}]} />
          <span className="rounded-full bg-emerald-50 px-2.5 py-1 text-xs font-semibold text-emerald-700 ring-1 ring-emerald-100">2 mois offerts</span>
        </div>
      </div>
      <p className="mt-4 text-center text-sm text-ink-500 sm:text-left">{markets[market].taxNote} {trial} d’essai gratuit sur le plan choisi. Cliquez sur un plan pour le repérer dans le comparatif.</p>

      <div key={market} role="radiogroup" aria-label="Plans" className="mt-10 grid gap-5 md:grid-cols-3">
        {list.map((tier, index) => {
          const plan = planOf(tier);
          const active = tier.name === selected;
          return (
            <div
              key={tier.name}
              onClick={() => setSelected(tier.name)}
              style={{'--rise-delay': `${index * 60}ms`} as CSSProperties}
              className={`card-lift animate-rise relative flex min-w-0 cursor-pointer flex-col rounded-3xl border bg-white p-6 ${active ? 'border-brand-500 shadow-2xl shadow-brand-900/15 ring-1 ring-brand-500' : 'border-slate-200 shadow-sm hover:border-brand-200 hover:shadow-lg hover:shadow-brand-900/5'}`}
            >
              {tier.highlighted && <span className="absolute -top-3 left-6 rounded-full bg-brand-600 px-3 py-1 text-xs font-semibold text-white shadow-md shadow-brand-600/30">Recommandé</span>}
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
                <AnimatedPrice amount={plan.price} currency={currency} className="block whitespace-nowrap text-3xl font-bold tracking-tight text-ink-900" />
                <p className="mt-1 text-sm text-ink-500">{`${ht ? 'HT ' : ''}par ${yearly ? 'an' : 'mois'}`}</p>
                {yearly && <p className="animate-rise text-sm text-ink-500">soit {price(perMonth(plan.price, currency), currency)}{ht} par mois</p>}
              </div>
              <ul className="mt-5 space-y-2.5 border-t border-slate-100 pt-5 text-sm">
                {[
                  {label: 'Établissements', value: quota(plan, 'locations.count')},
                  {label: 'Membres de l’équipe', value: quota(plan, 'staff.count')},
                  {label: 'Commandes par mois', value: orders(plan)},
                ].map(row => (
                  <li key={row.label} className="flex items-center justify-between gap-3">
                    <span className="text-ink-600">{row.label}</span>
                    <strong className="whitespace-nowrap text-ink-900">{row.value}</strong>
                  </li>
                ))}
              </ul>
              <ul className="mt-4 flex-1 space-y-2 border-t border-slate-100 pt-4 text-sm">
                {tier.highlights.map(item =>
                  item.endsWith(':') ? (
                    <li key={item} className="font-semibold text-ink-900">{item}</li>
                  ) : (
                    <li key={item} className="flex gap-2 text-ink-700"><Check className="mt-0.5 size-4 shrink-0 text-brand-600" aria-hidden="true" />{item}</li>
                  ),
                )}
              </ul>
              <Link
                href={link(tier)}
                onClick={event => event.stopPropagation()}
                className={`btn-motion group mt-6 inline-flex items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-semibold ${tier.highlighted ? 'bg-brand-600 text-white shadow-lg shadow-brand-600/30 hover:bg-brand-700' : 'bg-slate-100 text-ink-900 hover:bg-brand-50 hover:text-brand-700'}`}
              >
                Choisir {tier.name}
                <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
              </Link>
              <p className="mt-2 text-center text-xs text-ink-500">{trial} d’essai gratuit</p>
            </div>
          );
        })}
      </div>

      <div data-reveal className="mt-8 flex flex-col gap-5 rounded-2xl border border-brand-100 bg-gradient-to-r from-brand-50 via-white to-brand-50/60 p-5 sm:p-6 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex items-center gap-4">
          <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-white text-brand-600 shadow-md shadow-brand-900/10 ring-1 ring-brand-100"><Gift className="size-6" aria-hidden="true" /></span>
          <div>
            <p className="font-semibold text-ink-900">{trial} d’essai gratuit sur le plan choisi</p>
            <p className="text-sm text-ink-600">Vous travaillez en conditions réelles avant de payer quoi que ce soit.</p>
          </div>
        </div>
        <ul className="flex flex-wrap gap-x-6 gap-y-2 text-sm text-ink-700">
          {['Changement de plan à tout moment', 'Rien n’est supprimé en changeant de plan', 'Accompagnement au démarrage'].map(item => (
            <li key={item} className="flex items-center gap-2"><span className="grid size-5 place-items-center rounded-full bg-brand-600 text-white"><Check className="size-3" strokeWidth={3} aria-hidden="true" /></span>{item}</li>
          ))}
        </ul>
      </div>

      <section data-reveal aria-labelledby="comparatif" className="mt-14 overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-sm">
        <div className="p-6 sm:p-8">
          <h2 id="comparatif" className="text-2xl font-bold tracking-tight text-ink-900">Comparer les plans en détail</h2>
          <p className="mt-1 text-ink-600">Les volumes inclus et les fonctionnalités de chaque plan{yearly ? ', identiques en formule annuelle' : ''}.</p>
          <p className="mt-2 text-xs text-ink-500 sm:hidden">Faites glisser le tableau pour voir tous les plans.</p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[600px] border-t border-slate-100 text-left text-sm">
            <caption className="sr-only">Comparaison des plans</caption>
            <thead>
              <tr>
                <th scope="col" className="sticky left-0 z-10 w-40 bg-white px-4 py-4 sm:w-[40%] sm:px-6"><span className="sr-only">Critère</span></th>
                {list.map(tier => {
                  const active = tier.name === selected;
                  return (
                    <th key={tier.name} scope="col" className={`px-4 py-4 align-middle transition-colors duration-300 ${active ? 'bg-brand-50' : ''}`}>
                      <button type="button" onClick={() => setSelected(tier.name)} className="flex items-center gap-3 text-left">
                        <PlanIcon name={tier.name} size="size-10" />
                        <span>
                          <span className={`block font-semibold ${active ? 'text-brand-700' : 'text-ink-900'}`}>{tier.name}</span>
                          <span className="block text-xs font-normal text-ink-500">{`${price(planOf(tier).price, currency)}${ht} / ${yearly ? 'an' : 'mois'}`}</span>
                        </span>
                      </button>
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {compareSections.map(section => {
                const Icon = sectionIcons[section.title] ?? Layers;
                return (
                  <Fragment key={section.title}>
                    <tr className="border-t border-slate-200 bg-slate-50/70">
                      <th scope="colgroup" colSpan={list.length + 1} className="px-4 py-3 sm:px-6">
                        <span className="sticky left-4 flex items-center gap-2 font-semibold text-ink-900 sm:left-6"><Icon className="size-4 text-brand-600" aria-hidden="true" />{section.title}</span>
                      </th>
                    </tr>
                    {section.rows.map(row => (
                      <tr key={row.code} className="border-t border-slate-100">
                        <th scope="row" className="sticky left-0 z-10 bg-white px-4 py-3 text-xs font-normal text-ink-600 shadow-[1px_0_0_rgb(241_245_249)] sm:px-6 sm:text-sm sm:shadow-none">
                          {row.label}
                          {row.hint && <span className="block text-xs text-ink-500">{row.hint}</span>}
                        </th>
                        {list.map(tier => (
                          <td key={tier.name} className={`px-4 py-3 transition-colors duration-300 ${column(tier)}`}>
                            <CellValue cell={row.cell(planOf(tier), market)} />
                          </td>
                        ))}
                      </tr>
                    ))}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>

      <section data-reveal aria-labelledby="options" className="mt-14">
        <h2 id="options" className="text-2xl font-bold tracking-tight text-ink-900">Options</h2>
        <p className="mt-1 text-ink-600">À ajouter à votre plan depuis la console, par mois{ht}.</p>
        <ul className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
          {addOns(market).map(option => (
            <li key={option.name} className="flex flex-col rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
              <p className="flex items-center gap-2 font-semibold text-ink-900"><Plus className="size-4 shrink-0 text-brand-600" aria-hidden="true" />{option.name}</p>
              <p className="mt-1 flex-1 text-sm text-ink-500">{option.description}</p>
              <p className="mt-3 text-lg font-bold text-ink-900">{price(option.price, currency)}<span className="text-sm font-normal text-ink-500"> / mois</span></p>
              {option.availability && <p className="mt-2 self-start rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-semibold text-brand-700 ring-1 ring-brand-100">{option.availability}</p>}
            </li>
          ))}
        </ul>
      </section>
    </>
  );
}
