'use client';
import {useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {ArrowRight, Bike, Clock, MapPin, Plus, RefreshCw, Store, Wallet} from 'lucide-react';
import {config, hasSection, type SectionName} from '@/lib/config';
import {directionsUrl, money, openingText, priceParts, specialDayText, upcomingSpecialDays, weeklyRows} from '@/lib/format';
import {loadReviews, type Catalog, type CatalogProduct, type Reviews, type StorefrontLocation} from '@/lib/storefront';
import {Dish, Plate, Wax, pastel} from './art';
import {useSite} from './site-provider';
import {productIndex, sectionsOf, useCatalog} from './use-catalog';
import {Button, ButtonLink, Notice, Skeleton, Stars, cx} from './ui';

const carteHref = (location: StorefrontLocation | null, extra: Record<string, string> = {}) => {
  const query = new URLSearchParams({...(location ? {etablissement: location.id} : {}), ...extra});
  const text = query.toString();
  return `/carte/${text ? `?${text}` : ''}`;
};

export function Home() {
  const {front, location, error, retry} = useSite();
  const {catalog, error: catalogError} = useCatalog(location?.id ?? null);
  const [reviews, setReviews] = useState<Reviews | null>(null);

  useEffect(() => {
    if (!front || !hasSection('reviews')) return;
    const controller = new AbortController();
    loadReviews(3, controller.signal).then(setReviews).catch(() => {});
    return () => controller.abort();
  }, [front]);

  const strip = config.sections.filter((name): name is Extract<SectionName, 'booking' | 'hours' | 'reviews'> =>
    name === 'booking' || name === 'hours' || (name === 'reviews' && (reviews === null || reviews.count > 0)));

  return (
    <>
      {error && (
        <div className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
          <Notice tone="error">
            <span className="flex flex-wrap items-center justify-between gap-3">
              {error}
              <Button variant="ghost" className="min-h-9 px-3 py-1.5 text-sm" onClick={retry}><RefreshCw className="size-4" aria-hidden="true" />Réessayer</Button>
            </span>
          </Notice>
        </div>
      )}
      {hasSection('hero') && <Hero location={location} reviews={reviews} />}
      {hasSection('featured') && <Featured catalog={catalog} error={catalogError} location={location} failed={!!error} />}
      {strip.length > 0 && (
        <section aria-label="Réservation, horaires et avis" className={cx('mx-auto mt-6 grid max-w-6xl gap-5 px-4 sm:px-6', strip.length === 3 ? 'lg:grid-cols-[1.2fr_1fr_1fr]' : strip.length === 2 ? 'md:grid-cols-2' : '')}>
          {strip.map(name => name === 'booking' ? <BookingPanel key={name} />
            : name === 'hours' ? <HoursPanel key={name} location={location} failed={!!error} />
            : <ReviewsPanel key={name} reviews={reviews} failed={!!error} />)}
        </section>
      )}
    </>
  );
}

function Hero({location, reviews}: {location: StorefrontLocation | null; reviews: Reviews | null}) {
  const {front, error} = useSite();
  const status = location ? openingText(location) : null;
  const zones = location?.delivery?.zones ?? [];
  const modes = location?.serviceModes ?? [];
  const delivers = modes.includes('Delivery') && zones.length > 0;
  const ordering = front?.orderingEnabled !== false;
  const cover = front?.restaurant.coverImageUrl?.startsWith('https://') ? front.restaurant.coverImageUrl : null;
  const lead = front ? front.restaurant.tagline || config.texts.heroText : error ? config.texts.heroText : null;
  const currency = front?.restaurant.currencyCode ?? 'XOF';
  const minFee = zones.length ? Math.min(...zones.map(zone => zone.fee)) : null;
  const payment = location?.payment;

  return (
    <section className="mx-auto grid max-w-6xl items-center gap-10 px-4 pb-6 pt-8 sm:px-6 lg:grid-cols-[1.15fr_1fr] lg:pt-10">
      <div>
        {status ? (
          <span className={cx('inline-flex items-center gap-2 rounded-full border border-line bg-white px-3.5 py-1.5 text-sm font-semibold', status.open ? 'text-leaf' : 'text-muted')}>
            <span className={cx('size-2.5 rounded-full', status.open ? 'bg-[#2fae5b] shadow-[0_0_0_4px_#2fae5b22]' : 'bg-muted/60')} aria-hidden="true" />
            {status.text}
          </span>
        ) : !error && <Skeleton className="h-8 w-56 rounded-full" />}
        <h1 className="mt-5 font-serif text-[40px] font-bold leading-[1.06] tracking-tight sm:text-5xl lg:text-[50px] xl:text-[56px]">
          {config.texts.heroTitle}{config.texts.heroAccent && <><br /><em className="font-bold text-primary">{config.texts.heroAccent}</em></>}
        </h1>
        {lead !== null ? <p className="mt-4 max-w-xl text-lg leading-relaxed text-muted">{lead}</p> : <div className="mt-4 space-y-2"><Skeleton className="h-5 w-full max-w-lg" /><Skeleton className="h-5 w-80" /></div>}
        <div className="mt-7 flex flex-wrap gap-3 [&>a]:px-4 xl:[&>a]:px-5">
          {ordering ? (
            <>
              <ButtonLink href={carteHref(location, modes.includes('Takeaway') ? {mode: 'Takeaway'} : {})}>
                {modes.includes('Takeaway') ? 'Commander à emporter' : 'Commander'}<ArrowRight className="size-4" aria-hidden="true" />
              </ButtonLink>
              {delivers && <ButtonLink variant="ghost" href={carteHref(location, {mode: 'Delivery'})}>Se faire livrer</ButtonLink>}
            </>
          ) : <ButtonLink href={carteHref(location)}>Voir la carte<ArrowRight className="size-4" aria-hidden="true" /></ButtonLink>}
          {hasSection('booking') && <ButtonLink variant="ghost" href="/reserver/">Réserver une table</ButtonLink>}
        </div>
        {location && (
          <ul className="mt-8 flex flex-col gap-4 text-[15px] text-muted sm:flex-row sm:flex-wrap sm:gap-x-7">
            <Fact icon={Clock}>Prêt en {location.preparationMinutes} min environ</Fact>
            {delivers
              ? <Fact icon={Bike}>Livraison : {zones.slice(0, 2).map(zone => zone.name).join(', ')}{zones.length > 2 ? '…' : ''}</Fact>
              : <Fact icon={Store}>{modes.map(mode => mode === 'DineIn' ? 'Sur place' : mode === 'Takeaway' ? 'À emporter' : 'Livraison').join(' ou ')}</Fact>}
            {payment && <Fact icon={Wallet}>{payment.payOnline && payment.payAtCounter ? 'Paiement en ligne ou sur place' : payment.payOnline ? 'Paiement en ligne' : 'Paiement sur place'}</Fact>}
          </ul>
        )}
      </div>

      <div className="relative mx-2 h-[300px] sm:h-[400px] lg:mx-0 lg:h-[440px]">
        <div className="absolute inset-0 overflow-hidden rounded-[32px] bg-[color-mix(in_srgb,var(--primary)_55%,#f08a2e)] lg:rounded-[36px]">
          <Wax />
          <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2">
            {cover ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={cover} alt="" className="size-[220px] rounded-full border-[10px] border-white object-cover shadow-[0_20px_40px_rgba(60,20,5,.35)] sm:size-[300px] lg:size-[330px]" />
            ) : (
              <Dish variant={0} size={330} className="size-[230px] drop-shadow-[0_20px_30px_rgba(60,20,5,.35)] sm:size-[320px] lg:size-[360px]" />
            )}
          </div>
        </div>
        {reviews && reviews.count > 0 && reviews.averageRating !== null && (
          <div className="absolute -left-2 bottom-6 rounded-2xl bg-white px-4 py-3 text-sm shadow-[0_14px_30px_rgba(90,44,16,.16)] sm:-left-6 lg:-left-8 lg:bottom-16">
            <Stars rating={reviews.averageRating} className="text-lg" />
            <b className="block text-base">{reviews.averageRating.toLocaleString('fr-FR', {maximumFractionDigits: 1})} / 5</b>
            <span className="text-muted">{reviews.count} avis client{reviews.count > 1 ? 's' : ''}</span>
          </div>
        )}
        {delivers && minFee !== null && (
          <div className="absolute -right-2 top-6 rounded-2xl bg-white px-4 py-3 text-sm shadow-[0_14px_30px_rgba(90,44,16,.16)] sm:-right-4 lg:top-10">
            <b className="block text-base">{zones.every(zone => zone.fee === 0) ? 'Livraison offerte' : 'Livraison'}</b>
            {zones.every(zone => zone.fee === 0) ? `${zones.length} quartier${zones.length > 1 ? 's' : ''} desservi${zones.length > 1 ? 's' : ''}` : minFee === 0 ? 'offerte selon le quartier' : `dès ${money(minFee, currency)}`}
          </div>
        )}
      </div>
    </section>
  );
}

function Fact({icon: Icon, children}: {icon: typeof Clock; children: React.ReactNode}) {
  return (
    <li className="flex items-center gap-3">
      <span className="grid size-10 shrink-0 place-items-center rounded-xl border border-line bg-white text-primary"><Icon className="size-[18px]" aria-hidden="true" /></span>
      <span className="leading-snug">{children}</span>
    </li>
  );
}

function Featured({catalog, error, location, failed}: {catalog: Catalog | null; error: string | null; location: StorefrontLocation | null; failed: boolean}) {
  const [chip, setChip] = useState<string | null>(null);
  const sections = useMemo(() => (catalog ? sectionsOf(catalog) : []), [catalog]);
  const count = config.featured.count;
  const products = useMemo(() => {
    if (!catalog) return [];
    const chosen = sections.find(section => section.key === chip);
    if (chosen) return chosen.products.slice(0, count);
    const preferred = config.featured.section && sections.find(section => section.name.toLowerCase() === config.featured.section!.toLowerCase());
    const pool = preferred ? preferred.products : sections.flatMap(section => section.products);
    return [...new Map(pool.map(product => [product.id, product])).values()].slice(0, count);
  }, [catalog, sections, chip, count]);

  return (
    <section aria-labelledby="incontournables" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
      <div className="mb-5 flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h2 id="incontournables" className="font-serif text-3xl font-bold sm:text-[38px]">{config.texts.featuredTitle}</h2>
          {config.texts.featuredText && <p className="mt-1.5 text-muted">{config.texts.featuredText}</p>}
        </div>
        {sections.length > 1 && (
          <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:px-0" role="group" aria-label="Filtrer par catégorie">
            {[{key: null, name: 'Tout'}, ...sections.slice(0, 6)].map(item => (
              <button key={item.key ?? 'tout'} type="button" aria-pressed={chip === item.key} onClick={() => setChip(item.key)}
                className={cx('shrink-0 rounded-full border px-4 py-2 text-[15px] font-medium transition', chip === item.key ? 'border-ink bg-ink text-white' : 'border-line bg-white hover:border-ink/40')}>
                {item.name}
              </button>
            ))}
          </div>
        )}
      </div>

      {error ? (
        <Notice tone="error">{error}</Notice>
      ) : !catalog ? (
        failed ? <Notice>La carte s’affichera dès que le restaurant sera joignable.</Notice> : (
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-4" aria-busy="true" aria-label="Chargement de la carte">
            {Array.from({length: 4}, (_, index) => <Skeleton key={index} className="h-[300px] rounded-[22px]" />)}
          </div>
        )
      ) : products.length === 0 ? (
        <Notice>La carte n’est pas encore publiée.</Notice>
      ) : (
        <ul className="no-scrollbar -mx-4 flex snap-x snap-mandatory gap-4 overflow-x-auto px-4 pb-2 sm:mx-0 sm:grid sm:grid-cols-2 sm:gap-5 sm:overflow-visible sm:px-0 lg:grid-cols-4">
          {products.map(product => (
            <li key={product.id} className="w-[78%] shrink-0 snap-start sm:w-auto">
              <FeaturedCard product={product} catalog={catalog} location={location} />
            </li>
          ))}
        </ul>
      )}
      {catalog && products.length > 0 && (
        <p className="mt-5 text-center">
          <Link href={carteHref(location)} className="inline-flex items-center gap-2 font-semibold text-primary hover:underline">Voir toute la carte<ArrowRight className="size-4" aria-hidden="true" /></Link>
        </p>
      )}
    </section>
  );
}

function FeaturedCard({product, catalog, location}: {product: CatalogProduct; catalog: Catalog; location: StorefrontLocation | null}) {
  const index = productIndex(catalog, product.id);
  const from = product.variants.length > 0 ? Math.min(...product.variants.map(variant => variant.price)) : product.price;
  const price = priceParts(from, catalog.currencyCode);
  const href = carteHref(location, {produit: product.id});
  return (
    <article className={cx('flex h-full flex-col overflow-hidden rounded-[22px] border border-[#f0e2cf] bg-white shadow-[0_6px_18px_rgba(90,44,16,.05)]', !product.isAvailable && 'opacity-70')}>
      <div className="relative grid h-[150px] place-items-center" style={{background: pastel[index % 4]}}>
        {!product.isAvailable && <span className="absolute left-3.5 top-3.5 rounded-full bg-white px-2.5 py-1 text-xs font-semibold">Épuisé</span>}
        {(product.formulaSteps?.length ?? 0) > 0 && <span className="absolute left-3.5 top-3.5 rounded-full bg-white px-2.5 py-1 text-xs font-semibold text-primary">Formule</span>}
        <Plate src={product.thumbnails?.[0] ?? product.images[0]} index={index} size={124} muted={!product.isAvailable} />
      </div>
      <div className="flex flex-1 flex-col px-[18px] pb-[18px] pt-4">
        <h3 className="text-lg font-semibold leading-snug">{product.name}</h3>
        {product.description && <p className="mt-1 line-clamp-2 text-sm leading-relaxed text-muted">{product.description}</p>}
        <div className="mt-auto flex items-center justify-between gap-2 pt-3">
          <span className="text-lg font-bold">
            {product.variants.length > 0 && <span className="text-sm font-medium text-muted">dès </span>}
            {price.amount} <span className="text-[13px] font-medium text-muted">{price.unit}</span>
          </span>
          {product.isAvailable && (
            <Link href={href} aria-label={`Ajouter ${product.name}`} className="grid size-11 place-items-center rounded-xl bg-primary text-white hover:brightness-110">
              <Plus className="size-5" aria-hidden="true" />
            </Link>
          )}
        </div>
      </div>
    </article>
  );
}

function BookingPanel() {
  return (
    <div className="rounded-[22px] bg-dark px-6 py-6 text-[#fbe9d2]">
      <h2 className="font-serif text-[22px] font-bold text-white">Réservez votre table</h2>
      <p className="mt-2 leading-relaxed text-[#f3d8b8]">Anniversaire, repas d’équipe ou dîner à deux : choisissez le jour, l’heure et le nombre de personnes, la confirmation est immédiate.</p>
      <ButtonLink variant="sun" href="/reserver/" className="mt-4 py-2.5">Réserver une table</ButtonLink>
    </div>
  );
}

function HoursPanel({location, failed}: {location: StorefrontLocation | null; failed: boolean}) {
  const rows = location ? weeklyRows(location) : [];
  const special = location ? upcomingSpecialDays(location) : [];
  return (
    <div className="rounded-[22px] border border-[#f0e2cf] bg-white px-6 py-6">
      <h2 className="font-serif text-[22px] font-bold">Horaires</h2>
      {!location ? (
        failed ? <p className="mt-3 text-sm text-muted">Horaires indisponibles pour le moment.</p> : <div className="mt-3 space-y-2"><Skeleton className="h-4" /><Skeleton className="h-4" /><Skeleton className="h-4 w-2/3" /></div>
      ) : (
        <>
          <ul className="mt-3 space-y-1 text-[15px] text-muted">
            {rows.map(row => (
              <li key={row.label} className="flex justify-between gap-4"><span>{row.label}</span><b className="text-right font-semibold text-ink">{row.text}</b></li>
            ))}
          </ul>
          {special.length > 0 && (
            <div className="mt-3 rounded-xl bg-sand/60 px-3 py-2 text-sm">
              <p className="font-semibold">Horaires exceptionnels</p>
              <ul>{special.map(item => <li key={item.date}>{specialDayText(item)}</li>)}</ul>
            </div>
          )}
          <a href={directionsUrl(location)} target="_blank" rel="noopener noreferrer" className="mt-3 inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline">
            <MapPin className="size-4" aria-hidden="true" />{[location.street, location.city].filter(Boolean).join(', ')}
          </a>
        </>
      )}
    </div>
  );
}

function ReviewsPanel({reviews, failed}: {reviews: Reviews | null; failed: boolean}) {
  const comments = reviews?.reviews.filter(review => review.comment) ?? [];
  return (
    <div id="avis" className="scroll-mt-28 rounded-[22px] border border-[#f0e2cf] bg-white px-6 py-6">
      <h2 className="font-serif text-[22px] font-bold">Ils en parlent</h2>
      {!reviews ? (
        failed ? <p className="mt-3 text-sm text-muted">Avis indisponibles pour le moment.</p> : <div className="mt-3 space-y-2"><Skeleton className="h-5 w-32" /><Skeleton className="h-4" /><Skeleton className="h-4 w-3/4" /></div>
      ) : (
        <>
          {reviews.averageRating !== null && (
            <p className="mt-2 flex items-center gap-2">
              <Stars rating={reviews.averageRating} className="text-xl" />
              <span className="text-sm text-muted">{reviews.averageRating.toLocaleString('fr-FR', {maximumFractionDigits: 1})} / 5 · {reviews.count} avis</span>
            </p>
          )}
          {comments.slice(0, 2).map(review => (
            <figure key={review.id} className="mt-3">
              <blockquote className="text-[15px] leading-relaxed text-muted">« {review.comment} »</blockquote>
              <figcaption className="mt-1 text-[13px] text-muted">Client vérifié · {review.rating}/5</figcaption>
            </figure>
          ))}
        </>
      )}
    </div>
  );
}
