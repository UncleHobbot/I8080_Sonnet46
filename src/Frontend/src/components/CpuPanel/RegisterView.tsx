import type { CpuState } from '../../types/emulator';
import styles from './RegisterView.module.css';

interface Props {
  state: CpuState | null;
}

const h2 = (n: number) => n.toString(16).padStart(2, '0').toUpperCase();
const h4 = (n: number) => n.toString(16).padStart(4, '0').toUpperCase();

export function RegisterView({ state }: Props) {
  if (!state) return <div className={styles.panel}><span className={styles.dim}>Waiting for CPU state...</span></div>;

  const bc = (state.b << 8) | state.c;
  const de = (state.d << 8) | state.e;
  const hl = (state.h << 8) | state.l;

  const Flag = ({ name, val }: { name: string; val: boolean }) => (
    <span className={val ? styles.flagOn : styles.flagOff}>{name}</span>
  );

  return (
    <div className={styles.panel}>
      <div className={styles.title}>CPU Registers</div>
      <table className={styles.regs}>
        <tbody>
          <tr>
            <td className={styles.lbl}>A</td><td className={styles.val}>{h2(state.a)}</td>
            <td className={styles.lbl}>F</td>
            <td className={styles.val}>
              <Flag name="S" val={state.flagS} />
              <Flag name="Z" val={state.flagZ} />
              <Flag name="AC" val={state.flagAC} />
              <Flag name="P" val={state.flagP} />
              <Flag name="CY" val={state.flagCY} />
            </td>
          </tr>
          <tr>
            <td className={styles.lbl}>BC</td><td className={styles.val}>{h4(bc)}</td>
            <td className={styles.lbl}>DE</td><td className={styles.val}>{h4(de)}</td>
          </tr>
          <tr>
            <td className={styles.lbl}>HL</td><td className={styles.val}>{h4(hl)}</td>
            <td className={styles.lbl}>SP</td><td className={styles.val}>{h4(state.sp)}</td>
          </tr>
          <tr>
            <td className={styles.lbl}>PC</td>
            <td className={styles.val} colSpan={3}>
              {h4(state.pc)}
              {state.currentDisassembly && (
                <span className={styles.disasm}> ; {state.currentDisassembly}</span>
              )}
            </td>
          </tr>
        </tbody>
      </table>
      <div className={styles.cycles}>
        Cycles: {state.totalCycles.toLocaleString()}
        {state.halted && <span className={styles.halted}> [HALTED]</span>}
        {state.interruptsEnabled && <span className={styles.iff}> IFF</span>}
      </div>
    </div>
  );
}
