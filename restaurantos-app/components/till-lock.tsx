'use client';
import {useCallback, useEffect, useState} from 'react';
import {Clock3, Delete, Lock, LogIn, RefreshCw, Store} from 'lucide-react';
import {describe} from '@/lib/api';
import {clockOnTill, hours, tillState, unlockTill, writeTill, type TillDevice, type TillOperator, type TillState} from '@/lib/till';
import {Alert, Button, Spinner, cx, useToast} from './ui';

const initials = (name: string) =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') || '?';

const clockTime = (iso: string) => new Date(iso).toLocaleTimeString('fr-FR', {hour: '2-digit', minute: '2-digit'});

/** Pavé numérique du PIN : 4 à 6 chiffres, jamais affichés. */
function PinPad({operator, onCancel, onDone}: {operator: TillOperator; onCancel: () => void; onDone: (pin: string, action: 'unlock' | 'clock') => Promise<boolean>}) {
  const [pin, setPin] = useState('');
  const [busy, setBusy] = useState<'unlock' | 'clock' | null>(null);

  const press = (digit: string) => setPin(current => (current.length >= 6 ? current : current + digit));
  const submit = async (action: 'unlock' | 'clock') => {
    if (pin.length < 4) return;
    setBusy(action);
    const ok = await onDone(pin, action);
    setBusy(null);
    if (!ok) setPin('');
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (/^\d$/.test(event.key)) press(event.key);
      else if (event.key === 'Backspace') setPin(current => current.slice(0, -1));
      else if (event.key === 'Enter') void submit('unlock');
      else if (event.key === 'Escape') onCancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  return (
    <div className="mx-auto w-full max-w-xs">
      <p className="text-center text-lg font-semibold">{operator.name}</p>
      <p className="mt-1 text-center text-sm text-ink-500">Saisissez votre PIN</p>
      <div className="my-5 flex justify-center gap-3" aria-label={`${pin.length} chiffres saisis`}>
        {Array.from({length: Math.max(4, pin.length)}, (_, index) => (
          <span key={index} className={cx('size-3.5 rounded-full', index < pin.length ? 'bg-brand-600' : 'bg-slate-300')} />
        ))}
      </div>
      <div className="grid grid-cols-3 gap-3">
        {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map(digit => (
          <button key={digit} type="button" onClick={() => press(digit)} className="h-16 rounded-2xl bg-white text-2xl font-semibold shadow-sm ring-1 ring-slate-200 active:bg-slate-100">{digit}</button>
        ))}
        <button type="button" onClick={onCancel} className="h-16 rounded-2xl text-sm font-semibold text-ink-600">Annuler</button>
        <button type="button" onClick={() => press('0')} className="h-16 rounded-2xl bg-white text-2xl font-semibold shadow-sm ring-1 ring-slate-200 active:bg-slate-100">0</button>
        <button type="button" aria-label="Effacer" onClick={() => setPin(current => current.slice(0, -1))} className="grid h-16 place-items-center rounded-2xl text-ink-600"><Delete className="size-6" /></button>
      </div>
      <div className="mt-5 grid gap-2">
        <Button size="lg" busy={busy === 'unlock'} disabled={pin.length < 4 || !!busy} onClick={() => void submit('unlock')}><LogIn className="size-5" aria-hidden="true" />Ouvrir la caisse</Button>
        <Button size="lg" variant="secondary" busy={busy === 'clock'} disabled={pin.length < 4 || !!busy} onClick={() => void submit('clock')}>
          <Clock3 className="size-5" aria-hidden="true" />{operator.clockedInSinceUtc ? 'Pointer mon départ' : 'Pointer mon arrivée'}
        </Button>
      </div>
    </div>
  );
}

/**
 * Écran verrouillé d'une caisse partagée : chacun touche son nom puis tape son PIN, pour ouvrir la caisse à son nom ou
 * pointer son arrivée et son départ.
 */
export function TillLockScreen({device, onUnlocked}: {device: TillDevice; onUnlocked: (state: TillState) => void}) {
  const toast = useToast();
  const [state, setState] = useState<TillState | null>(null);
  const [error, setError] = useState<{message: string; revoked: boolean} | null>(null);
  const [chosen, setChosen] = useState<TillOperator | null>(null);

  const load = useCallback(async () => {
    try {
      setState(await tillState(device));
      setError(null);
    } catch (err) {
      const revoked = (err as {code?: string}).code === 'staff.till.invalid_key';
      setError({message: describe(err), revoked});
    }
  }, [device]);

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), 60_000);
    return () => window.clearInterval(timer);
  }, [load]);

  const done = async (pin: string, action: 'unlock' | 'clock') => {
    if (!chosen || !state) return false;
    try {
      if (action === 'unlock') {
        await unlockTill(device, chosen.staffMemberId, pin);
        onUnlocked(state);
        return true;
      }
      const result = await clockOnTill(device, chosen.staffMemberId, pin);
      const worked = result.worked ? hoursOf(result.worked) : null;
      toast('success', result.clockedIn
        ? `${chosen.name} : arrivée pointée à ${clockTime(result.atUtc)}.`
        : `${chosen.name} : départ pointé à ${clockTime(result.atUtc)}${worked ? ` (${worked})` : ''}.`);
      setChosen(null);
      await load();
      return true;
    } catch (err) {
      toast('error', describe(err));
      await load();
      return false;
    }
  };

  return (
    <div className="flex min-h-dvh flex-col bg-slate-100">
      <header className="flex items-center gap-3 bg-ink-900 px-5 py-3 text-white">
        <Store className="size-5" aria-hidden="true" />
        <span className="font-semibold">{device.tenantName}</span>
        <span className="text-white/60">· {state?.name ?? device.name}</span>
        <span className="ml-auto flex items-center gap-2 text-sm text-white/70"><Lock className="size-4" aria-hidden="true" />Caisse verrouillée</span>
      </header>
      <main className="flex flex-1 items-start justify-center p-6">
        {error ? (
          <div className="mt-16 w-full max-w-md space-y-4">
            <Alert>{error.message}</Alert>
            <div className="flex gap-2">
              <Button onClick={() => void load()}><RefreshCw className="size-4" aria-hidden="true" />Réessayer</Button>
              {error.revoked && <Button variant="secondary" onClick={() => { writeTill(null); window.location.replace('/connexion'); }}>Oublier cet appareil</Button>}
            </div>
          </div>
        ) : !state ? <Spinner label="Ouverture de la caisse…" /> : chosen ? (
          <div className="mt-8 w-full"><PinPad operator={chosen} onCancel={() => setChosen(null)} onDone={done} /></div>
        ) : (
          <div className="w-full max-w-4xl">
            <h1 className="mb-1 text-center text-2xl font-bold">Qui prend la caisse ?</h1>
            <p className="mb-6 text-center text-sm text-ink-500">Touchez votre nom puis saisissez votre PIN.</p>
            {state.operators.length === 0 ? (
              <Alert tone="amber">Personne n’est affecté à cet établissement. Le gérant affecte le personnel dans Personnel.</Alert>
            ) : (
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
                {state.operators.map(operator => (
                  <button key={operator.staffMemberId} type="button" disabled={!operator.hasPin || operator.pinLocked} onClick={() => setChosen(operator)}
                    className="flex flex-col items-center gap-2 rounded-2xl bg-white p-5 shadow-sm ring-1 ring-slate-200 transition active:scale-[0.98] enabled:hover:ring-brand-400 disabled:opacity-50">
                    <span className="grid size-14 place-items-center rounded-full bg-brand-100 text-lg font-bold text-brand-700">{initials(operator.name)}</span>
                    <span className="font-semibold">{operator.name}</span>
                    <span className="text-xs text-ink-500">
                      {!operator.hasPin ? 'PIN à choisir (Mon profil)' : operator.pinLocked ? 'PIN bloqué 15 min' : operator.clockedInSinceUtc ? `Pointé depuis ${clockTime(operator.clockedInSinceUtc)}` : 'Non pointé'}
                    </span>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
      </main>
    </div>
  );
}

/** Durée .NET (« 07:30:00 », « 1.02:00:00 ») en « 7 h 30 ». */
function hoursOf(span: string): string | null {
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)/.exec(span);
  if (!match) return null;
  const minutes = Number(match[1] ?? 0) * 1440 + Number(match[2]) * 60 + Number(match[3]);
  return hours(minutes);
}
