'use client';
import {useId} from 'react';
import {cx} from './ui';

// Illustrations du modèle : motif « wax » et assiettes dessinées, utilisées quand le restaurant n'a pas de photo.

export function Wax({className}: {className?: string}) {
  const id = useId().replace(/:/g, '');
  return (
    <svg className={cx('absolute inset-0 size-full', className)} aria-hidden="true" preserveAspectRatio="xMidYMid slice">
      <defs>
        <pattern id={`wax-${id}`} width="120" height="120" patternUnits="userSpaceOnUse">
          <circle cx="60" cy="60" r="40" fill="none" stroke="var(--accent)" strokeWidth="10" opacity=".55" />
          <circle cx="60" cy="60" r="16" fill="var(--primary)" opacity=".45" />
          <path d="M0 0 L30 0 L0 30Z M120 120 L90 120 L120 90Z M120 0 L120 30 L90 0Z M0 120 L0 90 L30 120Z" fill="var(--dark)" opacity=".35" />
        </pattern>
      </defs>
      <rect width="100%" height="100%" fill={`url(#wax-${id})`} />
    </svg>
  );
}

/** Une assiette dessinée ; `variant` choisit le plat (0 à 3). */
export function Dish({variant = 0, size = 140, className}: {variant?: number; size?: number; className?: string}) {
  const id = useId().replace(/:/g, '');
  const g = (name: string) => `url(#${name}-${id})`;
  const kind = ((variant % 4) + 4) % 4;
  return (
    <svg width={size} height={size} viewBox="-90 -90 180 180" className={className} aria-hidden="true">
      <defs>
        <radialGradient id={`plate-${id}`} cx="50%" cy="45%" r="55%"><stop offset="0" stopColor="#ffffff" /><stop offset=".8" stopColor="#f6efe6" /><stop offset="1" stopColor="#e3d5c4" /></radialGradient>
        <radialGradient id={`brown-${id}`} cx="40%" cy="35%" r="65%"><stop offset="0" stopColor="#e4a14c" /><stop offset=".7" stopColor="#b8661f" /><stop offset="1" stopColor="#7d3d10" /></radialGradient>
        <radialGradient id={`orange-${id}`} cx="40%" cy="35%" r="65%"><stop offset="0" stopColor="#f08a43" /><stop offset="1" stopColor="#c55419" /></radialGradient>
        <radialGradient id={`cream-${id}`} cx="40%" cy="35%" r="65%"><stop offset="0" stopColor="#fff6e3" /><stop offset="1" stopColor="#e8d3a6" /></radialGradient>
        <radialGradient id={`red-${id}`} cx="45%" cy="40%" r="60%"><stop offset="0" stopColor="#e2521f" /><stop offset="1" stopColor="#a5300f" /></radialGradient>
        <radialGradient id={`green-${id}`} cx="45%" cy="40%" r="60%"><stop offset="0" stopColor="#7f9b3a" /><stop offset="1" stopColor="#4b6a22" /></radialGradient>
      </defs>
      <circle r="84" cy="5" fill="#00000014" />
      <circle r="80" fill={g('plate')} />
      <circle r="62" fill="none" stroke="#e8d9c6" strokeWidth="2" />
      {kind === 0 && (
        <>
          <ellipse cx="-12" cy="-6" rx="40" ry="30" fill={g('brown')} transform="rotate(-20)" />
          <ellipse cx="22" cy="14" rx="26" ry="18" fill={g('brown')} transform="rotate(25)" />
          <circle cx="30" cy="-30" r="9" fill="#f4d35e" /><circle cx="40" cy="-18" r="7" fill="#e4572e" />
          <path d="M-40 30 q10 -12 20 0 q10 -12 20 0" stroke="#3f7d3a" strokeWidth="5" fill="none" strokeLinecap="round" />
        </>
      )}
      {kind === 1 && (
        <>
          <ellipse cx="-14" cy="4" rx="38" ry="32" fill={g('orange')} />
          <ellipse cx="26" cy="-14" rx="22" ry="16" fill={g('brown')} transform="rotate(30)" />
          <circle cx="24" cy="26" r="12" fill="#f6f1e4" /><circle cx="24" cy="26" r="5" fill="#f4b23c" />
          <path d="M-50 -30 l12 6 M-44 -40 l10 8" stroke="#3f7d3a" strokeWidth="4" strokeLinecap="round" />
        </>
      )}
      {kind === 2 && (
        <>
          <circle cx="-18" cy="-4" r="30" fill={g('cream')} />
          <path d="M8 -38 a40 40 0 0 1 0 76 a26 38 0 0 0 0 -76z" fill={g('red')} />
          <ellipse cx="26" cy="2" rx="18" ry="9" fill={g('brown')} transform="rotate(-30)" />
          <circle cx="-34" cy="30" r="5" fill="#e4572e" />
        </>
      )}
      {kind === 3 && (
        <>
          <circle cx="-20" cy="-8" r="28" fill={g('cream')} />
          <ellipse cx="22" cy="10" rx="34" ry="30" fill={g('green')} />
          <g fill="#a9c45b"><circle cx="16" cy="2" r="5" /><circle cx="30" cy="16" r="5" /><circle cx="14" cy="22" r="4" /><circle cx="34" cy="-4" r="4" /></g>
        </>
      )}
    </svg>
  );
}

export const pastel = ['#fbe2c8', '#fde8d2', '#fbdccd', '#e9efd8'];

/** Photo du plat dans une assiette ronde, ou une assiette dessinée. */
export function Plate({src, index, size, className, muted}: {src: string | null | undefined; index: number; size: number; className?: string; muted?: boolean}) {
  if (src) {
    return (
      // eslint-disable-next-line @next/next/no-img-element
      <img src={src} alt="" loading="lazy" width={size} height={size}
        className={cx('shrink-0 rounded-full border-[6px] border-white bg-white object-cover shadow-[0_8px_18px_rgba(90,44,16,.18)]', muted && 'grayscale', className)}
        style={{width: size, height: size}} />
    );
  }
  return <Dish variant={index} size={size} className={cx('shrink-0', muted && 'grayscale', className)} />;
}
