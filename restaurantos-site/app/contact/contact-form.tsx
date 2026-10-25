'use client';
import {useState, type FormEvent} from 'react';
import {useSearchParams} from 'next/navigation';
import {ArrowRight, CheckCircle2, ChevronDown, Loader2, Send} from 'lucide-react';
import {toast} from '@/components/toast';
import {site} from '@/content/site';
import {tiers} from '@/content/plans';

const field = 'mt-1.5 w-full rounded-xl border border-slate-200 bg-white px-3.5 py-2.5 text-sm text-ink-900 outline-none transition placeholder:text-ink-400 focus:border-brand-500 focus:ring-4 focus:ring-brand-100';
const label = 'text-[0.8rem] font-medium text-ink-700';
const select = `${field} cursor-pointer appearance-none pr-9`;
const chevron = <ChevronDown className="pointer-events-none absolute bottom-3 right-3.5 size-4 text-ink-400" aria-hidden="true" />;
const star = <span className="text-red-500" aria-hidden="true"> *</span>;

const subjects = ['Demande de démonstration', 'Tarifs et abonnement', 'Intégration et API', 'Autre question'];

const countries: {label: string; dial?: string}[] = [
  {label: 'Bénin', dial: '229'},
  {label: 'Côte d’Ivoire', dial: '225'},
  {label: 'Sénégal', dial: '221'},
  {label: 'Togo', dial: '228'},
  {label: 'Burkina Faso', dial: '226'},
  {label: 'Mali', dial: '223'},
  {label: 'Niger', dial: '227'},
  {label: 'France', dial: '33'},
  {label: 'Autre pays'},
];

const errors: Record<string, string> = {
  'common.email.invalid': 'L’adresse email n’est pas valide.',
  'common.phone.invalid': 'Le numéro de téléphone n’est pas valide : indiquez-le avec l’indicatif du pays, par exemple +229 01 97 00 00 00.',
  'tenants.demo_request.contact_missing': 'Indiquez au moins un email ou un téléphone pour que nous puissions vous répondre.',
  'tenants.demo_request.contact_name_invalid': 'Indiquez votre nom (2 à 100 caractères).',
  'tenants.demo_request.restaurant_name_invalid': 'Indiquez le nom de votre restaurant (2 à 100 caractères).',
  'tenants.demo_request.country_invalid': 'Choisissez votre pays.',
  'tenants.demo_request.message_invalid': 'Le message est limité à 2 000 caractères.',
};

/** Numéro au format international attendu par l'API : indicatif du pays ajouté s'il manque. */
function international(phone: string, country: string): string {
  const compact = phone.replace(/[\s.\-()]/g, '');
  if (!compact) return '';
  if (compact.startsWith('+')) return compact;
  if (compact.startsWith('00')) return `+${compact.slice(2)}`;
  const dial = countries.find(item => item.label === country)?.dial;
  if (!dial) return compact;
  return `+${dial}${dial === '33' ? compact.replace(/^0/, '') : compact}`;
}

export function ContactForm({initialSubjectIndex}: {initialSubjectIndex?: number} = {}) {
  const params = useSearchParams();
  const initialPlan = params.get('plan') ?? '';
  const initialCountry = params.get('pays') === 'fr' ? 'France' : params.get('pays') === 'bj' ? 'Bénin' : '';
  const initialSubject = params.get('sujet') === 'api' ? subjects[2] : initialPlan ? subjects[1] : initialSubjectIndex !== undefined ? subjects[initialSubjectIndex] ?? '' : '';
  const [values, setValues] = useState({name: '', restaurant: '', email: '', phone: '', country: initialCountry, subject: initialSubject, plan: initialPlan, message: '', website: ''});
  const [error, setError] = useState<string>();
  const [state, setState] = useState<'idle' | 'sending' | 'sent'>('idle');
  const set = (key: keyof typeof values) => (e: {target: {value: string}}) => setValues(v => ({...v, [key]: e.target.value}));
  const dial = countries.find(item => item.label === values.country)?.dial;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!values.email.trim() && !values.phone.trim()) {
      setError(errors['tenants.demo_request.contact_missing']);
      return;
    }
    setError(undefined);
    setState('sending');
    const message = [values.subject && `Sujet : ${values.subject}`, values.message.trim()].filter(Boolean).join('\n\n');
    try {
      const response = await fetch(`${site.apiUrl}/v1/demo-requests`, {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({
          contactName: values.name,
          restaurantName: values.restaurant,
          email: values.email.trim() || null,
          phone: international(values.phone, values.country) || null,
          country: values.country,
          planInterest: values.plan || null,
          message: message || null,
          website: values.website,
        }),
      });
      if (response.ok) {
        setState('sent');
        toast('Message envoyé : nous revenons vers vous très vite.');
        return;
      }
      let code: string | undefined;
      try {
        code = (await response.json()).code;
      } catch {}
      setError(
        response.status === 429
          ? 'Trop de demandes envoyées depuis votre connexion : réessayez dans une heure.'
          : (code && errors[code]) ?? 'Votre demande n’a pas pu être envoyée. Réessayez dans quelques instants.',
      );
    } catch {
      setError(`Votre demande n’a pas pu être envoyée (connexion impossible).${site.contact.email ? ` Écrivez-nous à ${site.contact.email}.` : ' Réessayez dans quelques instants.'}`);
    }
    setState('idle');
  };

  if (state === 'sent') {
    return (
      <div className="flex flex-col items-center py-10 text-center" role="status">
        <span className="animate-pop grid size-16 place-items-center rounded-full bg-emerald-50 ring-8 ring-emerald-50/50">
          <CheckCircle2 className="size-9 text-emerald-600" aria-hidden="true" />
        </span>
        <h3 className="mt-5 text-xl font-semibold text-ink-900">Merci, votre message est envoyé.</h3>
        <p className="mt-2 max-w-sm text-sm leading-relaxed text-ink-600">Nous revenons vers vous très vite au sujet de {values.restaurant || 'votre restaurant'}.</p>
      </div>
    );
  }

  return (
    <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
      <label className={label}>Nom complet{star}<input required minLength={2} maxLength={100} autoComplete="name" placeholder="Votre nom" className={field} value={values.name} onChange={set('name')} /></label>
      <label className={label}>Restaurant{star}<input required minLength={2} maxLength={100} autoComplete="organization" placeholder="Nom de votre restaurant" className={field} value={values.restaurant} onChange={set('restaurant')} /></label>
      <label className={label}>Email<input type="email" maxLength={254} autoComplete="email" placeholder="votre@email.com" className={field} value={values.email} onChange={set('email')} /></label>
      <div className={label}>
        <span id="contact-phone">Téléphone ou WhatsApp</span>
        <div className="mt-1.5 flex rounded-xl border border-slate-200 bg-white transition focus-within:border-brand-500 focus-within:ring-4 focus-within:ring-brand-100">
          <span className="relative flex shrink-0 items-center gap-1 border-r border-slate-200 pl-3.5 pr-2.5 text-sm text-ink-900">
            <span className={values.country ? '' : 'text-ink-400'}>{dial ? `+${dial}` : values.country ? 'Autre' : 'Pays'}</span>
            <ChevronDown className="size-3.5 text-ink-400" aria-hidden="true" />
            <select required aria-label="Pays" className="absolute inset-0 cursor-pointer opacity-0" value={values.country} onChange={set('country')}>
              <option value="">Choisir le pays</option>
              {countries.map(country => <option key={country.label} value={country.label}>{country.label}{country.dial ? ` (+${country.dial})` : ''}</option>)}
            </select>
          </span>
          <input type="tel" maxLength={25} autoComplete="tel" aria-labelledby="contact-phone" placeholder="97 00 00 00" className="min-w-0 flex-1 rounded-r-xl bg-transparent px-3 py-2.5 text-sm text-ink-900 outline-none placeholder:text-ink-400" value={values.phone} onChange={set('phone')} />
        </div>
      </div>
      <label className={`relative ${label}`}>Sujet{star}
        <select required className={`${select} ${values.subject ? '' : '!text-ink-400'}`} value={values.subject} onChange={set('subject')}>
          <option value="">Sélectionnez un sujet</option>
          {subjects.map(subject => <option key={subject}>{subject}</option>)}
        </select>
        {chevron}
      </label>
      <label className={`relative ${label}`}>Plan qui vous intéresse
        <select className={select} value={values.plan} onChange={set('plan')}>
          <option value="">Je ne sais pas encore</option>
          {tiers('fr').map(tier => <option key={tier.name}>{tier.name}</option>)}
        </select>
        {chevron}
      </label>
      <label className={`${label} sm:col-span-2`}>Message
        <textarea rows={4} maxLength={1900} className={`${field} resize-y`} value={values.message} onChange={set('message')} placeholder="Nombre d’établissements, de couverts, ce que vous utilisez aujourd’hui..." />
      </label>
      <label className="absolute -left-[9999px] h-px w-px overflow-hidden" aria-hidden="true">
        Site web
        <input tabIndex={-1} autoComplete="off" value={values.website} onChange={set('website')} />
      </label>
      {error && <p role="alert" className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-700 sm:col-span-2">{error}</p>}
      <button type="submit" disabled={state === 'sending'} className="btn-motion group inline-flex w-full items-center justify-center gap-2 rounded-xl bg-brand-600 px-6 py-3 text-sm font-semibold text-white shadow-lg shadow-brand-600/25 hover:bg-brand-700 hover:shadow-xl hover:shadow-brand-600/30 disabled:opacity-60 sm:col-span-2">
        {state === 'sending' ? <Loader2 className="size-4 animate-spin" aria-hidden="true" /> : <Send className="size-4" aria-hidden="true" />}
        {state === 'sending' ? 'Envoi…' : 'Envoyer le message'}
        {state !== 'sending' && <ArrowRight className="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />}
      </button>
      <p className="text-xs leading-relaxed text-ink-500 sm:col-span-2">
        Indiquez au moins un email ou un téléphone. Vos coordonnées servent uniquement à vous répondre (voir la page <a href="/confidentialite" className="font-medium text-brand-700 hover:underline">Confidentialité</a>).
      </p>
    </form>
  );
}
