'use client';
import type {ReactNode} from 'react';
import {CheckCircle2, CircleAlert, XCircle} from 'lucide-react';
import {cx} from '@/components/ui';

export function Switch({on, label, disabled, onChange}: {on: boolean; label: string; disabled?: boolean; onChange: () => void}) {
  return (
    <button type="button" role="switch" aria-checked={on} aria-label={label} disabled={disabled} onClick={onChange}
      className={cx('relative h-6 w-11 shrink-0 rounded-full transition disabled:opacity-50', on ? 'bg-brand-600' : 'bg-slate-300')}>
      <span className={cx('absolute top-0.5 size-5 rounded-full bg-white shadow transition-all', on ? 'left-[1.375rem]' : 'left-0.5')} />
    </button>
  );
}

const testTones = {
  green: {box: 'border-emerald-200 bg-emerald-50 text-emerald-900', icon: CheckCircle2},
  amber: {box: 'border-amber-200 bg-amber-50 text-amber-900', icon: CircleAlert},
  red: {box: 'border-red-200 bg-red-50 text-red-800', icon: XCircle},
};

/** Le résultat d'un test de configuration : constat, explication, et précisions éventuelles. */
export function TestResult({tone, title, detail, children}: {tone: 'green' | 'amber' | 'red'; title: string; detail?: string; children?: ReactNode}) {
  const {box, icon: Icon} = testTones[tone];
  return (
    <div role={tone === 'red' ? 'alert' : 'status'} className={cx('flex gap-2 rounded-xl border px-4 py-3 text-sm', box)}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div className="min-w-0 space-y-1">
        <p className="font-semibold">{title}</p>
        {detail && <p>{detail}</p>}
        {children}
      </div>
    </div>
  );
}
