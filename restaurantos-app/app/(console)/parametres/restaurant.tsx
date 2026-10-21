'use client';
import {useEffect, useState, type FormEvent, type ReactNode} from 'react';
import {Clock, Coins, Globe, ImageIcon, ImagePlus, Palette, Receipt, Store} from 'lucide-react';
import {IMAGE_TYPES, uploadCover, uploadLogo} from '@/lib/files';
import {timeZones} from '@/lib/profile';
import {changeCurrency, changePriceTaxMode, changeTimeZone, currencies, renameRestaurant, socialNetworks, updateBranding, updatePublicProfile} from '@/lib/settings';
import type {PublicProfile, SocialNetwork} from '@/lib/types';
import {useConsole} from '@/components/console';
import {Alert, Button, Card, Field, Input, Select, Textarea, cx, useAction} from '@/components/ui';

const HEX = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

function Section({icon, title, description, children}: {icon: ReactNode; title: string; description: string; children: ReactNode}) {
  return (
    <Card className="p-5">
      <div className="mb-4 flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-ink-600" aria-hidden="true">{icon}</span>
        <div>
          <h2 className="font-semibold">{title}</h2>
          <p className="text-sm text-ink-500">{description}</p>
        </div>
      </div>
      {children}
    </Card>
  );
}

export function RestaurantSettings() {
  const {tenant, can, reloadTenant} = useConsole();
  const editable = can('tenant.configure');
  const {busy, run} = useAction();
  const [name, setName] = useState(tenant.name);
  const [displayName, setDisplayName] = useState(tenant.branding.displayName);
  const [logoUrl, setLogoUrl] = useState(tenant.branding.logoUrl ?? '');
  const [color, setColor] = useState(tenant.branding.primaryColor ?? '');
  const [timeZone, setTimeZone] = useState(tenant.timeZoneId);
  const [currency, setCurrency] = useState(tenant.defaultCurrencyCode);
  const [taxIncluded, setTaxIncluded] = useState(tenant.pricesIncludeTax);
  const [logoFailed, setLogoFailed] = useState(false);

  useEffect(() => {
    setName(tenant.name);
    setDisplayName(tenant.branding.displayName);
    setLogoUrl(tenant.branding.logoUrl ?? '');
    setColor(tenant.branding.primaryColor ?? '');
    setTimeZone(tenant.timeZoneId);
    setCurrency(tenant.defaultCurrencyCode);
    setTaxIncluded(tenant.pricesIncludeTax);
  }, [tenant]);
  useEffect(() => setLogoFailed(false), [logoUrl]);

  const zones = timeZones.some(zone => zone.id === tenant.timeZoneId) ? timeZones : [{id: tenant.timeZoneId, label: tenant.timeZoneId}, ...timeZones];
  const currencyList = currencies.some(item => item.code === tenant.defaultCurrencyCode)
    ? currencies
    : [{code: tenant.defaultCurrencyCode, label: tenant.defaultCurrencyCode}, ...currencies];
  const brandingChanged = displayName !== tenant.branding.displayName || logoUrl !== (tenant.branding.logoUrl ?? '') || color !== (tenant.branding.primaryColor ?? '');
  const colorInvalid = color !== '' && !HEX.test(color);
  const logoInvalid = logoUrl.trim() !== '' && !/^https?:\/\/\S+$/i.test(logoUrl.trim());

  const save = (key: string, action: () => Promise<unknown>, success: string) => (event?: FormEvent) => {
    event?.preventDefault();
    void run(key, async () => {
      await action();
      await reloadTenant();
    }, success);
  };

  return (
    <div className="space-y-4">
      {!editable && <Alert tone="blue">Lecture seule : seuls les membres autorisés à régler le restaurant peuvent modifier ces paramètres.</Alert>}

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Section icon={<Store className="size-5" />} title="Nom du restaurant" description="Le nom de l’entreprise, utilisé sur vos factures et dans l’administration.">
          <form className="flex flex-col gap-2 sm:flex-row sm:items-end" onSubmit={save('name', () => renameRestaurant(name.trim()), 'Nom enregistré.')}>
            <Field label="Nom" className="flex-1"><Input required minLength={2} maxLength={100} disabled={!editable} value={name} onChange={event => setName(event.target.value)} /></Field>
            {editable && <Button type="submit" variant="secondary" busy={busy === 'name'} disabled={name.trim() === tenant.name || name.trim().length < 2}>Enregistrer</Button>}
          </form>
        </Section>

        <Section icon={<Clock className="size-5" />} title="Fuseau horaire" description="Heure de référence des rapports, des journées de commandes et des emails.">
          <form className="flex flex-col gap-2 sm:flex-row sm:items-end" onSubmit={save('zone', () => changeTimeZone(timeZone), 'Fuseau horaire enregistré.')}>
            <Field label="Fuseau" className="flex-1">
              <Select disabled={!editable} value={timeZone} onChange={event => setTimeZone(event.target.value)}>
                {zones.map(zone => <option key={zone.id} value={zone.id}>{zone.label}</option>)}
              </Select>
            </Field>
            {editable && <Button type="submit" variant="secondary" busy={busy === 'zone'} disabled={timeZone === tenant.timeZoneId}>Enregistrer</Button>}
          </form>
          <p className="mt-2 text-xs text-ink-500">Chaque établissement garde son propre fuseau pour ses horaires d’ouverture.</p>
        </Section>
      </div>

      <Section icon={<Palette className="size-5" />} title="Identité visuelle" description="Ce que voient vos clients : nom affiché, logo et couleur.">
        <form className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_18rem]" onSubmit={save('branding', () => updateBranding({
          displayName: displayName.trim(), logoUrl: logoUrl.trim() || null, primaryColor: color.trim() || null,
        }), 'Identité visuelle enregistrée.')}>
          <div className="space-y-4">
            <Field label="Nom affiché"><Input required maxLength={100} disabled={!editable} value={displayName} onChange={event => setDisplayName(event.target.value)} /></Field>
            <Field label="Logo (facultatif)" hint={logoInvalid ? 'Adresse complète en http(s)://' : 'Image carrée PNG, JPEG ou WebP de 2 Mo au plus, ou lien public vers une image.'}>
              <div className="flex gap-2">
                <Input type="url" disabled={!editable} value={logoUrl} onChange={event => setLogoUrl(event.target.value)} placeholder="https://…" />
                {editable && can('files.upload') && (
                  <label className={cx('inline-flex h-10 shrink-0 cursor-pointer items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 text-sm font-semibold text-ink-800 hover:bg-slate-50', busy === 'logo' && 'pointer-events-none opacity-60')}>
                    <ImagePlus className="size-4" aria-hidden="true" />{busy === 'logo' ? 'Envoi…' : 'Envoyer'}
                    <input type="file" accept={IMAGE_TYPES.join(',')} className="sr-only" aria-label="Envoyer un logo" onChange={event => {
                      const file = event.target.files?.[0];
                      event.target.value = '';
                      if (file) void run('logo', async () => setLogoUrl(await uploadLogo(tenant.id, file)), 'Logo envoyé : enregistrez l’identité pour l’appliquer.');
                    }} />
                  </label>
                )}
              </div>
            </Field>
            <Field label="Couleur principale (facultatif)" hint={colorInvalid ? 'Format attendu : #1A7F5A ou #1A7.' : undefined}>
              <div className="flex gap-2">
                <input type="color" aria-label="Choisir la couleur" disabled={!editable} value={HEX.test(color) && color.length === 7 ? color : '#16a34a'} onChange={event => setColor(event.target.value)}
                  className="h-10 w-12 shrink-0 cursor-pointer rounded-lg border border-slate-200 bg-white p-1 disabled:cursor-default" />
                <Input disabled={!editable} value={color} onChange={event => setColor(event.target.value)} placeholder="#1A7F5A" maxLength={7} />
              </div>
            </Field>
            {editable && <Button type="submit" busy={busy === 'branding'} disabled={!brandingChanged || !displayName.trim() || colorInvalid || logoInvalid}>Enregistrer l’identité</Button>}
          </div>
          <div aria-label="Aperçu" className="rounded-2xl border border-slate-200 p-4">
            <p className="mb-3 text-xs font-semibold text-ink-500">Aperçu</p>
            <div className="flex items-center gap-3">
              {logoUrl && !logoInvalid && !logoFailed ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={logoUrl} alt="" onError={() => setLogoFailed(true)} className="size-12 rounded-xl object-cover" />
              ) : (
                <span className="grid size-12 place-items-center rounded-xl text-lg font-bold text-white" style={{backgroundColor: HEX.test(color) ? color : '#16a34a'}}>
                  {logoUrl && !logoInvalid ? <ImageIcon className="size-5" aria-hidden="true" /> : (displayName.trim()[0] ?? '?').toUpperCase()}
                </span>
              )}
              <div className="min-w-0">
                <p className="truncate font-semibold">{displayName.trim() || 'Nom affiché'}</p>
                <p className="text-xs text-ink-500">Restaurant</p>
              </div>
            </div>
            <span className="mt-4 inline-flex h-9 items-center rounded-xl px-4 text-sm font-semibold text-white" style={{backgroundColor: HEX.test(color) ? color : '#16a34a'}}>Réserver une table</span>
            {logoFailed && <p className="mt-3 text-xs text-red-600">Le logo n’a pas pu être chargé depuis cette adresse.</p>}
          </div>
        </form>
      </Section>

      <PublicProfileSection editable={editable} />

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Section icon={<Coins className="size-5" />} title="Devise" description="Devise des prix, des commandes et des paiements.">
          <form className="flex flex-col gap-2 sm:flex-row sm:items-end" onSubmit={save('currency', () => changeCurrency(currency), 'Devise enregistrée.')}>
            <Field label="Devise" className="flex-1">
              <Select disabled={!editable} value={currency} onChange={event => setCurrency(event.target.value)}>
                {currencyList.map(item => <option key={item.code} value={item.code}>{item.label}</option>)}
              </Select>
            </Field>
            {editable && <Button type="submit" variant="secondary" busy={busy === 'currency'} disabled={currency === tenant.defaultCurrencyCode}>Enregistrer</Button>}
          </form>
          <p className="mt-2 text-xs text-ink-500">La devise ne peut plus changer dès qu’un produit a un prix au catalogue.</p>
        </Section>

        <Section icon={<Receipt className="size-5" />} title="Affichage des prix" description="Les prix saisis au catalogue incluent-ils les taxes ?">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2" role="radiogroup" aria-label="Mode de prix">
            {[
              {value: true, title: 'Taxes comprises (TTC)', hint: 'Le prix affiché est celui payé par le client.'},
              {value: false, title: 'Hors taxes (HT)', hint: 'Les taxes s’ajoutent au prix saisi.'},
            ].map(option => (
              <button key={String(option.value)} type="button" role="radio" aria-checked={taxIncluded === option.value} disabled={!editable}
                onClick={() => setTaxIncluded(option.value)}
                className={cx('rounded-xl border p-3 text-left text-sm disabled:cursor-default', taxIncluded === option.value ? 'border-brand-500 bg-brand-50 ring-1 ring-brand-500' : 'border-slate-200 hover:border-slate-300')}>
                <span className="block font-semibold">{option.title}</span>
                <span className="text-xs text-ink-500">{option.hint}</span>
              </button>
            ))}
          </div>
          {editable && taxIncluded !== tenant.pricesIncludeTax && (
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <Button variant="secondary" busy={busy === 'tax'} onClick={save('tax', () => changePriceTaxMode(taxIncluded), 'Mode de prix enregistré.')}>Enregistrer</Button>
              <span className="text-xs text-ink-500">S’applique aux nouvelles commandes ; les commandes en cours gardent leur mode.</span>
            </div>
          )}
        </Section>
      </div>
    </div>
  );
}

const URL_PATTERN = /^https?:\/\/\S+$/i;

const linksOf = (profile: PublicProfile | null | undefined) =>
  Object.fromEntries(socialNetworks.map(item => [item.network, profile?.links.find(link => link.network === item.network)?.url ?? ''])) as Record<SocialNetwork, string>;

/** Ce que le site et l'application du restaurant affichent en page d'accueil : accroche, présentation, couverture, liens. */
function PublicProfileSection({editable}: {editable: boolean}) {
  const {tenant, can, reloadTenant} = useConsole();
  const {busy, run} = useAction();
  const profile = tenant.publicProfile;
  const [tagline, setTagline] = useState(profile?.tagline ?? '');
  const [description, setDescription] = useState(profile?.description ?? '');
  const [cover, setCover] = useState(profile?.coverImageUrl ?? '');
  const [links, setLinks] = useState(() => linksOf(profile));
  const [coverFailed, setCoverFailed] = useState(false);

  useEffect(() => {
    setTagline(profile?.tagline ?? '');
    setDescription(profile?.description ?? '');
    setCover(profile?.coverImageUrl ?? '');
    setLinks(linksOf(profile));
  }, [profile]);
  useEffect(() => setCoverFailed(false), [cover]);

  const coverInvalid = cover.trim() !== '' && !URL_PATTERN.test(cover.trim());
  const invalidLinks = socialNetworks.filter(item => links[item.network].trim() !== '' && !URL_PATTERN.test(links[item.network].trim()));
  const changed = tagline !== (profile?.tagline ?? '') || description !== (profile?.description ?? '') || cover !== (profile?.coverImageUrl ?? '')
    || socialNetworks.some(item => links[item.network] !== (profile?.links.find(link => link.network === item.network)?.url ?? ''));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    void run('profile', async () => {
      await updatePublicProfile({
        tagline: tagline.trim() || null,
        description: description.trim() || null,
        coverImageUrl: cover.trim() || null,
        links: socialNetworks.filter(item => links[item.network].trim()).map(item => ({network: item.network, url: links[item.network].trim()})),
      });
      await reloadTenant();
    }, 'Présentation enregistrée.');
  };

  return (
    <Section icon={<Globe className="size-5" />} title="Présentation publique" description="Affichée en page d’accueil de votre site et de votre application : accroche, présentation, photo et liens.">
      <form className="space-y-5" onSubmit={submit}>
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <div className="space-y-4">
            <Field label="Accroche" hint={`${tagline.trim().length} / 120 · une phrase qui résume votre restaurant.`}>
              <Input maxLength={120} disabled={!editable} value={tagline} onChange={event => setTagline(event.target.value)} placeholder="Cuisine béninoise de quartier, depuis 2012" />
            </Field>
            <Field label="Présentation" hint={`${description.trim().length} / 2 000 · texte simple ; une ligne vide sépare deux paragraphes.`}>
              <Textarea rows={5} maxLength={2000} disabled={!editable} value={description} onChange={event => setDescription(event.target.value)}
                placeholder="Votre histoire, vos spécialités, ce qui vous rend unique…" />
            </Field>
            <Field label="Photo de couverture" hint={coverInvalid ? 'Adresse complète en http(s)://' : 'Grande photo horizontale (salle, plat signature), PNG, JPEG ou WebP.'}>
              <div className="flex gap-2">
                <Input type="url" disabled={!editable} value={cover} onChange={event => setCover(event.target.value)} placeholder="https://…" />
                {editable && can('files.upload') && (
                  <label className={cx('inline-flex h-10 shrink-0 cursor-pointer items-center gap-2 rounded-xl border border-slate-300 bg-white px-3 text-sm font-semibold text-ink-800 hover:bg-slate-50', busy === 'cover' && 'pointer-events-none opacity-60')}>
                    <ImagePlus className="size-4" aria-hidden="true" />{busy === 'cover' ? 'Envoi…' : 'Envoyer'}
                    <input type="file" accept={IMAGE_TYPES.join(',')} className="sr-only" aria-label="Envoyer une photo de couverture" onChange={event => {
                      const file = event.target.files?.[0];
                      event.target.value = '';
                      if (file) void run('cover', async () => setCover(await uploadCover(tenant.id, file)), 'Photo envoyée : enregistrez la présentation pour l’appliquer.');
                    }} />
                  </label>
                )}
              </div>
            </Field>
          </div>
          <div aria-label="Aperçu de la couverture" className="overflow-hidden rounded-2xl border border-slate-200">
            <div className="relative aspect-[16/10] bg-slate-100">
              {cover && !coverInvalid && !coverFailed ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={cover} alt="" onError={() => setCoverFailed(true)} className="absolute inset-0 size-full object-cover" />
              ) : (
                <span className="absolute inset-0 grid place-items-center text-ink-500"><ImageIcon className="size-6" aria-hidden="true" /></span>
              )}
            </div>
            <div className="p-4">
              <p className="truncate font-semibold">{tenant.branding.displayName}</p>
              <p className="mt-0.5 line-clamp-2 text-sm text-ink-500">{tagline.trim() || 'Votre accroche'}</p>
            </div>
            {coverFailed && <p className="px-4 pb-3 text-xs text-red-600">La photo n’a pas pu être chargée depuis cette adresse.</p>}
          </div>
        </div>

        <div className="border-t border-slate-100 pt-4">
          <p className="mb-3 text-sm font-semibold text-ink-800">Site et réseaux sociaux</p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {socialNetworks.map(item => (
              <Field key={item.network} label={item.label} hint={invalidLinks.includes(item) ? 'Adresse complète en https://' : undefined}>
                <Input type="url" disabled={!editable} value={links[item.network]} placeholder={item.placeholder}
                  onChange={event => setLinks(current => ({...current, [item.network]: event.target.value}))} />
              </Field>
            ))}
          </div>
        </div>

        {editable && <Button type="submit" busy={busy === 'profile'} disabled={!changed || coverInvalid || invalidLinks.length > 0}>Enregistrer la présentation</Button>}
      </form>
    </Section>
  );
}
