'use client';
import {useState} from 'react';
import {Trash2} from 'lucide-react';
import {useResource} from '@/lib/hooks';
import {addNote, deleteNote, loadNotes, stamp} from '@/lib/customers';
import {Alert, Button, Spinner, Textarea, useAction} from '@/components/ui';

/** Notes internes de l'équipe : datées, jamais montrées au client, effacées si la fiche est anonymisée. */
export function CustomerNotes({customerId, editable}: {customerId: string; editable: boolean}) {
  const {busy, run} = useAction();
  const {data, error, reload} = useResource(signal => loadNotes(customerId, signal), [customerId]);
  const [text, setText] = useState('');

  return (
    <div className="space-y-3">
      {editable && (
        <form className="space-y-2" onSubmit={event => {
          event.preventDefault();
          void run('note', async () => {
            await addNote(customerId, text.trim());
            setText('');
            await reload();
          }, 'Note ajoutée.');
        }}>
          <Textarea aria-label="Nouvelle note" rows={2} maxLength={1000} value={text} onChange={event => setText(event.target.value)}
            placeholder="Préfère la table près de la fenêtre, anniversaire le 12 mars…" />
          <Button type="submit" size="sm" variant="secondary" busy={busy === 'note'} disabled={!text.trim()}>Ajouter la note</Button>
        </form>
      )}
      {error ? <Alert>{error}</Alert> : !data ? <Spinner /> : data.length === 0 ? <p className="text-sm text-ink-500">Aucune note.</p> : (
        <ul className="space-y-2">
          {data.map(note => (
            <li key={note.id} className="flex items-start gap-2 rounded-xl bg-slate-50 px-3 py-2 text-sm">
              <div className="min-w-0 flex-1">
                <p className="whitespace-pre-line">{note.text}</p>
                <p className="mt-0.5 text-xs text-ink-500">{stamp(note.createdAtUtc)}</p>
              </div>
              {editable && (
                <Button size="sm" variant="ghost" className="text-red-600" aria-label="Supprimer la note" busy={busy === note.id} onClick={() => run(note.id, async () => {
                  await deleteNote(customerId, note.id);
                  await reload();
                }, 'Note supprimée.')}><Trash2 className="size-4" /></Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
