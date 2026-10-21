'use client';
import {useEffect, useRef, useState} from 'react';

/** Un chiffre clé qui compte jusqu'à sa valeur quand il entre à l'écran ; le texte autour du nombre est conservé. */
export function CountUp({value, duration = 1400}: {value: string; duration?: number}) {
  const match = value.match(/^(\D*)(\d(?:[\d\s.,]*\d)?)(.*)$/);
  const target = match ? Number(match[2].replace(/[\s.]/g, '').replace(',', '.')) : NaN;
  const [shown, setShown] = useState(value);
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!match || Number.isNaN(target) || window.matchMedia('(prefers-reduced-motion: reduce)').matches || !ref.current) return;
    const decimals = (match[2].split(',')[1] ?? '').length;
    const format = (n: number) => `${match[1]}${new Intl.NumberFormat('fr-FR', {minimumFractionDigits: decimals, maximumFractionDigits: decimals}).format(n)}${match[3]}`;
    setShown(format(0));
    let frame = 0;
    const observer = new IntersectionObserver(([entry]) => {
      if (!entry.isIntersecting) return;
      observer.disconnect();
      const start = performance.now();
      frame = requestAnimationFrame(function tick(now) {
        const t = Math.min(1, (now - start) / duration);
        setShown(format(target * (1 - Math.pow(1 - t, 3))));
        if (t < 1) frame = requestAnimationFrame(tick);
        else setShown(value);
      });
    }, {threshold: 0.4});
    observer.observe(ref.current);
    return () => {
      observer.disconnect();
      cancelAnimationFrame(frame);
    };
  }, [value]);

  return <span ref={ref} className="tabular-nums" aria-label={value}>{shown}</span>;
}
