'use client';
import {useEffect, useMemo, useState, type FormEvent} from 'react';
import Link from 'next/link';
import {useRouter} from 'next/navigation';
import {ArrowRight, Check, Eye, EyeOff, MailCheck, Sparkles} from 'lucide-react';
import {describe} from '@/lib/api';
import {loadSignUpOptions, PASSWORD_MAX, PASSWORD_MIN, resendVerification, signUp, type SignUpOptions, type SignUpPlan} from '@/lib/account';
import {money} from '@/lib/format';
import {useSession} from '@/components/console';
import {Logo} from '@/components/shell';
import {Alert, Button, cx, Field, Input, Select, Skeleton, useToast} from '@/components/ui';

const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL ?? 'http://localhost:3002').replace(/\/+$/, '');

// Le site public parle de marchés (fr, bj) ; ici, de pays.
const marketCountry: Record<string, string> = {fr: 'FR', bj: 'BJ'};

const quotas: [string, string][] = [
  ['locations.count', 'Établissements'],
  ['staff.count', 'Membres de l’équipe'],
  ['orders.monthly', 'Commandes par mois'],
];

const normalize = (value: string) => value.normalize('NFD').replace(/[̀-ͯ]/g, '').trim().toLowerCase();

function quota(plan: SignUpPlan, code: string) {
  const entitlement = plan.entitlements.find(item => item.featureCode === code);
  if (!entitlement?.enabled) return '—';
  return entitlement.limit === null ? 'Illimité' : new Intl.NumberFormat('fr-FR').format(entitlement.limit);
}

export default function SignUpPage() {
  const router = useRouter();
  const session = useSession();
  const [options, setOptions] = useState<SignUpOptions>();
  const [loadError, setLoadError] = useState<string>();
  const [country, setCountry] = useState('FR');
  const [interval, setBilling] = useState<'Monthly' | 'Yearly'>('Monthly');
  const [planId, setPlanId] = useState('');
  const [form, setForm] = useState({firstName: '', lastName: '', restaurantName: '', email: '', password: '', website: ''});
  const [showPassword, setShowPassword] = useState(false);
  const [acceptTerms, setAcceptTerms] = useState(false);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState<{email: string; trialDays: number} | null>(null);

  useEffect(() => {
    if (session) router.replace(session.tenantId ? '/tableau-de-bord' : '/restaurants');
  }, [session, router]);

  useEffect(() => {
    const controller = new AbortController();
    loadSignUpOptions(controller.signal)
      .then(loaded => {
        const params = new URLSearchParams(window.location.search);
        const requestedCountry = params.get('pays')?.trim() ?? '';
        const code = marketCountry[requestedCountry.toLowerCase()] ?? requestedCountry.toUpperCase();
        const chosenCountry = loaded.countries.find(item => item.code === code) ?? loaded.countries[0];
        const currency = chosenCountry?.currencyCode;
        const requestedPlan = normalize(params.get('plan') ?? '');
        const inCurrency = loaded.plans.filter(plan => plan.currencyCode === currency);
        const chosenPlan = inCurrency.find(plan => normalize(plan.name) === requestedPlan) ?? inCurrency.find(plan => plan.billingInterval === 'Monthly' && plan.price > 0) ?? inCurrency[0];
        setOptions(loaded);
        if (chosenCountry) setCountry(chosenCountry.code);
        if (chosenPlan) {
          setPlanId(chosenPlan.id);
          setBilling(chosenPlan.billingInterval);
        }
      })
      .catch(err => {
        if (!controller.signal.aborted) setLoadError(describe(err));
      });
    return () => controller.abort();
  }, []);

  const currency = options?.countries.find(item => item.code === country)?.currencyCode;
  const inCurrency = useMemo(() => options?.plans.filter(plan => plan.currencyCode === currency) ?? [], [options, currency]);
  const hasYearly = inCurrency.some(plan => plan.billingInterval === 'Yearly');
  const shown = inCurrency.filter(plan => plan.billingInterval === interval || (interval === 'Yearly' && plan.price === 0));
  const plan = inCurrency.find(item => item.id === planId);
  const trialDays = options?.trialDays ?? 30;

  // Changer de pays ou de rythme garde, si possible, le même plan (même nom de base).
  const pick = (nextCountry: string, nextInterval: 'Monthly' | 'Yearly') => {
    const nextCurrency = options?.countries.find(item => item.code === nextCountry)?.currencyCode;
    const candidates = options?.plans.filter(item => item.currencyCode === nextCurrency) ?? [];
    const base = plan ? normalize(plan.name).replace(/ annuel$/, '') : '';
    const sameTier = candidates.find(item => normalize(item.name).replace(/ annuel$/, '') === base && (item.billingInterval === nextInterval || item.price === 0));
    const fallback = candidates.find(item => item.billingInterval === nextInterval && item.price > 0) ?? candidates[0];
    setCountry(nextCountry);
    setBilling(nextInterval);
    setPlanId((sameTier ?? fallback)?.id ?? '');
  };

  const set = (key: keyof typeof form) => (value: string) => setForm(current => ({...current, [key]: value}));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!plan) return setError('Choisissez un plan.');
    if (form.password.length < PASSWORD_MIN) return setError(`Le mot de passe doit contenir au moins ${PASSWORD_MIN} caractères.`);
    if (!acceptTerms) return setError('Acceptez les conditions d’utilisation pour créer votre compte.');
    setBusy(true);
    setError(undefined);
    try {
      const email = form.email.trim();
      const result = await signUp({...form, email, firstName: form.firstName.trim(), lastName: form.lastName.trim(), restaurantName: form.restaurantName.trim(), country, planId: plan.id, acceptTerms});
      setSent({email, trialDays: result?.trialDays ?? trialDays});
      window.scrollTo({top: 0});
    } catch (err) {
      setError(describe(err));
    } finally {
      setBusy(false);
    }
  };

  if (sent) return <CheckInbox email={sent.email} trialDays={sent.trialDays} free={plan?.price === 0} />;

  return (
    <div className="min-h-dvh bg-gradient-to-b from-brand-50 to-slate-50 px-4 py-8 sm:py-12">
      <div className="mx-auto max-w-5xl">
        <div className="flex items-center justify-between gap-4">
          <a href={SITE_URL} aria-label="RestauSpace, site"><Logo /></a>
          <p className="text-sm text-ink-500">Déjà un compte ? <Link href="/connexion" className="font-semibold text-brand-700 hover:underline">Se connecter</Link></p>
        </div>

        <div className="mt-8 grid gap-6 lg:grid-cols-[1.1fr_1fr]">
          <section className="min-w-0 rounded-2xl border border-slate-200 bg-white p-6 shadow-xl shadow-brand-900/5 sm:p-7">
            <p className="inline-flex items-center gap-1.5 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-semibold text-brand-700"><Sparkles className="size-3.5" aria-hidden="true" />{trialLabel(trialDays)} d’essai gratuit</p>
            <h1 className="mt-3 text-2xl font-bold tracking-tight text-ink-900">Créez votre restaurant</h1>
            <p className="mt-1 text-sm text-ink-500">Choisissez votre plan : vous ne payez rien pendant l’essai, et vous pouvez en changer à tout moment.</p>

            {loadError ? (
              <div className="mt-6"><Alert>{loadError}</Alert></div>
            ) : !options ? (
              <div className="mt-6 space-y-3"><Skeleton className="h-10" /><Skeleton className="h-24" /><Skeleton className="h-24" /></div>
            ) : (
              <div className="mt-6 space-y-5">
                <div className="grid gap-4 sm:grid-cols-[1fr_auto] sm:items-end">
                  <Field label="Pays du restaurant" hint={currency ? `Prix et paiements en ${currency === 'XOF' ? 'francs CFA' : 'euros'}.` : undefined}>
                    <Select value={country} onChange={event => pick(event.target.value, interval)}>
                      {options.countries.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}
                    </Select>
                  </Field>
                  {hasYearly && (
                    <div role="radiogroup" aria-label="Facturation" className="inline-flex h-11 rounded-xl bg-slate-100 p-1">
                      {(['Monthly', 'Yearly'] as const).map(value => (
                        <button key={value} type="button" role="radio" aria-checked={interval === value} onClick={() => pick(country, value)}
                          className={cx('rounded-lg px-4 text-sm font-semibold transition', interval === value ? 'bg-white text-ink-900 shadow-sm' : 'text-ink-500 hover:text-ink-800')}>
                          {value === 'Monthly' ? 'Mensuel' : 'Annuel'}
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                {shown.length === 0 ? (
                  <Alert tone="amber">Aucun plan n’est encore proposé dans ce pays : contactez-nous pour ouvrir votre restaurant.</Alert>
                ) : (
                  <div role="radiogroup" aria-label="Plan" className="grid gap-3 sm:grid-cols-2">
                    {shown.map(item => {
                      const active = item.id === planId;
                      return (
                        <button key={item.id} type="button" role="radio" aria-checked={active} onClick={() => setPlanId(item.id)}
                          className={cx('min-w-0 rounded-xl border p-4 text-left transition', active ? 'border-brand-500 bg-brand-50/40 ring-1 ring-brand-500' : 'border-slate-200 hover:border-brand-200 hover:bg-slate-50')}>
                          <span className="flex items-start justify-between gap-2">
                            <span className="font-semibold text-ink-900">{item.name.replace(/ annuel$/i, '')}</span>
                            <span className={cx('grid size-5 shrink-0 place-items-center rounded-full border-2', active ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-300')}>
                              {active && <Check className="size-3" strokeWidth={3} aria-hidden="true" />}
                            </span>
                          </span>
                          <span className="mt-1 block text-lg font-bold text-ink-900">
                            {item.price === 0 ? 'Gratuit' : money(item.price, item.currencyCode)}
                            {item.price > 0 && <span className="text-sm font-normal text-ink-500"> / {item.billingInterval === 'Yearly' ? 'an' : 'mois'}{item.currencyCode === 'EUR' ? ' HT' : ''}</span>}
                          </span>
                          <span className="mt-3 block space-y-1 text-xs text-ink-600">
                            {quotas.map(([code, label]) => (
                              <span key={code} className="flex justify-between gap-2"><span>{label}</span><strong className="text-ink-900">{quota(item, code)}</strong></span>
                            ))}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                )}

                <ul className="space-y-1.5 text-sm text-ink-600">
                  {[`${trialLabel(trialDays)} pour tout essayer, sans carte bancaire`, 'Paiement en ligne quand vous le décidez, depuis la console', 'Changement de plan à tout moment'].map(line => (
                    <li key={line} className="flex items-center gap-2"><Check className="size-4 shrink-0 text-brand-600" aria-hidden="true" />{line}</li>
                  ))}
                </ul>
              </div>
            )}
          </section>

          <form onSubmit={submit} className="min-w-0 rounded-2xl border border-slate-200 bg-white p-6 shadow-xl shadow-brand-900/5 sm:p-7">
            <h2 className="text-lg font-bold text-ink-900">Votre compte</h2>
            <p className="mt-1 text-sm text-ink-500">Vous en serez le propriétaire : vous inviterez ensuite votre équipe.</p>
            <div className="mt-5 space-y-4">
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Prénom"><Input autoComplete="given-name" maxLength={60} value={form.firstName} onChange={event => set('firstName')(event.target.value)} /></Field>
                <Field label="Nom"><Input autoComplete="family-name" maxLength={60} value={form.lastName} onChange={event => set('lastName')(event.target.value)} /></Field>
              </div>
              <Field label="Nom du restaurant">
                <Input required minLength={2} maxLength={100} autoComplete="organization" value={form.restaurantName} onChange={event => set('restaurantName')(event.target.value)} placeholder="Le Bon Goût" />
              </Field>
              <Field label="Email professionnel" hint="Un lien de confirmation y sera envoyé.">
                <Input type="email" required autoComplete="email" value={form.email} onChange={event => set('email')(event.target.value)} />
              </Field>
              <Field label="Mot de passe" hint={`${PASSWORD_MIN} caractères au moins.`}>
                <div className="relative">
                  <Input type={showPassword ? 'text' : 'password'} required minLength={PASSWORD_MIN} maxLength={PASSWORD_MAX} autoComplete="new-password" className="pr-11" value={form.password} onChange={event => set('password')(event.target.value)} />
                  <button type="button" onClick={() => setShowPassword(value => !value)} aria-label={showPassword ? 'Masquer le mot de passe' : 'Afficher le mot de passe'}
                    className="absolute inset-y-0 right-0 grid w-11 place-items-center text-ink-500 hover:text-ink-800">
                    {showPassword ? <EyeOff className="size-4" aria-hidden="true" /> : <Eye className="size-4" aria-hidden="true" />}
                  </button>
                </div>
              </Field>
              <input type="text" name="website" tabIndex={-1} autoComplete="off" aria-hidden="true" className="hidden" value={form.website} onChange={event => set('website')(event.target.value)} />
              <label className="flex items-start gap-2.5 text-sm text-ink-600">
                <input type="checkbox" className="mt-0.5 size-4 shrink-0 accent-brand-600" checked={acceptTerms} onChange={event => setAcceptTerms(event.target.checked)} />
                <span>
                  J’accepte les <a href={`${SITE_URL}/mentions-legales`} target="_blank" rel="noreferrer" className="font-medium text-brand-700 hover:underline">conditions d’utilisation</a> et
                  la <a href={`${SITE_URL}/confidentialite`} target="_blank" rel="noreferrer" className="font-medium text-brand-700 hover:underline">politique de confidentialité</a>.
                </span>
              </label>
              {error && <Alert>{error}</Alert>}
              <Button type="submit" size="lg" busy={busy} disabled={!options || !plan} className="w-full">
                {plan?.price === 0 ? 'Créer mon compte gratuit' : `Démarrer mon essai${plan ? ` ${plan.name.replace(/ annuel$/i, '')}` : ''}`}
                {!busy && <ArrowRight className="size-4" aria-hidden="true" />}
              </Button>
              {plan && plan.price > 0 && (
                <p className="text-center text-xs text-ink-500">Ensuite {money(plan.price, plan.currencyCode)} / {plan.billingInterval === 'Yearly' ? 'an' : 'mois'}, si vous continuez.</p>
              )}
            </div>
          </form>
        </div>
      </div>
    </div>
  );
}

/** Durée d'essai lisible : environ un mois s'affiche « 1 mois ». */
function trialLabel(days: number): string {
  return days >= 28 && days <= 31 ? '1 mois' : `${days} jours`;
}

function CheckInbox({email, trialDays, free}: {email: string; trialDays: number; free: boolean}) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const resend = async () => {
    setBusy(true);
    try {
      await resendVerification(email);
      toast('success', 'Email renvoyé.');
    } catch (err) {
      toast('error', describe(err));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="grid min-h-dvh place-items-center bg-gradient-to-b from-brand-50 to-slate-50 px-4 py-10">
      <div className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-7 shadow-xl shadow-brand-900/5">
        <Logo />
        <span className="mt-6 grid size-12 place-items-center rounded-2xl bg-brand-50 text-brand-600"><MailCheck className="size-6" aria-hidden="true" /></span>
        <h1 className="mt-4 text-2xl font-bold tracking-tight">Vérifiez votre boîte mail</h1>
        <p className="mt-2 text-sm text-ink-600">
          Votre restaurant est créé. Nous avons envoyé un lien de confirmation à <strong className="break-all text-ink-900">{email}</strong> : ouvrez-le, confirmez avec votre mot de passe, puis connectez-vous.
        </p>
        <p className="mt-3 text-sm text-ink-600">
          {free ? 'Votre plan gratuit démarre tout de suite, pour un mois. Ensuite, choisissez un plan payant pour continuer.' : `Votre essai de ${trialLabel(trialDays)} a commencé. Vous pourrez payer en ligne depuis la page Abonnement quand vous le souhaitez.`}
        </p>
        <div className="mt-6 space-y-3">
          <Link href="/connexion" className="flex h-11 w-full items-center justify-center rounded-xl bg-brand-600 text-sm font-semibold text-white hover:bg-brand-700">Aller à la connexion</Link>
          <Button variant="secondary" size="lg" busy={busy} onClick={resend} className="w-full">Renvoyer l’email</Button>
        </div>
        <p className="mt-4 text-xs text-ink-500">Rien reçu ? Regardez dans les courriers indésirables. Le lien reste valable 24 heures.</p>
      </div>
    </div>
  );
}
