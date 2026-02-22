import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';

export function useSignalR(url: string) {
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const connRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build();

    conn.onreconnected(() => setConnected(true));
    conn.onreconnecting(() => setConnected(false));
    conn.onclose(() => setConnected(false));

    conn.start()
      .then(() => {
        setConnected(true);
        setConnection(conn);
      })
      .catch((e: Error) => console.error('SignalR connect failed:', e));

    connRef.current = conn;

    return () => {
      conn.stop();
    };
  }, [url]);

  return { connection, connected };
}
