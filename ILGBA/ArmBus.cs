namespace ARM;
public class Bus
{
    private Memory<byte> MemoryBuffer = new byte[0x10000000];

    private Memory<byte> SliceInclusive(Memory<byte> memory, int start, int end)
    {
        return memory.Slice(start, end - start + 1);
    }

    public Memory<byte> BIOSBuffer => SliceInclusive(MemoryBuffer, 0x00000000, 0x00003FFF);
    public Memory<byte> WRAMBuffer => SliceInclusive(MemoryBuffer, 0x02000000, 0x0203FFFF);
    public Memory<byte> IRAMBuffer => SliceInclusive(MemoryBuffer, 0x03000000, 0x03007FFF);
    public Memory<byte> IORegistersBuffer => SliceInclusive(MemoryBuffer, 0x04000000, 0x040003FF);
    public Memory<byte> PaletteRAMBuffer => SliceInclusive(MemoryBuffer, 0x05000000, 0x050003FF);
    public Memory<byte> VRAMBuffer => SliceInclusive(MemoryBuffer, 0x06000000, 0x06017FFF);
    public Memory<byte> OAMBuffer => SliceInclusive(MemoryBuffer, 0x07000000, 0x070003FF);
    public Memory<byte> GamePakROMBuffer => SliceInclusive(MemoryBuffer, 0x08000000, 0x0EFFFFFF);

    // Rom Ranges
    public Memory<byte> EntryPointInstruction => SliceInclusive(GamePakROMBuffer, 0x00000000, 0x00000003);
    public Memory<byte> NintendoLogo => SliceInclusive(GamePakROMBuffer, 0x00000004, 0x0000009F);
    public Memory<byte> Title => SliceInclusive(GamePakROMBuffer, 0x000000A0, 0x000000AB);
    public Memory<byte> GameCode => SliceInclusive(GamePakROMBuffer, 0x000000AC, 0x000000AF);

    public string GetTitle() => System.Text.Encoding.ASCII.GetString(Title.Span);

    public void LoadBIOS()
    {
        byte[] biosData = File.ReadAllBytes("/Users/olim/Desktop/GBA/ILGBA/res/bios.bin");
        biosData.CopyTo(BIOSBuffer);
    }

    public void LoadROM(string path)
    {
        byte[] romData = File.ReadAllBytes(path);
        romData.CopyTo(GamePakROMBuffer);
    }

    public uint ReadWord(uint address)
    {
        if (address % 4 != 0) throw new Exception($"Unaligned word read at address 0x{address:X8}");
        return BitConverter.ToUInt32(MemoryBuffer.Span.Slice((int)address & ~3, 4));
    }

    public uint ReadHalfword(uint address)
    {
        if (address % 2 != 0) throw new Exception($"Unaligned halfword read at address 0x{address:X8}");
        return BitConverter.ToUInt16(MemoryBuffer.Span.Slice((int)address & ~1, 2));
    }

    public byte ReadByte(uint address)
    {
        return MemoryBuffer.Span[(int)address];
    }
}