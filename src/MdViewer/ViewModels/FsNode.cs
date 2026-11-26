using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MdViewer.ViewModels;

/// <summary>Carpeta o archivo .md del explorador. Las carpetas cargan su contenido al expandirse.</summary>
public partial class FsNode : ObservableObject
{
    static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    bool _loaded;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsFolder { get; }
    public ObservableCollection<FsNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public FsNode(string name, string fullPath, bool isFolder)
    {
        Name = name;
        FullPath = fullPath;
        IsFolder = isFolder;
        if (isFolder) Children.Add(new FsNode("", "", false)); // marcador para que aparezca la flecha
    }

    // ponytail: carga síncrona en el hilo de UI; pasar a Task.Run si alguna carpeta enorme se nota lenta
    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || !IsFolder || _loaded) return;
        _loaded = true;
        Children.Clear();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(FullPath, "*", Options).Order(StringComparer.CurrentCultureIgnoreCase))
                Children.Add(new FsNode(Path.GetFileName(dir), dir, true));
            foreach (var file in Directory.EnumerateFiles(FullPath, "*.md", Options).Order(StringComparer.CurrentCultureIgnoreCase))
                Children.Add(new FsNode(Path.GetFileName(file), file, false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // carpeta ilegible: se queda vacía
        }
    }

    /// <summary>Agregado por el usuario (primer nivel del explorador): muestra el botón ✕ para cerrarlo.</summary>
    public bool IsRoot { get; init; }

    // ---------- Solo archivos abiertos (panel "Archivos" o vista previa): cada uno guarda su propio estado ----------

    /// <summary>Texto en memoria (con su historial de deshacer); null hasta que se abre por primera vez.</summary>
    public TextDocument? Document { get; set; }

    /// <summary>Tiene cambios sin guardar: el panel "Archivos" lo marca con ●.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Estaba en modo edición la última vez que se mostró.</summary>
    public bool IsEditing { get; set; }

    /// <summary>Nodo raíz para una ruta agregada por el usuario (carpeta o archivo).</summary>
    public static FsNode FromPath(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return new FsNode(name.Length > 0 ? name : path, path, Directory.Exists(path)) { IsRoot = true };
    }
}
