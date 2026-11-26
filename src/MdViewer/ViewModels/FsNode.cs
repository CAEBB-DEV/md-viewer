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

    public string Name { get; private set; }
    /// <summary>Ruta en disco; vacía en un documento nuevo que aún no se guardó ("Sin título").</summary>
    public string FullPath { get; private set; }
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

    /// <summary>Documento nuevo que todavía no tiene archivo en disco (se pide la ubicación al guardarlo).</summary>
    public bool IsUntitled => FullPath.Length == 0;

    /// <summary>Documento nuevo sin archivo: se muestra con este nombre hasta que se guarda.</summary>
    public static FsNode Untitled(string name) => new(name, "", false) { IsRoot = true, Document = new TextDocument() };

    /// <summary>Al guardar un documento nuevo: pasa a ser ese archivo.</summary>
    public void AssignPath(string path)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsUntitled));
    }

    /// <summary>Nodo raíz para una ruta agregada por el usuario (carpeta o archivo).</summary>
    public static FsNode FromPath(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return new FsNode(name.Length > 0 ? name : path, path, Directory.Exists(path)) { IsRoot = true };
    }
}
