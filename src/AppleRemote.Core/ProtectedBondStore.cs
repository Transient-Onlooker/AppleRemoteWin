// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Windows-user-scoped DPAPI store for one experimental A2854 bond. No JSON,
// no raw secret in filenames, no profile-wide certificate or machine key.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AppleRemote;

/// <summary>
/// Local CurrentUser DPAPI-protected bond store for an independently controlled
/// A2854 Bluetooth controller. The DPAPI ciphertext is not transferable to
/// another Windows user or computer by merely copying the file.
/// </summary>
public sealed class ProtectedBondStore
{
    private static readonly byte[] Magic = [(byte)'A', (byte)'2', (byte)'8', (byte)'5', (byte)'4', (byte)'B', 1];
    private const uint UiForbidden = 0x01;
    private readonly string _path;

    public ProtectedBondStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A local path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AppleRemoteWin", "A2854", "bond.v1.dpapi");

    public bool Exists => File.Exists(_path);

    /// <summary>Writes only CurrentUser DPAPI ciphertext. Never writes a plaintext bond.</summary>
    public void Save(string address, byte addressType, BondKeys keys)
    {
        EnsureWindows();
        if (addressType is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(addressType));
        byte[] addr = BdAddr.Parse(address);
        if (keys.Ltk.Length != 16 || keys.Rand.Length != 8)
            throw new ArgumentException("Legacy bond must contain a 16-byte LTK and 8-byte RAND.", nameof(keys));

        byte[] clear = new byte[Magic.Length + 1 + 6 + 16 + 2 + 8];
        byte[]? sealedBlob = null;
        try
        {
            int i = 0;
            Magic.CopyTo(clear, i); i += Magic.Length;
            clear[i++] = addressType;
            addr.CopyTo(clear, i); i += addr.Length;
            keys.Ltk.CopyTo(clear, i); i += keys.Ltk.Length;
            clear[i++] = (byte)keys.Ediv;
            clear[i++] = (byte)(keys.Ediv >> 8);
            keys.Rand.CopyTo(clear, i);

            sealedBlob = Dpapi(clear, protect: true);
            string dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            // Create a temporary ciphertext-only file in the same directory;
            // rename within the same volume to avoid torn protected bond files.
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    output.Write(sealedBlob);
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (sealedBlob is not null) CryptographicOperations.ZeroMemory(sealedBlob);
        }
    }

    /// <summary>
    /// Loads one user-protected bond into volatile memory. The caller must clear
    /// returned Ltk/Rand arrays after it is no longer using the encrypted link.
    /// </summary>
    public (string Address, byte AddressType, BondKeys Keys)? Load()
    {
        EnsureWindows();
        if (!File.Exists(_path)) return null;
        byte[] blob = File.ReadAllBytes(_path);
        byte[]? clear = null;
        try
        {
            if (blob.Length < 16 || blob.Length > 4096)
                throw new InvalidDataException("Invalid protected bond file length.");
            clear = Dpapi(blob, protect: false);
            if (clear.Length != Magic.Length + 1 + 6 + 16 + 2 + 8 ||
                !clear.AsSpan(0, Magic.Length).SequenceEqual(Magic))
                throw new InvalidDataException("Unknown protected bond version or invalid payload.");
            int i = Magic.Length;
            byte addressType = clear[i++];
            if (addressType is not (0 or 1))
                throw new InvalidDataException("Invalid stored Bluetooth address type.");
            string address = BdAddr.Format(clear.AsSpan(i, 6)); i += 6;
            byte[] ltk = clear.AsSpan(i, 16).ToArray(); i += 16;
            ushort ediv = (ushort)(clear[i++] | (clear[i++] << 8));
            byte[] rand = clear.AsSpan(i, 8).ToArray();
            return (address, addressType, new BondKeys(ltk, ediv, rand));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    public void Delete()
    {
        EnsureWindows();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A2854 user-protected bond storage requires Windows DPAPI.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", EntryPoint = "CryptProtectData", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", EntryPoint = "CryptUnprotectData", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Dpapi(byte[] inputBytes, bool protect)
    {
        IntPtr ptr = IntPtr.Zero;
        DataBlob output = default;
        try
        {
            ptr = Marshal.AllocHGlobal(inputBytes.Length);
            Marshal.Copy(inputBytes, 0, ptr, inputBytes.Length);
            var input = new DataBlob { Size = inputBytes.Length, Data = ptr };
            bool ok = protect
                ? CryptProtectData(ref input, "AppleRemoteWin A2854 local bond",
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(),
                protect ? "Failed to protect bond with current Windows user key." :
                    "Failed to decrypt bond with current Windows user key.");
            if (output.Size < 1 || output.Size > 4096 || output.Data == IntPtr.Zero)
                throw new InvalidDataException("Unexpected DPAPI output length.");
            byte[] bytes = new byte[output.Size];
            Marshal.Copy(output.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                // Native plaintext inputs are not managed by the GC.
                Marshal.Copy(new byte[inputBytes.Length], 0, ptr, inputBytes.Length);
                Marshal.FreeHGlobal(ptr);
            }
            if (output.Data != IntPtr.Zero)
            {
                // UnprotectData allocates plaintext unmanaged memory.
                Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                _ = LocalFree(output.Data);
            }
        }
    }
}
