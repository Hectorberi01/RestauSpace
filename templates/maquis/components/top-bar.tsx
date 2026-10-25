'use client';
import {useEffect, useRef} from 'react';

/** Bandeau et en-tête collés en haut ; leur hauteur est publiée dans --top pour les barres collantes des pages. */
export function TopBar({children}: {children: React.ReactNode}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const element = ref.current;
    if (!element) return;
    const update = () => document.documentElement.style.setProperty('--top', `${element.offsetHeight}px`);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return <div ref={ref} className="sticky top-0 z-40">{children}</div>;
}
