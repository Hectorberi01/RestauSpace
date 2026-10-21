import type {Metadata} from 'next';
import {Suspense} from 'react';
import {Unsubscribe} from './unsubscribe';

export const metadata: Metadata = {
  title: 'Désinscription',
  robots: {index: false, follow: false},
  referrer: 'no-referrer',
};

export default function UnsubscribePage() {
  return (
    <section className="bg-white">
      <div className="mx-auto max-w-xl px-4 py-20 sm:py-28">
        <div className="rounded-2xl border border-slate-200 p-6 sm:p-8">
          <Suspense fallback={null}><Unsubscribe /></Suspense>
        </div>
      </div>
    </section>
  );
}
