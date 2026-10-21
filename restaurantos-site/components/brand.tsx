import Link from 'next/link';
import {UtensilsCrossed} from 'lucide-react';

export function Brand({inverted = false}: {inverted?: boolean}) {
  return (
    <Link href="/" className="flex items-center gap-2.5 font-semibold tracking-tight" aria-label="RestauSpace, accueil">
      <span className="grid size-9 place-items-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow-sm">
        <UtensilsCrossed className="size-5" aria-hidden="true" />
      </span>
      <span className={`text-lg ${inverted ? 'text-white' : 'text-ink-900'}`}>
        Restau<span className={inverted ? 'text-brand-200' : 'text-brand-600'}>Space</span>
      </span>
    </Link>
  );
}
