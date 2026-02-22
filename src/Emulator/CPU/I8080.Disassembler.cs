namespace Emulator.CPU;

public sealed partial class I8080
{
    /// <summary>
    /// Disassemble one instruction at the given address.
    /// Returns the mnemonic string and the number of bytes consumed.
    /// </summary>
    public string Disassemble(ushort addr, out int length)
    {
        byte op = Memory.Read(addr);
        switch (op)
        {
            case 0x00: length = 1; return "NOP";
            case 0x01: length = 3; return $"LXI  B,{WordAt(addr)}";
            case 0x02: length = 1; return "STAX B";
            case 0x03: length = 1; return "INX  B";
            case 0x04: length = 1; return "INR  B";
            case 0x05: length = 1; return "DCR  B";
            case 0x06: length = 2; return $"MVI  B,{ByteAt(addr)}";
            case 0x07: length = 1; return "RLC";
            case 0x09: length = 1; return "DAD  B";
            case 0x0A: length = 1; return "LDAX B";
            case 0x0B: length = 1; return "DCX  B";
            case 0x0C: length = 1; return "INR  C";
            case 0x0D: length = 1; return "DCR  C";
            case 0x0E: length = 2; return $"MVI  C,{ByteAt(addr)}";
            case 0x0F: length = 1; return "RRC";
            case 0x11: length = 3; return $"LXI  D,{WordAt(addr)}";
            case 0x12: length = 1; return "STAX D";
            case 0x13: length = 1; return "INX  D";
            case 0x14: length = 1; return "INR  D";
            case 0x15: length = 1; return "DCR  D";
            case 0x16: length = 2; return $"MVI  D,{ByteAt(addr)}";
            case 0x17: length = 1; return "RAL";
            case 0x19: length = 1; return "DAD  D";
            case 0x1A: length = 1; return "LDAX D";
            case 0x1B: length = 1; return "DCX  D";
            case 0x1C: length = 1; return "INR  E";
            case 0x1D: length = 1; return "DCR  E";
            case 0x1E: length = 2; return $"MVI  E,{ByteAt(addr)}";
            case 0x1F: length = 1; return "RAR";
            case 0x21: length = 3; return $"LXI  H,{WordAt(addr)}";
            case 0x22: length = 3; return $"SHLD {WordAt(addr)}";
            case 0x23: length = 1; return "INX  H";
            case 0x24: length = 1; return "INR  H";
            case 0x25: length = 1; return "DCR  H";
            case 0x26: length = 2; return $"MVI  H,{ByteAt(addr)}";
            case 0x27: length = 1; return "DAA";
            case 0x29: length = 1; return "DAD  H";
            case 0x2A: length = 3; return $"LHLD {WordAt(addr)}";
            case 0x2B: length = 1; return "DCX  H";
            case 0x2C: length = 1; return "INR  L";
            case 0x2D: length = 1; return "DCR  L";
            case 0x2E: length = 2; return $"MVI  L,{ByteAt(addr)}";
            case 0x2F: length = 1; return "CMA";
            case 0x31: length = 3; return $"LXI  SP,{WordAt(addr)}";
            case 0x32: length = 3; return $"STA  {WordAt(addr)}";
            case 0x33: length = 1; return "INX  SP";
            case 0x34: length = 1; return "INR  M";
            case 0x35: length = 1; return "DCR  M";
            case 0x36: length = 2; return $"MVI  M,{ByteAt(addr)}";
            case 0x37: length = 1; return "STC";
            case 0x39: length = 1; return "DAD  SP";
            case 0x3A: length = 3; return $"LDA  {WordAt(addr)}";
            case 0x3B: length = 1; return "DCX  SP";
            case 0x3C: length = 1; return "INR  A";
            case 0x3D: length = 1; return "DCR  A";
            case 0x3E: length = 2; return $"MVI  A,{ByteAt(addr)}";
            case 0x3F: length = 1; return "CMC";

            case >= 0x40 and <= 0x7F when op != 0x76:
                length = 1;
                return $"MOV  {DReg((op >> 3) & 7)},{DReg(op & 7)}";
            case 0x76: length = 1; return "HLT";

            case >= 0x80 and <= 0x87: length = 1; return $"ADD  {DReg(op & 7)}";
            case >= 0x88 and <= 0x8F: length = 1; return $"ADC  {DReg(op & 7)}";
            case >= 0x90 and <= 0x97: length = 1; return $"SUB  {DReg(op & 7)}";
            case >= 0x98 and <= 0x9F: length = 1; return $"SBB  {DReg(op & 7)}";
            case >= 0xA0 and <= 0xA7: length = 1; return $"ANA  {DReg(op & 7)}";
            case >= 0xA8 and <= 0xAF: length = 1; return $"XRA  {DReg(op & 7)}";
            case >= 0xB0 and <= 0xB7: length = 1; return $"ORA  {DReg(op & 7)}";
            case >= 0xB8 and <= 0xBF: length = 1; return $"CMP  {DReg(op & 7)}";

            case 0xC0: length = 1; return "RNZ";
            case 0xC1: length = 1; return "POP  B";
            case 0xC2: length = 3; return $"JNZ  {WordAt(addr)}";
            case 0xC3: length = 3; return $"JMP  {WordAt(addr)}";
            case 0xC4: length = 3; return $"CNZ  {WordAt(addr)}";
            case 0xC5: length = 1; return "PUSH B";
            case 0xC6: length = 2; return $"ADI  {ByteAt(addr)}";
            case 0xC7: length = 1; return "RST  0";
            case 0xC8: length = 1; return "RZ";
            case 0xC9: length = 1; return "RET";
            case 0xCA: length = 3; return $"JZ   {WordAt(addr)}";
            case 0xCC: length = 3; return $"CZ   {WordAt(addr)}";
            case 0xCD: length = 3; return $"CALL {WordAt(addr)}";
            case 0xCE: length = 2; return $"ACI  {ByteAt(addr)}";
            case 0xCF: length = 1; return "RST  1";
            case 0xD0: length = 1; return "RNC";
            case 0xD1: length = 1; return "POP  D";
            case 0xD2: length = 3; return $"JNC  {WordAt(addr)}";
            case 0xD3: length = 2; return $"OUT  {ByteAt(addr)}";
            case 0xD4: length = 3; return $"CNC  {WordAt(addr)}";
            case 0xD5: length = 1; return "PUSH D";
            case 0xD6: length = 2; return $"SUI  {ByteAt(addr)}";
            case 0xD7: length = 1; return "RST  2";
            case 0xD8: length = 1; return "RC";
            case 0xD9: length = 1; return "RET*";
            case 0xDA: length = 3; return $"JC   {WordAt(addr)}";
            case 0xDB: length = 2; return $"IN   {ByteAt(addr)}";
            case 0xDC: length = 3; return $"CC   {WordAt(addr)}";
            case 0xDD: length = 3; return $"CALL*{WordAt(addr)}";
            case 0xDE: length = 2; return $"SBI  {ByteAt(addr)}";
            case 0xDF: length = 1; return "RST  3";
            case 0xE0: length = 1; return "RPO";
            case 0xE1: length = 1; return "POP  H";
            case 0xE2: length = 3; return $"JPO  {WordAt(addr)}";
            case 0xE3: length = 1; return "XTHL";
            case 0xE4: length = 3; return $"CPO  {WordAt(addr)}";
            case 0xE5: length = 1; return "PUSH H";
            case 0xE6: length = 2; return $"ANI  {ByteAt(addr)}";
            case 0xE7: length = 1; return "RST  4";
            case 0xE8: length = 1; return "RPE";
            case 0xE9: length = 1; return "PCHL";
            case 0xEA: length = 3; return $"JPE  {WordAt(addr)}";
            case 0xEB: length = 1; return "XCHG";
            case 0xEC: length = 3; return $"CPE  {WordAt(addr)}";
            case 0xED: length = 3; return $"CALL*{WordAt(addr)}";
            case 0xEE: length = 2; return $"XRI  {ByteAt(addr)}";
            case 0xEF: length = 1; return "RST  5";
            case 0xF0: length = 1; return "RP";
            case 0xF1: length = 1; return "POP  PSW";
            case 0xF2: length = 3; return $"JP   {WordAt(addr)}";
            case 0xF3: length = 1; return "DI";
            case 0xF4: length = 3; return $"CP   {WordAt(addr)}";
            case 0xF5: length = 1; return "PUSH PSW";
            case 0xF6: length = 2; return $"ORI  {ByteAt(addr)}";
            case 0xF7: length = 1; return "RST  6";
            case 0xF8: length = 1; return "RM";
            case 0xF9: length = 1; return "SPHL";
            case 0xFA: length = 3; return $"JM   {WordAt(addr)}";
            case 0xFB: length = 1; return "EI";
            case 0xFC: length = 3; return $"CM   {WordAt(addr)}";
            case 0xFD: length = 3; return $"CALL*{WordAt(addr)}";
            case 0xFE: length = 2; return $"CPI  {ByteAt(addr)}";
            case 0xFF: length = 1; return "RST  7";
            default: length = 1; return $"???  ({op:X2})";
        }
    }

    private string ByteAt(ushort addr) => $"{Memory.Read((ushort)(addr + 1)):X2}h";
    private string WordAt(ushort addr) => $"{Memory.ReadWord((ushort)(addr + 1)):X4}h";

    private static string DReg(int r) => r switch
    {
        0 => "B", 1 => "C", 2 => "D", 3 => "E",
        4 => "H", 5 => "L", 6 => "M", 7 => "A",
        _ => "?"
    };
}
