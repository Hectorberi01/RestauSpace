import {ButtonLink, PageShell} from '@/components/ui';

export default function NotFound() {
  return (
    <PageShell title="Page introuvable">
      <p className="text-muted">Cette page n’existe pas ou a été déplacée.</p>
      <ButtonLink href="/" className="mt-6">Retour à l’accueil</ButtonLink>
    </PageShell>
  );
}
