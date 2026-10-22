// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Security.Cryptography;
using Xunit;

namespace AppleRemote.Tests;

public sealed class ProtectedBondStoreTests
{
    [Fact]
    public void Current_user_DPAPI_roundtrip_protects_bond_without_plaintext()
    {
        // The store is Windows-user scoped by design; the CI also runs on Linux.
        if (!OperatingSystem.IsWindows()) return;

        string directory = Path.Combine(Path.GetTempPath(), "A2854-dpapi-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "bond.v1.dpapi");
        var store = new ProtectedBondStore(file);
        var keys = new BondKeys(
            [0x9F, 0xB7, 0x92, 0xE2, 0x60, 0xFE, 0x9C, 0x5A,
             0x11, 0xC5, 0x72, 0x8D, 0xAC, 0x21, 0xF3, 0x61],
            0xA517,
            [0x7B, 0x62, 0x2D, 0x63, 0x17, 0x89, 0x11, 0x3A]);
        try
        {
            Assert.False(store.Exists);
            store.Save("12:34:56:78:9A:BC", 0, keys);
            Assert.True(store.Exists);
            byte[] ciphertext = File.ReadAllBytes(file);
            // Ciphertext cannot contain any raw LTK or the whole plaintext input.
            Assert.True(ciphertext.Length > keys.Ltk.Length);
            Assert.False(ciphertext.AsSpan().IndexOf(keys.Ltk) >= 0);
            // A second pairing attempt may not silently clobber a known bond.
            Assert.Throws<IOException>(() => store.Save("12:34:56:78:9A:BC", 0, keys));
            Assert.Equal(ciphertext, File.ReadAllBytes(file));
            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal("12:34:56:78:9A:BC", loaded.Value.Address);
            Assert.Equal((byte)0, loaded.Value.AddressType);
            Assert.Equal(keys.Ltk, loaded.Value.Keys.Ltk);
            Assert.Equal(keys.Ediv, loaded.Value.Keys.Ediv);
            Assert.Equal(keys.Rand, loaded.Value.Keys.Rand);
            CryptographicOperations.ZeroMemory(loaded.Value.Keys.Ltk);
            CryptographicOperations.ZeroMemory(loaded.Value.Keys.Rand);
            store.Delete();
            Assert.False(store.Exists);
            Assert.Null(store.Load());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keys.Ltk);
            CryptographicOperations.ZeroMemory(keys.Rand);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DPAPI_rejects_corrupt_or_tampered_ciphertext()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "A2854-dpapi-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "bond.v1.dpapi");
        var store = new ProtectedBondStore(file);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(file, [0x01, 0x02, 0x03]);
            Assert.Throws<InvalidDataException>(() => store.Load());
            byte[] randomCiphertext = RandomNumberGenerator.GetBytes(100);
            try
            {
                File.WriteAllBytes(file, randomCiphertext);
                Assert.ThrowsAny<Exception>(() => store.Load());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(randomCiphertext);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_rejects_oversized_mock_file_before_DPAPI()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "A2854-dpapi-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "bond.v1.dpapi");
        var store = new ProtectedBondStore(file);
        try
        {
            Directory.CreateDirectory(directory);
            // Synthetic ciphertext-like bytes only; no device bond is read or used.
            File.WriteAllBytes(file, new byte[4097]);
            Assert.Throws<InvalidDataException>(() => store.Load());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tampered_mock_ciphertext_does_not_replace_or_delete_the_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "A2854-dpapi-test-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "bond.v1.dpapi");
        var store = new ProtectedBondStore(file);
        var keys = new BondKeys(new byte[16], 0x1234, new byte[8]);
        try
        {
            store.Save("12:34:56:78:9A:BC", 1, keys);
            byte[] ciphertext = File.ReadAllBytes(file);
            try
            {
                ciphertext[ciphertext.Length / 2] ^= 0x80;
                File.WriteAllBytes(file, ciphertext);
                byte[] tampered = File.ReadAllBytes(file);
                try
                {
                    Assert.ThrowsAny<Exception>(() => store.Load());
                    Assert.True(store.Exists);
                    Assert.Equal(tampered, File.ReadAllBytes(file));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(tampered);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keys.Ltk);
            CryptographicOperations.ZeroMemory(keys.Rand);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DPAPI_rejects_invalid_bond_fields_before_writing()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dpapi");
        var store = new ProtectedBondStore(path);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save("12:34:56:78:9A:BC", 2,
            new BondKeys(new byte[16], 0, new byte[8])));
        Assert.Throws<ArgumentException>(() => store.Save("12:34:56:78:9A:BC", 0,
            new BondKeys(new byte[12], 0, new byte[8])));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DPAPI_is_explicitly_Windows_only()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new ProtectedBondStore(Path.Combine(Path.GetTempPath(), "unused-a2854.dpapi"));
        Assert.Throws<PlatformNotSupportedException>(() => store.Load());
    }
}
