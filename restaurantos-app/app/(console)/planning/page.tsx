'use client';
import {useState} from 'react';
import {useSearchParams} from 'next/navigation';
import {CalendarClock, CalendarRange, ClipboardCheck, Repeat, UserRound} from 'lucide-react';
import {isoDate} from '@/lib/format';
import {useResource} from '@/lib/hooks';
import {listSwaps, listTimeOff, mondayOf, parseDay} from '@/lib/schedule';
import {useConsole} from '@/components/console';
import {Card, Empty, PageHeader, cx} from '@/components/ui';
import {WeekBoard} from './week';
import {Absences} from './absences';
import {Swaps} from './swaps';
import {Variance} from './variance';
import {MySchedule} from './mine';

type View = 'week' | 'absences' | 'swaps' | 'variance' | 'mine';

/**
 * Planning de l'équipe. Le gérant (staff.schedule) prépare la semaine en brouillon et la publie ; chacun voit son
 * planning publié dans « Mon planning », demande ses absences et propose ses créneaux à un collègue.
 */
export default function PlanningPage() {
  const {can, location} = useConsole();
  const params = useSearchParams();
  const manager = can('staff.schedule');
  const requested = params.get('semaine');
  const [weekStart, setWeekStart] = useState(() => isoDate(mondayOf(requested && /^\d{4}-\d{2}-\d{2}$/.test(requested) ? parseDay(requested) : new Date())));
  const [view, setView] = useState<View>(!manager || params.get('vue') === 'moi' ? 'mine' : 'week');
  const pending = useResource(
    async signal => {
      if (!manager) return {absences: 0, swaps: 0};
      const today = new Date();
      const [absences, swaps] = await Promise.all([
        listTimeOff(isoDate(today), isoDate(new Date(today.getFullYear(), today.getMonth(), today.getDate() + 84)), 'Pending', signal).catch(() => []),
        listSwaps(true, signal).catch(() => []),
      ]);
      return {absences: absences.length, swaps: swaps.filter(swap => swap.status === 'AwaitingManager').length};
    },
    [manager],
  );

  const tabs: {value: View; label: string; icon: typeof CalendarClock; count?: number}[] = manager
    ? [
        {value: 'week', label: 'Semaine', icon: CalendarRange},
        {value: 'absences', label: 'Absences', icon: CalendarClock, count: pending.data?.absences},
        {value: 'swaps', label: 'Remplacements', icon: Repeat, count: pending.data?.swaps},
        {value: 'variance', label: 'Prévu / pointé', icon: ClipboardCheck},
        {value: 'mine', label: 'Mon planning', icon: UserRound},
      ]
    : [];

  return (
    <div>
      <PageHeader
        title={view === 'mine' && !manager ? 'Mon planning' : 'Planning'}
        description={manager ? 'Préparez la semaine, publiez-la à l’équipe et suivez absences, remplacements et pointages.' : 'Vos créneaux publiés, vos absences et vos remplacements.'}
      />
      <Card className="p-4 sm:p-5">
        {tabs.length > 0 && (
          <div role="tablist" aria-label="Planning" className="-mx-1 mb-4 flex gap-1 overflow-x-auto border-b border-slate-200">
            {tabs.map(({value, label, icon: Icon, count}) => (
              <button key={value} type="button" role="tab" aria-selected={view === value} onClick={() => setView(value)}
                className={cx('-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold whitespace-nowrap', view === value ? 'border-brand-600 text-brand-700' : 'border-transparent text-ink-500 hover:text-ink-800')}>
                <Icon className="size-4" aria-hidden="true" />{label}
                {!!count && <span className="rounded-full bg-amber-100 px-1.5 text-xs font-bold text-amber-800">{count}</span>}
              </button>
            ))}
          </div>
        )}
        {view === 'mine' ? (
          <MySchedule />
        ) : !location ? (
          <Empty title="Aucun établissement">Choisissez un établissement en haut de l’écran.</Empty>
        ) : view === 'week' ? (
          <WeekBoard location={location} weekStart={weekStart} onWeekChange={setWeekStart} onPendingChanged={pending.reload} />
        ) : view === 'absences' ? (
          <Absences location={location} onChanged={pending.reload} />
        ) : view === 'swaps' ? (
          <Swaps onChanged={pending.reload} />
        ) : (
          <Variance location={location} weekStart={weekStart} onWeekChange={setWeekStart} />
        )}
      </Card>
    </div>
  );
}
