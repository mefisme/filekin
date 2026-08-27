using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Filekin.Infrastructure.Windows.Inspection.Interop;

/// <summary>
/// The Windows Property System, which is where Filekin gets type-specific file metadata. One API
/// gives image dimensions, media duration, and executable version/company for every format Windows
/// knows, so `/info` never has to carry a per-format parser (DECISIONS.md, 2026-08-27).
///
/// Verified on a thread-pool (MTA) thread, which is where inspection runs: the property store and
/// <c>SHGetFileInfo</c> both work there without an STA.
/// </summary>
internal static partial class ShellMetadataInterop
{
    private const ushort VtI4 = 3;
    private const ushort VtR8 = 5;
    private const ushort VtBstr = 8;
    private const ushort VtI8 = 20;
    private const ushort VtUi4 = 19;
    private const ushort VtUi8 = 21;
    private const ushort VtLpwstr = 31;

    /// <summary>SHGFI_TYPENAME — the friendly type text Explorer itself shows.</summary>
    private const uint ShgfiTypeName = 0x400;

    internal static readonly PropertyKey ImageWidth = new(new Guid("6444048F-4C8B-11D1-8B70-080036B11A03"), 3);
    internal static readonly PropertyKey ImageHeight = new(new Guid("6444048F-4C8B-11D1-8B70-080036B11A03"), 4);
    internal static readonly PropertyKey MediaDuration = new(new Guid("64440490-4C8B-11D1-8B70-080036B11A03"), 3);
    internal static readonly PropertyKey Company = new(new Guid("D5CDD502-2E9C-101B-9397-08002B2CF9AE"), 15);
    internal static readonly PropertyKey FileVersion = new(new Guid("0CEF7D53-FA64-11D1-A203-0000F81FEDEE"), 4);
    internal static readonly PropertyKey ProductName = new(new Guid("0CEF7D53-FA64-11D1-A203-0000F81FEDEE"), 7);

    /// <summary>The friendly type name for a path, or <c>null</c> when the shell will not name it.</summary>
    internal static unsafe string? GetTypeName(string path)
    {
        var info = default(ShellFileInfo);
        var result = SHGetFileInfoW(path, 0, ref info, (uint)sizeof(ShellFileInfo), ShgfiTypeName);
        if (result == IntPtr.Zero)
        {
            return null;
        }

        var name = new string(info.TypeName);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Opens a read-only property store for <paramref name="path"/>, or returns <c>null</c> when the
    /// shell has no handler for it. The caller disposes the returned store.
    /// </summary>
    internal static IPropertyStore? TryOpenPropertyStore(string path)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            var hr = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, in iid, out var store);
            return hr == 0 ? store : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Releases a store from <see cref="TryOpenPropertyStore"/>. A source-generated COM interface is
    /// not statically <see cref="IDisposable"/>, so the runtime wrapper is asked for it instead.
    /// </summary>
    internal static void Release(IPropertyStore store) => (store as IDisposable)?.Dispose();

    internal static string? ReadString(IPropertyStore store, PropertyKey key) =>
        Read(store, key, static value => value.Vt switch
        {
            VtLpwstr => Marshal.PtrToStringUni(value.Value),
            VtBstr => Marshal.PtrToStringBSTR(value.Value),
            _ => null,
        });

    internal static uint? ReadUInt32(IPropertyStore store, PropertyKey key) =>
        Read(store, key, static value => value.Vt is VtUi4 or VtI4
            ? (uint?)unchecked((uint)value.Value.ToInt64())
            : null);

    /// <summary>Reads a duration property, which the shell reports in 100-nanosecond units.</summary>
    internal static TimeSpan? ReadDuration(IPropertyStore store, PropertyKey key) =>
        Read(store, key, static value => value.Vt switch
        {
            VtUi8 or VtI8 => TimeSpan.FromTicks(value.Value.ToInt64()),
            VtR8 => TimeSpan.FromSeconds(BitConverter.Int64BitsToDouble(value.Value.ToInt64())),
            _ => (TimeSpan?)null,
        });

    private static T? Read<T>(IPropertyStore store, PropertyKey key, Func<PropVariant, T?> convert)
    {
        var requested = key;
        var value = default(PropVariant);
        try
        {
            store.GetValue(in requested, out value);
            return convert(value);
        }
        catch (COMException)
        {
            // A store that has no handler for this property is the normal case, not a failure.
            return default;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHGetPropertyStoreFromParsingName(
        string path,
        IntPtr bindContext,
        uint flags,
        in Guid iid,
        [MarshalUsing(typeof(ComInterfaceMarshaller<IPropertyStore>))] out IPropertyStore store);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SHGetFileInfoW(
        string path,
        uint fileAttributes,
        ref ShellFileInfo info,
        uint infoSize,
        uint flags);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);
}

/// <summary>A Windows <c>PROPERTYKEY</c>: the format GUID plus the property id inside it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;
}

/// <summary>
/// A Windows <c>PROPVARIANT</c>, kept deliberately blittable. Only the discriminator and the first
/// two union words are declared, which covers every type <c>/info</c> reads; anything else falls
/// through as unsupported and is cleared unchanged.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Value;
    public IntPtr ValueHigh;
}

/// <summary>
/// <c>SHFILEINFOW</c>. The text fields are fixed char buffers rather than <c>ByValTStr</c>, because
/// source-generated P/Invoke marshals only blittable types (SYSLIB1051) — the same rule that already
/// forced a <c>Span&lt;char&gt;</c> on <c>SHLoadIndirectString</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ShellFileInfo
{
    public IntPtr Icon;
    public int IconIndex;
    public uint Attributes;
    public fixed char DisplayName[260];
    public fixed char TypeName[80];
}

[GeneratedComInterface]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
internal partial interface IPropertyStore
{
    uint GetCount();

    void GetAt(uint index, out PropertyKey key);

    void GetValue(in PropertyKey key, out PropVariant value);

    void SetValue(in PropertyKey key, in PropVariant value);

    void Commit();
}
