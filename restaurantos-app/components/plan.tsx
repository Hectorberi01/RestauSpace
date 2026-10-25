'use client';
import {createContext, useCallback, useContext, useMemo, useState, type ReactNode} from 'react';
import Link from 'next/link';
import {Lock, Snowflake, Sparkles} from 'lucide-react';
import {loadPlanFeatures, optionFor, requiredPlan, type PlanFeature, type PlanFeatures} from '@/lib/plan';
import {useResource} from '@/lib/hooks';
import {useConsole} from './console';
import {cx} from './ui';

interface PlanState {
  data: PlanFeatures | null;
  /** Vrai seulement si l'API dit que le droit manque ; inconnu = permis (l'API tranche). */
  blocked: (code: string) => boolean;
  feature: (code: string) => PlanFeature | undefined;
  reload: () => Promise<void>;
  /** Change à chaque rechargement, pour les écrans qui en dépendent. */
  version: number;
}

const PlanContext = createContext<PlanState>({data: null, blocked: () => false, feature: () => undefined, reload: async () => {}, version: 0});

export function PlanFeaturesProvider({children}: {children: ReactNode}) {
  const {can, tenant} = useConsole();
  const allowed = can('billing.view');
  const [version, setVersion] = useState(0);
  const resource = useResource(
    signal => (allowed ? loadPlanFeatures(signal).catch(() => null) : Promise.resolve(null)),
    [allowed, tenant.id],
  );
  const {reload: load} = resource;
  const reload = useCallback(async () => {
    await load();
    setVersion(value => value + 1);
  }, [load]);
  const data = resource.data ?? null;
  const value = useMemo<PlanState>(() => {
    const byCode = new Map((data?.features ?? []).map(item => [item.code, item]));
    return {
      data,
      feature: code => byCode.get(code),
      blocked: code => !!data && !(byCode.get(code)?.enabled ?? false),
      reload,
      version,
    };
  }, [data, reload, version]);
  return <PlanContext.Provider value={value}>{children}</PlanContext.Provider>;
}

export function usePlanFeatures(): PlanState {
  return useContext(PlanContext);
}

/** Badge « Pro » / « Multi-sites » d'une fonction non incluse. */
export function PlanBadge({feature, className, force}: {feature: string; className?: string; force?: boolean}) {
  const {blocked} = usePlanFeatures();
  if (!force && !blocked(feature)) return null;
  return (
    <span className={cx('inline-flex items-center gap-1 rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-bold tracking-wide text-amber-800 uppercase', className)}>
      <Sparkles className="size-3" aria-hidden="true" />{requiredPlan(feature)}
    </span>
  );
}

/** Panneau d'incitation à la place d'actions que l'API refuserait. */
export function UpgradePanel({feature, title, children, compact}: {feature: string; title: string; children?: ReactNode; compact?: boolean}) {
  const plan = requiredPlan(feature);
  const option = optionFor(feature);
  const included = plan === 'Option' ? `Disponible avec l’option ${option}.` : `Inclus dans le plan ${plan}${option ? `, ou avec l’option ${option}` : ''}.`;
  return (
    <div className={cx('flex flex-col gap-3 rounded-2xl border border-amber-200 bg-gradient-to-br from-amber-50 to-orange-50 sm:flex-row sm:items-center', compact ? 'p-4' : 'p-5')}>
      <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-white text-amber-600 shadow-sm" aria-hidden="true"><Lock className="size-5" /></span>
      <div className="min-w-0 flex-1 text-sm">
        <p className="flex flex-wrap items-center gap-2 font-semibold text-ink-900">{title}<PlanBadge feature={feature} /></p>
        <p className="mt-0.5 text-ink-600">{children ? <>{children} </> : null}{included}</p>
      </div>
      <Link href="/abonnement" className="inline-flex h-9 shrink-0 items-center justify-center rounded-xl bg-brand-600 px-4 text-sm font-semibold text-white shadow-sm hover:bg-brand-700">Voir les plans</Link>
    </div>
  );
}

/** Affiche le contenu si le droit est là, sinon le panneau. */
export function RequireFeature({feature, title, hint, children}: {feature: string; title: string; hint?: ReactNode; children: ReactNode}) {
  const {blocked} = usePlanFeatures();
  if (blocked(feature)) return <UpgradePanel feature={feature} title={title}>{hint}</UpgradePanel>;
  return <>{children}</>;
}

export function FrozenBadge({className}: {className?: string}) {
  return (
    <span title="Au-delà de la limite de votre plan" className={cx('inline-flex items-center gap-1 rounded-full bg-sky-100 px-2 py-0.5 text-xs font-semibold whitespace-nowrap text-sky-800', className)}>
      <Snowflake className="size-3" aria-hidden="true" />Gelé
    </span>
  );
}

/** Explication du gel, en tête des listes concernées. */
export function FrozenNote({what, action, fix}: {what: string; action: string; fix: string}) {
  return (
    <div role="status" className="flex flex-col gap-3 rounded-xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm text-sky-900 sm:flex-row sm:items-center">
      <Snowflake className="size-4 shrink-0" aria-hidden="true" />
      <p className="min-w-0 flex-1">
        <strong>{what}</strong> : au-delà de la limite de votre plan. {action} Passez à un plan supérieur ou {fix}.
      </p>
      <Link href="/abonnement" className="shrink-0 font-semibold text-sky-800 underline-offset-2 hover:underline">Voir les plans</Link>
    </div>
  );
}
