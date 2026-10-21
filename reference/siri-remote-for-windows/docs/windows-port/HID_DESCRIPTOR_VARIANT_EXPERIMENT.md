# A2854 offline HID descriptor variant experiment (2026-10-17)

This experiment **does not touch the remote, Bluetooth pairing, Windows drivers, or the registry**. The owner's raw 38-byte report descriptor is never stored in GitHub or CI. The original was obtained from the exact failing A2854 HIDCLASS Event ID 3 v2, whose DeviceStartStatus was 0xC00000B9, and remains in the owner's local TEMP.

Confirmed original geometry: Feature Report ID 0xB8 = 50 bits x 6 = 300 payload bits; Input Report ID 0x04 = 8 bits x 17 = 136 payload bits. Input is Data/Variable with Buffered Bytes set, but its own local Usage list is empty because local items reset after Feature main items.

## Controlled variants

All variant edits preserve the 300-bit Feature and 136-bit Input sizes, checked by the offline scanner.

| Variant filename | Alteration |
| --- | --- |
| original.bin | None |
| single_input_usage.bin | One Usage 0x0B added before Input |
| input_usage_range.bin | Usage Minimum 1 and Maximum 17 before Input |
| feature_25x12.bin | Feature field width 25 bits x 12 fields, same 300 bits |
| range_and_25x12.bin | Usage range and narrowed Feature |
| unbuffered_input.bin | Clear Input Buffered Bytes flag |
| range_and_unbuffered.bin | Usage range and clear Buffered Bytes |
| range_25x12_unbuffered.bin | All three adjustments |

The first script is tools/protocol/descriptor_variants.py; the optional independent parser wrapper is tools/protocol/third_party_variant_check.py.

## Completed tests

A GitHub Windows CI job tested all eight variants using a *synthetic HID descriptor of the same report geometry*, not the actual private A2854 capture. All eight were structurally valid under the project's own offline rules. An independent parser, **hid-parser 0.1.0**, successfully constructed all eight synthetic descriptors.

The independent parser's get_input_report_size(0x04) returned **17 bytes** for original, Usage range, narrowed Feature, their combined variants, and Buffered Bytes-cleared variants. It returned **1 byte** for the variant with **one Usage only**, indicating that simply adding one Usage can materially change the parser's inferred input capability structure. This is not proof of a Windows failure or of a specific Windows bug.

The raw descriptor parser entry point that Microsoft documents, HidP_GetCollectionDescription, belongs to the **kernel-mode WDK hidparse** interface. Neither our parser nor hid-parser is the Windows HIDCLASS kernel parser. We have **not** validated that any variant would be accepted by the native Windows HID stack, and have **not** installed a test minidriver or injected descriptors into an existing device.

## Execute on the private original (PowerShell)

Run in the **existing PowerShell**, including cd; git fetch does not overwrite local modifications. These commands bring only the scripts into the local TEMP folder and never upload the original descriptor:

    cd "C:\Users\무조림.000\Desktop\코딩\01_working\siri-remote-for-windows"
    git fetch origin main
    git show origin/main:tools/protocol/descriptor_variants.py | Set-Content "$env:TEMP\descriptor_variants.py" -Encoding UTF8
    git show origin/main:tools/protocol/third_party_variant_check.py | Set-Content "$env:TEMP\third_party_variant_check.py" -Encoding UTF8
    py -3 "$env:TEMP\descriptor_variants.py" --input "$env:TEMP\a2854_hidclass_report_map.bin" --out-dir "$env:TEMP\a2854_descriptor_variants"
    py -3 -m pip install "hid-parser==0.1.0"
    py -3 "$env:TEMP\third_party_variant_check.py" --input "$env:TEMP\a2854_hidclass_report_map.bin"

The output directory must not exist yet. Use another output directory on retries. Share summary lines only; not raw ETL files, PnP paths, BLE addresses or descriptor binaries. Do **not** apply a mutated descriptor to a live driver or the paired remote.

## Supported conclusions

1. The reported Windows device-start failure and the 38-byte descriptor are **directly correlated** to the same failed A2854 HID child.
2. Report geometry can be preserved while independently varying local Usage declarations, field width and Buffered Bytes flags.
3. All eight **synthetic** shapes were accepted by an independent, non-Windows parser. Only one Usage yielded an unexpectedly short inferred Input length.
4. **Unresolved:** which item Windows rejected, whether this HIDCLASS event descriptor matches the remote's full over-the-air 0x2A4B Report Map, and what driver-side solution will be needed.

References:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpddi/nf-hidpddi-hidp_getcollectiondescription
- https://www.usb.org/hid
- https://wicg.github.io/webhid/index.html
- https://pypi.org/project/hid-parser/
