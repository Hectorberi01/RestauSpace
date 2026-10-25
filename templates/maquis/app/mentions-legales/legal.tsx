'use client';
import {config} from '@/lib/config';
import {useSite} from '@/components/site-provider';
import {PageShell} from '@/components/ui';

// Les textes juridiques sont fournis par le restaurant : bloc « legal » de site.config.json.

const missing = 'À compléter dans site.config.json';

export function Legal() {
  const {front, location} = useSite();
  const name = front?.restaurant.name ?? 'le restaurant';
  const contact = location?.place?.contact;
  const value = (text: string | null) => text ?? (config.demo ? 'Restaurant fictif de démonstration' : missing);

  return (
    <PageShell title="Mentions légales et confidentialité">
      <div className="space-y-8 leading-relaxed text-muted [&_h2]:mb-2 [&_h2]:font-serif [&_h2]:text-2xl [&_h2]:font-bold [&_h2]:text-ink">
        {config.demo && (
          <p className="rounded-xl border border-line bg-white px-4 py-3 text-sm text-ink">
            Ce site est une démonstration : le restaurant, la carte, les commandes et les paiements sont fictifs.
          </p>
        )}
        <section>
          <h2>Éditeur du site</h2>
          <dl className="grid gap-x-6 gap-y-1 sm:grid-cols-[200px_1fr]">
            <dt className="font-semibold text-ink">Établissement</dt><dd>{front?.restaurant.name ?? '…'}</dd>
            <dt className="font-semibold text-ink">Raison sociale</dt><dd>{value(config.legal.company)}</dd>
            <dt className="font-semibold text-ink">Immatriculation</dt><dd>{value(config.legal.registration)}</dd>
            <dt className="font-semibold text-ink">Adresse</dt><dd>{config.legal.address ?? (location ? [location.street, location.city].filter(Boolean).join(', ') : value(null))}</dd>
            <dt className="font-semibold text-ink">Directeur de la publication</dt><dd>{value(config.legal.publicationDirector)}</dd>
            {contact?.phone && <><dt className="font-semibold text-ink">Téléphone</dt><dd>{contact.phone}</dd></>}
            {contact?.email && <><dt className="font-semibold text-ink">E-mail</dt><dd>{contact.email}</dd></>}
          </dl>
        </section>
        <section>
          <h2>Hébergement</h2>
          <p>{value(config.legal.host)}</p>
        </section>
        <section>
          <h2>Commandes et paiements</h2>
          <p>
            Les commandes, réservations et comptes clients sont traités par {name} au moyen de la plateforme RestauSpace. Le paiement en ligne se fait sur la page du
            prestataire de paiement choisi par le restaurant : ce site ne voit et ne conserve jamais vos données bancaires. Une commande peut être annulée en ligne
            tant que le restaurant ne l’a pas acceptée.
          </p>
        </section>
        <section>
          <h2>Données personnelles</h2>
          <p>
            Nous demandons votre nom, votre téléphone ou votre e-mail et, pour une livraison, votre adresse, afin de préparer et livrer votre commande ou de
            confirmer votre réservation. Ces données sont reçues par {name}, par RestauSpace qui les traite pour son compte, et par le prestataire de paiement
            pour un paiement en ligne. Vous recevez des offres seulement si vous l’avez accepté. Pour consulter, corriger ou supprimer vos données, contactez le
            restaurant.
          </p>
        </section>
        <section>
          <h2>Cookies et stockage</h2>
          <p>
            Ce site ne dépose pas de cookie publicitaire. Votre navigateur garde votre panier, vos dernières commandes et, le temps de la visite, votre
            connexion au compte client. Si un contrôle anti-robot (Cloudflare Turnstile) est activé, il peut déposer un cookie technique.
          </p>
        </section>
      </div>
    </PageShell>
  );
}
