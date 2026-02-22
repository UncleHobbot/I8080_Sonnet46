import { HubConnection } from '@microsoft/signalr';
import { FitAddon } from '@xterm/addon-fit';
import { Terminal as XTerm } from '@xterm/xterm';
import '@xterm/xterm/css/xterm.css';
import { useEffect, useRef } from 'react';
import styles from './Terminal.module.css';

interface Props {
  connection: HubConnection | null;
  connected: boolean;
}

export function Terminal({ connection, connected }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const xtermRef = useRef<XTerm | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);

  // Mount xterm.js once
  useEffect(() => {
    if (!containerRef.current) return;

    const term = new XTerm({
      cols: 80,
      rows: 24,
      fontFamily: '"Courier New", "Lucida Console", monospace',
      fontSize: 14,
      theme: {
        background: '#000000',
        foreground: '#00FF00',
        cursor: '#00FF00',
        cursorAccent: '#000000',
        selectionBackground: '#00FF0044',
      },
      cursorBlink: true,
      allowTransparency: false,
      scrollback: 1000,
    });

    const fitAddon = new FitAddon();
    term.loadAddon(fitAddon);
    term.open(containerRef.current);

    // Fit after a brief layout paint
    setTimeout(() => fitAddon.fit(), 50);

    xtermRef.current = term;
    fitAddonRef.current = fitAddon;

    const observer = new ResizeObserver(() => fitAddon.fit());
    observer.observe(containerRef.current);

    return () => {
      observer.disconnect();
      term.dispose();
    };
  }, []);

  // Wire SignalR → xterm (terminal output)
  useEffect(() => {
    if (!connection) return;
    const handler = (text: string) => xtermRef.current?.write(text);
    connection.on('ReceiveOutput', handler);
    return () => connection.off('ReceiveOutput', handler);
  }, [connection]);

  // Wire xterm → SignalR (keyboard input)
  useEffect(() => {
    if (!connection || !xtermRef.current) return;
    const term = xtermRef.current;

    const disposable = term.onData((data: string) => {
      connection.invoke('SendInput', data).catch(console.error);
    });

    return () => disposable.dispose();
  }, [connection]);

  // Show connection status in terminal
  useEffect(() => {
    const term = xtermRef.current;
    if (!term) return;
    if (connected) {
      term.write('\r\n\x1b[32m[Connected to emulator]\x1b[0m\r\n');
    } else {
      term.write('\r\n\x1b[31m[Disconnected - reconnecting...]\x1b[0m\r\n');
    }
  }, [connected]);

  return (
    <div className={styles.wrapper}>
      <div ref={containerRef} className={styles.terminal} />
    </div>
  );
}
