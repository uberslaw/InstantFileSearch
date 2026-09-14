namespace InstantFileSearch;

/// <summary>
/// Identity protector for Linux unit tests. Production Windows builds use DPAPI.
/// </summary>
public sealed class PassThroughByteProtector : IByteProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return plaintext;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return ciphertext;
    }
}
