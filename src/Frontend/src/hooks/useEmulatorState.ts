import { HubConnection } from '@microsoft/signalr';
import { useEffect, useState } from 'react';
import type { CpuState } from '../types/emulator';

export function useEmulatorState(connection: HubConnection | null) {
  const [cpuState, setCpuState] = useState<CpuState | null>(null);

  useEffect(() => {
    if (!connection) return;

    const handler = (state: CpuState) => setCpuState(state);
    connection.on('CpuStateUpdate', handler);

    // Request initial state
    connection.invoke('GetCpuState')
      .then((s: CpuState | null) => { if (s) setCpuState(s); })
      .catch(() => {}); // server may not have CP/M loaded yet

    return () => connection.off('CpuStateUpdate', handler);
  }, [connection]);

  return cpuState;
}
