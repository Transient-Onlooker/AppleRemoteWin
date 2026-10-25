// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Pure A2854 HID button ownership policy. Pointer movement and keyboard
/// injection each retain their own opt-in. Never synthesize a click from touch
/// pressure; only the physical 0xFB button report provides click transitions.
/// </summary>
public static class A2854ButtonRouting
{
    public static bool PointerOwnsClick(bool pointerEnabled, bool clickAsKeyboard) =>
        pointerEnabled && !clickAsKeyboard;

    public static bool AllowKeyboardButton(string name, bool pointerEnabled, bool clickAsKeyboard) =>
        name is not ("tv" or "siri" or "power") &&
        (name != "click" || !PointerOwnsClick(pointerEnabled, clickAsKeyboard));

    /// <summary>Filter both edges consistently, keeping the original raw report and mask.</summary>
    public static ButtonsEvent ForKeyboard(ButtonsEvent buttons, bool pointerEnabled, bool clickAsKeyboard) =>
        new(buttons.Mask,
            buttons.Pressed.Where(name => AllowKeyboardButton(name, pointerEnabled, clickAsKeyboard)).ToArray(),
            buttons.Released.Where(name => AllowKeyboardButton(name, pointerEnabled, clickAsKeyboard)).ToArray(),
            buttons.Raw);
}
