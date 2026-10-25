'use client';
import {useEffect, useLayoutEffect, useRef, useState} from 'react';
import {ArrowLeft, ArrowRight, ExternalLink, MousePointerClick, RotateCcw} from 'lucide-react';
import {Segmented} from '@/components/segmented';

type Device = 'desktop' | 'tablet' | 'mobile';

const devices: Record<Device, {label: string; width: number; height: number}> = {
  desktop: {label: 'Ordinateur', width: 1280, height: 800},
  tablet: {label: 'Tablette', width: 820, height: 1180},
  mobile: {label: 'Mobile', width: 390, height: 844},
};

// Bordures du cadre (chrome du navigateur ou contour de l'appareil), en pixels affichés.
const chrome: Record<Device, {x: number; y: number}> = {
  desktop: {x: 1, y: 40},
  tablet: {x: 24, y: 24},
  mobile: {x: 20, y: 20},
};

export function DemoViewer({name, demoUrl, steps}: {name: string; demoUrl: string; steps: {title: string; text: string; path?: string}[]}) {
  const [device, setDevice] = useState<Device>('desktop');
  const [box, setBox] = useState<{width: number; maxHeight: number} | null>(null);
  const [step, setStep] = useState(0);
  const area = useRef<HTMLDivElement>(null);
  const frame = useRef<HTMLIFrameElement>(null);

  useEffect(() => {
    if (window.matchMedia('(max-width: 767px)').matches) setDevice('mobile');
  }, []);

  useLayoutEffect(() => {
    const element = area.current;
    if (!element) return;
    const measure = () => setBox({width: element.clientWidth, maxHeight: Math.max(480, Math.min(860, window.innerHeight - 120))});
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    window.addEventListener('resize', measure);
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', measure);
    };
  }, []);

  // Navigation de l'iframe sans la recréer : le panier de la démo est conservé.
  const open = (path = '') => {
    if (frame.current) frame.current.src = `${demoUrl}${path}`;
  };

  const size = devices[device];
  const edge = chrome[device];
  const scale = box ? Math.min(1, (box.width - edge.x * 2) / size.width, (box.maxHeight - edge.y * 2) / size.height) : 0;
  const current = steps[step];

  return (
    <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_21rem]">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Segmented
            label="Taille de l’écran"
            options={(Object.keys(devices) as Device[]).map(value => ({value, label: devices[value].label}))}
            value={device}
            onChange={setDevice}
          />
          <div className="flex items-center gap-2">
            <button type="button" onClick={() => open()} className="btn-motion inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-sm font-semibold text-ink-800 hover:border-slate-300 hover:bg-slate-50">
              <RotateCcw className="size-4" aria-hidden="true" /> Recommencer
            </button>
            <a href={demoUrl} target="_blank" rel="noopener noreferrer" className="btn-motion inline-flex items-center gap-2 rounded-xl border border-brand-200 bg-white px-3.5 py-2 text-sm font-semibold text-brand-700 hover:border-brand-400 hover:bg-brand-50">
              <ExternalLink className="size-4" aria-hidden="true" /> <span className="sm:hidden">Nouvel onglet</span><span className="hidden sm:inline">Ouvrir dans un nouvel onglet</span>
            </a>
          </div>
        </div>

        <div ref={area} className="mt-5 flex justify-center rounded-3xl bg-gradient-to-br from-slate-100 via-slate-50 to-brand-50/60 px-3 py-6 ring-1 ring-slate-200/70 sm:px-6 sm:py-8">
          {box ? (
            <div
              className={`transition-[width,height] duration-300 ${device === 'desktop' ? 'overflow-hidden rounded-xl bg-white shadow-2xl shadow-brand-900/15 ring-1 ring-slate-200' : `bg-ink-900 shadow-2xl shadow-ink-900/30 ring-1 ring-black/40 ${device === 'tablet' ? 'rounded-[1.8rem]' : 'rounded-[2.4rem]'}`}`}
              style={{width: size.width * scale + edge.x * 2, padding: device === 'desktop' ? 0 : `${edge.y}px ${edge.x}px`}}
            >
              {device === 'desktop' && (
                <div className="flex h-[38px] items-center gap-1.5 border-b border-slate-100 bg-slate-50/80 px-3.5">
                  <span className="size-2.5 rounded-full bg-[#ff5f57]" />
                  <span className="size-2.5 rounded-full bg-[#febc2e]" />
                  <span className="size-2.5 rounded-full bg-[#28c840]" />
                  <span className="ml-3 truncate rounded-md bg-white px-3 py-0.5 text-[0.7rem] text-ink-500 ring-1 ring-slate-200">{demoUrl.replace(/^https?:\/\//, '')}</span>
                </div>
              )}
              <div className={`relative overflow-hidden bg-white ${device === 'desktop' ? 'mx-px mb-px' : device === 'tablet' ? 'rounded-[0.9rem]' : 'rounded-[1.6rem]'}`} style={{width: size.width * scale, height: size.height * scale}}>
                <iframe
                  ref={frame}
                  src={demoUrl}
                  title={`Démonstration du modèle ${name}`}
                  sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-popups-to-escape-sandbox allow-top-navigation-by-user-activation"
                  referrerPolicy="strict-origin-when-cross-origin"
                  className="absolute left-0 top-0 origin-top-left border-0 bg-white"
                  style={{width: size.width, height: size.height, transform: `scale(${scale})`}}
                />
              </div>
            </div>
          ) : (
            <div className="skeleton aspect-[16/10] w-full rounded-xl" />
          )}
        </div>
        <p className="mt-3 text-center text-xs text-ink-500">
          Démonstration : restaurant fictif, aucune commande n’est réelle et le paiement est simulé.
          {scale > 0 && scale < 1 && ` Affichage réduit (${Math.round(scale * 100)} %).`}
        </p>
      </div>

      <aside aria-labelledby="parcours-titre" className="lg:sticky lg:top-20 lg:self-start">
        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <div className="flex items-center gap-3">
            <span className="grid size-10 place-items-center rounded-xl bg-brand-50 text-brand-600 ring-1 ring-brand-100"><MousePointerClick className="size-5" aria-hidden="true" /></span>
            <div>
              <h2 id="parcours-titre" className="font-bold text-ink-900">Parcours guidé</h2>
              <p className="text-xs text-ink-500">Étape {step + 1} sur {steps.length}</p>
            </div>
          </div>
          <ol className="mt-5 space-y-1.5">
            {steps.map((item, index) => {
              const active = index === step;
              const done = index < step;
              return (
                <li key={item.title}>
                  <button
                    type="button"
                    onClick={() => setStep(index)}
                    aria-current={active ? 'step' : undefined}
                    className={`flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left transition ${active ? 'bg-brand-50 ring-1 ring-brand-100' : 'hover:bg-slate-50'}`}
                  >
                    <span className={`grid size-7 shrink-0 place-items-center rounded-full text-xs font-bold transition ${active ? 'bg-brand-600 text-white shadow-md shadow-brand-600/30' : done ? 'bg-brand-100 text-brand-700' : 'bg-slate-100 text-ink-600'}`}>{index + 1}</span>
                    <span className="min-w-0">
                      <span className={`block text-sm font-semibold ${active ? 'text-brand-700' : 'text-ink-900'}`}>{item.title}</span>
                      {active && <span className="mt-1 block text-sm leading-relaxed text-ink-600">{item.text}</span>}
                    </span>
                  </button>
                  {active && item.path && (
                    <button type="button" onClick={() => open(item.path)} className="ml-13 mt-1 flex items-center gap-1.5 text-left text-sm font-semibold text-brand-700 hover:underline">
                      Afficher cette page dans la démo <ArrowRight className="size-3.5" aria-hidden="true" />
                    </button>
                  )}
                </li>
              );
            })}
          </ol>
          <div className="mt-5 flex items-center justify-between gap-2 border-t border-slate-100 pt-4">
            <button type="button" disabled={step === 0} onClick={() => setStep(step - 1)} className="inline-flex items-center gap-1.5 rounded-xl px-3 py-2 text-sm font-semibold text-ink-700 transition hover:bg-slate-100 disabled:pointer-events-none disabled:opacity-40">
              <ArrowLeft className="size-4" aria-hidden="true" /> Précédente
            </button>
            <button type="button" disabled={step === steps.length - 1} onClick={() => setStep(step + 1)} className="btn-motion inline-flex items-center gap-1.5 rounded-xl bg-brand-600 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-brand-600/25 hover:bg-brand-700 disabled:pointer-events-none disabled:opacity-40">
              Suivante <ArrowRight className="size-4" aria-hidden="true" />
            </button>
          </div>
        </div>
        {current && <p className="sr-only" aria-live="polite">Étape {step + 1} : {current.title}</p>}
      </aside>
    </div>
  );
}
