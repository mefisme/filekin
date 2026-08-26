using System.Runtime.InteropServices;

namespace Filekin.Infrastructure.Windows.Input;

/// <summary>
/// Resolves the character a virtual key produces on the user's current keyboard layout.
/// </summary>
/// <remarks>
/// A terminal surface needs this for Alt-prefixed keys. Windows treats Alt as a menu accelerator and
/// never raises a text-input event for it, so the character has to come from the key itself. Reading
/// the layout rather than assuming a US mapping keeps Alt shortcuts working on non-US keyboards.
/// </remarks>
public static partial class KeyboardCharacters
{
    private const uint MapVirtualKeyToCharacter = 2;

    /// <summary>
    /// The unshifted character for a Win32 virtual-key code, or null when the key produces no
    /// character (modifiers, function keys, navigation keys).
    /// </summary>
    public static char? ForVirtualKey(int virtualKey)
    {
        if (virtualKey <= 0)
        {
            return null;
        }

        // The high bit marks a dead key; the character itself is still in the low word.
        var mapped = (char)(MapVirtualKey((uint)virtualKey, MapVirtualKeyToCharacter) & 0xFFFF);
        return mapped < ' ' ? null : mapped;
    }

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint mapType);
}
