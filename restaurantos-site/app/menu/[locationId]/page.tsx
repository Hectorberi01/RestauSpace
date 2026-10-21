import type {Metadata} from 'next';
import {notFound} from 'next/navigation';
import {UtensilsCrossed} from 'lucide-react';
import {site} from '@/content/site';

interface Variant {
  name: string;
  price: number;
  isAvailable: boolean;
}

interface Item {
  name: string;
  description: string | null;
  price: number;
  currencyCode: string;
  isAvailable: boolean;
  variants: Variant[];
}

interface PublicMenu {
  restaurant: string;
  logoUrl: string | null;
  primaryColor: string | null;
  location: string;
  city: string;
  isOpen: boolean;
  pricesIncludeTax: boolean;
  menus: {name: string; sections: {name: string; items: Item[]}[]}[];
}

type Props = {params: Promise<{locationId: string}>; searchParams: Promise<{table?: string | string[]}>};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const HEX = /^#[0-9a-f]{6}$/i;

/** Le menu est lu côté serveur et mis en cache une minute : un QR scanné par toute une salle ne coûte qu'un appel. */
async function load(locationId: string): Promise<PublicMenu | null> {
  if (!GUID.test(locationId)) return null;
  try {
    // API_INTERNAL_URL : adresse de l'API vue du serveur du site (ex. Docker en local) ; sinon l'adresse publique.
    const api = (process.env.API_INTERNAL_URL ?? site.apiUrl).replace(/\/+$/, '');
    const response = await fetch(`${api}/v1/public/locations/${locationId}/menu`, {next: {revalidate: 60}});
    if (!response.ok) return null;
    return (await response.json()) as PublicMenu;
  } catch {
    return null;
  }
}

/** Libellé de table affiché tel quel, borné : il vient de l'URL imprimée sur le QR code. */
const tableOf = (value: string | string[] | undefined) => {
  const raw = (Array.isArray(value) ? value[0] : value)?.trim() ?? '';
  return raw.slice(0, 10);
};

const price = (value: number, currency: string) => {
  try {
    return new Intl.NumberFormat('fr-FR', {style: 'currency', currency}).format(value);
  } catch {
    return `${value} ${currency}`;
  }
};

export async function generateMetadata({params}: Props): Promise<Metadata> {
  const menu = await load((await params).locationId);
  return {
    title: menu ? `Menu · ${menu.restaurant}` : 'Menu introuvable',
    robots: {index: false, follow: false},
  };
}

export default async function MenuPage({params, searchParams}: Props) {
  const [{locationId}, query] = await Promise.all([params, searchParams]);
  const menu = await load(locationId);
  if (!menu) notFound();
  const table = tableOf(query.table);
  const accent = menu.primaryColor && HEX.test(menu.primaryColor) ? menu.primaryColor : '#5b3df5';
  const logo = menu.logoUrl?.startsWith('https://') ? menu.logoUrl : null;

  return (
    <div className="min-h-screen bg-slate-50 pb-16">
      <header className="px-4 pt-8 pb-6 text-center text-white" style={{backgroundColor: accent}}>
        {logo ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={logo} alt="" className="mx-auto mb-3 h-16 w-auto rounded-xl bg-white object-contain p-1" />
        ) : (
          <span className="mx-auto mb-3 grid size-14 place-items-center rounded-2xl bg-white/20" aria-hidden="true"><UtensilsCrossed className="size-7" /></span>
        )}
        <h1 className="text-2xl font-bold">{menu.restaurant}</h1>
        <p className="mt-1 text-sm text-white/85">{menu.location} · {menu.city}</p>
        {table && <p className="mx-auto mt-4 inline-block rounded-full bg-white px-4 py-1.5 text-sm font-semibold" style={{color: accent}}>Table {table}</p>}
      </header>

      <div className="mx-auto max-w-2xl px-4">
        {!menu.isOpen && (
          <p className="mt-4 rounded-xl bg-amber-50 p-3 text-center text-sm text-amber-900">L’établissement ne prend pas de commande pour le moment.</p>
        )}

        {menu.menus.length === 0 ? (
          <p className="mt-10 text-center text-ink-500">Le menu n’est pas encore disponible en ligne. Demandez-le à l’équipe.</p>
        ) : (
          <>
            {menu.menus.length > 1 && (
              <nav aria-label="Menus" className="sticky top-0 z-10 -mx-4 flex gap-2 overflow-x-auto bg-slate-50/95 px-4 py-3 backdrop-blur">
                {menu.menus.map((item, index) => (
                  <a key={index} href={`#menu-${index}`} className="shrink-0 rounded-full bg-white px-4 py-1.5 text-sm font-semibold shadow-sm ring-1 ring-slate-200">{item.name}</a>
                ))}
              </nav>
            )}
            {menu.menus.map((item, index) => (
              <section key={index} id={`menu-${index}`} className="mt-6 scroll-mt-16">
                {menu.menus.length > 1 && <h2 className="mb-3 text-xl font-bold">{item.name}</h2>}
                {item.sections.map((section, position) => (
                  <div key={position} className="mb-6">
                    <h3 className="mb-2 text-xs font-bold tracking-wider uppercase" style={{color: accent}}>{section.name}</h3>
                    <ul className="divide-y divide-slate-100 overflow-hidden rounded-2xl bg-white shadow-sm ring-1 ring-slate-200">
                      {section.items.map((product, rank) => (
                        <li key={rank} className={`px-4 py-3 ${product.isAvailable ? '' : 'opacity-55'}`}>
                          <div className="flex items-baseline gap-3">
                            <p className="min-w-0 flex-1 font-semibold">{product.name}</p>
                            <p className="shrink-0 font-semibold whitespace-nowrap">{price(product.price, product.currencyCode)}</p>
                          </div>
                          {product.description && <p className="mt-0.5 text-sm text-ink-600">{product.description}</p>}
                          {product.variants.length > 0 && (
                            <ul className="mt-1.5 space-y-0.5 text-sm">
                              {product.variants.map((variant, slot) => (
                                <li key={slot} className={`flex justify-between gap-3 ${variant.isAvailable ? '' : 'text-ink-500 line-through'}`}>
                                  <span>{variant.name}</span>
                                  <span className="whitespace-nowrap">{price(variant.price, product.currencyCode)}</span>
                                </li>
                              ))}
                            </ul>
                          )}
                          {!product.isAvailable && <p className="mt-1 text-xs font-semibold text-ink-500">Indisponible aujourd’hui</p>}
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </section>
            ))}
          </>
        )}
        <p className="mt-8 text-center text-xs text-ink-500">
          Prix {menu.pricesIncludeTax ? 'TTC' : 'HT, TVA en sus'}. Allergènes et composition : demandez à l’équipe.
        </p>
      </div>
    </div>
  );
}
