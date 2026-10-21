import type {ProductStatus} from '@/lib/types';

export const statusLabel: Record<ProductStatus, string> = {Draft: 'Brouillon', Active: 'Actif', Unavailable: 'Indisponible', Archived: 'Archivé'};

export const statusTone: Record<ProductStatus, 'neutral' | 'green' | 'amber' | 'red'> = {Draft: 'neutral', Active: 'green', Unavailable: 'amber', Archived: 'red'};

export const statusDot: Record<ProductStatus, string> = {Draft: 'bg-slate-400', Active: 'bg-emerald-500', Unavailable: 'bg-amber-500', Archived: 'bg-red-500'};
