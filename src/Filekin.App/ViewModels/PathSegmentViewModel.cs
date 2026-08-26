using System;

namespace Filekin.App.ViewModels;

/// <summary>
/// One clickable segment of the current-location path bar (UX-DESIGN.md — "Path segments can still be
/// clickable"). <see cref="FullPath"/> is the location this segment navigates to; <see cref="IsLast"/>
/// marks the current folder, which is drawn emphasized and does not navigate.
/// </summary>
public sealed class PathSegmentViewModel
{
    public PathSegmentViewModel(string text, string fullPath, bool isRoot, bool isLast)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fullPath);

        Text = text;
        FullPath = fullPath;
        IsRoot = isRoot;
        IsLast = isLast;
    }

    public string Text { get; }

    public string FullPath { get; }

    public bool IsRoot { get; }

    public bool IsLast { get; }

    /// <summary>Whether a <c>\</c> separator is drawn before this segment (every segment but the root).</summary>
    public bool ShowSeparator => !IsRoot;
}
