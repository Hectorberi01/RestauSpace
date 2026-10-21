'use client';
import {useMemo} from 'react';
import {useResource} from '@/lib/hooks';
import {loadCatalog, metricLabel, sourceLabel} from '@/lib/reports';
import {Alert, Spinner} from '@/components/ui';

export const MAX_METRICS = 30;

/** Les mesures du catalogue du reporting, cochées par groupe (30 au plus par rapport). */
export function MetricPicker({value, onChange}: {value: string[]; onChange: (metrics: string[]) => void}) {
  const catalog = useResource(signal => loadCatalog(signal), []);
  const groups = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const metric of catalog.data?.metrics ?? []) map.set(metric.source, [...(map.get(metric.source) ?? []), metric.code]);
    return [...map.entries()];
  }, [catalog.data]);
  if (catalog.error) return <Alert>{catalog.error}</Alert>;
  if (!catalog.data) return <Spinner />;
  const toggle = (code: string) => onChange(value.includes(code) ? value.filter(item => item !== code) : [...value, code]);

  return (
    <fieldset>
      <legend className="mb-1.5 flex w-full justify-between text-sm font-medium text-ink-800">
        Mesures<span className={value.length > MAX_METRICS ? 'text-red-600' : 'text-ink-500'}>{value.length} / {MAX_METRICS}</span>
      </legend>
      <div className="max-h-72 space-y-3 overflow-y-auto rounded-xl border border-slate-200 p-3">
        {groups.map(([source, codes]) => (
          <div key={source}>
            <p className="mb-1 text-xs font-semibold text-ink-500 uppercase">{sourceLabel[source] ?? source}</p>
            <div className="grid gap-x-3 sm:grid-cols-2">
              {codes.map(code => (
                <label key={code} className="flex items-center gap-2 py-0.5 text-sm">
                  <input type="checkbox" className="size-4 accent-brand-600" checked={value.includes(code)}
                    disabled={!value.includes(code) && value.length >= MAX_METRICS} onChange={() => toggle(code)} />
                  <span className="truncate" title={code}>{metricLabel[code] ?? code}</span>
                </label>
              ))}
            </div>
          </div>
        ))}
      </div>
    </fieldset>
  );
}
