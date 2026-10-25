'use client';
import {useEffect, useMemo, useState} from 'react';
import {Segmented} from '@/components/segmented';
import {useTween} from '@/lib/use-tween';
import Link from 'next/link';
import {site} from '@/content/site';
import {ArrowRight, Check, Minus, Sparkles, X} from 'lucide-react';
import {addOns, capacity, enabled, markets, price, tiers, trial, type AddOn, type Market, type Tier} from '@/content/plans';

interface Need {
  code: string;
  label: string;
  hint: string;
  steps: number[];
  unit: (value: number) => string;
}

const format = (value: number) => new Intl.NumberFormat('fr-FR').format(value);

const needs: Need[] = [
  {code: 'locations.count', label: 'Établissements', hint: 'Adresses gérées depuis le même compte', steps: [1, 2, 3, 4, 5, 6, 8, 10], unit: value => (value > 1 ? `${value} établissements` : '1 établissement')},
  {code: 'staff.count', label: 'Membres de l’équipe', hint: 'Personnes avec un accès à la console', steps: [1, 2, 3, 5, 8, 10, 15, 20, 25, 30, 40, 60, 80, 100], unit: value => (value > 1 ? `${value} personnes` : '1 personne')},
  {code: 'orders.monthly', label: 'Commandes par mois', hint: 'Tous canaux confondus : salle, comptoir, site, borne', steps: [100, 300, 500, 1000, 2000, 3000, 5000, 8000, 12000, 20000, 50000, 100000], unit: value => `${format(value)} commandes`},
];

const initial: Record<string, number> = {'locations.count': 0, 'staff.count': 3, 'orders.monthly': 3};

/** Besoins oui/non : le code du droit, et l'option qui peut le donner. */
const wishes = [
  {code: 'storefront.api', label: 'Site de commande ou API', hint: 'Commande en ligne sur votre propre site, ou relier vos outils'},
  {code: 'marketing.campaigns', label: 'Campagnes marketing', hint: 'Emails, SMS et WhatsApp à vos clients'},
] as const;

type Wish = (typeof wishes)[number]['code'];

interface Quote {
  tier: Tier;
  options: {option: AddOn; count: number}[];
  /** Raison pour laquelle le plan ne convient pas. */
  blocked?: string;
  monthly: number;
}

/** Prix d'un plan avec les options nécessaires, ou la raison pour laquelle il ne convient pas. */
function quoteFor(tier: Tier, values: Record<string, number>, wanted: Set<Wish>, options: AddOn[]): Quote {
  const plan = tier.monthly;
  const result: Quote = {tier, options: [], monthly: plan.price};
  const add = (code: string, count = 1) => {
    const option = options.find(o => o.plan.entitlements.some(e => e.featureCode === code));
    if (!option) return false;
    result.options.push({option, count});
    result.monthly += option.price * count;
    return true;
  };
  if (capacity(plan, 'staff.count') < values['staff.count']) return {...result, blocked: 'équipe trop grande'};
  if (capacity(plan, 'orders.monthly') < values['orders.monthly']) return {...result, blocked: 'trop de commandes'};
  const extra = values['locations.count'] - capacity(plan, 'locations.count');
  if (extra > 0 && !(enabled(plan, 'locations.extra') && add('locations.count', extra))) return {...result, blocked: 'trop d’établissements'};
  for (const wish of wanted) {
    if (enabled(plan, wish)) continue;
    if (!add(wish)) return {...result, blocked: 'campagnes non incluses'};
  }
  return result;
}

export function Simulator() {
  const [market, setMarket] = useState<Market>('fr');
  const [yearly, setYearly] = useState(false);
  const [levels, setLevels] = useState(initial);
  const [wanted, setWanted] = useState<Set<Wish>>(new Set());
  const list = useMemo(() => tiers(market), [market]);
  const options = useMemo(() => addOns(market), [market]);
  const values = Object.fromEntries(needs.map(need => [need.code, need.steps[levels[need.code]]]));
  const quotes = list.map(tier => quoteFor(tier, values, wanted, options));
  // Le moins cher qui convient ; à prix égal, le plan le plus complet.
  const chosen = quotes.filter(q => !q.blocked).reduce<Quote | undefined>((best, q) => (!best || q.monthly <= best.monthly ? q : best), undefined);
  const currency = markets[market].currency;
  const optionsTotal = chosen ? chosen.monthly - chosen.tier.monthly.price : 0;
  const yearlyPlan = chosen?.tier.yearly;
  const perMonth = chosen ? (yearly && yearlyPlan ? yearlyPlan.price / 12 : chosen.tier.monthly.price) + optionsTotal : 0;
  const saving = chosen && yearlyPlan ? chosen.tier.monthly.price * 12 - yearlyPlan.price : 0;
  const shown = useTween(perMonth);
  const ht = market === 'fr' ? ' HT' : '';

  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get('pays');
    if (requested === 'fr' || requested === 'bj') setMarket(requested);
  }, []);

  const toggle = (code: Wish) =>
    setWanted(current => {
      const next = new Set(current);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });

  return (
    <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_24rem]">
      <div className="rounded-3xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <h2 className="text-lg font-bold text-ink-900">Votre activité</h2>
          <Segmented label="Pays" value={market} onChange={setMarket} options={(Object.keys(markets) as Market[]).map(key => ({value: key, label: markets[key].label}))} />
        </div>
        <div className="mt-8 space-y-8">
          {needs.map(need => {
            const level = levels[need.code];
            const percent = (level / (need.steps.length - 1)) * 100;
            return (
              <div key={need.code}>
                <div className="flex items-baseline justify-between gap-4">
                  <label htmlFor={`need-${need.code}`} className="font-semibold text-ink-900">{need.label}</label>
                  <output htmlFor={`need-${need.code}`} className="rounded-lg bg-brand-50 px-2.5 py-1 text-sm font-semibold tabular-nums text-brand-700">{need.unit(values[need.code])}</output>
                </div>
                <p className="mt-0.5 text-sm text-ink-500">{need.hint}</p>
                <input
                  id={`need-${need.code}`}
                  type="range"
                  min={0}
                  max={need.steps.length - 1}
                  step={1}
                  value={level}
                  aria-valuetext={need.unit(values[need.code])}
                  onChange={event => setLevels(current => ({...current, [need.code]: Number(event.target.value)}))}
                  className="range mt-3 w-full"
                  style={{background: `linear-gradient(to right, var(--color-brand-600) ${percent}%, rgb(226 232 240) ${percent}%)`}}
                />
              </div>
            );
          })}
        </div>
        <div className="mt-10 border-t border-slate-100 pt-8">
          <fieldset>
            <legend className="font-semibold text-ink-900">Vos besoins</legend>
            <div className="mt-4 grid gap-3 sm:grid-cols-3">
              {wishes.map(wish => {
                const on = wanted.has(wish.code);
                return (
                  <label key={wish.code} className={`flex cursor-pointer gap-3 rounded-2xl border p-4 text-sm transition ${on ? 'border-brand-400 bg-brand-50 ring-1 ring-brand-400' : 'border-slate-200 hover:border-brand-200'}`}>
                    <input type="checkbox" checked={on} onChange={() => toggle(wish.code)} className="mt-0.5 size-4 shrink-0 accent-brand-600" />
                    <span>
                      <span className="block font-semibold text-ink-900">{wish.label}</span>
                      <span className="mt-0.5 block text-ink-500">{wish.hint}</span>
                    </span>
                  </label>
                );
              })}
            </div>
          </fieldset>
        </div>
      </div>

      <aside className="lg:sticky lg:top-24 lg:self-start">
        <div className="relative overflow-hidden rounded-3xl bg-gradient-to-br from-brand-700 via-brand-600 to-violet-600 p-6 text-white shadow-2xl shadow-brand-900/25 sm:p-7">
          <div className="pointer-events-none absolute -right-16 -top-16 size-56 rounded-full bg-white/10 blur-2xl" aria-hidden="true" />
          <p className="flex items-center gap-2 text-sm font-semibold text-brand-100"><Sparkles className="size-4" aria-hidden="true" />Plan conseillé</p>
          {chosen ? (
            <>
              <p key={chosen.tier.name} className="animate-rise mt-2 text-3xl font-bold">{chosen.tier.name}</p>
              <p className="text-sm text-brand-100">{chosen.tier.audience}</p>
              <p className="mt-6 flex items-baseline gap-1.5" aria-live="polite">
                <span className="text-4xl font-bold tabular-nums tracking-tight">{price(Math.round(shown * 100) / 100, currency)}</span>
                <span className="text-brand-100">/ mois{ht}</span>
              </p>
              <ul className="mt-3 space-y-1 text-sm text-brand-100">
                <li className="flex justify-between gap-3">
                  <span>Plan {chosen.tier.name}{yearly && yearlyPlan ? ', payé à l’année' : ''}</span>
                  <span className="tabular-nums">{price(yearly && yearlyPlan ? yearlyPlan.price : chosen.tier.monthly.price, currency)} / {yearly && yearlyPlan ? 'an' : 'mois'}</span>
                </li>
                {chosen.options.map(({option, count}) => (
                  <li key={option.name} className="flex justify-between gap-3">
                    <span>{count > 1 ? `${count} × ` : ''}{option.name}</span>
                    <span className="tabular-nums">{price(option.price * count, currency)} / mois</span>
                  </li>
                ))}
              </ul>
              {yearlyPlan && (
                <label className="mt-5 flex cursor-pointer items-center justify-between gap-3 rounded-2xl bg-white/10 px-4 py-3 text-sm ring-1 ring-white/15">
                  <span>Paiement annuel du plan{saving > 0 && <span className="block text-xs text-brand-100">{price(saving, currency)} d’économie par an</span>}</span>
                  <input type="checkbox" checked={yearly} onChange={event => setYearly(event.target.checked)} className="peer sr-only" />
                  <span className="relative h-6 w-11 shrink-0 rounded-full bg-white/25 transition peer-checked:bg-emerald-400 peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-white after:absolute after:left-0.5 after:top-0.5 after:size-5 after:rounded-full after:bg-white after:shadow after:transition peer-checked:after:translate-x-5" aria-hidden="true" />
                </label>
              )}
              <Link href={`${site.signupUrl}?plan=${encodeURIComponent(yearly && yearlyPlan ? yearlyPlan.name : chosen.tier.name)}&pays=${market}`} className="btn-motion mt-6 flex items-center justify-center gap-2 rounded-xl bg-white px-5 py-3 text-sm font-semibold text-brand-700 shadow-lg shadow-black/10 hover:bg-brand-50">
                Essayer {trial} gratuitement <ArrowRight className="size-4" aria-hidden="true" />
              </Link>
            </>
          ) : (
            <>
              <p className="animate-rise mt-2 text-3xl font-bold">Sur mesure</p>
              <p className="mt-2 text-sm leading-relaxed text-brand-100">Votre activité dépasse le plan {list[list.length - 1]?.name}. Parlons-en : nous préparons une offre adaptée à votre enseigne.</p>
              <Link href={`/contact?pays=${market}`} className="mt-6 flex items-center justify-center gap-2 rounded-xl bg-white px-5 py-3 text-sm font-semibold text-brand-700 shadow-lg shadow-black/10 transition hover:bg-brand-50">
                Nous contacter <ArrowRight className="size-4" aria-hidden="true" />
              </Link>
            </>
          )}
        </div>

        <ul className="mt-5 space-y-2">
          {quotes.map(quote => {
            const ok = !quote.blocked;
            const active = quote === chosen;
            return (
              <li key={quote.tier.name} className={`flex items-center justify-between gap-3 rounded-2xl border px-4 py-3 text-sm transition duration-300 ${active ? 'border-brand-300 bg-brand-50 shadow-md shadow-brand-900/5' : ok ? 'border-slate-200 bg-white' : 'border-slate-200 bg-slate-50 text-ink-500'}`}>
                <span className="flex items-center gap-2.5">
                  <span className={`grid size-6 shrink-0 place-items-center rounded-full ${active ? 'bg-brand-600 text-white' : ok ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-200 text-ink-500'}`}>
                    {active ? <Check className="size-3.5" aria-hidden="true" /> : ok ? <Minus className="size-3.5" aria-hidden="true" /> : <X className="size-3.5" aria-hidden="true" />}
                  </span>
                  <span className={`font-semibold ${active ? 'text-brand-700' : ok ? 'text-ink-900' : ''}`}>{quote.tier.name}</span>
                  <span className="sr-only">{active ? 'conseillé' : ok ? 'convient aussi' : 'ne suffit pas'}</span>
                </span>
                <span className="text-right tabular-nums">{ok ? `${price(quote.monthly, currency)} / mois` : quote.blocked}</span>
              </li>
            );
          })}
        </ul>
        <p className="mt-4 text-xs leading-relaxed text-ink-500">
          {markets[market].taxNote} Prix par mois, options comprises. Les options se paient au mois. Les messages envoyés avec votre propre compte (email, SMS, WhatsApp) sont sans limite. Détail complet sur la page <Link href="/tarifs" className="font-semibold text-brand-700 hover:underline">Tarifs</Link>.
        </p>
      </aside>
    </div>
  );
}
