# Reproducibility notes — WSL2/Linux proof-of-concept (NOT Windows product)

## Tested environment

- Windows laptop with internal Intel AX200 Bluetooth USB controller (`8087:0029`).
- Ubuntu 22.04 under WSL2; BlueZ 5.64; Rust 1.99.1.
- Physical 3rd-generation Siri Remote A2854.
- Bluetooth controller temporarily exported to WSL2 using `usbipd`, then accessed as a Linux USB Bluetooth controller.

## BLE passthrough (historical diagnostics)

From **administrator PowerShell**, Bluetooth was shared and attached to WSL:

```powershell
usbipd bind --force --busid 1-3
usbipd attach --wsl --busid 1-3
```

`1-3` is the bus ID observed during this experiment, **not a universal bus ID**. Check `usbipd list` before using it on another system. Windows itself cannot use that Bluetooth controller while it is attached to WSL.

The adapter appeared through `lsusb` and `bluetoothctl list`. BLE scanning identified the physical remote.

## Temporary build workaround

Upstream Linux build initially failed in `libspa 0.9.2` because Ubuntu 22.04's older PipeWire SPA headers lacked members / had ABI type mismatch. For the experimental binary only:
- `Cargo.toml` audio native dependencies `opus`, `pipewire`, `libspa` were removed.
- `src/main.rs` references to `mod mic;` and `mod audio;` were removed.
- The `Mic` command dispatch was replaced by a clear `mic disabled in this build` error.
- Original files had `.bak-wsl` local backups.
- `cargo build --release -j 2` then succeeded.

Do **not** treat this as an upstream patch or a functional microphone feature. The Linux `mic` help entry may still be visible although functionality was deliberately disabled.

## BlueZ HID plugin interference

Before the fix, `bluetoothctl info` reported `Paired: yes` and `Connected: yes` while `events` saw only **5 services**, missing `0x1812`. The journal reported:

```text
input-hog profile accept failed
```

A systemd override in WSL restarted `bluetoothd` with:

```ini
[Service]
ExecStart=
ExecStart=/usr/lib/bluetooth/bluetoothd --noplugin=input,hog
```

This restored HID visibility (`6 services`) and raw notifications. The binary still logged ATT `0x82` for one input-enable write, but buttons and touch were received. This override is **WSL experimentation only**, not a recommended change to native Windows or production Linux systems.

Remove the WSL override if still present:

```bash
sudo rm -f /etc/systemd/system/bluetooth.service.d/siri-remote.conf
sudo systemctl daemon-reload
sudo systemctl restart bluetooth
```

## Restore Windows Bluetooth after experimentation

Once the WSL BLE experiment is finished, on **administrator PowerShell**:

```powershell
usbipd detach --busid 1-3
usbipd unbind --busid 1-3
usbipd list
```

Check that the adapter is no longer `Shared`, verify it works in Windows Bluetooth settings, and restart/rescan hardware if necessary. **These are restoration commands to run on the owner's computer; writing this document does not execute them.**

No credentials, actual hardware MAC address, serial number, or full local hostname are stored here.
