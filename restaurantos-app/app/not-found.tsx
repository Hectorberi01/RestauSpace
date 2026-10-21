import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="mx-auto max-w-md px-6 pt-24 text-center">
      <p className="text-sm font-semibold text-brand-700">404</p>
      <h1 className="mt-2 text-2xl font-bold">Page introuvable</h1>
      <Link href="/tableau-de-bord" className="mt-6 inline-block font-semibold text-brand-700">Retour au tableau de bord</Link>
    </div>
  );
}
