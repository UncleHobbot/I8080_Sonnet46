import { HubConnection } from '@microsoft/signalr';
import { useCallback, useEffect, useState } from 'react';
import styles from './MemoryView.module.css';

interface Props {
  connection: HubConnection | null;
  watchAddr?: number; // highlight address (e.g. PC)
}

const ROWS = 8;
const COLS = 16;

export function MemoryView({ connection, watchAddr }: Props) {
  const [address, setAddress] = useState(0x0100);
  const [inputVal, setInputVal] = useState('0100');
  const [bytes, setBytes] = useState<Uint8Array>(new Uint8Array(ROWS * COLS));

  const refresh = useCallback(async () => {
    if (!connection) return;
    try {
      const data: number[] = await connection.invoke('ReadMemory', address, ROWS * COLS);
      setBytes(new Uint8Array(data));
    } catch { /* server not ready */ }
  }, [connection, address]);

  // Auto-refresh every 500ms when connected
  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 500);
    return () => clearInterval(id);
  }, [refresh]);

  const handleAddrSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const parsed = parseInt(inputVal, 16);
    if (!isNaN(parsed)) setAddress(parsed & 0xFFFF);
  };

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <span className={styles.title}>Memory</span>
        <form onSubmit={handleAddrSubmit} className={styles.addrForm}>
          <span className={styles.prefix}>0x</span>
          <input
            className={styles.addrInput}
            value={inputVal}
            onChange={e => setInputVal(e.target.value.toUpperCase())}
            maxLength={4}
          />
          <button type="submit" className={styles.goBtn}>Go</button>
        </form>
      </div>

      <div className={styles.hexGrid}>
        <div className={styles.gridRow + ' ' + styles.gridHeader}>
          <span className={styles.addrCol}> Addr </span>
          {Array.from({ length: COLS }, (_, i) => (
            <span key={i} className={styles.hexCell}>
              {i.toString(16).toUpperCase().padStart(2, '0')}
            </span>
          ))}
          <span className={styles.asciiCol}>ASCII</span>
        </div>

        {Array.from({ length: ROWS }, (_, row) => {
          const rowAddr = address + row * COLS;
          const ascii = Array.from({ length: COLS }, (_, col) => {
            const b = bytes[row * COLS + col] ?? 0;
            return b >= 32 && b < 127 ? String.fromCharCode(b) : '.';
          }).join('');

          return (
            <div key={row} className={styles.gridRow}>
              <span className={styles.addrCol}>
                {rowAddr.toString(16).padStart(4, '0').toUpperCase()}
              </span>
              {Array.from({ length: COLS }, (_, col) => {
                const absAddr = rowAddr + col;
                const isWatch = watchAddr !== undefined && absAddr === watchAddr;
                const b = bytes[row * COLS + col] ?? 0;
                return (
                  <span
                    key={col}
                    className={`${styles.hexCell} ${isWatch ? styles.watched : ''}`}
                  >
                    {b.toString(16).padStart(2, '0').toUpperCase()}
                  </span>
                );
              })}
              <span className={styles.asciiCol}>{ascii}</span>
            </div>
          );
        })}
      </div>

      <div className={styles.navRow}>
        <button className={styles.navBtn} onClick={() => setAddress(a => Math.max(0, a - ROWS * COLS))}>◀</button>
        <button className={styles.navBtn} onClick={() => setAddress(a => Math.min(0xFFFF - ROWS * COLS, a + ROWS * COLS))}>▶</button>
        <button className={styles.navBtn} onClick={() => { setAddress(0x0000); setInputVal('0000'); }}>Zero page</button>
        <button className={styles.navBtn} onClick={() => { setAddress(0x0100); setInputVal('0100'); }}>TPA</button>
      </div>
    </div>
  );
}
