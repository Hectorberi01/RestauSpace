'use client';
import type {ReactNode} from 'react';
import {ConsoleProvider} from '@/components/console';
import {PlanFeaturesProvider} from '@/components/plan';
import {Shell} from '@/components/shell';

export default function ConsoleLayout({children}: {children: ReactNode}) {
  return (
    <ConsoleProvider>
      <PlanFeaturesProvider>
        <Shell>{children}</Shell>
      </PlanFeaturesProvider>
    </ConsoleProvider>
  );
}
