using System.Diagnostics;

namespace ARM
{
    public enum ArmKind
    {
        SoftwareInterrupt,
        BranchAndExchange,
        BranchAndLink,
        DataProcessing,
        SingleDataTransfer,
        HalfwordTransfer,
        Multiply,
        MultiplyLong,
        Swap,
        PSRTransfer,
        Undefined,
    }

    public enum ArmCondition : byte
    {
        Equal = 0b0000, // Z==1          (EQ)
        NotEqual = 0b0001, // Z==0          (NE)
        CarrySet_HigherSame = 0b0010, // C==1          (CS/HS)
        CarryClear_Lower = 0b0011, // C==0          (CC/LO)
        Minus_Negative = 0b0100, // N==1          (MI)
        Plus_PositiveOrZero = 0b0101, // N==0          (PL)
        OverflowSet = 0b0110, // V==1          (VS)
        OverflowClear = 0b0111, // V==0          (VC)
        UnsignedHigher = 0b1000, // C==1 && Z==0  (HI)
        UnsignedLowerOrSame = 0b1001, // C==0 || Z==1  (LS)
        SignedGE = 0b1010, // N==V          (GE)
        SignedLT = 0b1011, // N!=V          (LT)
        SignedGT = 0b1100, // Z==0 && N==V  (GT)
        SignedLE = 0b1101, // Z==1 || N!=V  (LE)
        Always = 0b1110, // always        (AL)
        Never = 0b1111, // never (unused on ARM7TDMI) (NV)
    }

    public class Pattern
    {
        public uint Mask { get; init; }
        public uint Value { get; init; }
        public Dictionary<char, uint> FieldMasks { get; init; } = [];
        public ArmKind Kind { get; init; }
        public string Name { get; init; } = "";

        public override string ToString() => $"{Name} (Kind={Kind}, Mask=0x{Mask:X8}, Value=0x{Value:X8})";
    }

    public static class PatternCompiler
    {
        public static Pattern Compile32(string pattern, ArmKind kind, string name)
        {
            uint mask = 0, value = 0;
            var fieldMasks = new Dictionary<char, uint>();
            int bit = 31;

            foreach (char raw in pattern)
            {
                char ch = raw;
                if (ch == ' ' || ch == '_' || ch == '-') continue;
                if (bit < 0) break;

                if (ch == '0' || ch == '1')
                {
                    mask |= 1u << bit;
                    if (ch == '1') value |= 1u << bit;
                }
                else
                {
                    // named field
                    if (!fieldMasks.TryGetValue(ch, out uint fm)) fm = 0;
                    fm |= 1u << bit;
                    fieldMasks[ch] = fm;
                }
                bit--;
            }

            Debug.Assert(bit == -1, "Pattern must specify 32 bits (ignoring spaces/_/-).");

            return new Pattern
            {
                Mask = mask,
                Value = value,
                FieldMasks = fieldMasks,
                Kind = kind,
                Name = name ?? kind.ToString()
            };
        }

        public static uint Extract(uint op, uint fieldMask)
        {
            if (fieldMask == 0) return 0;
            uint result = 0;
            for (int bit = 31; bit >= 0; bit--)
            {
                uint sel = 1u << bit;
                if ((fieldMask & sel) != 0)
                {
                    result = (result << 1) | ((op & sel) != 0 ? 1u : 0u);
                }
            }
            return result;
        }
    }

    public struct ArmDecodedInst
    {
        public ArmKind Kind;
        public string Name;
        public uint Op;

        public ArmCondition Cond;

        public uint Opcode, Rn, Rd, Rm, Rs;
        public uint Imm8, Imm12, Imm24, Rot;

        public bool ToSpsr;
        public byte PsrFieldMask;

        public bool Immediate, SetFlags, PreIndex, Up, Byte, WriteBack, LinkOrLoad;
    }

    public sealed class Decoder
    {
        private readonly List<Pattern> _table;

        private static readonly Lazy<Decoder> _instance =
        new Lazy<Decoder>(() => new Decoder());

        public static Decoder Instance => _instance.Value;

        private Decoder()
        {
            _table = BuildTable();
        }

        public ArmDecodedInst Decode(uint op)
        {
            foreach (var p in _table)
            {
                if ((op & p.Mask) != p.Value) continue;

                var r = new ArmDecodedInst
                {
                    Kind = p.Kind,
                    Name = p.Name,
                    Op = op,

                    Cond = (ArmCondition)Get(p, op, 'c'),
                    Opcode = Get(p, op, 'o'),
                    Rn = Get(p, op, 'n'),
                    Rd = Get(p, op, 'd'),
                    Rm = Get(p, op, 'm'),
                    Rs = Get(p, op, 's'),
                    Imm8 = Get(p, op, 'i'),   // meaning depends on pattern
                    Imm12 = Get(p, op, 'i'),   // meaning depends on pattern
                    Imm24 = Get(p, op, 'i'),   // meaning depends on pattern
                    Rot = Get(p, op, 'r'),

                    ToSpsr = GetBit(p, op, 'R'),
                    PsrFieldMask = (byte)((GetBit(p, op, 'F') ? 8 : 0) |
                                    (GetBit(p, op, 'S') ? 4 : 0) |
                                    (GetBit(p, op, 'X') ? 2 : 0) |
                                    (GetBit(p, op, 'C') ? 1 : 0)),

                    Immediate = GetBit(p, op, 'I'),
                    SetFlags = GetBit(p, op, 'S'),
                    PreIndex = GetBit(p, op, 'P'),
                    Up = GetBit(p, op, 'U'),
                    Byte = GetBit(p, op, 'B'),
                    WriteBack = GetBit(p, op, 'W'),
                    LinkOrLoad = GetBit(p, op, 'L'),
                };

                return r;
            }
            return new ArmDecodedInst { Kind = ArmKind.Undefined, Name = "Undefined", Op = op };
        }

        private static uint Get(Pattern p, uint op, char id)
            => p.FieldMasks.TryGetValue(id, out var fm) ? PatternCompiler.Extract(op, fm) : 0u;

        private static bool GetBit(Pattern p, uint op, char id)
            => p.FieldMasks.TryGetValue(id, out var fm) && (PatternCompiler.Extract(op, fm) & 1u) != 0;


        private static List<Pattern> BuildTable()
        {
            var C = PatternCompiler.Compile32;

            // helper to validate we cover exactly 32 significant chars (no spaces/'_')
            static void ValidatePattern(string pattern, string name)
            {
                int count = 0;
                foreach (var ch in pattern)
                    if (ch != ' ' && ch != '_') count++; // we DO count letters (incl. 'x'), ignore spaces/underscores only
                if (count != 32)
                    throw new InvalidOperationException($"Pattern '{name}' must specify 32 bits; found {count}.");
            }

            List<(string pat, ArmKind kind, string name)> raw = new()
            {
                ("cccc 1111 iiii iiii iiii iiii iiii iiii", ArmKind.SoftwareInterrupt, "SoftwareInterrupt"),
                ("cccc 0001 0010 1111 1111 1111 0001 mmmm", ArmKind.BranchAndExchange, "BranchAndExchange"),
                ("cccc 101L iiii iiii iiii iiii iiii iiii", ArmKind.BranchAndLink,    "Branch/BranchLink"),

                ("cccc 0001 0R00 1111 dddd 0000 0000 0000", ArmKind.PSRTransfer, "MRS"),
                ("cccc 0001 0R10 FSXC 1111 0000 0000 mmmm", ArmKind.PSRTransfer, "MSR"),
                ("cccc 0011 0R10 FSXC 1111 rrrr iiii iiii", ArmKind.PSRTransfer, "MSRImmediate"),

                ("cccc 00I oooo Snnn nddd d rrrr iiii iiii", ArmKind.DataProcessing, "DataProcessing"),

                ("cccc 01I P U B W L nnnn dddd iiii iiii iiii", ArmKind.SingleDataTransfer, "SingleDataTransfer"),
                ("cccc 000 P U I W L nnnn dddd iiii 1001 mmmm", ArmKind.HalfwordTransfer,   "HalfwordTransfer"),
                ("cccc 0000 00A0 dddd nnnn ssss 1001 mmmm",    ArmKind.Multiply,            "Multiply"),
                ("cccc 0000 1UA0 hhhh dddd ssss 1001 mmmm",    ArmKind.MultiplyLong,        "MultiplyLong"),
                ("cccc 0001 0B00 dddd nnnn 0000 1001 mmmm",    ArmKind.Swap,                "Swap/SwapByte"),
            };

            var list = new List<Pattern>(raw.Count);
            foreach (var (pat, kind, name) in raw)
            {
                ValidatePattern(pat, name);
                list.Add(C(pat, kind, name));
            }
            return list;
        }
    }
}