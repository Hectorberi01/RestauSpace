'use client';
import type {ReactNode} from 'react';
import {ConsoleProvider} from '@/components/console';
import {Shell} from '@/components/shell';

export default function ConsoleLayout({children}: {children: ReactNode}) {
  return (
    <ConsoleProvider>
      <Shell>{children}</Shell>
    </ConsoleProvider>
  );
}
