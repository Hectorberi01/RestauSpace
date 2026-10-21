'use client';
import {useEffect, useMemo, useState} from 'react';
import Link from 'next/link';
import {Archive, Check, EyeOff, Globe, MessageSquareText, Plus, Star} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {loadCustomer, type Customer} from '@/lib/customers';
import {
  archiveFeedback, feedbackStatusLabel, feedbackSummary, listFeedbacks, markReviewed, setFeedbackPublished, submitFeedback, type Feedback, type FeedbackFilter,
  type FeedbackStatus,
} from '@/lib/reviews';
import {useConsole} from '@/components/console';
import {CustomerPickerDialog} from '@/components/customer-picker';
import {Alert, Badge, Button, Card, Empty, Field, Modal, Select, Spinner, Textarea, cx, useAction} from '@/components/ui';

const stamp = (iso: string) =>
  new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit'}).format(new Date(iso));

function Stars({rating, size = 'size-4'}: {rating: number; size?: string}) {
  return (
    <span className="inline-flex gap-0.5" role="img" aria-label={`${rating} sur 5`}>
      {[1, 2, 3, 4, 5].map(value => (
        <Star key={value} className={cx(size, value <= rating ? 'fill-amber-400 text-amber-400' : 'text-slate-300')} aria-hidden="true" />
      ))}
    </span>
  );
}

function periodStart(period: string): string | undefined {
  if (!period) return undefined;
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() - Number(period));
  return date.toISOString();
}

function SubmitDialog({open, onClose, onDone}: {open: boolean; onClose: () => void; onDone: () => Promise<void>}) {
  const {location, locations, can} = useConsole();
  const {busy, run} = useAction();
  const [locationId, setLocationId] = useState('');
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState('');
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [picking, setPicking] = useState(false);
  useEffect(() => {
    if (!open) return;
    setLocationId(location?.id ?? locations[0]?.id ?? '');
    setRating(5);
    setComment('');
    setCustomer(null);
  }, [open, location?.id, locations]);

  return (
    <Modal open={open} onClose={onClose} title="Saisir un avis" footer={
      <>
        <Button variant="secondary" onClick={onClose}>Annuler</Button>
        <Button busy={busy === 'submit'} disabled={!locationId} onClick={() => run('submit', async () => {
          await submitFeedback({locationId, rating, comment: comment.trim() || null, customerId: customer?.id ?? null, orderId: null});
          await onDone();
          onClose();
        }, 'Avis enregistré.')}>Enregistrer</Button>
      </>
    }>
      <div className="space-y-4">
        <p className="text-sm text-ink-600">Pour un avis donné de vive voix, par téléphone ou sur un carnet.</p>
        {locations.length > 1 && (
          <Field label="Établissement">
            <Select value={locationId} onChange={event => setLocationId(event.target.value)}>
              {locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </Select>
          </Field>
        )}
        <fieldset>
          <legend className="mb-1.5 text-sm font-medium">Note</legend>
          <div className="flex gap-1" role="radiogroup" aria-label="Note">
            {[1, 2, 3, 4, 5].map(value => (
              <button key={value} type="button" role="radio" aria-checked={rating === value} aria-label={`${value} sur 5`} onClick={() => setRating(value)} className="rounded-lg p-1 hover:bg-amber-50">
                <Star className={cx('size-7', value <= rating ? 'fill-amber-400 text-amber-400' : 'text-slate-300')} />
              </button>
            ))}
          </div>
        </fieldset>
        <Field label="Commentaire (facultatif)"><Textarea rows={3} maxLength={2000} value={comment} onChange={event => setComment(event.target.value)} /></Field>
        {can('customers.view') && (
          <div className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 text-sm">
            <span className="min-w-0 flex-1 truncate">{customer ? <>Client : <strong>{customer.name}</strong></> : 'Client (facultatif)'}</span>
            {customer && <Button size="sm" variant="ghost" onClick={() => setCustomer(null)}>Retirer</Button>}
            <Button size="sm" variant="secondary" onClick={() => setPicking(true)}>{customer ? 'Changer' : 'Choisir'}</Button>
          </div>
        )}
      </div>
      <CustomerPickerDialog open={picking} onClose={() => setPicking(false)} onPick={picked => setCustomer(picked)} />
    </Modal>
  );
}

export function Feedbacks() {
  const {can, locations, location} = useConsole();
  const {busy, run} = useAction();
  const [locationId, setLocationId] = useState(location?.id ?? '');
  const [status, setStatus] = useState<FeedbackStatus | ''>('');
  const [rating, setRating] = useState('');
  const [period, setPeriod] = useState('30');
  const [commentOnly, setCommentOnly] = useState(false);
  const [publishedOnly, setPublishedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [submitting, setSubmitting] = useState(false);
  const [names, setNames] = useState<Record<string, string>>({});

  const filter: FeedbackFilter = useMemo(() => ({
    locationId: locationId || undefined,
    status: status || undefined,
    includeArchived: status === 'Archived' || undefined,
    minRating: rating === 'low' ? 1 : rating ? Number(rating) : undefined,
    maxRating: rating === 'low' ? 2 : rating ? Number(rating) : undefined,
    from: periodStart(period),
    hasComment: commentOnly || undefined,
    published: publishedOnly || undefined,
  }), [locationId, status, rating, period, commentOnly, publishedOnly]);
  const key = JSON.stringify(filter);
  useEffect(() => setPage(1), [key]);
  const summary = useResource(signal => feedbackSummary({...filter, status: undefined, published: undefined}, signal), [key]);
  const list = useResource(signal => listFeedbacks(filter, page, signal), [key, page]);

  useEffect(() => {
    if (!list.data || !can('customers.view')) return;
    const missing = [...new Set(list.data.items.map(item => item.customerId).filter((id): id is string => !!id && !(id in names)))];
    if (missing.length === 0) return;
    void Promise.all(missing.map(id => loadCustomer(id).then(customer => [id, customer.name] as const).catch(() => [id, 'Client'] as const)))
      .then(entries => setNames(current => ({...current, ...Object.fromEntries(entries)})));
  }, [list.data, can, names]);

  const max = Math.max(1, ...(summary.data?.countByRating.map(item => item.count) ?? [1]));
  const manage = can('reviews.manage');
  const done = {reviewed: 'Avis marqué comme traité.', archive: 'Avis archivé.', publish: 'Avis publié sur votre site et votre application.', unpublish: 'Avis retiré du site.'};
  const act = (item: Feedback, action: keyof typeof done) => run(`${action}-${item.id}`, async () => {
    if (action === 'reviewed') await markReviewed(item.id);
    else if (action === 'archive') await archiveFeedback(item.id);
    else await setFeedbackPublished(item.id, action === 'publish');
    await Promise.all([list.reload(), summary.reload()]);
  }, done[action]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        {locations.length > 1 && (
          <Select aria-label="Établissement" className="w-full! sm:w-auto!" value={locationId} onChange={event => setLocationId(event.target.value)}>
            <option value="">Tous les établissements</option>
            {locations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        )}
        <Select aria-label="Période" className="w-full! sm:w-auto!" value={period} onChange={event => setPeriod(event.target.value)}>
          <option value="7">7 derniers jours</option>
          <option value="30">30 derniers jours</option>
          <option value="90">3 derniers mois</option>
          <option value="365">12 derniers mois</option>
          <option value="">Depuis toujours</option>
        </Select>
        <Select aria-label="Statut" className="w-full! sm:w-auto!" value={status} onChange={event => setStatus(event.target.value as FeedbackStatus | '')}>
          <option value="">À traiter et traités</option>
          {(Object.keys(feedbackStatusLabel) as FeedbackStatus[]).map(item => <option key={item} value={item}>{feedbackStatusLabel[item]}</option>)}
        </Select>
        <Select aria-label="Note" className="w-full! sm:w-auto!" value={rating} onChange={event => setRating(event.target.value)}>
          <option value="">Toutes les notes</option>
          <option value="low">Négatifs (1–2)</option>
          {[5, 4, 3, 2, 1].map(value => <option key={value} value={value}>{value} étoile{value > 1 ? 's' : ''}</option>)}
        </Select>
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" className="size-4 accent-brand-600" checked={commentOnly} onChange={event => setCommentOnly(event.target.checked)} />Avec commentaire
        </label>
        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input type="checkbox" className="size-4 accent-brand-600" checked={publishedOnly} onChange={event => setPublishedOnly(event.target.checked)} />Publiés sur le site
        </label>
        {can('reviews.submit') && <Button className="ml-auto" variant="secondary" onClick={() => setSubmitting(true)}><Plus className="size-4" aria-hidden="true" />Saisir un avis</Button>}
      </div>

      {summary.data && (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-[16rem_1fr_14rem]">
          <Card className="flex flex-col items-center justify-center p-5 text-center">
            <p className="text-4xl font-bold tracking-tight">{summary.data.averageRating?.toFixed(1).replace('.', ',') ?? '—'}</p>
            <Stars rating={Math.round(summary.data.averageRating ?? 0)} size="size-5" />
            <p className="mt-1 text-sm text-ink-500">{summary.data.count} avis</p>
          </Card>
          <Card className="space-y-1.5 p-5">
            {[5, 4, 3, 2, 1].map(value => {
              const found = summary.data!.countByRating.find(item => item.rating === value)?.count ?? 0;
              return (
                <button key={value} type="button" onClick={() => setRating(rating === String(value) ? '' : String(value))} className="flex w-full items-center gap-3 text-sm">
                  <span className="w-10 text-right text-ink-600">{value} ★</span>
                  <span className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                    <span className={cx('block h-full rounded-full', value >= 4 ? 'bg-emerald-500' : value === 3 ? 'bg-amber-400' : 'bg-red-500')} style={{width: `${(found / max) * 100}%`}} />
                  </span>
                  <span className="w-8 text-right tabular-nums text-ink-600">{found}</span>
                </button>
              );
            })}
          </Card>
          <Card className="space-y-2 p-5 text-sm">
            <p className="flex justify-between"><span className="text-ink-500">À traiter</span><strong>{summary.data.submitted}</strong></p>
            <p className="flex justify-between"><span className="text-ink-500">Traités</span><strong>{summary.data.reviewed}</strong></p>
            <p className="flex justify-between"><span className="text-ink-500">Avec commentaire</span><strong>{summary.data.withComment}</strong></p>
          </Card>
        </div>
      )}

      {list.error ? <Alert>{list.error}</Alert> : !list.data ? <Spinner /> : list.data.items.length === 0 ? (
        <Card className="p-6"><Empty icon={<MessageSquareText className="size-8" />} title="Aucun avis">Aucun avis ne correspond à ces filtres.</Empty></Card>
      ) : (
        <div className="space-y-3">
          {list.data.items.map(item => (
            <Card key={item.id} className={cx('p-4', item.rating <= 2 && item.status === 'Submitted' && 'border-red-200')}>
              <div className="flex flex-wrap items-start gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <Stars rating={item.rating} />
                    <Badge tone={item.status === 'Submitted' ? 'amber' : item.status === 'Reviewed' ? 'green' : 'neutral'}>{feedbackStatusLabel[item.status]}</Badge>
                    {item.publishedAtUtc && <Badge tone="blue" className="gap-1"><Globe className="size-3" aria-hidden="true" />Sur le site</Badge>}
                  </div>
                  <p className="mt-1 text-xs text-ink-500">
                    {stamp(item.submittedAtUtc)}
                    {locations.length > 1 && ` · ${locations.find(entry => entry.id === item.locationId)?.name ?? ''}`}
                    {item.customerId && can('customers.view') && <> · <Link href={`/clients/${item.customerId}`} className="font-medium text-brand-700 hover:underline">{names[item.customerId] ?? 'Client'}</Link></>}
                    {item.isCustomerDetached && ' · client anonymisé'}
                    {item.orderId && <> · <Link href={`/commandes/${item.orderId}`} className="font-medium text-brand-700 hover:underline">commande</Link></>}
                  </p>
                </div>
                {manage && item.status !== 'Archived' && (
                  <div className="flex flex-wrap gap-1">
                    {item.status === 'Submitted' && <Button size="sm" variant="secondary" busy={busy === `reviewed-${item.id}`} onClick={() => act(item, 'reviewed')}><Check className="size-4" aria-hidden="true" />Traité</Button>}
                    {item.publishedAtUtc
                      ? <Button size="sm" variant="ghost" busy={busy === `unpublish-${item.id}`} onClick={() => act(item, 'unpublish')}><EyeOff className="size-4" aria-hidden="true" />Retirer du site</Button>
                      : <Button size="sm" variant="ghost" busy={busy === `publish-${item.id}`} onClick={() => act(item, 'publish')} title="Note, commentaire et date, sans le nom du client"><Globe className="size-4" aria-hidden="true" />Publier</Button>}
                    <Button size="sm" variant="ghost" busy={busy === `archive-${item.id}`} onClick={() => act(item, 'archive')}><Archive className="size-4" aria-hidden="true" />Archiver</Button>
                  </div>
                )}
              </div>
              {item.comment ? <p className="mt-3 text-sm whitespace-pre-line text-ink-800">{item.comment}</p> : <p className="mt-3 text-sm text-ink-400">Sans commentaire.</p>}
            </Card>
          ))}
          {(page > 1 || list.data.hasMore) && (
            <div className="flex justify-end gap-2">
              <Button size="sm" variant="secondary" disabled={page === 1} onClick={() => setPage(page - 1)}>Plus récents</Button>
              <Button size="sm" variant="secondary" disabled={!list.data.hasMore} onClick={() => setPage(page + 1)}>Plus anciens</Button>
            </div>
          )}
        </div>
      )}
      <SubmitDialog open={submitting} onClose={() => setSubmitting(false)} onDone={async () => {
        await Promise.all([list.reload(), summary.reload()]);
      }} />
    </div>
  );
}
