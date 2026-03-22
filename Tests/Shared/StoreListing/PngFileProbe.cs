namespace DevProjex.Tests.Shared.StoreListing;

internal static class PngFileProbe
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static bool TryReadInfo(string path, out PngFileInfo info)
    {
        info = default;

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var signature = reader.ReadBytes(PngSignature.Length);
        if (signature.Length != PngSignature.Length || !signature.SequenceEqual(PngSignature))
        {
            return false;
        }

        // PNG starts with an 8-byte signature and an IHDR chunk.
        // We read the minimum possible data directly from the file so the validator
        // stays cross-platform and does not depend on System.Drawing or native codecs.
        var chunkLength = ReadBigEndianInt32(reader);
        var chunkType = new string(reader.ReadChars(4));

        if (chunkLength < 13 || !string.Equals(chunkType, "IHDR", StringComparison.Ordinal))
        {
            return false;
        }

        var width = ReadBigEndianInt32(reader);
        var height = ReadBigEndianInt32(reader);

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        info = new PngFileInfo(width, height);
        return true;
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        Span<byte> buffer = stackalloc byte[4];
        var read = reader.Read(buffer);
        if (read != 4)
        {
            throw new EndOfStreamException("Unexpected end of PNG stream.");
        }

        if (BitConverter.IsLittleEndian)
        {
            buffer.Reverse();
        }

        return BitConverter.ToInt32(buffer);
    }
}

internal readonly record struct PngFileInfo(int Width, int Height);
