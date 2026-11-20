using System.Security.Cryptography;

namespace TrueInjector.Core;

/// <summary>
/// Cryptographically seeded random payloads used for on-disk cloaking.
/// </summary>
public sealed class PayloadRandomizer
{
    public static PayloadRandomizer Shared { get; } = new();

    private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    public byte[] Bytes(int size)
    {
        byte[] random = new byte[Math.Max(0, size)];
        _rng.GetBytes(random);
        return random;
    }

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int NextInt(int min, int max)
    {
        if (max <= min)
        {
            return min;
        }

        Span<byte> buffer = stackalloc byte[4];
        _rng.GetBytes(buffer);
        uint sample = BitConverter.ToUInt32(buffer);

        // Rejection sampling keeps the distribution unbiased.
        uint range = (uint)(max - min + 1);
        uint threshold = uint.MaxValue - (uint.MaxValue % range);

        do
        {
            _rng.GetBytes(buffer);
            sample = BitConverter.ToUInt32(buffer);
        }
        while (sample >= threshold);

        return (int)(sample % range) + min;
    }

    public string String(char[] chars, int length)
    {
        char[] destination = new char[length];
        Span<byte> buffer = stackalloc byte[4];

        for (int i = 0; i < length; i++)
        {
            _rng.GetBytes(buffer);
            uint sample = BitConverter.ToUInt32(buffer);
            destination[i] = chars[sample % chars.Length];
        }

        return new string(destination);
    }
}
