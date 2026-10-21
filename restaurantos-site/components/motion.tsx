'use client';
import {useEffect, useState} from 'react';
import {usePathname} from 'next/navigation';
import {ArrowUp} from 'lucide-react';

/**
 * Animations du site vitrine : apparition des blocs marqués data-reveal (ou de leurs enfants avec data-reveal-stagger)
 * au défilement, barre de progression de lecture et bouton de retour en haut. Rien n'est masqué sans JavaScript ni
 * quand le visiteur a demandé moins d'animations.
 */
export function Motion() {
  const path = usePathname();
  const [progress, setProgress] = useState(0);
  const [far, setFar] = useState(false);

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || !('IntersectionObserver' in window)) return;
    const root = document.documentElement;
    root.classList.add('js-motion');
    const observer = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        entry.target.classList.add('is-visible');
        observer.unobserve(entry.target);
      }
    }, {rootMargin: '0px 0px -8% 0px', threshold: 0.08});
    const scan = () => {
      document.querySelectorAll<HTMLElement>('[data-reveal]:not(.is-visible), [data-reveal-stagger]:not(.is-visible)').forEach(element => {
        if (element.hasAttribute('data-reveal-stagger')) {
          Array.from(element.children).forEach((child, index) => (child as HTMLElement).style.setProperty('--reveal-delay', `${Math.min(index, 8) * 70}ms`));
        }
        observer.observe(element);
      });
    };
    scan();
    const mutations = new MutationObserver(scan);
    mutations.observe(document.getElementById('contenu') ?? document.body, {childList: true, subtree: true});
    return () => {
      observer.disconnect();
      mutations.disconnect();
    };
  }, [path]);

  useEffect(() => {
    let frame = 0;
    const update = () => {
      frame = 0;
      const max = document.documentElement.scrollHeight - window.innerHeight;
      setProgress(max > 0 ? Math.min(1, window.scrollY / max) : 0);
      setFar(window.scrollY > window.innerHeight * 1.2 && window.scrollY + window.innerHeight < document.documentElement.scrollHeight - 160);
    };
    const onScroll = () => {
      if (!frame) frame = requestAnimationFrame(update);
    };
    update();
    window.addEventListener('scroll', onScroll, {passive: true});
    window.addEventListener('resize', onScroll);
    return () => {
      window.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', onScroll);
      cancelAnimationFrame(frame);
    };
  }, [path]);

  return (
    <>
      <div className="pointer-events-none fixed inset-x-0 top-0 z-50 h-0.5" aria-hidden="true">
        <div className="h-full origin-left bg-gradient-to-r from-brand-600 to-violet-500" style={{transform: `scaleX(${progress})`}} />
      </div>
      <button
        type="button"
        onClick={() => window.scrollTo({top: 0, behavior: 'smooth'})}
        aria-label="Revenir en haut de la page"
        tabIndex={far ? 0 : -1}
        className={`fixed bottom-5 right-5 z-40 grid size-11 place-items-center rounded-full bg-white text-brand-700 shadow-lg shadow-brand-900/15 ring-1 ring-slate-200 transition duration-300 hover:-translate-y-0.5 hover:bg-brand-50 ${far ? 'opacity-100' : 'pointer-events-none translate-y-3 opacity-0'}`}
      >
        <ArrowUp className="size-5" aria-hidden="true" />
      </button>
    </>
  );
}
