// https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.ilgenerator?view=net-9.0

using System.Diagnostics;
using System.Numerics;
using System.Reflection.Emit;
using ARM;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using ImGuiNET;

Tests.DecoderTests.LinkBit();

ARM.CPU cpu = new();

var running = false;
GUI.Window.OnTick += () =>
{
    ImGui.SetNextWindowSize(new Vector2(GUI.Window.Width / 2, GUI.Window.Height), ImGuiCond.Always);
    ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);

    ImGui.Begin("LeftPane", ImGuiWindowFlags.NoMove |
    ImGuiWindowFlags.NoCollapse |
    ImGuiWindowFlags.NoResize |
    ImGuiWindowFlags.NoTitleBar |
    ImGuiWindowFlags.NoBringToFrontOnFocus);

    ImGui.Text("Controls");
    ImGui.Separator();

    if (ImGui.Button("Start"))
    {
        cpu.bus.LoadBIOS();

        cpu.bus.LoadROM("/Users/olim/Desktop/GBA/ILGBA/res/mini.gba");

        cpu.R[15] = 0x08000000; // PC starts at beginning of ROM

        cpu.Step();
    }
    ImGui.SameLine();

    if (ImGui.Button("Step"))
        cpu.Step();

    ImGui.SameLine();
    if (ImGui.Button(running ? "Pause" : "Run"))
        running = !running;

    ImGui.SameLine();
    if (ImGui.Button("Reset"))
        cpu = new ARM.CPU();
    // reload memory here

    ImGui.Text("CPU Registers");
    ImGui.Separator();

    ImGui.Text($"PC: 0x{cpu.R[15]:X8}   LR: 0x{cpu.R[14]:X8}   SP: 0x{cpu.R[13]:X8}");
    for (int i = 0; i < 16; i++)
    {
        ImGui.Text($"R{i,2}: 0x{cpu.R[i]:X8}");
        if ((i % 4) != 3) ImGui.SameLine();
    }

    ImGui.Text($"CPSR: 0x{cpu.CPSR:X8}   SPSR: 0x{cpu.SPSR:X8}");

    ImGui.Text($"N:{(cpu.NegativeFlag ? 1 : 0)}  Z:{(cpu.ZeroFlag ? 1 : 0)}  C:{(cpu.CarryFlag ? 1 : 0)}  V:{(cpu.OverflowFlag ? 1 : 0)}   State: {(cpu.ThumbState ? "Thumb" : "ARM")}");

    ImGui.Separator();
    uint pc = cpu.R[15] & ~3u;
    for (int i = 0; i < 40; i++)
    {
        uint addr = pc + (uint)(i * 4);
        uint op = cpu.bus.ReadWord(addr);
        var d = ARM.Decoder.Instance.Decode(op);
        bool isPc = addr == (cpu.R[15] & ~3u);

        if (isPc) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 1f, 0.4f, 1f));
        ImGui.Text($"0x{addr:X8}:");
        var bytes = BitConverter.GetBytes(op);
        ImGui.Text($"    {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2} |");
        ImGui.SameLine();
        using (var disasm = CapstoneDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm))
        {
            disasm.EnableInstructionDetails = true;

            var instructions = disasm.Disassemble(bytes, addr);

            foreach (var ins in instructions)
            {
                ImGui.Text($" {ins.Mnemonic} {ins.Operand}");
                ImGui.SameLine();
            }
        }
        ImGui.Text($"| { d.Name}");
        if (isPc) ImGui.PopStyleColor();
    }

    ImGui.Separator();

    ImGui.End();

    //make this window next to the previous one
    ImGui.SetNextWindowPos(new Vector2(GUI.Window.Width / 2, 0), ImGuiCond.Always);
    ImGui.SetNextWindowSize(new Vector2(GUI.Window.Width, GUI.Window.Height), ImGuiCond.Always);
    ImGui.Begin("RightPane", ImGuiWindowFlags.NoMove |
    ImGuiWindowFlags.NoCollapse |
    ImGuiWindowFlags.NoResize |
    ImGuiWindowFlags.NoTitleBar |
    ImGuiWindowFlags.NoBringToFrontOnFocus);
    ImGui.Text("Execution Log");

    if (ImGui.Button("Clear"))
        CPU.ExecutionLog.Clear();

    ImGui.Separator();

    foreach (var entry in CPU.ExecutionLog.Entries)
    {
        ImGui.TextUnformatted(entry);
        ImGui.Separator();
    }

    // ImGui.Separator();

    // ImGui.Text("Memory Views");
    // ImGui.Separator();
    // ImGui.SameLine();

    // DrawHexView(cpu, "IWRAM 0x03000000", 0x03000000, 512);
    // DrawHexView(cpu, "ROM 0x08000000", 0x08000000, 256);

    ImGui.End();
};

GUI.Window.Create();