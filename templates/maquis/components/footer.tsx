'use client';
import Link from 'next/link';
import {Camera, Globe, Mail, MapPin, MessageCircle, Phone} from 'lucide-react';
import {config} from '@/lib/config';
import {directionsUrl, whatsAppUrl} from '@/lib/format';
import {useSite} from './site-provider';

const networkLabel: Record<string, string> = {
  Website: 'Site web', Facebook: 'Facebook', Instagram: 'Instagram', TikTok: 'TikTok', X: 'X', YouTube: 'YouTube', GoogleMaps: 'Google Maps', WhatsApp: 'WhatsApp',
};

/** Liens du restaurant : ceux de site.config.json d'abord, puis ceux de l'API (un par réseau). */
export function useSocialLinks() {
  const {front} = useSite();
  const fromConfig = [
    config.links.instagram && {network: 'Instagram', url: config.links.instagram},
    config.links.facebook && {network: 'Facebook', url: config.links.facebook},
    config.links.whatsapp && {network: 'WhatsApp', url: config.links.whatsapp.startsWith('https://') ? config.links.whatsapp : whatsAppUrl(config.links.whatsapp)},
  ].filter((item): item is {network: string; url: string} => !!item);
  const fromApi = (front?.restaurant.links ?? []).filter(link => link.url.startsWith('https://'));
  return [...fromConfig, ...fromApi.filter(link => !fromConfig.some(item => item.network === link.network))]
    .filter(link => link.url.startsWith('https://'));
}

function NetworkIcon({network}: {network: string}) {
  if (network === 'Instagram') return <Camera className="size-4" aria-hidden="true" />;
  if (network === 'WhatsApp') return <MessageCircle className="size-4" aria-hidden="true" />;
  if (network === 'GoogleMaps') return <MapPin className="size-4" aria-hidden="true" />;
  return <Globe className="size-4" aria-hidden="true" />;
}

export function Footer() {
  const {front, location} = useSite();
  const links = useSocialLinks();
  const contact = location?.place?.contact;
  const name = front?.restaurant.name ?? '';

  return (
    <footer id="infos" className="mt-16 scroll-mt-28 bg-ink text-[#e9d9c3]">
      <div className="mx-auto grid max-w-6xl gap-8 px-4 py-10 sm:grid-cols-2 sm:px-6 lg:grid-cols-3">
        <div>
          <h2 className="font-serif text-xl font-bold text-white">Nous trouver</h2>
          {location ? (
            <>
              {front && front.locations.length > 1 && <p className="mt-2 font-semibold text-white">{location.name}</p>}
              <p className="mt-2 text-sm leading-relaxed">{[location.street, location.city].filter(Boolean).join(', ')}</p>
              <a href={directionsUrl(location)} target="_blank" rel="noopener noreferrer" className="mt-3 inline-flex items-center gap-2 text-sm font-semibold text-accent hover:underline">
                <MapPin className="size-4" aria-hidden="true" />Itinéraire
              </a>
            </>
          ) : <p className="mt-2 text-sm opacity-80">Adresse indisponible pour le moment.</p>}
        </div>
        <div>
          <h2 className="font-serif text-xl font-bold text-white">Contact</h2>
          <ul className="mt-2 space-y-2 text-sm">
            {contact?.phone && <li><a href={`tel:${contact.phone}`} className="inline-flex items-center gap-2 hover:text-white hover:underline"><Phone className="size-4" aria-hidden="true" />{contact.phone}</a></li>}
            {contact?.whatsApp && <li><a href={whatsAppUrl(contact.whatsApp)} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-2 hover:text-white hover:underline"><MessageCircle className="size-4" aria-hidden="true" />WhatsApp</a></li>}
            {contact?.email && <li><a href={`mailto:${contact.email}`} className="inline-flex items-center gap-2 hover:text-white hover:underline"><Mail className="size-4" aria-hidden="true" />{contact.email}</a></li>}
            {!contact?.phone && !contact?.whatsApp && !contact?.email && <li className="opacity-80">Commandez et réservez directement sur ce site.</li>}
          </ul>
        </div>
        {links.length > 0 && (
          <div>
            <h2 className="font-serif text-xl font-bold text-white">Suivez-nous</h2>
            <ul className="mt-2 flex flex-wrap gap-2">
              {links.map(link => (
                <li key={link.network}>
                  <a href={link.url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-2 rounded-full border border-white/20 px-3 py-1.5 text-sm hover:bg-white/10">
                    <NetworkIcon network={link.network} />{networkLabel[link.network] ?? link.network}
                  </a>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
      <div className="border-t border-white/10">
        <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-4 text-[13px] sm:flex-row sm:items-center sm:justify-between sm:px-6">
          <p>© {name || 'Restaurant'} · <Link href="/mentions-legales/" className="hover:text-white hover:underline">Mentions légales et confidentialité</Link></p>
          <p>Commandes et paiements propulsés par <a href="https://restauspace.com" className="hover:text-white hover:underline">RestauSpace</a></p>
        </div>
      </div>
    </footer>
  );
}
