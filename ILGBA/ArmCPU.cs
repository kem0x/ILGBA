using System.Diagnostics;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;

namespace ARM
{
    public class CPU
    {
        public Bus bus = new();

        public enum StepSize
        {
            Word = 4,
            Halfword = 2,
            Byte = 1
        }

        public uint[] R = new uint[16]; // R0-R15

        public uint CPSR = 0;

        public uint SPSR = 0;

        public enum CPSRBits
        {
            Negative = 1 << 31,
            Zero = 1 << 30,
            Carry = 1 << 29,
            Overflow = 1 << 28,
            IRQDisable = 1 << 7,
            FIQDisable = 1 << 6,
            ThumbState = 1 << 5,
            ModeMask = 0x1F,
        }

        public uint GetCPSRBit(uint Reg, CPSRBits bit) => Reg & (uint)bit;
        public void SetCPSRBit(ref uint Reg, CPSRBits bit, uint value) => Reg = (Reg & ~(uint)bit) | (value != 0 ? (uint)bit : 0);

        public bool NegativeFlag { get => GetCPSRBit(CPSR, CPSRBits.Negative) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.Negative, value ? 1u : 0); }
        public bool ZeroFlag { get => GetCPSRBit(CPSR, CPSRBits.Zero) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.Zero, value ? 1u : 0); }
        public bool CarryFlag { get => GetCPSRBit(CPSR, CPSRBits.Carry) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.Carry, value ? 1u : 0); }
        public bool OverflowFlag { get => GetCPSRBit(CPSR, CPSRBits.Overflow) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.Overflow, value ? 1u : 0); }

        public bool IRQDisabled { get => GetCPSRBit(CPSR, CPSRBits.IRQDisable) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.IRQDisable, value ? 1u : 0); }
        public bool FIQDisabled { get => GetCPSRBit(CPSR, CPSRBits.FIQDisable) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.FIQDisable, value ? 1u : 0); }

        public bool ThumbState { get => GetCPSRBit(CPSR, CPSRBits.ThumbState) != 0; set => SetCPSRBit(ref CPSR, CPSRBits.ThumbState, value ? 1u : 0); }

        public uint Mode { get => GetCPSRBit(CPSR, CPSRBits.ModeMask); set => SetCPSRBit(ref CPSR, CPSRBits.ModeMask, value); }

        public class ExecutionLog
        { 
            static public List<string> Entries = [];
            static private string currLogEntry = "";
            static public void Add(string entry) => currLogEntry += entry;
            static public void AddAndFlush(string entry)
            {
                currLogEntry += entry;
                FlushCurrentEntry();
            }
            static public void FlushCurrentEntry()
            {
                if (!string.IsNullOrEmpty(currLogEntry))
                {
                    Entries.Add(currLogEntry);
                    currLogEntry = "";
                }
            }
            static public void Clear() => Entries.Clear();
        }


        public bool EvalCondition(ArmCondition cond)
        {
            return cond switch
            {
                ArmCondition.Equal => ZeroFlag,
                ArmCondition.NotEqual => !ZeroFlag,
                ArmCondition.CarrySet_HigherSame => CarryFlag,
                ArmCondition.CarryClear_Lower => !CarryFlag,
                ArmCondition.Minus_Negative => NegativeFlag,
                ArmCondition.Plus_PositiveOrZero => !NegativeFlag,
                ArmCondition.OverflowSet => OverflowFlag,
                ArmCondition.OverflowClear => !OverflowFlag,
                ArmCondition.UnsignedHigher => CarryFlag && !ZeroFlag,
                ArmCondition.UnsignedLowerOrSame => !CarryFlag || ZeroFlag,
                ArmCondition.SignedGE => NegativeFlag == OverflowFlag,
                ArmCondition.SignedLT => NegativeFlag != OverflowFlag,
                ArmCondition.SignedGT => !ZeroFlag && (NegativeFlag == OverflowFlag),
                ArmCondition.SignedLE => ZeroFlag || (NegativeFlag != OverflowFlag),
                ArmCondition.Always => true,
                ArmCondition.Never => false,
                _ => throw new NotImplementedException($"Unknown condition {cond}"),
            };
        }

        private static int SignExtBranchOffset(uint imm24)
        {
            // imm24 -> left shift 2, sign-extend from 26 bits
            return ((int)(imm24 << 8)) >> 6;
        }

        static uint RotR(uint x, int r) { r &= 31; return r == 0 ? x : (x >> r) | (x << (32 - r)); }

        // chatgpt
        static (uint val, uint carry) BarrelShift(uint val, uint type, uint amount, uint carryIn)
        {
            switch (type & 3)
            {
                case 0: // LSL
                    if (amount == 0) return (val, carryIn);
                    if (amount < 32) return (val << (int)amount, (val >> (int)(32 - amount)) & 1u);
                    if (amount == 32) return (0u, val & 1u);
                    return (0u, 0u);
                case 1: // LSR
                    if (amount == 0 || amount == 32) return (0u, (val >> 31) & 1u);
                    if (amount < 32) return (val >> (int)amount, (val >> (int)(amount - 1)) & 1u);
                    return (0u, 0u);
                case 2: // ASR
                    if (amount == 0 || amount >= 32) { uint s = val >> 31; return (s != 0 ? 0xFFFFFFFFu : 0u, s); }
                    return ((uint)((int)val >> (int)amount), (val >> (int)(amount - 1)) & 1u);
                default: // ROR / RRX
                    if (amount == 0) { uint outv = ((carryIn & 1u) << 31) | (val >> 1); return (outv, val & 1u); }
                    amount &= 31;
                    if (amount == 0) return (val, (val >> 31) & 1u);
                    return ((val >> (int)amount) | (val << (int)(32 - amount)), (val >> (int)(amount - 1)) & 1u);
            }
        }

        private uint ComputeBranchTargetARM(uint curPc, uint imm24)
        {
            int offset = SignExtBranchOffset(imm24);
            uint visPC = curPc + 8;
            uint target = visPC + (uint)offset;
            return target & ~1u;
        }

        private void ExecBranch(ArmDecodedInst r, uint op, uint curPc)
        {
            bool link = ((op >> 24) & 1) != 0;
            if (link) R[14] = curPc + 4;

            uint target = ComputeBranchTargetARM(curPc, r.Imm24);
            R[15] = target;

            ExecutionLog.Add($"{r.Name} -> 0x{target:X8}");
            if (link) ExecutionLog.Add($" LR=0x{R[14]:X8}");
            ExecutionLog.FlushCurrentEntry();
        }

        private void ExecDataProc(ArmDecodedInst r, uint op, uint curPc)
        {
            uint op2, shCarry;

            if (r.Immediate)
            {
                uint rot2 = (r.Rot & 0xF) * 2;
                op2 = RotR(r.Imm8, (int)rot2);
                shCarry = (rot2 == 0) ? (CarryFlag ? 1u : 0u) : (op2 >> 31);
            }
            else
            {
                bool regShift = ((op >> 4) & 1) != 0;
                uint type = (op >> 5) & 3u;
                uint amount = regShift ? (R[(op >> 8) & 0xF] & 0xFF) : ((op >> 7) & 0x1Fu);
                uint rmVal = (r.Rm == 15) ? (curPc + 8) : R[r.Rm];
                (op2, shCarry) = BarrelShift(rmVal, type, amount, CarryFlag ? 1u : 0u);
            }

            switch (r.Opcode)
            {
                case 0xD: // MOV
                    {
                        R[r.Rd] = op2;
                        ExecutionLog.Add($"{r.Name} R{r.Rd} = 0x{op2:X8}");

                        if (r.SetFlags)
                        {
                            NegativeFlag = (op2 >> 31) != 0;
                            ZeroFlag = op2 == 0;
                            CarryFlag = shCarry != 0;
                            // V unaffected
                            ExecutionLog.Add($"{r.Name} R{r.Rd} = 0x{op2:X8} (N={(NegativeFlag ? 1 : 0)} Z={(ZeroFlag ? 1 : 0)} C={(CarryFlag ? 1 : 0)})");
                        }

                        if (r.Rd == 15)
                        {
                            // change of PC, flush pipeline
                            ThumbState = (R[15] & 1) != 0;
                            R[15] &= ~1u;
                            ExecutionLog.Add($" -> State={(ThumbState ? "Thumb" : "ARM")}");
                        }
                        ExecutionLog.FlushCurrentEntry();
                        return;
                    }
            }
        }

        private void ExecPsrTransfer(ArmDecodedInst r, uint op, uint curPc)
        {
            ExecutionLog.Add($"PSRTransfer: {r.Name} ");

            switch (r.Name)
            {
                case "MSR":
                    {
                        if (r.ToSpsr)
                        {
                            throw new NotImplementedException("MSR to SPSR not implemented yet");
                        }

                        var UpdateFlags = (r.PsrFieldMask & 8) != 0;
                        var UpdateControl = (r.PsrFieldMask & 1) != 0;

                        ExecutionLog.Add($"UpdateFlags={UpdateFlags} UpdateControl={UpdateControl} ");

                        uint value = R[r.Rm];
                        if (UpdateFlags)
                        {
                            NegativeFlag = GetCPSRBit(value, CPSRBits.Negative) != 0;
                            ZeroFlag = GetCPSRBit(value, CPSRBits.Zero) != 0;
                            CarryFlag = GetCPSRBit(value, CPSRBits.Carry) != 0;
                            OverflowFlag = GetCPSRBit(value, CPSRBits.Overflow) != 0;

                            ExecutionLog.Add($"\n(flags) N={(NegativeFlag ? 1 : 0)} Z={(ZeroFlag ? 1 : 0)} C={(CarryFlag ? 1 : 0)} V={(OverflowFlag ? 1 : 0)}");
                        }

                        if (UpdateControl)
                        {
                            IRQDisabled = GetCPSRBit(value, CPSRBits.IRQDisable) != 0;
                            FIQDisabled = GetCPSRBit(value, CPSRBits.FIQDisable) != 0;
                            ThumbState = GetCPSRBit(value, CPSRBits.ThumbState) != 0;
                            Mode = GetCPSRBit(value, CPSRBits.ModeMask);

                            ExecutionLog.Add($"\n(control) IRQD={(IRQDisabled ? 1 : 0)} FIQD={(FIQDisabled ? 1 : 0)} Mode=0x{Mode:X2} State={(ThumbState ? "Thumb" : "ARM")}");
                        }
                        ExecutionLog.FlushCurrentEntry();
                        return;
                    }
                case "MSRImmediate":
                    {
                        throw new NotImplementedException("MSRImmediate not implemented yet");
                    }
                case "MRS":
                    {
                        throw new NotImplementedException("MRS not implemented yet");
                    }

            }
        }

        public void Step()
        {
            if (ThumbState)
            {
                Debug.Print("Thumb not implemented yet");
                return;
            }

            uint curPc = R[15];
            uint op = bus.ReadWord(curPc & ~3u);
            var r = Decoder.Instance.Decode(op);

            // Condition check
            if (!EvalCondition(r.Cond))
            {
                R[15] = curPc + 4; // failed cond = NOP advance

                ExecutionLog.AddAndFlush($"{op:X8}  <{r.Name}>   ; (cond {r.Cond} failed, skipping)");
                return;
            }

            R[15] = curPc + 4;

            switch (r.Kind)
            {
                case ArmKind.BranchAndLink:
                    {
                        ExecBranch(r, op, curPc);
                        return;
                    }

                case ArmKind.DataProcessing:
                    {
                        ExecDataProc(r, op, curPc);
                        return;
                    }

                case ArmKind.PSRTransfer:
                    {
                        ExecPsrTransfer(r, op, curPc);
                        return;
                    }

                default:
                    throw new NotImplementedException($"Instruction kind {r.Kind} not implemented");
            }

        }
    }
}