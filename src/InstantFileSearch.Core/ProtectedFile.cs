using System.Security.Cryptography;

namespace InstantFileSearch;

public static class ProtectedFile
{
    public static readonly byte[] Magic = "IFS1"u8.ToArray();

    public static void WriteAll(string filePath, byte[] plaintext, IByteProtector protector)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(protector);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var cipher = protector.Protect(plaintext);
        var blob = new byte[Magic.Length + cipher.Length];
        Magic.CopyTo(blob, 0);
        Buffer.BlockCopy(cipher, 0, blob, Magic.Length, cipher.Length);

        var tmp = filePath + ".tmp";
        File.WriteAllBytes(tmp, blob);
        File.Move(tmp, filePath, overwrite: true);
    }

    public static bool TryReadAll(string filePath, IByteProtector protector, out byte[] plaintext)
    {
        plaintext = [];
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= Magic.Length && bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                var cipher = bytes.AsSpan(Magic.Length).ToArray();
                plaintext = protector.Unprotect(cipher);
                return true;
            }

            // Unprotected JSON leftover from v1 cache files.
            if (bytes.Length > 0 && bytes[0] == (byte)'{')
            {
                plaintext = bytes;
                return true;
            }

            return false;
        }
        catch (Exception ex) when (
            ex is CryptographicException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
