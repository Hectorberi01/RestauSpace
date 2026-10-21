'use client';
import {useEffect, useMemo, useRef, useState} from 'react';
import Link from 'next/link';
import {ArrowRight, CreditCard, Headset, Link2, MessageSquareText, Rocket, Search, Settings, ShieldCheck, UsersRound, Wallet, X, type LucideIcon} from 'lucide-react';
import {Accordion} from '@/components/accordion';
import type {QuestionGroup} from '@/content/faq';

const icons: Record<string, LucideIcon> = {
  demarrer: Rocket,
  tarifs: CreditCard,
  fonctionnalites: Settings,
  securite: ShieldCheck,
  equipe: UsersRound,
  integrations: Link2,
  paiements: Wallet,
  assistance: Headset,
};

const popular: {label: string; query: string}[] = [
  {label: 'Tarifs', query: 'plan'},
  {label: 'Installation', query: 'installer'},
  {label: 'Menu', query: 'menu'},
  {label: 'Équipe', query: 'équipe'},
  {label: 'Paiement', query: 'paiement'},
];

const normalize = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/** La recherche (dans le héros) : partagée avec la liste par un événement, pour garder le héros côté serveur. */
export function FaqSearch() {
  const [query, setQuery] = useState('');
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    window.dispatchEvent(new CustomEvent('faq:query', {detail: query}));
  }, [query]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        input.current?.focus();
      }
    };
    const onSet = (event: Event) => setQuery((event as CustomEvent<string>).detail);
    window.addEventListener('keydown', onKey);
    window.addEventListener('faq:set', onSet);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('faq:set', onSet);
    };
  }, []);

  const choose = (value: string) => {
    setQuery(value);
    document.getElementById('reponses')?.scrollIntoView({behavior: 'smooth', block: 'start'});
  };

  return (
    <div className="mt-8 max-w-xl">
      <label className="flex items-center gap-3 rounded-2xl border border-white bg-white/90 px-4 py-3.5 shadow-xl shadow-brand-900/10 ring-1 ring-slate-200/70 backdrop-blur transition focus-within:ring-4 focus-within:ring-brand-100">
        <Search className="size-5 shrink-0 text-ink-500" aria-hidden="true" />
        <span className="sr-only">Rechercher une question</span>
        <input
          ref={input}
          type="search"
          value={query}
          onChange={event => setQuery(event.target.value)}
          onKeyDown={event => event.key === 'Enter' && choose(query)}
          placeholder="Rechercher une question…"
          className="min-w-0 flex-1 bg-transparent text-ink-900 outline-none placeholder:text-ink-400 [&::-webkit-search-cancel-button]:hidden"
        />
        {query ? (
          <button type="button" onClick={() => setQuery('')} aria-label="Effacer la recherche" className="rounded-lg p-1 text-ink-500 hover:bg-slate-100">
            <X className="size-4" aria-hidden="true" />
          </button>
        ) : (
          <kbd className="hidden rounded-md bg-slate-100 px-1.5 py-0.5 font-sans text-xs text-ink-500 sm:block">⌘ K</kbd>
        )}
      </label>
      <div className="mt-4 flex flex-wrap items-center gap-2 text-sm">
        <span className="font-medium text-ink-700">Populaires :</span>
        {popular.map(item => (
          <button key={item.label} type="button" onClick={() => choose(item.query)} className="btn-motion rounded-full border border-slate-200 bg-white/80 px-3 py-1 text-ink-600 hover:border-brand-200 hover:text-brand-700">
            {item.label}
          </button>
        ))}
      </div>
    </div>
  );
}

export function FaqBrowser({groups}: {groups: QuestionGroup[]}) {
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(groups[0]?.id);

  useEffect(() => {
    const onQuery = (event: Event) => setQuery((event as CustomEvent<string>).detail);
    window.addEventListener('faq:query', onQuery);
    return () => window.removeEventListener('faq:query', onQuery);
  }, []);

  const shown = useMemo(() => {
    const needle = normalize(query.trim());
    if (!needle) return groups;
    return groups
      .map(group => ({...group, questions: group.questions.filter(item => normalize(`${item.q} ${item.a} ${group.title}`).includes(needle))}))
      .filter(group => group.questions.length > 0);
  }, [groups, query]);
  const total = shown.reduce((sum, group) => sum + group.questions.length, 0);

  useEffect(() => {
    const sections = shown.map(group => document.getElementById(`faq-${group.id}`)).filter(Boolean) as HTMLElement[];
    const onScroll = () => {
      let current = sections[0]?.id;
      for (const section of sections) if (section.getBoundingClientRect().top < 160) current = section.id;
      if (current) setActive(current.replace('faq-', ''));
    };
    onScroll();
    window.addEventListener('scroll', onScroll, {passive: true});
    return () => window.removeEventListener('scroll', onScroll);
  }, [shown]);

  return (
    <div id="reponses" className="grid scroll-mt-24 grid-cols-1 gap-10 lg:grid-cols-[18rem_minmax(0,1fr)]">
      <aside className="min-w-0 lg:sticky lg:top-24 lg:self-start">
        <nav aria-label="Thèmes" className="rounded-3xl border border-slate-200 bg-white p-3 shadow-sm">
          <ul className="flex gap-1 overflow-x-auto lg:flex-col lg:overflow-visible">
            {groups.map(group => {
              const Icon = icons[group.id] ?? MessageSquareText;
              const count = shown.find(item => item.id === group.id)?.questions.length ?? 0;
              const isActive = active === group.id && count > 0;
              return (
                <li key={group.id} className="shrink-0">
                  <a
                    href={`#faq-${group.id}`}
                    aria-current={isActive ? 'true' : undefined}
                    className={`group flex items-center gap-3 rounded-2xl px-3 py-2.5 transition ${isActive ? 'bg-brand-50 text-brand-700' : count ? 'text-ink-800 hover:bg-slate-50' : 'pointer-events-none text-ink-400'}`}
                  >
                    <Icon className={`size-5 shrink-0 ${isActive ? 'text-brand-600' : 'text-ink-500'}`} aria-hidden="true" />
                    <span className="min-w-0 flex-1">
                      <span className="block whitespace-nowrap text-sm font-semibold">{group.title}</span>
                      <span className="hidden text-xs text-ink-500 lg:block">{count} question{count > 1 ? 's' : ''}</span>
                    </span>
                    <ArrowRight className={`hidden size-4 transition lg:block ${isActive ? 'translate-x-0 opacity-100' : '-translate-x-1 opacity-0'}`} aria-hidden="true" />
                  </a>
                </li>
              );
            })}
          </ul>
          <div className="mt-3 hidden rounded-2xl bg-gradient-to-br from-brand-50 to-white p-4 ring-1 ring-brand-100 lg:block">
            <p className="flex items-start gap-2.5 text-sm font-semibold text-ink-900">
              <MessageSquareText className="mt-0.5 size-5 shrink-0 text-brand-600" aria-hidden="true" />
              Vous ne trouvez pas votre réponse ?
            </p>
            <Link href="/contact" className="btn-motion mt-3 flex items-center justify-center gap-1.5 rounded-xl border border-brand-200 bg-white px-4 py-2.5 text-sm font-semibold text-brand-700 hover:bg-brand-50">
              Nous contacter <ArrowRight className="size-4" aria-hidden="true" />
            </Link>
          </div>
        </nav>
      </aside>

      <div aria-live="polite" className="min-w-0">
        {query.trim() && (
          <p className="mb-6 flex flex-wrap items-center gap-2 text-sm text-ink-600">
            {total ? `${total} réponse${total > 1 ? 's' : ''} pour « ${query.trim()} »` : `Aucune réponse pour « ${query.trim()} ».`}
            <button type="button" onClick={() => window.dispatchEvent(new CustomEvent('faq:set', {detail: ''}))} className="font-semibold text-brand-700 hover:underline">Tout afficher</button>
          </p>
        )}
        {total === 0 && (
          <div className="rounded-3xl border border-dashed border-slate-300 p-10 text-center">
            <p className="font-semibold text-ink-900">Pas encore de réponse à cette question.</p>
            <p className="mt-1 text-sm text-ink-600">Posez-la nous directement : nous répondons à chaque message.</p>
            <Link href="/contact" className="btn-motion mt-5 inline-flex items-center gap-2 rounded-xl bg-brand-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-brand-700">Nous contacter <ArrowRight className="size-4" aria-hidden="true" /></Link>
          </div>
        )}
        <div className="space-y-12">
          {shown.map((group, index) => {
            const Icon = icons[group.id] ?? MessageSquareText;
            return (
              <section key={group.id} id={`faq-${group.id}`} aria-labelledby={`titre-${group.id}`} className="scroll-mt-24">
                <div className="flex items-center gap-4">
                  <span className="grid size-14 shrink-0 place-items-center rounded-2xl bg-brand-50 text-brand-600 ring-1 ring-brand-100"><Icon className="size-6" aria-hidden="true" /></span>
                  <div>
                    <h2 id={`titre-${group.id}`} className="text-xl font-bold text-ink-900">{group.title}</h2>
                    <p className="text-sm text-ink-500">{group.intro}</p>
                  </div>
                </div>
                <div className="mt-5">
                  <Accordion key={query} items={group.questions} defaultOpen={index === 0 ? 0 : null} />
                </div>
              </section>
            );
          })}
        </div>
      </div>
    </div>
  );
}
