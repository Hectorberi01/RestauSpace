import Link from 'next/link';

export default function NotFound() {
  return (
    <section className="mx-auto max-w-xl px-4 py-32 text-center">
      <p className="text-sm font-semibold text-brand-600">Erreur 404</p>
      <h1 className="mt-3 text-3xl font-bold">Cette page n’existe pas</h1>
      <Link href="/" className="mt-8 inline-block rounded-xl bg-brand-600 px-6 py-3 font-semibold text-white">Retour à l’accueil</Link>
    </section>
  );
}
