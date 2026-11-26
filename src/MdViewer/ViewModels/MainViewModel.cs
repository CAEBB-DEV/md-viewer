using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MdViewer.ViewModels;

public enum UnsavedChoice { Save, Discard, Cancel }

/// <summary>
/// Explorador y documento mostrado. Como las pestañas de VS Code: "Archivos" lista los archivos abiertos, cada uno
/// con sus propios cambios sin guardar. Un clic en "Carpetas" abre una vista previa que no se lista; pasa a
/// "Archivos" con doble clic o al editarla, y la siguiente vista previa la reemplaza.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>Carpetas y archivos abiertos del explorador, una ruta por línea.</summary>
    static readonly string ListPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdViewer", "explorador.txt");

    static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Documento mostrado: uno de <see cref="Files"/> o la vista previa (abierto desde "Carpetas").</summary>
    FsNode? _active;

    /// <summary>Archivos abiertos (sección superior del explorador).</summary>
    public ObservableCollection<FsNode> Files { get; } = [];

    /// <summary>Carpetas agregadas (sección inferior del explorador).</summary>
    public ObservableCollection<FsNode> Folders { get; } = [];

    /// <summary>Texto del documento mostrado (cada archivo abierto tiene el suyo).</summary>
    [ObservableProperty]
    public partial TextDocument Document { get; set; } = new();

    [ObservableProperty]
    public partial FsNode? SelectedFile { get; set; }

    /// <summary>Elemento seleccionado en el árbol de carpetas (la carpeta raíz o algo dentro de ella).</summary>
    [ObservableProperty]
    public partial FsNode? SelectedFolderItem { get; set; }

    /// <summary>Texto que se muestra en la vista; se actualiza al salir del modo edición.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NativeMarkdown))]
    public partial string Markdown { get; set; } = "";

    /// <summary>El documento tiene HTML: se muestra con el visor web (si no, con el nativo, que es más rápido).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNativeViewer), nameof(ShowWebViewer), nameof(NativeMarkdown))]
    public partial bool UsesHtml { get; set; }

    /// <summary>No hay navegador del sistema disponible: todo se muestra con el visor nativo.</summary>
    [ObservableProperty]
    public partial bool WebViewerUnavailable { get; set; }

    partial void OnMarkdownChanged(string value) => UsesHtml = !WebViewerUnavailable && MarkdownHtml.ContainsHtml(value);
    partial void OnWebViewerUnavailableChanged(bool value) => UsesHtml = false;

    /// <summary>Contenido del visor nativo: vacío si el documento va al visor web, para no procesarlo dos veces.</summary>
    public string NativeMarkdown => UsesHtml ? "" : Markdown;

    public bool ShowNativeViewer => FileName is not null && !IsEditing && !UsesHtml;
    public bool ShowWebViewer => FileName is not null && !IsEditing && UsesHtml;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing), nameof(ShowNativeViewer), nameof(ShowWebViewer))]
    public partial bool IsEditing { get; set; }

    /// <summary>Hay un archivo abierto en modo visualización (muestra la barra de ancho de lectura).</summary>
    public bool IsViewing => FileName is not null && !IsEditing;

    /// <summary>El documento mostrado tiene cambios sin guardar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(IsViewing))]
    [NotifyPropertyChangedFor(nameof(ShowNativeViewer), nameof(ShowWebViewer))]
    public partial string? FileName { get; set; }

    /// <summary>Carpeta del archivo abierto: base para las imágenes con ruta relativa (logo.png, docs/captura.png).</summary>
    public string? DocumentFolder => _active is null ? null : Path.GetDirectoryName(_active.FullPath);

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string Title => FileName is null ? "MD Viewer" : FileName + (IsDirty ? " •" : "");

    /// <summary>Algún archivo abierto tiene cambios sin guardar (se pregunta al cerrar la app).</summary>
    public bool HasUnsaved => Files.Any(f => f.IsDirty);

    public MainViewModel()
    {
        try
        {
            if (File.Exists(ListPath))
                AddNodes(File.ReadAllLines(ListPath).Where(l => l.Length > 0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = "No se pudo leer la lista del explorador: " + e.Message;
        }
    }

    /// <summary>Agrega carpetas al explorador (sin duplicados) y guarda la lista.</summary>
    public void AddFolders(IEnumerable<string> paths)
    {
        AddNodes(paths);
        SaveList();
    }

    void AddNodes(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Files.Concat(Folders).Any(r => SamePath(r.FullPath, path))) continue;
            var node = FsNode.FromPath(path);
            (node.IsFolder ? Folders : Files).Add(node);
        }
    }

    /// <summary>
    /// Archivo abierto desde fuera de la app (Explorador de Windows, "Abrir con", línea de comandos) o un enlace
    /// a otro .md: se agrega a "Archivos" y se muestra.
    /// </summary>
    public void OpenExternal(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            Error = "No se encontró el archivo: " + path;
            return;
        }
        OpenFiles([path]);
    }

    /// <summary>Agrega los archivos a "Archivos" y muestra el último.</summary>
    public void OpenFiles(IEnumerable<string> paths)
    {
        FsNode? last = null;
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            last = FindOpen(path) ?? (IsPreview(path) ? _active! : FsNode.FromPath(path));
            Keep(last);
        }
        if (last is not null) Activate(last);
    }

    /// <summary>Doble clic en un archivo del explorador: se muestra y queda en "Archivos".</summary>
    public void KeepOpen(FsNode node)
    {
        if (node.IsFolder || !Preview(node.FullPath)) return;
        Keep(_active!);
        SyncSelection();
    }

    FsNode? FindOpen(string path) => Files.FirstOrDefault(f => SamePath(f.FullPath, path));

    bool IsPreview(string path) => _active is not null && !Files.Contains(_active) && SamePath(_active.FullPath, path);

    static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

    // Pasa a la lista de "Archivos" (si no estaba)
    void Keep(FsNode node)
    {
        if (Files.Contains(node)) return;
        Files.Add(node);
        SaveList();
    }

    // Muestra un archivo de "Carpetas": el que ya está en "Archivos" (con sus cambios) o una vista previa nueva
    bool Preview(string path) =>
        FindOpen(path) is { } open ? Activate(open) : IsPreview(path) || Activate(FsNode.FromPath(path));

    /// <summary>Muestra el archivo. Devuelve false si no se pudo leer.</summary>
    bool Activate(FsNode node)
    {
        if (node != _active)
        {
            if (!Load(node)) return false;
            _active = node;
            FileName = node.Name;
            Document = node.Document!;
            IsDirty = node.IsDirty;
            Markdown = node.Document!.Text;
            IsEditing = node.IsEditing;
            Error = null;
            OnPropertyChanged(nameof(DocumentFolder));
        }
        SyncSelection();
        return true;
    }

    // Lee el archivo del disco, salvo que tenga cambios sin guardar (así se ve lo último si cambió por fuera)
    bool Load(FsNode node)
    {
        if (node.Document is not null && node.IsDirty) return true;
        string text;
        try { text = File.ReadAllText(node.FullPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
            return false;
        }
        if (node.Document is null)
        {
            node.Document = new TextDocument(text);
            node.Document.TextChanged += (_, _) => MarkDirty(node);
        }
        else if (node.Document.Text != text)
        {
            node.Document.Text = text;
            node.Document.UndoStack.ClearAll();
            node.IsDirty = false;
        }
        return true;
    }

    void MarkDirty(FsNode node)
    {
        node.IsDirty = true;
        if (node == _active) IsDirty = true;
    }

    // La selección de "Archivos" sigue al documento mostrado; si está ahí, se quita la de "Carpetas" (así un
    // nuevo clic en ese archivo de "Carpetas" vuelve a funcionar)
    void SyncSelection() => WithoutOpening(() =>
    {
        SelectedFile = _active is not null && Files.Contains(_active) ? _active : null;
        if (SelectedFile is not null) SelectedFolderItem = null;
    });

    /// <summary>
    /// Botón ✕ de una fila o Supr: cierra un archivo de "Archivos" (preguntando si tiene cambios) o saca de la
    /// lista la carpeta raíz que contiene <paramref name="node"/>. No toca el disco.
    /// </summary>
    [RelayCommand]
    Task CloseItem(FsNode? node)
    {
        if (node is null) return Task.CompletedTask;
        if (Files.Contains(node)) return CloseFileAsync(node);
        if (Folders.FirstOrDefault(f => IsUnder(node.FullPath, f.FullPath)) is { } root)
        {
            Folders.Remove(root);
            SaveList();
            // La vista previa (nunca tiene cambios: editarla la pasa a "Archivos") se cierra con su carpeta
            if (_active is not null && IsPreview(_active.FullPath) && IsUnder(_active.FullPath, root.FullPath)) CloseDocument();
        }
        return Task.CompletedTask;
    }

    /// <summary>Botón ✕ de la barra superior: cierra el documento mostrado y lo quita de "Archivos".</summary>
    [RelayCommand]
    Task CloseView()
    {
        if (_active is null) return Task.CompletedTask;
        if (Files.Contains(_active)) return CloseFileAsync(_active);
        CloseDocument();
        return Task.CompletedTask;
    }

    // Si se cierra el documento mostrado, pasa al archivo vecino de la lista (como al cerrar una pestaña)
    async Task CloseFileAsync(FsNode node)
    {
        if (!await ConfirmSaveAsync(node)) return;
        var index = Files.IndexOf(node);
        Files.Remove(node);
        SaveList();
        node.Document = null;
        node.IsDirty = false;
        node.IsEditing = false;
        if (node != _active)
        {
            SyncSelection();
            return;
        }
        var next = Files.Count == 0 ? null : Files[Math.Min(index, Files.Count - 1)];
        if (next is null || !Activate(next)) CloseDocument();
    }

    static bool IsUnder(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(root, PathComparison) || path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    // Se quita la selección para que un clic en el archivo lo vuelva a abrir
    void CloseDocument()
    {
        _active = null;
        WithoutOpening(() => { SelectedFile = null; SelectedFolderItem = null; });
        FileName = null;
        IsEditing = false;
        Document = new TextDocument();
        IsDirty = false;
        Markdown = "";
        OnPropertyChanged(nameof(DocumentFolder));
    }

    void SaveList()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ListPath)!);
            File.WriteAllLines(ListPath, Files.Concat(Folders).Select(r => r.FullPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = "No se pudo guardar la lista del explorador: " + e.Message;
        }
    }

    /// <summary>La vista pregunta qué hacer con los cambios sin guardar (recibe el nombre del archivo).</summary>
    public Func<string, Task<UnsavedChoice>>? ConfirmUnsaved { get; set; }

    bool _revertingSelection;

    partial void OnSelectedFileChanged(FsNode? oldValue, FsNode? newValue)
    {
        if (_revertingSelection || newValue is null) return;
        if (!Activate(newValue)) WithoutOpening(() => SelectedFile = oldValue);
    }

    // Un archivo de "Carpetas" se muestra como vista previa (no pasa a "Archivos" con un solo clic)
    partial void OnSelectedFolderItemChanged(FsNode? oldValue, FsNode? newValue)
    {
        if (_revertingSelection || newValue is null || newValue.IsFolder) return;
        if (!Preview(newValue.FullPath)) WithoutOpening(() => SelectedFolderItem = oldValue);
    }

    void WithoutOpening(Action changeSelection)
    {
        _revertingSelection = true;
        changeSelection();
        _revertingSelection = false;
    }

    /// <summary>
    /// Antes de cerrar un archivo con cambios: true si el usuario eligió guardar (y se guardó) o no guardar.
    /// False si canceló o falló el guardado.
    /// </summary>
    async Task<bool> ConfirmSaveAsync(FsNode node)
    {
        if (!node.IsDirty || ConfirmUnsaved is null) return true;
        return await ConfirmUnsaved(node.Name) switch
        {
            UnsavedChoice.Save => TrySave(node),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>Al cerrar la app: pregunta por cada archivo con cambios (mostrándolo). False si se canceló.</summary>
    public async Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var node in Files.Where(f => f.IsDirty).ToList())
        {
            Activate(node);
            if (!await ConfirmSaveAsync(node)) return false;
        }
        return true;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (_active is null) return;
        _active.IsEditing = value;
        if (value)
        {
            Keep(_active); // editar una vista previa la deja en "Archivos"
            SyncSelection();
        }
        else Markdown = Document.Text;
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    void Save()
    {
        if (_active is not null) TrySave(_active);
    }

    /// <summary>Guarda si hay cambios (solo por acción del usuario). Devuelve false si falló la escritura.</summary>
    bool TrySave(FsNode node)
    {
        if (!node.IsDirty || node.Document is null) return true;
        try
        {
            File.WriteAllText(node.FullPath, node.Document.Text);
            node.IsDirty = false;
            if (node == _active) IsDirty = false;
            Error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = "No se pudo guardar: " + e.Message;
            return false;
        }
    }
}
