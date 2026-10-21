// SPDX-License-Identifier: AGPL-3.0-or-later
// Privacy-preserving markers for A2854 pairing advertisements.
// Only the fixed manufacturer header is compared; rotating BLE/identity addresses are never exposed.

using System.Text;

namespace AppleRemote;

public readonly record struct A2854AdvertisementSignals(
    bool AppleCompany,
    bool HidService,
    bool RemoteNameHint,
    bool ReadmeManufacturerPrefix,
    bool CapturedManufacturerPrefix)
{
    // Evidence of the Apple HID-over-GATT pairing format, not a cryptographic device identity.
    public bool StrongCandidate => AppleCompany && HidService &&
        (ReadmeManufacturerPrefix || CapturedManufacturerPrefix);
}

public static class A2854Advertisement
{
    // Reference pairing fingerprint: 07 0D 02 15 03 02 | 6-byte identity | 4F 50 50.
    static ReadOnlySpan<byte> ReadmePrefix => [0x07, 0x0D, 0x02, 0x15, 0x03, 0x02];

    // Actual A2854 pairing advertisement in tests/AppleRemote.Tests/SiriRemoteBleTests.cs.
    // This captured format differs from the README fingerprint; never require one variant only.
    static ReadOnlySpan<byte> CapturedPrefix => [0x8A, 0x07, 0x6D, 0x02, 0x02];

    public static A2854AdvertisementSignals Classify(byte[] advertisingData)
    {
        bool apple = false, hid = false, name = false, readme = false, captured = false;
        foreach (var (type, value) in LeCentral.AdStructures(advertisingData))
        {
            if (type == 0xFF && value.Length >= 2 &&
                value[0] == 0x4C && value[1] == 0x00)
            {
                apple = true;
                ReadOnlySpan<byte> payload = value.AsSpan(2);
                readme |= payload.StartsWith(ReadmePrefix);
                captured |= payload.StartsWith(CapturedPrefix);
            }
            else if (type is 0x02 or 0x03)
            {
                for (int i = 0; i + 1 < value.Length; i += 2)
                    hid |= value[i] == 0x12 && value[i + 1] == 0x18;
            }
            else if (type is 0x08 or 0x09)
            {
                // A paired A2854 was seen as DL1... on Windows; a local name may be absent.
                string localName = Encoding.UTF8.GetString(value);
                name |= localName.StartsWith("DL1", StringComparison.OrdinalIgnoreCase) ||
                    localName.Contains("Siri Remote", StringComparison.OrdinalIgnoreCase);
            }
        }
        return new A2854AdvertisementSignals(apple, hid, name, readme, captured);
    }
}
