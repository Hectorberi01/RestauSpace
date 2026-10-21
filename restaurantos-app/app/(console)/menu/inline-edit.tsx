'use client';
import {useState, type ReactNode} from 'react';
import {Check, Pencil, X} from 'lucide-react';
import {Button, Input, cx, useAction} from '@/components/ui';

/**
 * Valeur affichée avec un crayon ; un clic la remplace par un champ, Entrée enregistre, Échap annule. `parse` refuse
 * une saisie invalide (retourne null) sans appeler le serveur.
 */
export function InlineEdit<T>({
  value, label, display, editable, onSave, parse, inputMode, maxLength, suffix, success, className, inputClassName,
}: {
  value: string;
  label: string;
  display?: ReactNode;
  editable: boolean;
  onSave: (value: T) => Promise<unknown>;
  parse: (raw: string) => T | null;
  inputMode?: 'text' | 'decimal';
  maxLength?: number;
  suffix?: string;
  success?: string;
  className?: string;
  inputClassName?: string;
}) {
  const {busy, run} = useAction();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const parsed = parse(draft);
  const close = () => {
    setDraft(value);
    setEditing(false);
  };

  if (!editing) {
    return (
      <span className={cx('inline-flex min-w-0 items-center gap-1', className)}>
        <span className="min-w-0 truncate">{display ?? value}</span>
        {editable && (
          <button type="button" aria-label={`Modifier : ${label}`} title="Modifier"
            onClick={() => {
              setDraft(value);
              setEditing(true);
            }}
            className="shrink-0 rounded p-1 text-ink-400 hover:bg-slate-100 hover:text-ink-700">
            <Pencil className="size-3.5" />
          </button>
        )}
      </span>
    );
  }

  return (
    <form className={cx('flex min-w-0 items-center gap-1', className)} onSubmit={event => {
      event.preventDefault();
      if (parsed === null) return;
      if (draft.trim() === value.trim()) return close();
      void run('save', async () => {
        await onSave(parsed);
        setEditing(false);
      }, success);
    }}>
      <Input autoFocus aria-label={label} value={draft} maxLength={maxLength} inputMode={inputMode}
        aria-invalid={parsed === null}
        onChange={event => setDraft(event.target.value)}
        onKeyDown={event => {
          if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            close();
          }
        }}
        className={cx('h-8 min-w-0 py-0 text-sm', parsed === null && 'border-red-400', inputClassName)} />
      {suffix && <span className="shrink-0 text-xs text-ink-500">{suffix}</span>}
      <Button type="submit" size="sm" busy={busy === 'save'} disabled={parsed === null} aria-label="Enregistrer"><Check className="size-4" /></Button>
      <Button type="button" size="sm" variant="ghost" aria-label="Annuler" onClick={close}><X className="size-4" /></Button>
    </form>
  );
}

export const nameOf = (max: number) => (raw: string) => {
  const value = raw.trim();
  return value && value.length <= max ? value : null;
};
