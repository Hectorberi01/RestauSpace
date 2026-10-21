import type {Metadata} from 'next';
import {PageHero} from '@/components/section';
import {site} from '@/content/site';

export const metadata: Metadata = {title: 'Mentions légales', robots: {index: false}, alternates: {canonical: '/mentions-legales'}};

const value = (text: string) => text || '[à compléter]';

export default function Legal() {
  const rows: [string, string][] = [
    ['Éditeur du site', `${site.legal.company}${site.legal.form ? `, ${site.legal.form}` : ''}`],
    ['Immatriculation', value(site.legal.registration)],
    ['Siège', value(site.legal.address)],
    ['Directeur de la publication', value(site.legal.publisher)],
    ['Contact', value(site.contact.email)],
    ['Hébergement', value(site.legal.host)],
  ];
  return (
    <>
      <PageHero eyebrow="Informations" title="Mentions légales" intro="Les informations relatives à l’éditeur et à l’hébergeur de ce site." />
      <section className="bg-white">
        <div className="mx-auto max-w-3xl px-4 pb-20 sm:px-6">
          <dl className="divide-y divide-slate-200 rounded-2xl border border-slate-200">
            {rows.map(([label, text]) => (
              <div key={label} className="grid gap-1 px-5 py-4 sm:grid-cols-3">
                <dt className="font-medium text-ink-600">{label}</dt>
                <dd className="sm:col-span-2">{text}</dd>
              </div>
            ))}
          </dl>
          <p className="mt-8 text-sm leading-relaxed text-ink-600">
            Les marques, textes et visuels de ce site sont la propriété de {site.legal.company}. Toute reproduction sans
            autorisation est interdite.
          </p>
        </div>
      </section>
    </>
  );
}
