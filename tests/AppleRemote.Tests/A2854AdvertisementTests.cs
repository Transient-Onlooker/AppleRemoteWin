// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public class A2854AdvertisementTests
{
    [Fact]
    public void Captured_real_pairing_advertisement_uses_alternate_manufacturer_header()
    {
        A2854AdvertisementSignals info = A2854Advertisement.Classify(FakeRemote.PairingAdvertisement);

        Assert.True(info.AppleCompany);
        Assert.True(info.HidService);
        Assert.True(info.CapturedManufacturerPrefix);
        Assert.False(info.ReadmeManufacturerPrefix);
        Assert.True(info.StrongCandidate);
    }

    [Fact]
    public void Reference_fingerprint_with_HID_is_a_strong_candidate()
    {
        // Apple company ID, fixed prefix, synthetic 6-byte identity and documented OPP suffix.
        byte[] ad =
        [
            0x03, 0x03, 0x12, 0x18, // complete 16-bit services: HID
            0x12, 0xFF, 0x4C, 0x00, 0x07, 0x0D, 0x02, 0x15, 0x03, 0x02,
            0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x4F, 0x50, 0x50,
        ];

        A2854AdvertisementSignals info = A2854Advertisement.Classify(ad);

        Assert.True(info.ReadmeManufacturerPrefix);
        Assert.True(info.AppleCompany);
        Assert.True(info.HidService);
        Assert.True(info.StrongCandidate);
    }

    [Fact]
    public void Apple_iphone_advertisement_does_not_match_A2854()
    {
        A2854AdvertisementSignals info = A2854Advertisement.Classify(FakeRemote.PhoneAdvertisement);
        Assert.True(info.AppleCompany);
        Assert.False(info.HidService);
        Assert.False(info.StrongCandidate);
    }

    [Fact]
    public void Apple_and_HID_without_observed_A2854_header_remains_ambiguous()
    {
        byte[] ad = FakeRemote.PairingAdvertisement.ToArray();
        ad[11] = 0xAB; // Replace only first captured manufacturer byte, keep Apple+HID.
        A2854AdvertisementSignals info = A2854Advertisement.Classify(ad);
        Assert.True(info.AppleCompany);
        Assert.True(info.HidService);
        Assert.False(info.StrongCandidate);
    }

    [Fact]
    public void Name_DL1_is_a_nonexclusive_hint_even_without_HID()
    {
        byte[] ad = [0x05, 0x09, (byte)'D', (byte)'L', (byte)'1', (byte)'A'];
        A2854AdvertisementSignals info = A2854Advertisement.Classify(ad);
        Assert.True(info.RemoteNameHint);
        Assert.False(info.StrongCandidate);
    }
}
