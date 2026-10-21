'use client';
import {useState} from 'react';
import {Star} from 'lucide-react';
import {errorMessage, rememberAnswered, submitSurvey, type Survey, type SurveyAnswer} from '@/lib/storefront';
import {AccentButton, cx, Notice} from '@/components/order-ui';
import {tr, useLang} from '@/lib/i18n';

type Values = Record<string, {rating?: number; text?: string; choices?: string[]}>;

/** Formulaire d'un questionnaire : une note en étoiles, un texte libre ou des choix selon la question. */
export function SurveyForm({survey, orderToken, locationId}: {survey: Survey; orderToken: string | null; locationId: string | null}) {
  useLang();
  const [values, setValues] = useState<Values>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const set = (id: string, value: Values[string]) => setValues(current => ({...current, [id]: {...current[id], ...value}}));
  const answered = (id: string) => {
    const value = values[id];
    return Boolean(value && (value.rating || value.text?.trim() || value.choices?.length));
  };
  const missing = survey.questions.filter(question => question.isRequired && !answered(question.id));

  if (done) return <Notice tone="success">{tr('Merci pour vos réponses ! Elles aident l’équipe à s’améliorer.')}</Notice>;
  if (!survey.isOpen) return <Notice>{tr('Ce questionnaire est clos. Merci de votre intérêt.')}</Notice>;

  const submit = async () => {
    setBusy(true);
    setError(null);
    const answers: SurveyAnswer[] = survey.questions.filter(question => answered(question.id)).map(question => {
      const value = values[question.id];
      if (question.type === 'Rating') return {questionId: question.id, rating: value.rating};
      if (question.type === 'FreeText') return {questionId: question.id, text: value.text!.trim()};
      return {questionId: question.id, choices: value.choices};
    });
    try {
      await submitSurvey(survey.id, answers, orderToken, locationId);
      rememberAnswered(orderToken ? `${survey.id}:${orderToken}` : survey.id);
      setDone(true);
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="space-y-5" onSubmit={event => {
      event.preventDefault();
      void submit();
    }}>
      {survey.questions.map(question => (
        <fieldset key={question.id} className="rounded-3xl bg-white p-5 shadow-sm">
          <legend className="sr-only">{question.text}</legend>
          <p className="font-semibold">{question.text}{question.isRequired && <span className="text-red-600" aria-hidden="true"> *</span>}</p>
          {question.type === 'Rating' && (
            <div className="mt-3 flex gap-1" role="radiogroup" aria-label={question.text}>
              {[1, 2, 3, 4, 5].map(value => {
                const active = (values[question.id]?.rating ?? 0) >= value;
                return (
                  <button key={value} type="button" role="radio" aria-checked={values[question.id]?.rating === value} aria-label={`${value} sur 5`}
                    onClick={() => set(question.id, {rating: value})} className="grid size-11 place-items-center rounded-xl hover:bg-slate-100">
                    <Star className={cx('size-7', active ? 'fill-amber-400 text-amber-400' : 'text-slate-300')} />
                  </button>
                );
              })}
            </div>
          )}
          {question.type === 'FreeText' && (
            <textarea aria-label={question.text} maxLength={1000} rows={3} value={values[question.id]?.text ?? ''} onChange={event => set(question.id, {text: event.target.value})}
              className="mt-3 w-full rounded-xl border border-slate-200 px-3 py-2 text-sm focus:border-[var(--accent)] focus:outline-none" />
          )}
          {(question.type === 'SingleChoice' || question.type === 'MultipleChoice') && (
            <div className="mt-3 space-y-2">
              {question.choices.map(choice => {
                const chosen = values[question.id]?.choices ?? [];
                const checked = chosen.includes(choice);
                return (
                  <label key={choice} className={cx('flex cursor-pointer items-center gap-3 rounded-xl border px-3 py-2.5 text-sm', checked ? 'border-[var(--accent)] bg-slate-50' : 'border-slate-200')}>
                    <input type={question.type === 'SingleChoice' ? 'radio' : 'checkbox'} name={question.id} className="size-4 accent-[var(--accent)]" checked={checked}
                      onChange={() => set(question.id, {
                        choices: question.type === 'SingleChoice' ? [choice] : checked ? chosen.filter(item => item !== choice) : [...chosen, choice],
                      })} />
                    {choice}
                  </label>
                );
              })}
            </div>
          )}
        </fieldset>
      ))}
      {error && <Notice tone="error">{error}</Notice>}
      <AccentButton type="submit" className="w-full" busy={busy} disabled={missing.length > 0}>{tr('Envoyer mes réponses')}</AccentButton>
      {missing.length > 0 && <p className="text-center text-xs text-ink-500">{tr('Les questions marquées d’un astérisque sont obligatoires.')}</p>}
      <p className="text-center text-xs text-ink-500">{tr('Vos réponses sont anonymes.')}</p>
    </form>
  );
}
