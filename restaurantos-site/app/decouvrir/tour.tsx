'use client';
import {useCallback, useEffect, useRef, useState} from 'react';
import Image from 'next/image';
import Link from 'next/link';
import {ArrowLeft, ArrowRight, CircleCheck, Pause, Play} from 'lucide-react';
import {FeatureIconView} from '@/components/icon';
import {Segmented} from '@/components/segmented';
import type {FeatureIcon} from '@/content/features';

export interface Journey {
  id: string;
  title: string;
  intro: string;
  steps: {id: string; moment: string; title: string; icon: FeatureIcon; summary: string; points: string[]; src: string; portrait: boolean}[];
}

const duration = 7000;

export function Tour({journeys}: {journeys: Journey[]}) {
  const [journeyIndex, setJourneyIndex] = useState(0);
  const [step, setStep] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [held, setHeld] = useState(false);
  const [visible, setVisible] = useState(true);
  const [elapsed, setElapsed] = useState(0);
  const root = useRef<HTMLDivElement>(null);
  const journey = journeys[journeyIndex];
  const current = journey.steps[step];
  const running = playing && !held && visible;

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) setPlaying(false);
    const observer = new IntersectionObserver(([entry]) => setVisible(entry.isIntersecting), {threshold: 0.25});
    if (root.current) observer.observe(root.current);
    const onVisibility = () => setVisible(document.visibilityState === 'visible');
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      observer.disconnect();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, []);

  const go = useCallback((next: number) => {
    setStep((next + journey.steps.length) % journey.steps.length);
    setElapsed(0);
  }, [journey.steps.length]);

  useEffect(() => {
    if (!running) return;
    let last = performance.now();
    let frame = requestAnimationFrame(function tick(now) {
      setElapsed(value => {
        const total = value + (now - last);
        return total;
      });
      last = now;
      frame = requestAnimationFrame(tick);
    });
    return () => cancelAnimationFrame(frame);
  }, [running]);

  useEffect(() => {
    if (elapsed < duration) return;
    if (step < journey.steps.length - 1) {
      go(step + 1);
    } else {
      setJourneyIndex(index => (index + 1) % journeys.length);
      setStep(0);
      setElapsed(0);
    }
  }, [elapsed, step, journey.steps.length, journeys.length, go]);

  const choose = (index: number) => {
    setJourneyIndex(index);
    setStep(0);
    setElapsed(0);
  };

  return (
    <div ref={root} onMouseEnter={() => setHeld(true)} onMouseLeave={() => setHeld(false)} onFocus={() => setHeld(true)} onBlur={() => setHeld(false)}>
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Segmented role="tab" label="Parcours" controls="visite" value={journey.id} onChange={id => choose(journeys.findIndex(item => item.id === id))} options={journeys.map(item => ({value: item.id, label: item.title}))} />
        <div className="flex items-center gap-2">
          <p className="mr-2 hidden text-sm text-ink-500 sm:block">{journey.intro}</p>
          <button type="button" onClick={() => go(step - 1)} aria-label="Étape précédente" className="grid size-10 place-items-center rounded-xl border border-slate-200 bg-white text-ink-700 transition hover:border-brand-200 hover:text-brand-700">
            <ArrowLeft className="size-4" aria-hidden="true" />
          </button>
          <button type="button" onClick={() => setPlaying(value => !value)} aria-label={playing ? 'Mettre la visite en pause' : 'Lancer la visite'} className="grid size-10 place-items-center rounded-xl bg-brand-600 text-white shadow-md shadow-brand-600/25 transition hover:bg-brand-700">
            {playing ? <Pause className="size-4" aria-hidden="true" /> : <Play className="size-4" aria-hidden="true" />}
          </button>
          <button type="button" onClick={() => go(step + 1)} aria-label="Étape suivante" className="grid size-10 place-items-center rounded-xl border border-slate-200 bg-white text-ink-700 transition hover:border-brand-200 hover:text-brand-700">
            <ArrowRight className="size-4" aria-hidden="true" />
          </button>
        </div>
      </div>

      <div id="visite" role="tabpanel" className="mt-8 grid gap-8 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <ol className="flex gap-3 overflow-x-auto pb-2 lg:flex-col lg:overflow-visible lg:pb-0">
          {journey.steps.map((item, index) => {
            const active = index === step;
            const fill = active ? Math.min(1, elapsed / duration) : index < step ? 1 : 0;
            return (
              <li key={`${journey.id}-${item.id}`} className="min-w-60 lg:min-w-0">
                <button
                  type="button"
                  onClick={() => go(index)}
                  aria-current={active ? 'step' : undefined}
                  className={`group relative w-full overflow-hidden rounded-2xl border p-4 text-left transition ${active ? 'border-brand-200 bg-brand-50/60 shadow-md shadow-brand-900/5' : 'border-slate-200 bg-white hover:border-brand-200'}`}
                >
                  <span className="flex items-center gap-3">
                    <span className={`grid size-9 shrink-0 place-items-center rounded-xl transition ${active ? 'bg-brand-600 text-white shadow-md shadow-brand-600/30' : 'bg-slate-100 text-ink-500 group-hover:text-brand-700'}`}>
                      <FeatureIconView name={item.icon} />
                    </span>
                    <span className="min-w-0">
                      <span className="block text-xs font-semibold uppercase tracking-wider text-ink-500">Étape {index + 1} · {item.title}</span>
                      <span className={`mt-0.5 block text-sm font-semibold leading-snug ${active ? 'text-ink-900' : 'text-ink-700'}`}>{item.moment}</span>
                    </span>
                  </span>
                  <span className="absolute inset-x-0 bottom-0 h-0.5 bg-slate-100" aria-hidden="true">
                    <span className="block h-full origin-left bg-brand-600" style={{transform: `scaleX(${fill})`}} />
                  </span>
                </button>
              </li>
            );
          })}
        </ol>

        <div className="relative">
          <div className="absolute -inset-6 -z-10 rounded-[2.5rem] bg-gradient-to-br from-brand-200/40 via-white to-sky-100/60 blur-2xl" aria-hidden="true" />
          <div className="overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-2xl shadow-brand-900/15 ring-1 ring-black/5">
            <div className="flex items-center gap-1.5 border-b border-slate-100 bg-slate-50/80 px-3.5 py-2.5">
              <span className="size-2.5 rounded-full bg-[#ff5f57]" />
              <span className="size-2.5 rounded-full bg-[#febc2e]" />
              <span className="size-2.5 rounded-full bg-[#28c840]" />
              <span className="ml-3 truncate rounded-md bg-white px-3 py-0.5 text-[0.7rem] text-ink-500 ring-1 ring-slate-200">RestauSpace · {current.title}</span>
            </div>
            <div className="relative aspect-[1536/1104] bg-gradient-to-br from-slate-50 to-brand-50">
              {journey.steps.map((item, index) => (
                <Image
                  key={`${journey.id}-${item.id}`}
                  src={item.src}
                  alt={index === step ? `Écran ${item.title} de RestauSpace` : ''}
                  fill
                  sizes={item.portrait ? '320px' : '(min-width: 1024px) 860px, 100vw'}
                  priority={journeyIndex === 0 && index === 0}
                  className={`transition duration-700 ease-out ${item.portrait ? 'object-contain py-6 drop-shadow-xl' : 'object-cover object-left-top'} ${index === step ? 'scale-100 opacity-100' : 'scale-[1.03] opacity-0'}`}
                />
              ))}
            </div>
          </div>
          <div key={`${journey.id}-${current.id}`} className="animate-rise relative z-10 mx-4 -mt-10 rounded-2xl bg-white/95 p-5 shadow-xl shadow-brand-900/10 ring-1 ring-slate-200/80 backdrop-blur sm:mx-8 lg:absolute lg:-bottom-8 lg:right-6 lg:mx-0 lg:mt-0 lg:w-[22rem]" aria-live="polite">
            <p className="text-xs font-semibold uppercase tracking-wider text-brand-600">{current.title}</p>
            <p className="mt-1.5 text-sm leading-relaxed text-ink-700">{current.summary}</p>
            <ul className="mt-3 space-y-1.5">
              {current.points.map(point => (
                <li key={point} className="flex gap-2 text-[0.8rem] text-ink-600"><CircleCheck className="mt-0.5 size-3.5 shrink-0 text-brand-600" aria-hidden="true" />{point}</li>
              ))}
            </ul>
            <Link href={`/fonctionnalites#${current.id}`} className="mt-3 inline-flex items-center gap-1 text-sm font-semibold text-brand-700 hover:underline">
              Tout sur ce module <ArrowRight className="size-3.5" aria-hidden="true" />
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
