import type {Metadata} from 'next';
import {Suspense} from 'react';
import {notFound} from 'next/navigation';
import {Spinner} from '@/components/order-ui';
import {SurveyPage} from './survey-page';

type Props = {params: Promise<{id: string}>};

const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const metadata: Metadata = {title: 'Votre avis', robots: {index: false, follow: false}};

/** Questionnaire de satisfaction : lien partagé, QR code en salle, ou proposé après une commande (jeton de suivi). */
export default async function QuestionnairePage({params}: Props) {
  const {id} = await params;
  if (!ID.test(id)) notFound();
  return (
    <Suspense fallback={<Spinner />}>
      <SurveyPage id={id} />
    </Suspense>
  );
}
