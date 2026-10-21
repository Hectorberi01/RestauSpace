'use client';
import {useEffect, useState} from 'react';
import {useSearchParams} from 'next/navigation';
import {accentOf, answeredSurveys, errorMessage, loadSurvey, storefront, type Storefront, type Survey} from '@/lib/storefront';
import {Notice, Spinner} from '@/components/order-ui';
import {SurveyForm} from '@/components/survey-form';
import {tr, useLang} from '@/lib/i18n';

const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function SurveyPage({id}: {id: string}) {
  useLang();
  const params = useSearchParams();
  const orderToken = params.get('jeton');
  const location = params.get('etablissement');
  const [survey, setSurvey] = useState<Survey | null>(null);
  const [restaurant, setRestaurant] = useState<{name: string; accent: string} | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [already, setAlready] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setAlready(answeredSurveys().includes(orderToken ? `${id}:${orderToken}` : id));
    loadSurvey(id, controller.signal)
      .then(found => found && setSurvey(found))
      .catch(reason => !controller.signal.aborted && setError(errorMessage(reason)));
    storefront<Storefront>('/', {signal: controller.signal})
      .then(front => front && setRestaurant({name: front.restaurant.name, accent: accentOf(front.restaurant.primaryColor)}))
      .catch(() => {});
    return () => controller.abort();
  }, [id, orderToken]);

  return (
    <div style={{'--accent': restaurant?.accent ?? '#5b3df5'} as React.CSSProperties} className="min-h-screen bg-slate-50">
      <main className="mx-auto max-w-xl px-4 py-10">
        {restaurant && <p className="text-sm font-semibold text-[var(--accent)]">{restaurant.name}</p>}
        {error ? <Notice tone="error">{error}</Notice> : !survey ? <Spinner /> : (
          <>
            <h1 className="mt-1 mb-6 text-2xl font-bold tracking-tight">{survey.title}</h1>
            {already ? <Notice tone="success">{tr('Vous avez déjà répondu. Merci !')}</Notice> : (
              <SurveyForm survey={survey} orderToken={orderToken} locationId={location && ID.test(location) ? location : null} />
            )}
          </>
        )}
      </main>
    </div>
  );
}
