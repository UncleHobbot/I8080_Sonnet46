import { ControlPanel } from './components/ControlPanel/ControlPanel';
import { RegisterView } from './components/CpuPanel/RegisterView';
import { DiskPanel } from './components/DiskPanel/DiskPanel';
import { MemoryView } from './components/MemoryPanel/MemoryView';
import { Terminal } from './components/Terminal/Terminal';
import { useEmulatorState } from './hooks/useEmulatorState';
import { useSignalR } from './hooks/useSignalR';
import styles from './App.module.css';

const HUB_URL = '/hubs/emulator';

function App() {
  const { connection, connected } = useSignalR(HUB_URL);
  const cpuState = useEmulatorState(connection);

  return (
    <div className={styles.root}>
      <header className={styles.header}>
        <span className={styles.logo}>Intel 8080 / CP/M 2.2 Emulator</span>
        <span className={connected ? styles.connOn : styles.connOff}>
          {connected ? '● Online' : '○ Offline'}
        </span>
      </header>

      <div className={styles.main}>
        <div className={styles.terminalPane}>
          <Terminal connection={connection} connected={connected} />
        </div>

        <div className={styles.sidePane}>
          <ControlPanel
            connection={connection}
            connected={connected}
            cpuState={cpuState}
          />
          <RegisterView state={cpuState} />
          <MemoryView
            connection={connection}
            watchAddr={cpuState?.pc}
          />
          <DiskPanel connection={connection} />
        </div>
      </div>
    </div>
  );
}

export default App;
