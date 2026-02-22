using System.Runtime.CompilerServices;

namespace Emulator.CPU;

// Opcode cycle counts for the 8080A
// Source: Intel 8080A/8085 Assembly Language Programming Manual
public sealed partial class I8080
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int Dispatch(byte op)
    {
        switch (op)
        {
            // ── NOP ─────────────────────────────────────────────────────────
            case 0x00: return 4;  // NOP

            // ── LXI ─────────────────────────────────────────────────────────
            case 0x01: BC = FetchWord(); return 10; // LXI B,nn
            case 0x11: DE = FetchWord(); return 10; // LXI D,nn
            case 0x21: HL = FetchWord(); return 10; // LXI H,nn
            case 0x31: SP = FetchWord(); return 10; // LXI SP,nn

            // ── STAX / LDAX ─────────────────────────────────────────────────
            case 0x02: Memory.Write(BC, A); return 7;  // STAX B
            case 0x12: Memory.Write(DE, A); return 7;  // STAX D
            case 0x0A: A = Memory.Read(BC); return 7;  // LDAX B
            case 0x1A: A = Memory.Read(DE); return 7;  // LDAX D

            // ── INX ─────────────────────────────────────────────────────────
            case 0x03: BC++; return 5; // INX B
            case 0x13: DE++; return 5; // INX D
            case 0x23: HL++; return 5; // INX H
            case 0x33: SP++; return 5; // INX SP

            // ── DCX ─────────────────────────────────────────────────────────
            case 0x0B: BC--; return 5; // DCX B
            case 0x1B: DE--; return 5; // DCX D
            case 0x2B: HL--; return 5; // DCX H
            case 0x3B: SP--; return 5; // DCX SP

            // ── INR ─────────────────────────────────────────────────────────
            case 0x04: B = Inr(B); return 5;  // INR B
            case 0x0C: C = Inr(C); return 5;  // INR C
            case 0x14: D = Inr(D); return 5;  // INR D
            case 0x1C: E = Inr(E); return 5;  // INR E
            case 0x24: H = Inr(H); return 5;  // INR H
            case 0x2C: L = Inr(L); return 5;  // INR L
            case 0x34: Memory.Write(HL, Inr(Memory.Read(HL))); return 10; // INR M
            case 0x3C: A = Inr(A); return 5;  // INR A

            // ── DCR ─────────────────────────────────────────────────────────
            case 0x05: B = Dcr(B); return 5;  // DCR B
            case 0x0D: C = Dcr(C); return 5;  // DCR C
            case 0x15: D = Dcr(D); return 5;  // DCR D
            case 0x1D: E = Dcr(E); return 5;  // DCR E
            case 0x25: H = Dcr(H); return 5;  // DCR H
            case 0x2D: L = Dcr(L); return 5;  // DCR L
            case 0x35: Memory.Write(HL, Dcr(Memory.Read(HL))); return 10; // DCR M
            case 0x3D: A = Dcr(A); return 5;  // DCR A

            // ── MVI ─────────────────────────────────────────────────────────
            case 0x06: B = Fetch(); return 7;  // MVI B,n
            case 0x0E: C = Fetch(); return 7;  // MVI C,n
            case 0x16: D = Fetch(); return 7;  // MVI D,n
            case 0x1E: E = Fetch(); return 7;  // MVI E,n
            case 0x26: H = Fetch(); return 7;  // MVI H,n
            case 0x2E: L = Fetch(); return 7;  // MVI L,n
            case 0x36: Memory.Write(HL, Fetch()); return 10; // MVI M,n
            case 0x3E: A = Fetch(); return 7;  // MVI A,n

            // ── RLC / RRC / RAL / RAR ────────────────────────────────────────
            case 0x07: // RLC
            {
                bool bit7 = (A & 0x80) != 0;
                A = (byte)((A << 1) | (bit7 ? 1 : 0));
                FlagCY = bit7;
                return 4;
            }
            case 0x0F: // RRC
            {
                bool bit0 = (A & 0x01) != 0;
                A = (byte)((A >> 1) | (bit0 ? 0x80 : 0));
                FlagCY = bit0;
                return 4;
            }
            case 0x17: // RAL
            {
                bool bit7 = (A & 0x80) != 0;
                A = (byte)((A << 1) | (FlagCY ? 1 : 0));
                FlagCY = bit7;
                return 4;
            }
            case 0x1F: // RAR
            {
                bool bit0 = (A & 0x01) != 0;
                A = (byte)((A >> 1) | (FlagCY ? 0x80 : 0));
                FlagCY = bit0;
                return 4;
            }

            // ── DAD ─────────────────────────────────────────────────────────
            case 0x09: Dad(BC); return 10; // DAD B
            case 0x19: Dad(DE); return 10; // DAD D
            case 0x29: Dad(HL); return 10; // DAD H
            case 0x39: Dad(SP); return 10; // DAD SP

            // ── SHLD / LHLD ─────────────────────────────────────────────────
            case 0x22: // SHLD addr
            {
                ushort addr = FetchWord();
                Memory.WriteWord(addr, HL);
                return 16;
            }
            case 0x2A: // LHLD addr
            {
                ushort addr = FetchWord();
                HL = Memory.ReadWord(addr);
                return 16;
            }

            // ── STA / LDA ────────────────────────────────────────────────────
            case 0x32: // STA addr
            {
                ushort addr = FetchWord();
                Memory.Write(addr, A);
                return 13;
            }
            case 0x3A: // LDA addr
            {
                ushort addr = FetchWord();
                A = Memory.Read(addr);
                return 13;
            }

            // ── DAA ─────────────────────────────────────────────────────────
            case 0x27: // DAA
            {
                byte correction = 0;
                bool newCY = FlagCY;

                if (FlagAC || (A & 0x0F) > 9)
                {
                    correction |= 0x06;
                }
                if (FlagCY || A > 0x99)
                {
                    correction |= 0x60;
                    newCY = true;
                }
                FlagAC = ((A & 0x0F) + (correction & 0x0F)) > 0x0F;
                A += correction;
                FlagCY = newCY;
                SetSZP(A);
                return 4;
            }

            // ── CMA ─────────────────────────────────────────────────────────
            case 0x2F: A = (byte)~A; return 4; // CMA

            // ── STC / CMC ────────────────────────────────────────────────────
            case 0x37: FlagCY = true; return 4;   // STC
            case 0x3F: FlagCY = !FlagCY; return 4; // CMC

            // ── MOV ─────────────────────────────────────────────────────────
            // MOV B,r
            case 0x40: return 5; // MOV B,B (NOP)
            case 0x41: B = C; return 5;
            case 0x42: B = D; return 5;
            case 0x43: B = E; return 5;
            case 0x44: B = H; return 5;
            case 0x45: B = L; return 5;
            case 0x46: B = Memory.Read(HL); return 7; // MOV B,M
            case 0x47: B = A; return 5;
            // MOV C,r
            case 0x48: C = B; return 5;
            case 0x49: return 5; // MOV C,C
            case 0x4A: C = D; return 5;
            case 0x4B: C = E; return 5;
            case 0x4C: C = H; return 5;
            case 0x4D: C = L; return 5;
            case 0x4E: C = Memory.Read(HL); return 7;
            case 0x4F: C = A; return 5;
            // MOV D,r
            case 0x50: D = B; return 5;
            case 0x51: D = C; return 5;
            case 0x52: return 5; // MOV D,D
            case 0x53: D = E; return 5;
            case 0x54: D = H; return 5;
            case 0x55: D = L; return 5;
            case 0x56: D = Memory.Read(HL); return 7;
            case 0x57: D = A; return 5;
            // MOV E,r
            case 0x58: E = B; return 5;
            case 0x59: E = C; return 5;
            case 0x5A: E = D; return 5;
            case 0x5B: return 5; // MOV E,E
            case 0x5C: E = H; return 5;
            case 0x5D: E = L; return 5;
            case 0x5E: E = Memory.Read(HL); return 7;
            case 0x5F: E = A; return 5;
            // MOV H,r
            case 0x60: H = B; return 5;
            case 0x61: H = C; return 5;
            case 0x62: H = D; return 5;
            case 0x63: H = E; return 5;
            case 0x64: return 5; // MOV H,H
            case 0x65: H = L; return 5;
            case 0x66: H = Memory.Read(HL); return 7;
            case 0x67: H = A; return 5;
            // MOV L,r
            case 0x68: L = B; return 5;
            case 0x69: L = C; return 5;
            case 0x6A: L = D; return 5;
            case 0x6B: L = E; return 5;
            case 0x6C: L = H; return 5;
            case 0x6D: return 5; // MOV L,L
            case 0x6E: L = Memory.Read(HL); return 7;
            case 0x6F: L = A; return 5;
            // MOV M,r
            case 0x70: Memory.Write(HL, B); return 7;
            case 0x71: Memory.Write(HL, C); return 7;
            case 0x72: Memory.Write(HL, D); return 7;
            case 0x73: Memory.Write(HL, E); return 7;
            case 0x74: Memory.Write(HL, H); return 7;
            case 0x75: Memory.Write(HL, L); return 7;
            case 0x76: Halted = true; return 7; // HLT
            case 0x77: Memory.Write(HL, A); return 7;
            // MOV A,r
            case 0x78: A = B; return 5;
            case 0x79: A = C; return 5;
            case 0x7A: A = D; return 5;
            case 0x7B: A = E; return 5;
            case 0x7C: A = H; return 5;
            case 0x7D: A = L; return 5;
            case 0x7E: A = Memory.Read(HL); return 7;
            case 0x7F: return 5; // MOV A,A

            // ── ADD ─────────────────────────────────────────────────────────
            case 0x80: A = Add(A, B); return 4;
            case 0x81: A = Add(A, C); return 4;
            case 0x82: A = Add(A, D); return 4;
            case 0x83: A = Add(A, E); return 4;
            case 0x84: A = Add(A, H); return 4;
            case 0x85: A = Add(A, L); return 4;
            case 0x86: A = Add(A, Memory.Read(HL)); return 7; // ADD M
            case 0x87: A = Add(A, A); return 4;

            // ── ADC ─────────────────────────────────────────────────────────
            case 0x88: A = Adc(A, B); return 4;
            case 0x89: A = Adc(A, C); return 4;
            case 0x8A: A = Adc(A, D); return 4;
            case 0x8B: A = Adc(A, E); return 4;
            case 0x8C: A = Adc(A, H); return 4;
            case 0x8D: A = Adc(A, L); return 4;
            case 0x8E: A = Adc(A, Memory.Read(HL)); return 7; // ADC M
            case 0x8F: A = Adc(A, A); return 4;

            // ── SUB ─────────────────────────────────────────────────────────
            case 0x90: A = Sub(A, B); return 4;
            case 0x91: A = Sub(A, C); return 4;
            case 0x92: A = Sub(A, D); return 4;
            case 0x93: A = Sub(A, E); return 4;
            case 0x94: A = Sub(A, H); return 4;
            case 0x95: A = Sub(A, L); return 4;
            case 0x96: A = Sub(A, Memory.Read(HL)); return 7; // SUB M
            case 0x97: A = Sub(A, A); return 4;

            // ── SBB ─────────────────────────────────────────────────────────
            case 0x98: A = Sbb(A, B); return 4;
            case 0x99: A = Sbb(A, C); return 4;
            case 0x9A: A = Sbb(A, D); return 4;
            case 0x9B: A = Sbb(A, E); return 4;
            case 0x9C: A = Sbb(A, H); return 4;
            case 0x9D: A = Sbb(A, L); return 4;
            case 0x9E: A = Sbb(A, Memory.Read(HL)); return 7; // SBB M
            case 0x9F: A = Sbb(A, A); return 4;

            // ── ANA ─────────────────────────────────────────────────────────
            case 0xA0: Ana(B); return 4;
            case 0xA1: Ana(C); return 4;
            case 0xA2: Ana(D); return 4;
            case 0xA3: Ana(E); return 4;
            case 0xA4: Ana(H); return 4;
            case 0xA5: Ana(L); return 4;
            case 0xA6: Ana(Memory.Read(HL)); return 7; // ANA M
            case 0xA7: Ana(A); return 4;

            // ── XRA ─────────────────────────────────────────────────────────
            case 0xA8: Xra(B); return 4;
            case 0xA9: Xra(C); return 4;
            case 0xAA: Xra(D); return 4;
            case 0xAB: Xra(E); return 4;
            case 0xAC: Xra(H); return 4;
            case 0xAD: Xra(L); return 4;
            case 0xAE: Xra(Memory.Read(HL)); return 7; // XRA M
            case 0xAF: Xra(A); return 4;

            // ── ORA ─────────────────────────────────────────────────────────
            case 0xB0: Ora(B); return 4;
            case 0xB1: Ora(C); return 4;
            case 0xB2: Ora(D); return 4;
            case 0xB3: Ora(E); return 4;
            case 0xB4: Ora(H); return 4;
            case 0xB5: Ora(L); return 4;
            case 0xB6: Ora(Memory.Read(HL)); return 7; // ORA M
            case 0xB7: Ora(A); return 4;

            // ── CMP ─────────────────────────────────────────────────────────
            case 0xB8: Cmp(B); return 4;
            case 0xB9: Cmp(C); return 4;
            case 0xBA: Cmp(D); return 4;
            case 0xBB: Cmp(E); return 4;
            case 0xBC: Cmp(H); return 4;
            case 0xBD: Cmp(L); return 4;
            case 0xBE: Cmp(Memory.Read(HL)); return 7; // CMP M
            case 0xBF: Cmp(A); return 4;

            // ── RNZ / RZ / RNC / RC / RPO / RPE / RP / RM ──────────────────
            case 0xC0: if (!FlagZ)  { PC = Pop(); return 11; } return 5; // RNZ
            case 0xC8: if ( FlagZ)  { PC = Pop(); return 11; } return 5; // RZ
            case 0xD0: if (!FlagCY) { PC = Pop(); return 11; } return 5; // RNC
            case 0xD8: if ( FlagCY) { PC = Pop(); return 11; } return 5; // RC
            case 0xE0: if (!FlagP)  { PC = Pop(); return 11; } return 5; // RPO
            case 0xE8: if ( FlagP)  { PC = Pop(); return 11; } return 5; // RPE
            case 0xF0: if (!FlagS)  { PC = Pop(); return 11; } return 5; // RP
            case 0xF8: if ( FlagS)  { PC = Pop(); return 11; } return 5; // RM

            // ── POP ─────────────────────────────────────────────────────────
            case 0xC1: BC = Pop(); return 10; // POP B
            case 0xD1: DE = Pop(); return 10; // POP D
            case 0xE1: HL = Pop(); return 10; // POP H
            case 0xF1: // POP PSW
            {
                ushort psw = Pop();
                A = (byte)(psw >> 8);
                (FlagS, FlagZ, FlagAC, FlagP, FlagCY) = Flags.Unpack((byte)psw);
                return 10;
            }

            // ── JNZ / JZ / JNC / JC / JPO / JPE / JP / JM ──────────────────
            case 0xC2: { ushort t = FetchWord(); if (!FlagZ)  PC = t; return 10; } // JNZ
            case 0xCA: { ushort t = FetchWord(); if ( FlagZ)  PC = t; return 10; } // JZ
            case 0xD2: { ushort t = FetchWord(); if (!FlagCY) PC = t; return 10; } // JNC
            case 0xDA: { ushort t = FetchWord(); if ( FlagCY) PC = t; return 10; } // JC
            case 0xE2: { ushort t = FetchWord(); if (!FlagP)  PC = t; return 10; } // JPO
            case 0xEA: { ushort t = FetchWord(); if ( FlagP)  PC = t; return 10; } // JPE
            case 0xF2: { ushort t = FetchWord(); if (!FlagS)  PC = t; return 10; } // JP
            case 0xFA: { ushort t = FetchWord(); if ( FlagS)  PC = t; return 10; } // JM

            // ── JMP ─────────────────────────────────────────────────────────
            case 0xC3: PC = FetchWord(); return 10; // JMP
            case 0xCB: PC = FetchWord(); return 10; // JMP (undocumented)

            // ── CNZ / CZ / CNC / CC / CPO / CPE / CP / CM ──────────────────
            case 0xC4: // CNZ
            {
                ushort t = FetchWord();
                if (!FlagZ) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xCC: // CZ
            {
                ushort t = FetchWord();
                if (FlagZ) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xD4: // CNC
            {
                ushort t = FetchWord();
                if (!FlagCY) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xDC: // CC
            {
                ushort t = FetchWord();
                if (FlagCY) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xE4: // CPO
            {
                ushort t = FetchWord();
                if (!FlagP) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xEC: // CPE
            {
                ushort t = FetchWord();
                if (FlagP) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xF4: // CP
            {
                ushort t = FetchWord();
                if (!FlagS) { Push(PC); PC = t; return 17; }
                return 11;
            }
            case 0xFC: // CM
            {
                ushort t = FetchWord();
                if (FlagS) { Push(PC); PC = t; return 17; }
                return 11;
            }

            // ── CALL ────────────────────────────────────────────────────────
            case 0xCD: // CALL
            case 0xDD: // CALL (undocumented)
            case 0xED: // CALL (undocumented)
            case 0xFD: // CALL (undocumented)
            {
                ushort t = FetchWord();
                Push(PC);
                PC = t;
                return 17;
            }

            // ── RET ─────────────────────────────────────────────────────────
            case 0xC9: PC = Pop(); return 10; // RET
            case 0xD9: PC = Pop(); return 10; // RET (undocumented)

            // ── PUSH ────────────────────────────────────────────────────────
            case 0xC5: Push(BC); return 11; // PUSH B
            case 0xD5: Push(DE); return 11; // PUSH D
            case 0xE5: Push(HL); return 11; // PUSH H
            case 0xF5: // PUSH PSW
                Push((ushort)((A << 8) | Flags.Pack(FlagS, FlagZ, FlagAC, FlagP, FlagCY)));
                return 11;

            // ── ADI / ACI / SUI / SBI / ANI / XRI / ORI / CPI ──────────────
            case 0xC6: A = Add(A, Fetch()); return 7;  // ADI n
            case 0xCE: A = Adc(A, Fetch()); return 7;  // ACI n
            case 0xD6: A = Sub(A, Fetch()); return 7;  // SUI n
            case 0xDE: A = Sbb(A, Fetch()); return 7;  // SBI n
            case 0xE6: Ana(Fetch()); return 7;          // ANI n
            case 0xEE: Xra(Fetch()); return 7;          // XRI n
            case 0xF6: Ora(Fetch()); return 7;          // ORI n
            case 0xFE: Cmp(Fetch()); return 7;          // CPI n

            // ── RST ─────────────────────────────────────────────────────────
            case 0xC7: Push(PC); PC = 0x00; return 11; // RST 0
            case 0xCF: Push(PC); PC = 0x08; return 11; // RST 1
            case 0xD7: Push(PC); PC = 0x10; return 11; // RST 2
            case 0xDF: Push(PC); PC = 0x18; return 11; // RST 3
            case 0xE7: Push(PC); PC = 0x20; return 11; // RST 4
            case 0xEF: Push(PC); PC = 0x28; return 11; // RST 5
            case 0xF7: Push(PC); PC = 0x30; return 11; // RST 6
            case 0xFF: Push(PC); PC = 0x38; return 11; // RST 7

            // ── PCHL ────────────────────────────────────────────────────────
            case 0xE9: PC = HL; return 5; // PCHL

            // ── SPHL ────────────────────────────────────────────────────────
            case 0xF9: SP = HL; return 5; // SPHL

            // ── XTHL ────────────────────────────────────────────────────────
            case 0xE3: // XTHL
            {
                ushort top = Memory.ReadWord(SP);
                Memory.WriteWord(SP, HL);
                HL = top;
                return 18;
            }

            // ── XCHG ────────────────────────────────────────────────────────
            case 0xEB: // XCHG
            {
                ushort tmp = HL;
                HL = DE;
                DE = tmp;
                return 4;
            }

            // ── EI / DI ─────────────────────────────────────────────────────
            case 0xFB: IFF = true;  return 4; // EI
            case 0xF3: IFF = false; return 4; // DI

            // ── IN / OUT ────────────────────────────────────────────────────
            case 0xDB: // IN n
            {
                byte port = Fetch();
                A = _io.In(port, this);
                return 10;
            }
            case 0xD3: // OUT n
            {
                byte port = Fetch();
                _io.Out(port, A, this);
                return 10;
            }

            // ── Undocumented / undefined → treat as NOP ──────────────────────
            default: return 4;
        }
    }
}
