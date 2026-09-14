namespace InstantFileSearch;

public interface IByteProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

public static class ByteProtector
{
    /// <summary>
    /// Windows: DPAPI CurrentUser so other local accounts cannot read the cache.
    /// Other OS: pass-through (this product is Windows; Linux is for tests).
    /// </summary>
    public static IByteProtector CreateDefault() =>
        OperatingSystem.IsWindows() ? new DpapiByteProtector() : new PassThroughByteProtector();
}
