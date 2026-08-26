using System.Globalization;
using System.IO;
using Filekin.Core.FileSystem;

namespace Filekin.App.ViewModels;

/// <summary>One row in the <c>/recycle</c> Recycle Bin view: display strings plus the underlying item for restore.</summary>
public sealed class RecycledItemViewModel
{
    public RecycledItemViewModel(RecycledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Item = item;
        TypeCode = FileTypeCode.For(item.Name, item.IsDirectory);
        OriginalLocation = Path.GetDirectoryName(item.OriginalPath) ?? item.OriginalPath;
        Deleted = item.DeletedWhen?.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "—";
        Size = ByteSize.Format(item.SizeBytes);
    }

    public RecycledItem Item { get; }

    public string TypeCode { get; }

    public string Name => Item.Name;

    public bool IsDirectory => Item.IsDirectory;

    public string OriginalLocation { get; }

    public string Deleted { get; }

    public string Size { get; }
}
