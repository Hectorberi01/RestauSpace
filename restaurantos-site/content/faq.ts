import {trialDays} from './plans';

export interface Question {
  q: string;
  a: string;
}

export interface QuestionGroup {
  id: string;
  title: string;
  intro: string;
  questions: Question[];
}

export const faq: QuestionGroup[] = [
  {
    id: 'demarrer',
    title: 'Démarrer',
    intro: 'Tout pour bien commencer avec RestauSpace.',
    questions: [
      {
        q: 'Comment commencer ?',
        a: `Choisissez votre plan sur la page Tarifs et créez votre compte en deux minutes : votre restaurant est prêt dès la confirmation de votre email, et vous profitez de ${trialDays} jours d’essai avant de payer quoi que ce soit, en ligne depuis la console. Vous préférez être accompagné ? Demandez une démonstration.`,
      },
      {
        q: 'Faut-il installer un logiciel ?',
        a: 'Non. RestauSpace fonctionne dans le navigateur, sur ordinateur, tablette ou téléphone. Une connexion internet est nécessaire.',
      },
      {
        q: 'Puis-je importer mon menu existant ?',
        a: 'Oui : nous vous accompagnons pour saisir vos produits, variantes, options et prix au démarrage.',
      },
    ],
  },
  {
    id: 'tarifs',
    title: 'Tarifs et abonnement',
    intro: 'Nos plans, les limites et la facturation.',
    questions: [
      {
        q: 'Quelle différence entre les plans ?',
        a: 'Tous les modules sont inclus dans tous les plans. Les plans diffèrent par les volumes : nombre d’établissements, de membres de l’équipe, de commandes par mois et de messages envoyés.',
      },
      {
        q: 'Que se passe-t-il si je dépasse une limite ?',
        a: 'L’action concernée est refusée avec un message clair (par exemple, une nouvelle commande au-delà du plafond mensuel). Vous pouvez passer au plan supérieur à tout moment.',
      },
      {
        q: 'Puis-je changer de plan ou résilier ?',
        a: 'Oui. Le changement de plan est possible pendant l’essai et pendant une période payée. La résiliation prend effet à la fin de la période en cours.',
      },
      {
        q: 'Dans quelle devise suis-je facturé ?',
        a: 'Dans la devise de votre restaurant : francs CFA au Bénin et en zone UEMOA, euros hors taxes en France.',
      },
    ],
  },
  {
    id: 'fonctionnalites',
    title: 'Fonctionnalités',
    intro: 'Ce que RestauSpace fait pour votre restaurant.',
    questions: [
      {
        q: 'Quels modules sont inclus dans mon plan ?',
        a: 'Tous. Chaque plan donne accès à tous les modules : commandes, cuisine, réservations, carte, salle, stock, personnel, clients, marketing, rapports, encaissement, vente en ligne et API. Les plans ne diffèrent que par les volumes.',
      },
      {
        q: 'Mes clients peuvent-ils commander en ligne ?',
        a: 'Oui : site de commande à vos couleurs, application mobile, borne en libre-service et commande à table par QR code. Le client paie en ligne ou au comptoir, et suit sa commande en direct.',
      },
      {
        q: 'Puis-je gérer plusieurs établissements ?',
        a: 'Oui, depuis un seul compte. Chaque établissement garde ses horaires, ses prix et son stock, et vos rapports sont consolidés ou par établissement. Le nombre d’établissements dépend de votre plan.',
      },
    ],
  },
  {
    id: 'securite',
    title: 'Sécurité et données',
    intro: 'Comment vos données et celles de vos clients sont protégées.',
    questions: [
      {
        q: 'Mes données sont-elles séparées de celles des autres restaurants ?',
        a: 'Oui. Chaque restaurant ne voit que ses propres données ; le restaurant concerné est déterminé par votre connexion, jamais par une valeur que l’on pourrait modifier.',
      },
      {
        q: 'Qui peut faire quoi dans mon équipe ?',
        a: 'Vous attribuez des rôles avec des permissions précises (prise de commande, cuisine, remises, remboursements, configuration...) et pouvez les limiter à un établissement. Les actions sensibles sont inscrites dans un journal qui ne peut pas être modifié.',
      },
      {
        q: 'Comment sont protégés les identifiants de mes prestataires ?',
        a: 'Les clés de vos prestataires de paiement et d’envoi de messages sont chiffrées et ne sont jamais réaffichées, ni dans l’application, ni dans les journaux.',
      },
      {
        q: 'Et les données de mes clients ?',
        a: 'Vous ne conservez que l’utile, vous enregistrez leurs consentements par canal, et vous pouvez anonymiser un client à sa demande.',
      },
    ],
  },
  {
    id: 'equipe',
    title: 'Gestion d’équipe',
    intro: 'Inviter votre équipe et suivre son activité.',
    questions: [
      {
        q: 'Comment ajouter les membres de mon équipe ?',
        a: 'Invitez chaque membre par e-mail, puis attribuez-lui un rôle et les établissements où il travaille. Il n’accède qu’à ce dont il a besoin.',
      },
      {
        q: 'Puis-je suivre les heures de mon équipe ?',
        a: 'Oui : le pointage enregistre les arrivées et les départs de chacun, et les actions sensibles sont tracées dans un journal.',
      },
    ],
  },
  {
    id: 'integrations',
    title: 'Intégrations',
    intro: 'Relier RestauSpace à vos autres outils.',
    questions: [
      {
        q: 'Puis-je relier RestauSpace à mes autres logiciels ?',
        a: 'Oui. L’API d’intégration alimente votre caisse, votre comptabilité ou vos tableaux de bord, et des webhooks signés vous préviennent des commandes, paiements, réservations et mouvements de stock. Chaque clé d’API a des droits limités.',
      },
      {
        q: 'Puis-je créer mon propre site ou ma propre application ?',
        a: 'Oui : l’API de vente en ligne est documentée, avec des clients prêts à l’emploi en TypeScript, Flutter, Swift et Kotlin. Tout est expliqué dans l’Espace développeurs.',
      },
      {
        q: 'Recevez-vous les commandes des plateformes de livraison ?',
        a: 'Oui : les commandes des plateformes arrivent avec toutes les autres, en caisse et en cuisine.',
      },
    ],
  },
  {
    id: 'paiements',
    title: 'Paiements',
    intro: 'Les moyens de paiement acceptés au comptoir et en ligne.',
    questions: [
      {
        q: 'Quels moyens de paiement puis-je accepter ?',
        a: 'Au comptoir : espèces, carte bancaire sur votre terminal, mobile money et titres-restaurant. Le paiement en ligne passe par des prestataires dont l’intégration est en cours.',
      },
    ],
  },
  {
    id: 'assistance',
    title: 'Assistance',
    intro: 'Nous joindre et être accompagné.',
    questions: [
      {
        q: 'Comment contacter votre équipe ?',
        a: 'Par le formulaire de la page Contact : nous répondons à chaque message.',
      },
      {
        q: 'Suis-je accompagné au démarrage ?',
        a: 'Oui : nous créons votre compte et configurons votre restaurant avec vous, menu, tables, équipe et moyens de paiement.',
      },
    ],
  },
];
