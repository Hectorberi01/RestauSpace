'use client';
import {useEffect, useState} from 'react';
import {useSearchParams} from 'next/navigation';
import {CheckCircle2, Loader2, MailX} from 'lucide-react';
import {site} from '@/content/site';
import {tr, translated, useLang} from '@/lib/i18n';

type Channel = 'Email' | 'Sms' | 'WhatsApp';
type Link = {restaurant: string; channel: Channel};
type State = {step: 'loading'} | {step: 'invalid'} | {step: 'ready'; link: Link} | {step: 'sending'; link: Link} | {step: 'done'; link: Link};

const channelLabel: Record<Channel, string> = translated({Email: 'par e-mail', Sms: 'par SMS', WhatsApp: 'sur WhatsApp'});

async function call(path: string, token: string): Promise<Link | null> {
  const response = await fetch(`${site.apiUrl}/v1/public/notifications/unsubscribe${path}`, {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({token}),
  });
  if (response.status === 404 || response.status === 400) return null;
  if (!response.ok) throw new Error(String(response.status));
  return (await response.json()) as Link;
}

/** La lecture du lien n'a aucun effet : seul le bouton désinscrit (les messageries ouvrent parfois les liens d'elles-mêmes). */
export function Unsubscribe() {
  useLang();
  const token = useSearchParams().get('jeton') ?? '';
  const [state, setState] = useState<State>({step: 'loading'});
  const [error, setError] = useState<string>();

  useEffect(() => {
    if (!token) {
      setState({step: 'invalid'});
      return;
    }
    call('/preview', token)
      .then(link => setState(link ? {step: 'ready', link} : {step: 'invalid'}))
      .catch(() => {
        setState({step: 'invalid'});
        setError(tr('Le service est momentanément indisponible. Réessayez dans quelques instants.'));
      });
  }, [token]);

  const confirm = async (link: Link) => {
    setError(undefined);
    setState({step: 'sending', link});
    try {
      const done = await call('', token);
      setState(done ? {step: 'done', link: done} : {step: 'invalid'});
    } catch {
      setState({step: 'ready', link});
      setError(tr('Votre désinscription n’a pas pu être enregistrée. Réessayez dans quelques instants.'));
    }
  };

  if (state.step === 'loading') {
    return <p className="flex items-center justify-center gap-2 py-8 text-ink-600"><Loader2 className="size-4 animate-spin" aria-hidden="true" />{tr('Chargement…')}</p>;
  }

  if (state.step === 'invalid') {
    return (
      <div className="py-4 text-center">
        <MailX className="mx-auto size-12 text-ink-400" aria-hidden="true" />
        <h1 className="mt-4 text-xl font-semibold">{tr('Lien de désinscription invalide')}</h1>
        <p className="mt-2 text-ink-600">{error ?? tr('Ce lien est incomplet ou n’est plus valable. Utilisez le lien du dernier message reçu, ou répondez au restaurant pour ne plus rien recevoir.')}</p>
      </div>
    );
  }

  if (state.step === 'done') {
    return (
      <div className="py-4 text-center" role="status">
        <CheckCircle2 className="mx-auto size-12 text-emerald-600" aria-hidden="true" />
        <h1 className="mt-4 text-xl font-semibold">{tr('Vous êtes désinscrit')}</h1>
        <p className="mt-2 text-ink-600">
          {tr('Vous ne recevrez plus d’offres ni d’actualités de')} {state.link.restaurant} {channelLabel[state.link.channel]}{tr('. Les messages liés à vos commandes et réservations continuent de vous parvenir.')}
        </p>
      </div>
    );
  }

  return (
    <div className="text-center">
      <h1 className="text-xl font-semibold">{tr('Ne plus recevoir les offres de')} {state.link.restaurant}</h1>
      <p className="mt-2 text-ink-600">
        {tr('Vous ne recevrez plus de messages promotionnels de')} {state.link.restaurant} {channelLabel[state.link.channel]}{tr('. Les confirmations de commande et de réservation ne sont pas concernées.')}
      </p>
      {error && <p role="alert" className="mt-4 rounded-xl bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>}
      <button type="button" disabled={state.step === 'sending'} onClick={() => confirm(state.link)}
        className="mt-6 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-brand-600 px-6 py-3 font-semibold text-white transition hover:bg-brand-700 disabled:opacity-60 sm:w-auto">
        {state.step === 'sending' && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
        {state.step === 'sending' ? tr('Enregistrement…') : tr('Me désinscrire')}
      </button>
    </div>
  );
}
