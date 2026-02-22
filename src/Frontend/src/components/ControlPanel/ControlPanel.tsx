import { HubConnection } from '@microsoft/signalr';
import { useState } from 'react';
import type { CpuState } from '../../types/emulator';
import styles from './ControlPanel.module.css';

interface Props {
  connection: HubConnection | null;
  connected: boolean;
  cpuState: CpuState | null;
}

export function ControlPanel({ connection, connected, cpuState }: Props) {
  const [speed, setSpeed] = useState(2_000_000);

  const invoke = (method: string, ...args: unknown[]) =>
    connection?.invoke(method, ...args).catch(console.error);

  const isRunning = cpuState?.isRunning ?? false;

  return (
    <div className={styles.panel}>
      <div className={styles.title}>Control</div>

      <div className={styles.statusRow}>
        <span className={connected ? styles.ledOn : styles.ledOff}>●</span>
        <span className={styles.statusText}>
          {!connected ? 'Disconnected' : isRunning ? 'Running' : 'Paused'}
        </span>
      </div>

      <div className={styles.buttons}>
        <button
          className={`${styles.btn} ${isRunning ? styles.btnPause : styles.btnRun}`}
          onClick={() => invoke(isRunning ? 'Pause' : 'Resume')}
          disabled={!connected}
        >
          {isRunning ? '⏸ Pause' : '▶ Run'}
        </button>
        <button
          className={styles.btn}
          onClick={() => invoke('Step')}
          disabled={!connected || isRunning}
          title="Execute one instruction"
        >
          ⏭ Step
        </button>
      </div>

      <div className={styles.buttons}>
        <button
          className={`${styles.btn} ${styles.btnReset}`}
          onClick={() => invoke('Reset')}
          disabled={!connected}
          title="Warm boot (reload CCP)"
        >
          ↺ Warm Boot
        </button>
        <button
          className={`${styles.btn} ${styles.btnReset}`}
          onClick={() => invoke('HardReset')}
          disabled={!connected}
          title="Cold boot (restart from BOOT)"
        >
          ⚡ Cold Boot
        </button>
      </div>

      <div className={styles.speedSection}>
        <label className={styles.speedLabel}>
          CPU Speed: {formatHz(speed)}
        </label>
        <input
          type="range"
          className={styles.slider}
          min={100_000}
          max={10_000_000}
          step={100_000}
          value={speed}
          onChange={e => {
            const hz = Number(e.target.value);
            setSpeed(hz);
            invoke('SetSpeed', hz);
          }}
        />
        <div className={styles.speedHints}>
          <span>0.1 MHz</span><span>10 MHz</span>
        </div>
      </div>

      {cpuState && (
        <div className={styles.pcLine}>
          PC: <span className={styles.pcVal}>
            {cpuState.pc.toString(16).padStart(4,'0').toUpperCase()}h
          </span>
          {cpuState.currentDisassembly && (
            <span className={styles.disasm}> {cpuState.currentDisassembly}</span>
          )}
        </div>
      )}
    </div>
  );
}

function formatHz(hz: number): string {
  if (hz >= 1_000_000) return `${(hz / 1_000_000).toFixed(1)} MHz`;
  return `${(hz / 1_000).toFixed(0)} KHz`;
}
