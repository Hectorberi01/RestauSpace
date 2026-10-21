import fs from 'node:fs';
import path from 'node:path';
import Image from 'next/image';
import type {ReactNode} from 'react';
import type {FeatureIcon, Module, Visual} from '@/content/features';
import {ModuleIcon} from './icon';

export {ModuleIcon};

/** La photo déposée dans public/photos/<id>.webp, si elle existe. */
export function photoOf(module: Module): string | null {
  const file = path.join(process.cwd(), 'public', 'photos', `${module.photo ?? module.id}.webp`);
  return fs.existsSync(file) ? `/photos/${module.photo ?? module.id}.webp` : null;
}

/** Une capture cadrée : la zone utile remplit le cadre, agrandie si demandé. */
export function Capture({visual, alt, sizes, priority, className = ''}: {visual: Visual; alt: string; sizes: string; priority?: boolean; className?: string}) {
  const focus = visual.focus ?? '50% 0%';
  return (
    <div className={`skeleton relative overflow-hidden ${className}`}>
      <Image
        src={visual.src}
        alt={alt}
        fill
        sizes={sizes}
        priority={priority}
        className="object-cover"
        style={{objectPosition: focus, transform: visual.zoom ? `scale(${visual.zoom})` : undefined, transformOrigin: focus}}
      />
    </div>
  );
}

/** La vignette d'un module : la photo de l'équipe si elle existe, sinon la capture de la console. */
export function ModuleVisual({module, sizes, thumb = false, className = ''}: {module: Module; sizes: string; thumb?: boolean; className?: string}) {
  const photo = photoOf(module);
  return photo
    ? <Capture visual={{src: photo, focus: '50% 50%'}} alt={module.title} sizes={sizes} className={className} />
    : <Capture visual={thumb ? module.thumb : module.visual} alt={`Écran ${module.title} de RestauSpace`} sizes={sizes} className={`bg-slate-50 ${className}`} />;
}

export function BrowserFrame({children, label, className = ''}: {children: ReactNode; label?: string; className?: string}) {
  return (
    <div className={`overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-2xl shadow-brand-900/15 ring-1 ring-black/5 ${className}`}>
      <div className="flex items-center gap-1.5 border-b border-slate-100 bg-slate-50/80 px-3.5 py-2.5">
        <span className="size-2.5 rounded-full bg-[#ff5f57]" />
        <span className="size-2.5 rounded-full bg-[#febc2e]" />
        <span className="size-2.5 rounded-full bg-[#28c840]" />
        {label && <span className="ml-3 truncate rounded-md bg-white px-3 py-0.5 text-[0.7rem] text-ink-500 ring-1 ring-slate-200">{label}</span>}
      </div>
      {children}
    </div>
  );
}

export function TabletFrame({children, className = ''}: {children: ReactNode; className?: string}) {
  return (
    <div className={`rounded-[1.6rem] bg-ink-900 p-2.5 shadow-2xl shadow-ink-900/30 ring-1 ring-black/40 ${className}`}>
      <div className="overflow-hidden rounded-[1.1rem] bg-white">{children}</div>
    </div>
  );
}

export function PhoneFrame({children, className = ''}: {children: ReactNode; className?: string}) {
  return (
    <div className={`rounded-[2.2rem] bg-ink-900 p-2 shadow-2xl shadow-ink-900/30 ring-1 ring-black/40 ${className}`}>
      <div className="relative overflow-hidden rounded-[1.7rem] bg-white">
        <span className="absolute left-1/2 top-1.5 z-10 h-4 w-14 -translate-x-1/2 rounded-full bg-ink-900" aria-hidden="true" />
        {children}
      </div>
    </div>
  );
}

/** Le tableau de bord, la cuisine sur tablette et le site de commande sur téléphone. */
export function ProductComposition({compact = false}: {compact?: boolean}) {
  return (
    <div className="relative">
      <div className="absolute -inset-8 -z-10 rounded-[3rem] bg-gradient-to-br from-brand-200/50 via-white to-sky-100/70 blur-2xl" aria-hidden="true" />
      <BrowserFrame label="console.restauspace.app" className={compact ? '' : 'lg:ml-10'}>
        <Capture
          visual={{src: '/captures/hero-dashboard.webp', focus: '0% 0%'}}
          alt="Tableau de bord de RestauSpace : chiffre d’affaires, commandes, alertes de stock et avis"
          sizes="(min-width: 1024px) 640px, 100vw"
          priority
          className="aspect-[16/10]"
        />
      </BrowserFrame>
      {!compact && (
        <TabletFrame className="animate-float absolute -bottom-14 -left-2 hidden w-[52%] -rotate-2 sm:block lg:-left-8">
          <Capture
            visual={{src: '/captures/hero-kitchen.webp', focus: '0% 0%'}}
            alt="Écran cuisine : tickets à préparer, en cours et prêts"
            sizes="(min-width: 1024px) 340px, 50vw"
            className="aspect-[16/10]"
          />
        </TabletFrame>
      )}
      <PhoneFrame className="animate-float absolute -bottom-16 right-2 w-[27%] min-w-28 rotate-3 [--float-delay:-3s] sm:-right-4">
        <Capture
          visual={{src: '/captures/hero-phone.webp', focus: '50% 0%'}}
          alt="Site de commande du restaurant sur téléphone"
          sizes="(min-width: 1024px) 180px, 30vw"
          className="aspect-[9/19]"
        />
      </PhoneFrame>
    </div>
  );
}

/** Une pastille d'icône flottante autour des écrans du héros. */
function FloatingTile({children, className, delay}: {children: ReactNode; className: string; delay: string}) {
  return (
    <span aria-hidden="true" style={{'--float-delay': delay} as React.CSSProperties} className={`animate-float absolute z-20 grid size-14 place-items-center rounded-2xl bg-white/90 shadow-xl shadow-brand-900/15 ring-1 ring-white backdrop-blur sm:size-16 ${className}`}>
      {children}
    </span>
  );
}

/** Le tableau de bord sur un ordinateur portable, le site de commande sur téléphone, et des pastilles flottantes. */
export function HeroDevices({tiles}: {tiles: ReactNode[]}) {
  return (
    <div className="relative mx-auto w-full max-w-2xl pb-6 pl-10 sm:pl-16 lg:max-w-none">
      <div className="absolute -inset-10 -z-10 rounded-[3rem] bg-gradient-to-br from-brand-300/40 via-white/0 to-sky-200/40 blur-3xl" aria-hidden="true" />
      <div className="lg:[transform:perspective(2200px)_rotateY(-9deg)_rotateX(3deg)]">
        <div className="rounded-t-[1.4rem] bg-ink-900 p-2.5 pb-3 shadow-2xl shadow-brand-900/30 ring-1 ring-black/40 sm:p-3">
          <div className="overflow-hidden rounded-lg bg-white">
            <Capture visual={{src: '/captures/hero-dashboard.webp', focus: '0% 0%'}} alt="Tableau de bord de RestauSpace : chiffre d’affaires, commandes, alertes et réservations" sizes="(min-width: 1024px) 720px, 100vw" priority className="aspect-[16/10]" />
          </div>
        </div>
        <div className="relative -mx-[4%] h-3 rounded-b-xl bg-gradient-to-b from-slate-300 to-slate-400 shadow-xl shadow-ink-900/20 sm:h-4">
          <span className="absolute left-1/2 top-0 h-1.5 w-[16%] -translate-x-1/2 rounded-b-lg bg-slate-500/40" />
        </div>
      </div>
      <PhoneFrame className="animate-float absolute -bottom-2 left-0 z-10 w-[24%] min-w-24 -rotate-3 [--float-delay:-2s]">
        <Capture visual={{src: '/captures/hero-phone.webp', focus: '50% 0%'}} alt="Site de commande du restaurant sur téléphone" sizes="(min-width: 1024px) 180px, 25vw" className="aspect-[9/19]" />
      </PhoneFrame>
      {tiles[0] && <FloatingTile className="-top-6 left-[22%]" delay="-1s">{tiles[0]}</FloatingTile>}
      {tiles[1] && <FloatingTile className="-right-3 top-[22%] sm:-right-6" delay="-3s">{tiles[1]}</FloatingTile>}
      {tiles[2] && <FloatingTile className="-right-1 bottom-[22%] hidden sm:-right-4 sm:grid" delay="-4.5s">{tiles[2]}</FloatingTile>}
    </div>
  );
}
