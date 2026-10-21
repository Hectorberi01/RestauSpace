'use client';
import {useEffect, useState} from 'react';
import QRCode from 'qrcode';
import {Copy, Printer, Star} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {dateTime, number, percent} from '@/lib/format';
import {surveyResults, type Survey} from '@/lib/reviews';
import {useConsole} from '@/components/console';
import {Alert, Button, Spinner, useToast} from '@/components/ui';

const siteUrl = (process.env.NEXT_PUBLIC_SITE_URL ?? (process.env.NODE_ENV === 'development' ? 'http://localhost:3002' : '')).replace(/\/+$/, '');

/** Lien public du questionnaire (et son QR code) : à afficher en salle, sur le ticket ou à envoyer. */
export function SurveyShare({survey}: {survey: Survey}) {
  const {location} = useConsole();
  const toast = useToast();
  const [withLocation, setWithLocation] = useState(true);
  const [qr, setQr] = useState<string | null>(null);
  const url = `${siteUrl}/questionnaire/${survey.id}${withLocation && location ? `?etablissement=${location.id}` : ''}`;
  useEffect(() => {
    if (!siteUrl) return;
    let alive = true;
    void QRCode.toString(url, {type: 'svg', errorCorrectionLevel: 'M', margin: 1}).then(svg => {
      if (alive) setQr(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`);
    });
    return () => {
      alive = false;
    };
  }, [url]);

  if (!siteUrl) return <Alert>L’adresse du site public n’est pas configurée (variable NEXT_PUBLIC_SITE_URL) : le lien ne peut pas être produit.</Alert>;

  return (
    <section className="flex flex-col gap-4 rounded-2xl border border-slate-200 p-4 sm:flex-row sm:items-center">
      {qr ? <img src={qr} alt="QR code du questionnaire" className="size-32 shrink-0 self-center" /> : <div className="size-32 shrink-0 animate-pulse self-center rounded-lg bg-slate-100" />}
      <div className="min-w-0 flex-1 space-y-2 text-sm">
        <p className="font-semibold">Recueillir les réponses</p>
        <p className="text-ink-500">Affichez ce QR code en salle ou envoyez le lien. Après une commande en ligne remise, le site propose aussi le dernier questionnaire publié.</p>
        <p className="truncate rounded-lg bg-slate-50 px-2 py-1 font-mono text-xs">{url}</p>
        {location && (
          <label className="flex items-center gap-2 text-xs text-ink-600">
            <input type="checkbox" className="size-4 accent-brand-600" checked={withLocation} onChange={event => setWithLocation(event.target.checked)} />
            Rattacher les réponses à {location.name}
          </label>
        )}
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" onClick={() => void navigator.clipboard?.writeText(url).then(() => toast('success', 'Lien copié.'), () => toast('error', 'Copie impossible.'))}>
            <Copy className="size-4" aria-hidden="true" />Copier le lien
          </Button>
          {qr && (
            <Button size="sm" variant="secondary" onClick={() => {
              const win = window.open('', '_blank', 'noopener=no,width=480,height=640');
              if (!win) return;
              win.document.title = survey.title;
              const figure = win.document.createElement('div');
              figure.style.cssText = 'font-family:sans-serif;text-align:center;padding:32px';
              const title = win.document.createElement('h1');
              title.textContent = survey.title;
              const image = win.document.createElement('img');
              image.src = qr;
              image.style.cssText = 'width:260px;height:260px';
              const caption = win.document.createElement('p');
              caption.textContent = 'Scannez pour donner votre avis (anonyme)';
              figure.append(title, image, caption);
              win.document.body.append(figure);
              image.onload = () => win.print();
            }}><Printer className="size-4" aria-hidden="true" />Imprimer le QR</Button>
          )}
        </div>
      </div>
    </section>
  );
}

/** Synthèse des réponses : notes (moyenne, répartition), choix, dernières réponses libres. */
export function SurveyResultsPanel({survey}: {survey: Survey}) {
  const [days, setDays] = useState<number | null>(30);
  const from = days === null ? null : new Date(Date.now() - days * 86_400_000).toISOString();
  const {data, error} = useResource(signal => surveyResults(survey.id, from, null, signal), [survey.id, days]);

  return (
    <section className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold">Résultats{data ? ` · ${data.responses} réponse${data.responses > 1 ? 's' : ''}` : ''}</h3>
        <div className="flex gap-1" role="group" aria-label="Période">
          {[[7, '7 j'], [30, '30 j'], [null, 'Tout']].map(([value, label]) => (
            <button key={String(value)} type="button" onClick={() => setDays(value as number | null)}
              className={`rounded-lg px-2.5 py-1 text-xs font-semibold ${days === value ? 'bg-brand-50 text-brand-700' : 'text-ink-500 hover:bg-slate-100'}`}>{label}</button>
          ))}
        </div>
      </div>
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.responses === 0 ? (
        <p className="rounded-xl bg-slate-50 p-4 text-sm text-ink-500">Aucune réponse sur la période.</p>
      ) : (
        <ol className="space-y-3">
          {data.isSampled && <Alert tone="blue">Synthèse calculée sur les 5 000 réponses les plus récentes.</Alert>}
          {data.questions.map(question => (
            <li key={question.questionId} className="rounded-xl border border-slate-200 p-3 text-sm">
              <p className="font-medium">{question.text}</p>
              <p className="text-xs text-ink-500">{question.answered} réponse{question.answered > 1 ? 's' : ''}</p>
              {question.ratings && (
                <div className="mt-2 flex flex-wrap items-center gap-4">
                  <p className="flex items-center gap-1 text-2xl font-bold">{question.average === null ? '–' : number(question.average, 1)}<Star className="size-5 fill-amber-400 text-amber-400" aria-hidden="true" /></p>
                  <div className="min-w-48 flex-1 space-y-1">
                    {[5, 4, 3, 2, 1].map(value => {
                      const count = question.ratings![value - 1];
                      const share = question.answered ? count / question.answered : 0;
                      return (
                        <div key={value} className="flex items-center gap-2 text-xs">
                          <span className="w-3 text-right">{value}</span>
                          <span className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100"><span className="block h-full rounded-full bg-amber-400" style={{width: `${share * 100}%`}} /></span>
                          <span className="w-8 text-right tabular-nums text-ink-500">{count}</span>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
              {question.choices && (
                <ul className="mt-2 space-y-1">
                  {question.choices.map(choice => {
                    const share = question.answered ? choice.count / question.answered : 0;
                    return (
                      <li key={choice.choice} className="text-xs">
                        <div className="flex justify-between"><span>{choice.choice}</span><span className="tabular-nums text-ink-500">{choice.count} · {percent(share)}</span></div>
                        <span className="mt-0.5 block h-1.5 overflow-hidden rounded-full bg-slate-100"><span className="block h-full rounded-full bg-brand-500" style={{width: `${share * 100}%`}} /></span>
                      </li>
                    );
                  })}
                </ul>
              )}
              {question.texts && (
                question.texts.length === 0 ? <p className="mt-1 text-xs text-ink-500">Aucune réponse libre.</p> : (
                  <ul className="mt-2 max-h-56 space-y-2 overflow-y-auto">
                    {question.texts.map((item, index) => (
                      <li key={index} className="rounded-lg bg-slate-50 px-3 py-2">
                        <p className="whitespace-pre-line">{item.text}</p>
                        <p className="mt-0.5 text-xs text-ink-500">{dateTime(item.submittedAtUtc)}</p>
                      </li>
                    ))}
                  </ul>
                )
              )}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
