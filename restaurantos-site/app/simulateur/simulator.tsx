'use client';
import {useEffect, useMemo, useState} from 'react';
import {Segmented} from '@/components/segmented';
import {useTween} from '@/lib/use-tween';
import Link from 'next/link';
import {site} from '@/content/site';
import {ArrowRight, Check, Minus, Sparkles, X} from 'lucide-react';
import {markets, price, tiers, trial, type Market, type Tier} from '@/content/plans';

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
  {code: 'staff.count', label: 'Membres de l’équipe', hint: 'Personnes avec un accès à la console', steps: [1, 2, 3, 5, 8, 10, 15, 20, 25, 30, 40, 60], unit: value => (value > 1 ? `${value} personnes` : '1 personne')},
  {code: 'orders.monthly', label: 'Commandes par mois', hint: 'Tous canaux confondus : salle, comptoir, site, borne', steps: [50, 100, 150, 300, 500, 1000, 1500, 2000, 3000, 5000, 8000, 12000, 20000], unit: value => `${format(value)} commandes`},
  {code: 'notifications.sms.monthly', label: 'SMS par mois', hint: 'Confirmations et rappels envoyés à vos clients', steps: [0, 25, 50, 100, 200, 500, 1000, 2000], unit: value => (value ? `${format(value)} SMS` : 'Aucun')},
  {code: 'notifications.whatsapp.monthly', label: 'Messages WhatsApp par mois', hint: 'Notifications envoyées sur WhatsApp', steps: [0, 50, 100, 200, 500, 1000, 2000], unit: value => (value ? `${format(value)} messages` : 'Aucun')},
];

const initial: Record<string, number> = {'locations.count': 0, 'staff.count': 3, 'orders.monthly': 5, 'notifications.sms.monthly': 0, 'notifications.whatsapp.monthly': 0};

function capacity(tier: Tier, code: string): number {
  const entitlement = tier.monthly.entitlements.find(item => item.featureCode === code);
  if (!entitlement || !entitlement.enabled) return 0;
  return entitlement.limit ?? Number.POSITIVE_INFINITY;
}

export function Simulator() {
  const [market, setMarket] = useState<Market>('fr');
  const [yearly, setYearly] = useState(false);
  const [levels, setLevels] = useState(initial);
  const list = useMemo(() => tiers(market).sort((a, b) => a.monthly.price - b.monthly.price), [market]);
  const values = Object.fromEntries(needs.map(need => [need.code, need.steps[levels[need.code]]]));
  const fits = (tier: Tier) => needs.every(need => capacity(tier, need.code) >= values[need.code]);
  const chosen = list.find(fits);
  const currency = markets[market].currency;
  const billed = chosen ? (yearly && chosen.yearly ? chosen.yearly.price : chosen.monthly.price) : 0;
  const perMonth = chosen ? (yearly && chosen.yearly ? chosen.yearly.price / 12 : chosen.monthly.price) : 0;
  const saving = chosen?.yearly ? chosen.monthly.price * 12 - chosen.yearly.price : 0;
  const shown = useTween(perMonth);
  const limiting = chosen && list.indexOf(chosen) > 0
    ? needs.filter(need => capacity(list[list.indexOf(chosen) - 1], need.code) < values[need.code]).map(need => need.label.toLowerCase())
    : [];

  useEffect(() => {
    const requested = new URLSearchParams(window.location.search).get('pays');
    if (requested === 'fr' || requested === 'bj') setMarket(requested);
  }, []);

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
      </div>

      <aside className="lg:sticky lg:top-24 lg:self-start">
        <div className="relative overflow-hidden rounded-3xl bg-gradient-to-br from-brand-700 via-brand-600 to-violet-600 p-6 text-white shadow-2xl shadow-brand-900/25 sm:p-7">
          <div className="pointer-events-none absolute -right-16 -top-16 size-56 rounded-full bg-white/10 blur-2xl" aria-hidden="true" />
          <p className="flex items-center gap-2 text-sm font-semibold text-brand-100"><Sparkles className="size-4" aria-hidden="true" />Plan conseillé</p>
          {chosen ? (
            <>
              <p key={chosen.name} className="animate-rise mt-2 text-3xl font-bold">{chosen.name}</p>
              <p className="text-sm text-brand-100">{chosen.audience}</p>
              <p className="mt-6 flex items-baseline gap-1.5" aria-live="polite">
                <span className="text-4xl font-bold tabular-nums tracking-tight">{price(Math.round(shown * 100) / 100, currency)}</span>
                <span className="text-brand-100">/ mois{market === 'fr' ? ' HT' : ''}</span>
              </p>
              {yearly && chosen.yearly && <p className="mt-1 text-sm text-brand-100">Soit {price(billed, currency)} payés une fois par an.</p>}
              {chosen.monthly.price > 0 && chosen.yearly && (
                <label className="mt-5 flex cursor-pointer items-center justify-between gap-3 rounded-2xl bg-white/10 px-4 py-3 text-sm ring-1 ring-white/15">
                  <span>Paiement annuel{saving > 0 && <span className="block text-xs text-brand-100">{price(saving, currency)} d’économie par an</span>}</span>
                  <input type="checkbox" checked={yearly} onChange={event => setYearly(event.target.checked)} className="peer sr-only" />
                  <span className="relative h-6 w-11 shrink-0 rounded-full bg-white/25 transition peer-checked:bg-emerald-400 peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-white after:absolute after:left-0.5 after:top-0.5 after:size-5 after:rounded-full after:bg-white after:shadow after:transition peer-checked:after:translate-x-5" aria-hidden="true" />
                </label>
              )}
              {limiting.length > 0 && <p className="mt-4 text-sm text-brand-100">Choisi pour : {limiting.join(', ')}.</p>}
              <Link href={`${site.signupUrl}?plan=${encodeURIComponent(yearly && chosen.yearly ? chosen.yearly.name : chosen.name)}&pays=${market}`} className="btn-motion mt-6 flex items-center justify-center gap-2 rounded-xl bg-white px-5 py-3 text-sm font-semibold text-brand-700 shadow-lg shadow-black/10 hover:bg-brand-50">
                {chosen.monthly.price > 0 ? `Essayer ${trial}` : 'Commencer'} <ArrowRight className="size-4" aria-hidden="true" />
              </Link>
            </>
          ) : (
            <>
              <p className="animate-rise mt-2 text-3xl font-bold">Sur mesure</p>
              <p className="mt-2 text-sm leading-relaxed text-brand-100">Votre activité dépasse le plan Groupe. Parlons-en : nous préparons une offre adaptée à votre enseigne.</p>
              <Link href={`/contact?pays=${market}`} className="mt-6 flex items-center justify-center gap-2 rounded-xl bg-white px-5 py-3 text-sm font-semibold text-brand-700 shadow-lg shadow-black/10 transition hover:bg-brand-50">
                Nous contacter <ArrowRight className="size-4" aria-hidden="true" />
              </Link>
            </>
          )}
        </div>

        <ul className="mt-5 space-y-2">
          {list.map(tier => {
            const ok = fits(tier);
            const active = tier === chosen;
            return (
              <li key={tier.name} className={`flex items-center justify-between gap-3 rounded-2xl border px-4 py-3 text-sm transition duration-300 ${active ? 'border-brand-300 bg-brand-50 shadow-md shadow-brand-900/5' : ok ? 'border-slate-200 bg-white' : 'border-slate-200 bg-slate-50 text-ink-500'}`}>
                <span className="flex items-center gap-2.5">
                  <span className={`grid size-6 place-items-center rounded-full ${active ? 'bg-brand-600 text-white' : ok ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-200 text-ink-500'}`}>
                    {active ? <Check className="size-3.5" aria-hidden="true" /> : ok ? <Minus className="size-3.5" aria-hidden="true" /> : <X className="size-3.5" aria-hidden="true" />}
                  </span>
                  <span className={`font-semibold ${active ? 'text-brand-700' : ok ? 'text-ink-900' : ''}`}>{tier.name}</span>
                  <span className="sr-only">{active ? 'conseillé' : ok ? 'convient aussi' : 'ne suffit pas'}</span>
                </span>
                <span className="tabular-nums">{price(tier.monthly.price, currency)} / mois</span>
              </li>
            );
          })}
        </ul>
        <p className="mt-4 text-xs leading-relaxed text-ink-500">
          {markets[market].taxNote} Tous les modules sont inclus dans chaque plan. Détail complet sur la page <Link href="/tarifs" className="font-semibold text-brand-700 hover:underline">Tarifs</Link>.
        </p>
      </aside>
    </div>
  );
}
