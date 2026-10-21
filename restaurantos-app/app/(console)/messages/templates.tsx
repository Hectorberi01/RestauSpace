'use client';
import {useMemo, useRef, useState} from 'react';
import {Archive, Eye, FilePlus2, Pencil, Power, Send} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {
  archiveTemplate, channelLabel, createTemplate, customerChannels, effectiveTemplate, languages, listTemplates, loadMessageTypes, newVersion, render,
  sampleValues, smsSegments, templateAction, updateTemplate, variableLabel, variablesIn, type MessageChannel, type MessageTemplate, type MessageType,
} from '@/lib/messages';
import {useConsole} from '@/components/console';
import {Alert, Badge, Button, Card, Field, Input, Modal, Select, Spinner, cx, useAction} from '@/components/ui';

const day = (iso: string) => new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short', year: 'numeric'}).format(new Date(iso));

interface Slot {
  type: MessageType;
  channel: MessageChannel;
  language: string;
}

/** Contenu et contrôles d'un modèle, alignés sur le backend pour signaler l'erreur avant l'envoi. */
function check(type: MessageType, channel: MessageChannel, subject: string, body: string) {
  const errors: string[] = [];
  const all = variablesIn(`${subject}\n${body}`);
  if (all.invalid) errors.push('Une variable s’écrit {{nom.variable}} : vérifiez les accolades.');
  const unknown = all.used.filter(name => !type.variables.includes(name));
  if (unknown.length) errors.push(`Variable inconnue pour ce message : ${unknown.map(name => `{{${name}}}`).join(', ')}.`);
  if (channel === 'Email' && !subject.trim()) errors.push('Un e-mail a besoin d’un objet.');
  if (subject.length > 200) errors.push('Objet : 200 caractères au plus.');
  if (!body.trim()) errors.push('Le message est vide.');
  if (body.length > 20000) errors.push('Message : 20 000 caractères au plus.');
  if (type.marketing && channel === 'Email' && !all.used.includes('unsubscribe.url')) errors.push('Un e-mail marketing doit contenir le lien de désinscription {{unsubscribe.url}}.');
  return errors;
}

function Editor({slot, template, base, onCancel, onSaved}: {
  slot: Slot;
  template: MessageTemplate | null;
  base: MessageTemplate | null;
  onCancel: () => void;
  onSaved: () => Promise<void>;
}) {
  const {busy, run} = useAction();
  const source = template ?? base;
  const [subject, setSubject] = useState(slot.channel === 'Sms' || slot.channel === 'WhatsApp' ? '' : source?.subject ?? '');
  const [body, setBody] = useState(source?.body ?? '');
  const area = useRef<HTMLTextAreaElement>(null);
  const errors = check(slot.type, slot.channel, subject, body);
  const hasSubject = slot.channel === 'Email';
  const sms = slot.channel === 'Sms' ? smsSegments(render(body, sampleValues)) : null;

  const insert = (name: string) => {
    const element = area.current;
    const token = `{{${name}}}`;
    if (!element) return setBody(current => current + token);
    const start = element.selectionStart ?? body.length;
    const end = element.selectionEnd ?? body.length;
    const next = body.slice(0, start) + token + body.slice(end);
    setBody(next);
    requestAnimationFrame(() => {
      element.focus();
      element.setSelectionRange(start + token.length, start + token.length);
    });
  };

  const save = (publish: boolean) => run(publish ? 'publish' : 'save', async () => {
    const cleanSubject = hasSubject ? subject.trim() : null;
    const id = template
      ? (await updateTemplate(template.id, cleanSubject, body), template.id)
      : await createTemplate({key: slot.type.key, channel: slot.channel, language: slot.language, subject: cleanSubject, body});
    if (publish) await templateAction(id, 'publish');
    await onSaved();
  }, publish ? 'Modèle publié : il est utilisé dès maintenant.' : 'Brouillon enregistré.');

  return (
    <div className="space-y-4">
      {!template && base && <Alert tone="blue">Le contenu du modèle standard est repris : modifiez-le, puis publiez pour l’utiliser à sa place.</Alert>}
      {hasSubject && (
        <Field label="Objet"><Input maxLength={200} value={subject} onChange={event => setSubject(event.target.value)} placeholder="Votre commande est prête" /></Field>
      )}
      <Field label="Message">
        <textarea ref={area} rows={slot.channel === 'Email' ? 9 : 5} value={body} onChange={event => setBody(event.target.value)} maxLength={20000}
          className="w-full rounded-xl border border-slate-200 bg-white px-3 py-2 font-mono text-sm focus:border-brand-500 focus:ring-2 focus:ring-brand-200 focus:outline-none" />
      </Field>
      <div>
        <p className="mb-1.5 text-xs font-semibold text-ink-600">Insérer une information (remplacée à l’envoi)</p>
        <div className="flex flex-wrap gap-1.5">
          {slot.type.variables.map(name => (
            <button key={name} type="button" onClick={() => insert(name)} className="rounded-full bg-slate-100 px-2.5 py-1 text-xs text-ink-700 hover:bg-brand-50 hover:text-brand-700" title={`{{${name}}}`}>
              {variableLabel[name] ?? name}
            </button>
          ))}
        </div>
      </div>
      {sms && <p className="text-xs text-ink-500">Aperçu : {sms.length} caractères, {sms.segments} SMS{sms.unicode ? ' (caractères spéciaux : 70 par SMS)' : ''}.</p>}
      {errors.length > 0 ? (
        <Alert><ul className="list-inside list-disc">{errors.map(error => <li key={error}>{error}</li>)}</ul></Alert>
      ) : (
        <div className="rounded-xl bg-slate-50 p-3 text-sm">
          <p className="mb-1 flex items-center gap-1 text-xs font-semibold text-ink-500"><Eye className="size-3.5" aria-hidden="true" />Aperçu avec des données d’exemple</p>
          {hasSubject && <p className="font-semibold">{render(subject, sampleValues)}</p>}
          <p className="whitespace-pre-line text-ink-800">{render(body, sampleValues)}</p>
        </div>
      )}
      <div className="flex flex-wrap justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>Annuler</Button>
        <Button variant="secondary" busy={busy === 'save'} disabled={errors.length > 0} onClick={() => save(false)}>Enregistrer le brouillon</Button>
        <Button busy={busy === 'publish'} disabled={errors.length > 0} onClick={() => save(true)}><Send className="size-4" aria-hidden="true" />Enregistrer et publier</Button>
      </div>
    </div>
  );
}

function SlotDialog({slot, templates, manage, onClose, onChanged}: {slot: Slot; templates: MessageTemplate[]; manage: boolean; onClose: () => void; onChanged: () => Promise<void>}) {
  const {busy, run} = useAction();
  const [editing, setEditing] = useState<{template: MessageTemplate | null} | null>(null);
  const same = (item: MessageTemplate) => item.key === slot.type.key && item.channel === slot.channel && item.language === slot.language;
  const own = templates.filter(item => !item.isPlatform && same(item)).sort((a, b) => b.version - a.version);
  const platform = templates.filter(item => item.isPlatform && same(item) && item.status === 'Published').sort((a, b) => b.version - a.version)[0] ?? null;
  const effective = effectiveTemplate(templates, slot.type.key, slot.channel, slot.language);
  const draft = own.find(item => item.status === 'Draft');
  const latestPublished = own.find(item => item.status === 'Published');
  const latest = own.find(item => item.status !== 'Draft');
  const title = `${slot.type.label} · ${channelLabel[slot.channel]} · ${languages.find(([code]) => code === slot.language)?.[1] ?? slot.language}`;
  const act = (key: string, action: () => Promise<unknown>, success: string) => run(key, async () => {
    await action();
    await onChanged();
  }, success);

  return (
    <Modal wide open onClose={onClose} title={title}>
      {editing ? (
        <Editor slot={slot} template={editing.template} base={editing.template ? null : latestPublished ?? platform} onCancel={() => setEditing(null)}
          onSaved={async () => {
            setEditing(null);
            await onChanged();
          }} />
      ) : (
        <div className="space-y-5">
          <section className="rounded-xl border border-slate-200 p-4">
            <p className="text-xs font-semibold tracking-wide text-ink-500 uppercase">Envoyé aujourd’hui</p>
            {effective ? (
              <>
                <p className="mt-1 text-sm font-semibold">{effective.isPlatform ? 'Modèle standard RestauSpace' : `Votre modèle, version ${effective.version}`}</p>
                {effective.subject && <p className="mt-2 text-sm font-semibold">{render(effective.subject, sampleValues)}</p>}
                <p className="mt-1 text-sm whitespace-pre-line text-ink-700">{render(effective.body, sampleValues)}</p>
              </>
            ) : (
              <p className="mt-1 text-sm text-amber-800">Aucun modèle publié et actif : ce message n’est pas envoyé sur ce canal dans cette langue.</p>
            )}
          </section>

          <section>
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <h3 className="flex-1 text-sm font-semibold">Vos versions</h3>
              {manage && !draft && (
                latest
                  ? <Button size="sm" variant="secondary" busy={busy === 'version'} onClick={() => run('version', async () => {
                    const id = await newVersion(latest.id);
                    await onChanged();
                    setEditing({template: {...latest, id, status: 'Draft', version: latest.version + 1}});
                  })}><FilePlus2 className="size-4" aria-hidden="true" />Nouvelle version</Button>
                  : <Button size="sm" onClick={() => setEditing({template: null})}><Pencil className="size-4" aria-hidden="true" />Personnaliser</Button>
              )}
            </div>
            {own.length === 0 ? (
              <p className="text-sm text-ink-500">{platform ? 'Vous utilisez le modèle standard. Personnalisez-le pour parler comme votre restaurant.' : 'Aucun modèle : créez-en un pour envoyer ce message.'}</p>
            ) : (
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
                {own.map(item => (
                  <li key={item.id} className="flex flex-wrap items-center gap-2 px-4 py-3 text-sm">
                    <span className="font-semibold">v{item.version}</span>
                    <Badge tone={item.status === 'Published' ? 'green' : item.status === 'Draft' ? 'blue' : 'neutral'}>{item.status === 'Published' ? 'Publiée' : item.status === 'Draft' ? 'Brouillon' : 'Archivée'}</Badge>
                    {item.status !== 'Archived' && !item.isEnabled && <Badge tone="amber">Désactivée</Badge>}
                    {effective?.id === item.id && <Badge tone="brand">En service</Badge>}
                    <span className="min-w-0 flex-1 truncate text-xs text-ink-500">{item.publishedAtUtc ? `publiée le ${day(item.publishedAtUtc)}` : `modifiée le ${day(item.updatedAtUtc)}`}</span>
                    {manage && item.status !== 'Archived' && (
                      <span className="flex flex-wrap gap-1">
                        {item.status === 'Draft' && (
                          <>
                            <Button size="sm" variant="ghost" onClick={() => setEditing({template: item})}><Pencil className="size-4" aria-hidden="true" />Modifier</Button>
                            <Button size="sm" variant="ghost" busy={busy === `p-${item.id}`} onClick={() => act(`p-${item.id}`, () => templateAction(item.id, 'publish'), 'Modèle publié.')}><Send className="size-4" aria-hidden="true" />Publier</Button>
                          </>
                        )}
                        {item.status === 'Published' && item.id === latestPublished?.id && (
                          <Button size="sm" variant="ghost" busy={busy === `t-${item.id}`}
                            onClick={() => act(`t-${item.id}`, () => templateAction(item.id, item.isEnabled ? 'disable' : 'enable'), item.isEnabled ? 'Modèle désactivé : le modèle standard reprend la main.' : 'Modèle réactivé.')}>
                            <Power className="size-4" aria-hidden="true" />{item.isEnabled ? 'Désactiver' : 'Activer'}
                          </Button>
                        )}
                        <Button size="sm" variant="ghost" className="text-red-600" aria-label={`Archiver la version ${item.version}`} title="Archiver" busy={busy === `a-${item.id}`}
                          onClick={() => act(`a-${item.id}`, () => archiveTemplate(item.id), 'Version archivée.')}><Archive className="size-4" /></Button>
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            )}
            {manage && <p className="mt-2 text-xs text-ink-500">Une version publiée ne se modifie plus : elle prouve ce qui a été envoyé. Pour changer le texte, créez une nouvelle version.</p>}
          </section>
        </div>
      )}
    </Modal>
  );
}

export function Templates() {
  const {can} = useConsole();
  const manage = can('notifications.configure');
  const [language, setLanguage] = useState('fr');
  const [slot, setSlot] = useState<{key: string; channel: MessageChannel} | null>(null);
  const {data, error, reload} = useResource(signal => listTemplates(signal), []);
  const types = useResource(signal => loadMessageTypes(signal), []);
  const groups = useMemo(() => {
    const map = new Map<string, MessageType[]>();
    for (const type of types.data ?? []) map.set(type.group, [...(map.get(type.group) ?? []), type]);
    return [...map.entries()];
  }, [types.data]);

  if (error || types.error) return <Alert>{error ?? types.error}</Alert>;
  if (!data || !types.data) return <Spinner />;
  const current = slot ? types.data.find(type => type.key === slot.key) : undefined;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <p className="min-w-0 flex-1 text-sm text-ink-600">
          Le texte de chaque message envoyé à vos clients. Sans modèle à vous, le modèle standard de RestauSpace est utilisé.
        </p>
        <Field label="Langue">
          <Select className="w-full! sm:w-auto!" value={language} onChange={event => setLanguage(event.target.value)}>
            {languages.map(([code, label]) => <option key={code} value={code}>{label}</option>)}
          </Select>
        </Field>
      </div>
      {groups.map(([group, types]) => (
        <Card key={group} className="overflow-hidden">
          <h2 className="border-b border-slate-100 bg-slate-50 px-4 py-2 text-sm font-semibold">{group}{group === 'Marketing' && <span className="ml-2 font-normal text-ink-500">· envoyés seulement aux clients qui ont accepté</span>}</h2>
          <ul className="divide-y divide-slate-100">
            {types.map(type => (
              <li key={type.key} className="flex flex-col gap-2 px-4 py-3 lg:flex-row lg:items-center">
                <span className="min-w-0 flex-1 text-sm font-medium">{type.label}</span>
                <span className="grid grid-cols-3 gap-2 lg:w-[30rem]">
                  {customerChannels.map(channel => {
                    const effective = effectiveTemplate(data, type.key, channel, language);
                    const draft = data.some(item => !item.isPlatform && item.key === type.key && item.channel === channel && item.language === language && item.status === 'Draft');
                    return (
                      <button key={channel} type="button" onClick={() => setSlot({key: type.key, channel})}
                        aria-label={`${type.label} par ${channelLabel[channel]}`}
                        className={cx('rounded-xl border px-2.5 py-2 text-left transition hover:border-brand-300 hover:bg-brand-50/40',
                          effective ? 'border-slate-200' : 'border-dashed border-slate-300')}>
                        <span className="block text-xs font-semibold text-ink-700">{channelLabel[channel]}</span>
                        <span className={cx('block text-xs leading-snug', !effective ? 'text-ink-400' : effective.isPlatform ? 'text-ink-500' : 'font-medium text-brand-700')}>
                          {!effective ? 'Non envoyé' : effective.isPlatform ? 'Standard' : `Personnalisé v${effective.version}`}
                          {draft ? ' · brouillon' : ''}
                        </span>
                      </button>
                    );
                  })}
                </span>
              </li>
            ))}
          </ul>
        </Card>
      ))}
      {slot && current && (
        <SlotDialog slot={{type: current, channel: slot.channel, language}} templates={data} manage={manage} onClose={() => setSlot(null)} onChanged={reload} />
      )}
    </div>
  );
}
