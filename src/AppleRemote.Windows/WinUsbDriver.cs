// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AppleRemote.Windows;

public enum AdapterDriver
{
    /// <summary>Original driver (Windows Bluetooth stack).</summary>
    Original,
    /// <summary>WinUSB (installed by this application or by Zadig).</summary>
    WinUsb,
}

/// <summary>
/// Zadig equivalent for the Bluetooth adapter: installs WinUSB (INF generated for the VID/PID, self-signed catalog,
/// trusted certificate), or removes that driver so Windows takes back the original one. <see cref="Install"/> et
/// <see cref="Restore"/> require administrator rights; <see cref="GetState"/> doesn't.
/// </summary>
public static class WinUsbDriver
{
    const string Publisher = "AppleRemoteWin", CertSubject = "CN=AppleRemoteWin WinUSB";
    const int CertificateLifetimeDays = 30;

    static string HardwareId(ushort vid, ushort pid) => $@"USB\VID_{vid:X4}&PID_{pid:X4}";

    /// <summary>A present USB Bluetooth adapter, identified by its PnP instance ID.</summary>
    public sealed record Adapter(string InstanceId, string Name, UsbHci.UsbId Id, AdapterDriver Driver)
    {
        public override string ToString() => $"{Name} ({Id})";
    }

    /// <summary>
    /// Present USB Bluetooth adapters (USB class E0/01/01), under their original driver or already under WinUSB.
    /// </summary>
    public static IReadOnlyList<Adapter> List()
    {
        string output = Ps("""
            [Console]::OutputEncoding = [Text.Encoding]::UTF8
            Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
                Where-Object { $_.InstanceId -like 'USB\VID_*' } |
                ForEach-Object {
                    $id = $_.InstanceId
                    $compatible = @((
                        Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_CompatibleIds' -ErrorAction SilentlyContinue
                    ).Data)
                    if ($compatible -contains 'USB\Class_E0&SubClass_01&Prot_01') {
                        $service = (
                            Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_Service' -ErrorAction SilentlyContinue
                        ).Data
                        "$id|$service|$($_.FriendlyName)"
                    }
                }
            exit 0
            """, Encoding.UTF8);
        var adapters = new List<Adapter>();
        foreach (string line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = line.Split('|', 3);
            var m = System.Text.RegularExpressions.Regex.Match(f[0], @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (f.Length < 3 || !m.Success) continue;
            adapters.Add(new Adapter(f[0], f[2],
                new UsbHci.UsbId(Convert.ToUInt16(m.Groups[1].Value, 16), Convert.ToUInt16(m.Groups[2].Value, 16)),
                f[1].Equals("WinUSB", StringComparison.OrdinalIgnoreCase) ? AdapterDriver.WinUsb : AdapterDriver.Original));
        }
        return adapters;
    }

    static Adapter Get(string instanceId) =>
        List().FirstOrDefault(a => a.InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Bluetooth adapter {instanceId} not found.");

    /// <summary>Installs WinUSB on the adapter (administrator). Returns true if Windows requests a restart.</summary>
    public static bool Install(string instanceId)
    {
        var id = Get(instanceId).Id;
        var sameId = List().Where(a => a.Id == id).ToArray();
        if (sameId.Length != 1 || !sameId[0].InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected one present {id} adapter matching the selected instance; found {sameId.Length}.");
        // ponytail: installs by VID/PID, so two identical adapters both switch to WinUSB; DiInstallDevice on the devnode if that matters.
        string hwid = HardwareId(id.Vid, id.Pid);
        string statePath = $@"HKLM:\SOFTWARE\AppleRemoteWin\WinUsbDriver\{StateName(id)}";
        string root = Path.Combine(Path.GetTempPath(), "AppleRemoteWinUsb-" + Guid.NewGuid().ToString("N"));
        string pkg = Path.Combine(root, "pkg");
        string phase = "creating the signing certificate and catalog";
        string thumbprint = "";
        bool updateAttempted = false;
        Directory.CreateDirectory(pkg);
        try
        {
            string inf = Path.Combine(pkg, "AppleRemoteWinUsb.inf");
            File.WriteAllText(inf, BuildInf(hwid), new UTF8Encoding(false));

            // Catalog signed with a self-signed certificate trusted by the machine (what libwdi/Zadig does).
            string signingOutput = Ps($$"""
                $c = Get-ChildItem Cert:\LocalMachine\My | Where-Object Subject -eq '{{CertSubject}}' | Select-Object -First 1
                if ($c) { throw 'An AppleRemoteWin USB signing certificate already exists. Review its thumbprint before installing.' }
                $c = New-SelfSignedCertificate -Type CodeSigningCert -Subject '{{CertSubject}}' -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddDays({{CertificateLifetimeDays}})
                Write-Output "APPLE_REMOTE_CERT_THUMBPRINT=$($c.Thumbprint)"
                $state = '{{statePath}}'
                New-Item -Path $state -Force | Out-Null
                Set-ItemProperty -LiteralPath $state -Name CertificateThumbprint -Value $c.Thumbprint
                $cer = Join-Path '{{root}}' 'cert.cer'
                Export-Certificate -Cert $c -FilePath $cer | Out-Null
                foreach ($s in 'Root', 'TrustedPublisher') { Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\$s" | Out-Null }
                $cat = Join-Path '{{root}}' 'AppleRemoteWinUsb.cat'
                New-FileCatalog -Path '{{pkg}}' -CatalogFilePath $cat -CatalogVersion 2.0 | Out-Null
                $signed = Set-AuthenticodeSignature -FilePath $cat -Certificate $c
                if ($signed.Status -ne 'Valid') { throw "Catalog signing failed: $($signed.Status)" }
                Copy-Item $cat '{{pkg}}'
                """);
            var thumbprintMatch = System.Text.RegularExpressions.Regex.Match(signingOutput, @"APPLE_REMOTE_CERT_THUMBPRINT=([0-9A-F]{40})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!thumbprintMatch.Success) throw new InvalidOperationException("Signing phase did not return a certificate thumbprint.");
            thumbprint = thumbprintMatch.Groups[1].Value.ToUpperInvariant();

            // INSTALLFLAG_FORCE: takes the current driver's place even if it ranks better (like Zadig).
            phase = "binding WinUSB to the selected VID/PID";
            updateAttempted = true;
            if (!Native.UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, hwid, inf, 1, out bool reboot))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install the WinUSB driver");
            return reboot;
        }
        catch (Exception e)
        {
            string activeService = "unknown";
            if (updateAttempted)
            {
                try { activeService = Ps($"(Get-PnpDeviceProperty -InstanceId '{instanceId.Replace("'", "''")}' -KeyName 'DEVPKEY_Device_Service').Data").Trim(); }
                catch { }
            }
            if (thumbprint.Length != 40)
            {
                var match = System.Text.RegularExpressions.Regex.Match(e.Message,
                    @"APPLE_REMOTE_CERT_THUMBPRINT=([0-9A-F]{40})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) thumbprint = match.Groups[1].Value.ToUpperInvariant();
            }
            bool retainedForRollback = updateAttempted &&
                (activeService.Equals("WinUSB", StringComparison.OrdinalIgnoreCase) || activeService == "unknown");
            if (!retainedForRollback && thumbprint.Length == 40)
            {
                try { RemoveRecordedCertificate(id, thumbprint); }
                catch (Exception cleanup) { throw new InvalidOperationException($"Install failed during {phase}; certificate {thumbprint} cleanup also failed: {cleanup.Message}", e); }
            }
            string state = retainedForRollback
                ? $"WinUSB is active; certificate {thumbprint} and its thumbprint record were retained for Restore()."
                : thumbprint.Length == 40 ? $"certificate {thumbprint} was removed from the three machine stores where present." : "certificate thumbprint was not recovered; inspect the machine stores before retrying.";
            throw new InvalidOperationException($"WinUSB install failed during {phase}; active device service={activeService}; {state}", e);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Removes the WinUSB driver from the adapter (administrator): the original package, still in the driver store,
    /// is reinstalled by Windows. Returns true if there was nothing to remove.
    /// </summary>
    public static bool Restore(string instanceId)
    {
        if (Get(instanceId).Driver != AdapterDriver.WinUsb) return true;
        string inf = Ps($"(Get-PnpDeviceProperty -InstanceId '{instanceId}' -KeyName 'DEVPKEY_Device_DriverInfPath').Data").Trim();
        if (!inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase)) return true; // no third-party WinUSB (or inbox driver)

        string provider = Ps($"(Get-PnpDeviceProperty -InstanceId '{instanceId.Replace("'", "''")}' -KeyName 'DEVPKEY_Device_DriverProvider').Data").Trim();
        Run("pnputil.exe", $"/delete-driver {inf} /uninstall /force");
        Run("pnputil.exe", "/scan-devices");
        if (provider == Publisher)
        {
            var id = Get(instanceId).Id;
            string thumbprint = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\AppleRemoteWin\WinUsbDriver\{id}", "CertificateThumbprint", "") as string ?? "";
            if (thumbprint.Length != 40) throw new InvalidOperationException("The install certificate thumbprint is missing; left all certificates untouched.");
            string service = Ps($"(Get-PnpDeviceProperty -InstanceId '{instanceId.Replace("'", "''")}' -KeyName 'DEVPKEY_Device_Service').Data").Trim();
            if (service.Equals("WinUSB", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("WinUSB is still active after rescan; kept the recorded certificate for safety.");
            RemoveRecordedCertificate(id, thumbprint);
        }
        return false;
    }

    static void RemoveRecordedCertificate(UsbHci.UsbId id, string thumbprint)
    {
        string stateKey = $@"SOFTWARE\AppleRemoteWin\WinUsbDriver\{StateName(id)}";
        Ps($$"""
            $thumb = '{{thumbprint}}'
            foreach ($s in 'My', 'Root', 'TrustedPublisher') {
                Get-ChildItem "Cert:\LocalMachine\$s" | Where-Object Thumbprint -eq $thumb | Remove-Item
            }
            Remove-ItemProperty -Path 'HKLM:\{{stateKey}}' -Name CertificateThumbprint -ErrorAction SilentlyContinue
            """);
    }

    static string StateName(UsbHci.UsbId id) => $"VID_{id.Vid:X4}_PID_{id.Pid:X4}";

    static string BuildInf(string hwid) => $$"""
        [Version]
        Signature   = "$Windows NT$"
        Class       = USBDevice
        ClassGuid   = {88BAE032-5A81-49f0-BC3D-A4FF138216D6}
        Provider    = %Provider%
        CatalogFile = AppleRemoteWinUsb.cat
        DriverVer   = 01/01/2024,1.0.0.0

        [Manufacturer]
        %Provider% = Devices,NTamd64

        [Devices.NTamd64]
        %DeviceName% = USB_Install, {{hwid}}

        [USB_Install]
        Include = winusb.inf
        Needs   = WINUSB.NT

        [USB_Install.Services]
        Include = winusb.inf
        Needs   = WINUSB.NT.Services

        [USB_Install.HW]
        AddReg = Dev_AddReg

        [Dev_AddReg]
        HKR,,DeviceInterfaceGUIDs,0x10000,"{6B3E8C52-9D41-4F6A-8A1E-5C0D7B2F4E19}"

        [Strings]
        Provider   = "{{Publisher}}"
        DeviceName = "Adaptateur Bluetooth (WinUSB)"
        """;

    static string Ps(string script, Encoding? encoding = null)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " + script));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}", encoding);
    }

    static string Run(string file, string args, Encoding? encoding = null)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = encoding,
        })!;
        string error = "";
        p.ErrorDataReceived += (_, e) => error += e.Data + "\n";
        p.BeginErrorReadLine();
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{file} failed ({p.ExitCode}) : {(error + output).Trim()}");
        return output;
    }

    static class Native
    {
        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "UpdateDriverForPlugAndPlayDevicesW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr owner, string hardwareId, string infPath, uint flags,
            [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);
    }
}
