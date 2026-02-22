export interface CpuState {
  a: number;
  b: number; c: number;
  d: number; e: number;
  h: number; l: number;
  sp: number;
  pc: number;
  flagS: boolean;
  flagZ: boolean;
  flagAC: boolean;
  flagP: boolean;
  flagCY: boolean;
  halted: boolean;
  interruptsEnabled: boolean;
  totalCycles: number;
  isRunning: boolean;
  speedHz: number;
  currentDisassembly: string | null;
}

export interface StatusMessage {
  level: 'info' | 'warn' | 'error';
  text: string;
}
