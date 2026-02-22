import { HubConnection } from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';
import styles from './DiskPanel.module.css';

interface Props {
  connection: HubConnection | null;
}

const DRIVES = ['A', 'B', 'C', 'D'];
const DISK_SIZE = 256256;

export function DiskPanel({ connection }: Props) {
  const [selectedDrive, setSelectedDrive] = useState(0);
  const [files, setFiles] = useState<string[]>([]);
  const [diskActivity, setDiskActivity] = useState<{ drive: number; writing: boolean } | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Listen for disk activity events
  useEffect(() => {
    if (!connection) return;
    const handler = (drive: number, writing: boolean) => {
      setDiskActivity({ drive, writing });
      setTimeout(() => setDiskActivity(null), 200);
    };
    connection.on('DiskActivity', handler);

    const msgHandler = (_level: string, text: string) => {
      setStatus(text);
      setTimeout(() => setStatus(null), 3000);
    };
    connection.on('StatusMessage', msgHandler);

    return () => {
      connection.off('DiskActivity', handler);
      connection.off('StatusMessage', msgHandler);
    };
  }, [connection]);

  const refreshFiles = async () => {
    if (!connection) return;
    try {
      const list: string[] = await connection.invoke('ListDiskFiles', selectedDrive);
      setFiles(list);
    } catch {
      setFiles([]);
    }
  };

  useEffect(() => {
    refreshFiles();
  }, [connection, selectedDrive]);

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file || !connection) return;

    if (file.size !== DISK_SIZE) {
      setStatus(`Invalid size: ${file.size} bytes (expected ${DISK_SIZE})`);
      return;
    }

    const buffer = await file.arrayBuffer();
    const data = Array.from(new Uint8Array(buffer));
    await connection.invoke('UploadDisk', selectedDrive, file.name, data);
    await refreshFiles();
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  const handleDownload = async () => {
    if (!connection) return;
    const data: number[] = await connection.invoke('DownloadDisk', selectedDrive);
    if (!data.length) return;

    const blob = new Blob([new Uint8Array(data)], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `drive_${DRIVES[selectedDrive]}.dsk`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className={styles.panel}>
      <div className={styles.title}>Disk Drives</div>

      <div className={styles.driveTabs}>
        {DRIVES.map((d, i) => {
          const isActive = diskActivity?.drive === i;
          const isWrite = diskActivity?.writing;
          return (
            <button
              key={d}
              className={`${styles.driveTab} ${selectedDrive === i ? styles.selected : ''} ${isActive ? (isWrite ? styles.writing : styles.reading) : ''}`}
              onClick={() => setSelectedDrive(i)}
            >
              {d}:
            </button>
          );
        })}
      </div>

      <div className={styles.fileList}>
        {files.length === 0 ? (
          <div className={styles.emptyMsg}>No disk / empty</div>
        ) : (
          files.map((f, i) => (
            <div key={i} className={styles.fileEntry}>{f}</div>
          ))
        )}
      </div>

      <div className={styles.actions}>
        <button className={styles.actionBtn} onClick={refreshFiles}>↻ Refresh</button>
        <button className={styles.actionBtn} onClick={handleDownload}>↓ Download</button>
        <label className={styles.actionBtn}>
          ↑ Upload
          <input
            ref={fileInputRef}
            type="file"
            accept=".dsk"
            onChange={handleUpload}
            style={{ display: 'none' }}
          />
        </label>
      </div>

      {status && <div className={styles.statusMsg}>{status}</div>}
    </div>
  );
}
