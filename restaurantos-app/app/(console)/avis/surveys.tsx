'use client';
import {useEffect, useState} from 'react';
import {ArrowDown, ArrowUp, ClipboardList, Pencil, Plus, Send, Trash2, X} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  addQuestion, changeQuestion, createSurvey, listSurveys, moveQuestion, questionTypeLabel, removeQuestion, renameSurvey, surveyAction,
  surveyStatusLabel, type Question, type QuestionType, type Survey,
} from '@/lib/reviews';
import {SurveyResultsPanel, SurveyShare} from './survey-results';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Empty, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';

function QuestionDialog({survey, question, open, onClose, onDone}: {survey: Survey; question: Question | null; open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [text, setText] = useState('');
  const [type, setType] = useState<QuestionType>('Rating');
  const [required, setRequired] = useState(false);
  const [choices, setChoices] = useState<string[]>(['', '']);
  useEffect(() => {
    if (!open) return;
    setText(question?.text ?? '');
    setType(question?.type ?? 'Rating');
    setRequired(question?.isRequired ?? false);
    setChoices(question?.choices.length ? question.choices : ['', '']);
  }, [open, question]);
  const withChoices = type === 'SingleChoice' || type === 'MultipleChoice';
  const cleaned = choices.map(choice => choice.trim()).filter(Boolean);
  const invalid = !text.trim() || (withChoices && (cleaned.length < 2 || new Set(cleaned).size !== cleaned.length));

  return (
    <Modal open={open} onClose={onClose} title={question ? 'Modifier la question' : 'Nouvelle question'} footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button type="submit" form="question-form" busy={busy === 'question'} disabled={invalid}>Enregistrer</Button>
      </>
    }>
      <form id="question-form" className="space-y-4" onSubmit={event => {
        event.preventDefault();
        void run('question', async () => {
          const input = {text: text.trim(), type, isRequired: required, choices: withChoices ? cleaned : null};
          if (question) await changeQuestion(survey.id, question.id, input);
          else await addQuestion(survey.id, input);
          await onDone();
          onClose();
        }, 'Question enregistrée.');
      }}>
        <Field label="Question"><Input required maxLength={500} value={text} onChange={event => setText(event.target.value)} placeholder="Comment avez-vous trouvé l’accueil ?" /></Field>
        <Field label="Type de réponse">
          <Select value={type} onChange={event => setType(event.target.value as QuestionType)}>
            {(Object.keys(questionTypeLabel) as QuestionType[]).map(item => <option key={item} value={item}>{questionTypeLabel[item]}</option>)}
          </Select>
        </Field>
        {withChoices && (
          <fieldset className="space-y-2">
            <legend className="mb-1 text-sm font-medium">Choix proposés</legend>
            {choices.map((choice, index) => (
              <div key={index} className="flex gap-2">
                <Input aria-label={`Choix ${index + 1}`} maxLength={200} value={choice} onChange={event => setChoices(current => current.map((item, i) => (i === index ? event.target.value : item)))} />
                {choices.length > 2 && <Button variant="ghost" aria-label={`Retirer le choix ${index + 1}`} onClick={() => setChoices(current => current.filter((_, i) => i !== index))}><X className="size-4" /></Button>}
              </div>
            ))}
            {choices.length < 20 && <Button size="sm" variant="ghost" onClick={() => setChoices(current => [...current, ''])}><Plus className="size-4" aria-hidden="true" />Ajouter un choix</Button>}
          </fieldset>
        )}
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="size-4 accent-brand-600" checked={required} onChange={event => setRequired(event.target.checked)} />Réponse obligatoire
        </label>
      </form>
    </Modal>
  );
}

function SurveyDialog({survey, onClose, onChanged}: {survey: Survey | null; onClose: () => void; onChanged: () => Promise<void>}) {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const [editing, setEditing] = useState<Question | 'new' | null>(null);
  const [title, setTitle] = useState('');
  useEffect(() => setTitle(survey?.title ?? ''), [survey?.id, survey?.title]);
  if (!survey) return null;
  const draft = survey.status === 'Draft' && can('reviews.manage');
  const questions = [...survey.questions].sort((a, b) => a.order - b.order);

  return (
    <Modal wide open onClose={onClose} title={survey.title} footer={can('reviews.manage') && survey.status !== 'Closed' ? (
      survey.status === 'Draft' ? (
        <Button busy={busy === 'publish'} disabled={questions.length === 0} onClick={() => run('publish', async () => {
          await surveyAction(survey.id, 'publish');
          await onChanged();
        }, 'Questionnaire publié.')}><Send className="size-4" aria-hidden="true" />Publier</Button>
      ) : (
        <Button variant="secondary" busy={busy === 'close'} onClick={() => run('close', async () => {
          await surveyAction(survey.id, 'close');
          await onChanged();
        }, 'Questionnaire clos.')}>Clore le questionnaire</Button>
      )
    ) : undefined}>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={survey.status === 'Published' ? 'green' : survey.status === 'Draft' ? 'neutral' : 'red'}>{surveyStatusLabel[survey.status]}</Badge>
          {survey.status === 'Draft' && <span className="text-sm text-ink-500">Modifiable jusqu’à sa publication.</span>}
          {survey.status === 'Published' && <span className="text-sm text-ink-500">Figé : clôturez-le pour en créer une nouvelle version.</span>}
        </div>
        {draft && (
          <form className="flex items-end gap-2" onSubmit={event => {
            event.preventDefault();
            void run('title', async () => {
              await renameSurvey(survey.id, title.trim());
              await onChanged();
            }, 'Titre enregistré.');
          }}>
            <Field label="Titre" className="flex-1"><Input required maxLength={200} value={title} onChange={event => setTitle(event.target.value)} /></Field>
            <Button type="submit" variant="secondary" busy={busy === 'title'} disabled={!title.trim() || title.trim() === survey.title}>OK</Button>
          </form>
        )}
        {questions.length === 0 ? <p className="rounded-xl bg-slate-50 p-4 text-sm text-ink-500">Aucune question : ajoutez-en au moins une pour publier.</p> : (
          <ol className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {questions.map((question, index) => (
              <li key={question.id} className="flex items-start gap-3 px-3 py-3 text-sm">
                <span className="grid size-7 shrink-0 place-items-center rounded-full bg-slate-100 text-xs font-semibold">{index + 1}</span>
                <div className="min-w-0 flex-1">
                  <p className="font-medium">{question.text}{question.isRequired && <span className="text-red-600"> *</span>}</p>
                  <p className="text-xs text-ink-500">{questionTypeLabel[question.type]}{question.choices.length ? ` : ${question.choices.join(' · ')}` : ''}</p>
                </div>
                {draft && (
                  <div className="flex shrink-0 gap-0.5">
                    <Button size="sm" variant="ghost" aria-label="Monter" disabled={index === 0 || !!busy} onClick={() => run(`up-${question.id}`, async () => {
                      await moveQuestion(survey.id, question.id, index);
                      await onChanged();
                    })}><ArrowUp className="size-4" /></Button>
                    <Button size="sm" variant="ghost" aria-label="Descendre" disabled={index === questions.length - 1 || !!busy} onClick={() => run(`down-${question.id}`, async () => {
                      await moveQuestion(survey.id, question.id, index + 2);
                      await onChanged();
                    })}><ArrowDown className="size-4" /></Button>
                    <Button size="sm" variant="ghost" aria-label="Modifier" onClick={() => setEditing(question)}><Pencil className="size-4" /></Button>
                    <Button size="sm" variant="ghost" className="text-red-600" aria-label="Supprimer" busy={busy === `rm-${question.id}`} onClick={() => run(`rm-${question.id}`, async () => {
                      await removeQuestion(survey.id, question.id);
                      await onChanged();
                    }, 'Question supprimée.')}><Trash2 className="size-4" /></Button>
                  </div>
                )}
              </li>
            ))}
          </ol>
        )}
        {draft && questions.length < 50 && <Button variant="secondary" onClick={() => setEditing('new')}><Plus className="size-4" aria-hidden="true" />Ajouter une question</Button>}
        {survey.status === 'Published' && <SurveyShare survey={survey} />}
        {survey.status !== 'Draft' && <SurveyResultsPanel survey={survey} />}
      </div>
      <QuestionDialog survey={survey} question={editing === 'new' ? null : editing} open={editing !== null} onClose={() => setEditing(null)} onDone={onChanged} />
    </Modal>
  );
}

export function Surveys() {
  const {can} = useConsole();
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => listSurveys(signal), []);
  const [openId, setOpenId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [title, setTitle] = useState('');
  const opened = data?.find(item => item.id === openId) ?? null;

  return (
    <div className="space-y-4">
      <Alert tone="blue">Publiez un questionnaire pour le partager par lien ou QR code ; le dernier publié est aussi proposé après chaque commande en ligne remise. Les réponses sont anonymes.</Alert>
      {can('reviews.manage') && (
        <div className="flex justify-end"><Button onClick={() => {
          setTitle('');
          setCreating(true);
        }}><Plus className="size-4" aria-hidden="true" />Nouveau questionnaire</Button></div>
      )}
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.length === 0 ? (
        <Card className="p-6"><Empty icon={<ClipboardList className="size-8" />} title="Aucun questionnaire">Notes, questions ouvertes ou à choix : construisez le questionnaire qui vous convient.</Empty></Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
          {data.map(survey => (
            <button key={survey.id} type="button" className="text-left" onClick={() => setOpenId(survey.id)}>
              <Card className="h-full p-4 transition hover:border-brand-300 hover:shadow-md">
                <div className="flex items-start justify-between gap-2">
                  <p className="font-semibold">{survey.title}</p>
                  <Badge tone={survey.status === 'Published' ? 'green' : survey.status === 'Draft' ? 'neutral' : 'red'}>{surveyStatusLabel[survey.status]}</Badge>
                </div>
                <p className={cx('mt-2 text-sm text-ink-500')}>{survey.questions.length} question{survey.questions.length > 1 ? 's' : ''}</p>
              </Card>
            </button>
          ))}
        </div>
      )}
      <SurveyDialog survey={opened} onClose={() => setOpenId(null)} onChanged={reload} />
      <Modal open={creating} onClose={() => setCreating(false)} title="Nouveau questionnaire" footer={
        <>
          <Button variant="secondary" onClick={() => setCreating(false)}>Annuler</Button>
          <Button type="submit" form="survey-form" busy={busy === 'create'} disabled={!title.trim()}>Créer</Button>
        </>
      }>
        <form id="survey-form" onSubmit={event => {
          event.preventDefault();
          void run('create', async () => {
            const {id} = await createSurvey(title.trim());
            await reload();
            setCreating(false);
            setOpenId(id);
          }, 'Questionnaire créé.');
        }}>
          <Field label="Titre"><Input required maxLength={200} value={title} onChange={event => setTitle(event.target.value)} placeholder="Votre avis sur votre repas" /></Field>
        </form>
      </Modal>
    </div>
  );
}
