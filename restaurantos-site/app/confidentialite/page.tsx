import type {Metadata} from 'next';
import {PageHero} from '@/components/section';
import {site} from '@/content/site';

export const metadata: Metadata = {title: 'Confidentialité', alternates: {canonical: '/confidentialite'}};

const sections: {title: string; text: string}[] = [
  {
    title: 'Ce que ce site collecte',
    text: 'Uniquement ce que vous saisissez dans le formulaire de demande de démonstration. Ce site ne dépose aucun cookie, n’utilise aucun outil de mesure d’audience ni de publicité, et ne charge aucune ressource d’un site tiers (les polices sont servies par notre propre serveur).',
  },
  {
    title: 'Le formulaire de contact',
    text: 'Votre demande (nom, restaurant, pays, email ou téléphone, plan qui vous intéresse, message) est enregistrée sur nos serveurs et n’est consultable que par notre équipe. Elle sert uniquement à vous répondre et à organiser une démonstration : elle n’est ni revendue, ni utilisée pour de la publicité. Votre adresse IP est utilisée quelques instants, sans être enregistrée, pour limiter les envois abusifs.',
  },
  {
    title: 'Durée de conservation',
    text: 'Votre demande est conservée le temps de la traiter, puis au plus trois ans après notre dernier échange si vous ne devenez pas client. Vous pouvez en demander la suppression à tout moment.',
  },
  {
    title: 'Vos droits',
    text: 'Vous pouvez demander l’accès à vos données, leur correction ou leur suppression, et vous opposer à leur utilisation, en nous écrivant. En France, vous pouvez aussi saisir la CNIL ; au Bénin, l’APDP.',
  },
  {
    title: 'L’application RestauSpace',
    text: 'Les données traitées dans l’application par nos clients restaurateurs (commandes, clients, réservations) font l’objet de conditions contractuelles distinctes.',
  },
];

export default function Privacy() {
  return (
    <>
      <PageHero eyebrow="Informations" title="Confidentialité" intro={`Comment ${site.legal.company} traite les données des visiteurs de ce site.`} />
      <section className="bg-white">
        <div className="mx-auto max-w-3xl space-y-8 px-4 pb-20 sm:px-6">
          {sections.map(section => (
            <div key={section.title}>
              <h2 className="text-lg font-semibold">{section.title}</h2>
              <p className="mt-2 leading-relaxed text-ink-600">{section.text}</p>
            </div>
          ))}
          <p className="text-sm text-ink-500">Contact : {site.contact.email || '[à compléter]'}</p>
        </div>
      </section>
    </>
  );
}
