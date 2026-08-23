using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace MirrorStudio.App.Services;

/// <summary>
/// Thin, testable wrapper around Avalonia storage-provider file dialogs.
/// Centralizes every file/folder pick in the app (spec §7).
/// </summary>
public sealed class DialogService
{
    private readonly Window _owner;

    public DialogService(Window owner) => _owner = owner;

    public async Task<string?> PickFileAsync(string title, params (string Name, string[] Extensions)[] filters)
    {
        var files = await PickFilesCoreAsync(title, allowMultiple: false, filters);
        return files?.FirstOrDefault();
    }

    public async Task<string[]?> PickFilesAsync(string title, params (string Name, string[] Extensions)[] filters)
        => await PickFilesCoreAsync(title, allowMultiple: true, filters);

    public async Task<string?> PickFolderAsync(string title)
    {
        var files = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return files.Count > 0 ? Uri.UnescapeDataString(files[0].Path.AbsolutePath) : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, params (string Name, string[] Extensions)[] filters)
    {
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = ToChoices(filters)
        });
        return file is null ? null : Uri.UnescapeDataString(file.Path.AbsolutePath);
    }

    private async Task<string[]?> PickFilesCoreAsync(
        string title, bool allowMultiple, (string Name, string[] Extensions)[] filters)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = ToChoices(filters)
        });
        if (files.Count == 0) return null;
        return files.Select(f => Uri.UnescapeDataString(f.Path.AbsolutePath)).ToArray();
    }

    private static List<FilePickerFileType> ToChoices((string Name, string[] Extensions)[] filters) =>
        filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Extensions }).ToList();
}

/// <summary>App-specific dialogs built on <see cref="DialogService"/>.</summary>
public sealed class ProjectDialogService(DialogService dialogs)
{
    public const string MediaPatterns = "*.mp4;*.mov;*.mkv;*.avi;*.webm;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.wav;*.mp3;*.flac;*.ogg";

    private static readonly (string Name, string[] Extensions)[] ProjectFilter =
        { ("Mirror Studio Project", new[] { "msp" }) };

    private static readonly (string Name, string[] Extensions)[] MediaFilters =
    {
        ("Media Files", new[] { "mp4", "mov", "mkv", "avi", "webm", "png", "jpg", "jpeg", "bmp", "gif", "wav", "mp3", "flac", "ogg" }),
        ("Video", new[] { "mp4", "mov", "mkv", "avi", "webm" }),
        ("Images", new[] { "png", "jpg", "jpeg", "bmp", "gif" }),
        ("Audio", new[] { "wav", "mp3", "flac", "ogg" }),
        ("All Files", new[] { "*" })
    };

    public Task<string?> OpenProjectAsync() => dialogs.PickFileAsync("Open Project");

    public Task<string?> SaveProjectBundleAsync(string projectName) =>
        dialogs.SaveFileAsync("Save Project As", projectName + ".msp", ProjectFilter);

    public Task<string[]?> ImportMediaAsync() => dialogs.PickFilesAsync("Import Media Files", MediaFilters);

    public Task<string?> ChooseProjectFolderAsync() =>
        dialogs.PickFolderAsync("Choose a folder for the new project");
}
