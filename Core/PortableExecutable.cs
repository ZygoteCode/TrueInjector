using System.IO;

namespace TrueInjector.Core;

/// <summary>
/// Minimal read-only PE header inspection (machine type, DLL flag, entry point presence).
/// </summary>
public sealed class PortableExecutable
{
    public const ushort MachineI386 = 0x014C;
    public const ushort MachineAmd64 = 0x8664;
    public const ushort MachineArm64 = 0xAA64;

    private PortableExecutable(ushort machine, uint addressOfEntryPoint, bool isDll)
    {
        Machine = machine;
        AddressOfEntryPoint = addressOfEntryPoint;
        IsDll = isDll;
    }

    public ushort Machine { get; }

    public uint AddressOfEntryPoint { get; }

    public bool IsDll { get; }

    public bool HasEntryPoint => AddressOfEntryPoint != 0;

    public string MachineLabel => Machine switch
    {
        MachineAmd64 => "x64",
        MachineI386 => "x86",
        MachineArm64 => "ARM64",
        _ => $"0x{Machine:X4}",
    };

    public static bool TryRead(string path, out PortableExecutable? image)
    {
        image = null;

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            Span<byte> buffer = stackalloc byte[4096];
            int read = 0;

            while (read < buffer.Length)
            {
                int chunk = stream.Read(buffer[read..]);

                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            if (read < 0x40)
            {
                return false;
            }

            int peOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer[0x3C..]);

            if (peOffset <= 0 || peOffset + 24 > read)
            {
                return false;
            }

            if (buffer[peOffset] != 0x50 || buffer[peOffset + 1] != 0x45)
            {
                return false;
            }

            ushort machine = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer[(peOffset + 4)..]);
            ushort characteristics = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer[(peOffset + 22)..]);
            ushort optionalMagic = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer[(peOffset + 24)..]);

            int optionalStart = peOffset + 24;

            if (optionalMagic != 0x10B && optionalMagic != 0x20B)
            {
                return false;
            }

            if (optionalStart + 18 > read)
            {
                return false;
            }

            uint entryPoint = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer[(optionalStart + 16)..]);
            image = new PortableExecutable(machine, entryPoint, (characteristics & 0x2000) != 0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
