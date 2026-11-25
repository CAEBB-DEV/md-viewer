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

public partial class MainViewModel : ObservableObject
{
    /// <summary>Carpetas y archivos agregados al explorador, una ruta por línea.</summary>
    static readonly string ListPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdViewer", "explorador.txt");

    static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    string? _path;

    /// <summary>Archivos .md agregados sueltos (sección superior del explorador).</summary>
    public ObservableCollection<FsNode> Files { get; } = [];

    /// <summary>Carpetas agregadas (sección inferior del explorador).</summary>
    public ObservableCollection<FsNode> Folders { get; } = [];

    public TextDocument Document { get; } = new();

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(IsViewing))]
    [NotifyPropertyChangedFor(nameof(DocumentFolder), nameof(ShowNativeViewer), nameof(ShowWebViewer))]
    public partial string? FileName { get; set; }

    /// <summary>Carpeta del archivo abierto: base para las imágenes con ruta relativa (logo.png, docs/captura.png).</summary>
    public string? DocumentFolder => _path is null ? null : Path.GetDirectoryName(_path);

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string Title => FileName is null ? "MD Viewer" : FileName + (IsDirty ? " •" : "");

    public MainViewModel()
    {
        Document.TextChanged += (_, _) => IsDirty = true;
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

    /// <summary>Agrega carpetas o archivos al explorador (sin duplicados) y guarda la lista.</summary>
    public void Add(IEnumerable<string> paths)
    {
        AddNodes(paths);
        SaveList();
    }

    /// <summary>
    /// Archivo abierto desde fuera de la app (Explorador de Windows, "Abrir con", línea de comandos):
    /// se agrega a "Archivos" y se selecciona, lo que lo abre.
    /// </summary>
    public void OpenExternal(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            Error = "No se encontró el archivo: " + path;
            return;
        }
        Add([path]);
        SelectedFile = Files.FirstOrDefault(f => string.Equals(Path.GetFullPath(f.FullPath), path, PathComparison));
    }

    void AddNodes(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Files.Concat(Folders).Any(r => string.Equals(r.FullPath, path, PathComparison))) continue;
            var node = FsNode.FromPath(path);
            (node.IsFolder ? Folders : Files).Add(node);
        }
    }

    /// <summary>
    /// Saca de la lista un archivo de "Archivos" o la carpeta raíz que contiene <paramref name="node"/> en
    /// "Carpetas" (botón ✕ de la fila o Supr sobre la selección). No toca el disco.
    /// </summary>
    [RelayCommand]
    Task CloseItem(FsNode? node)
    {
        if (node is null) return Task.CompletedTask;
        if (Files.Contains(node)) return CloseRootAsync(node, Files);
        var root = Folders.FirstOrDefault(f => IsUnder(node.FullPath, f.FullPath));
        return root is null ? Task.CompletedTask : CloseRootAsync(root, Folders);
    }

    // Si el archivo abierto está dentro de lo que se cierra, se pregunta por los cambios pendientes
    // y, si el usuario continúa, también se cierra en el visor.
    async Task CloseRootAsync(FsNode root, ObservableCollection<FsNode> list)
    {
        var affectsOpen = _path is not null && IsUnder(_path, root.FullPath);
        if (affectsOpen && !await CanLeaveAsync()) return;
        list.Remove(root);
        SaveList();
        if (affectsOpen) CloseDocument();
    }

    static bool IsUnder(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(root, PathComparison) || path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>
    /// Botón ✕ de la barra superior: deja de mostrar el documento (sigue en el explorador). Si hay cambios sin
    /// guardar pregunta antes, igual que al cambiar de archivo.
    /// </summary>
    [RelayCommand]
    async Task CloseView()
    {
        if (await CanLeaveAsync()) CloseDocument();
    }

    // Se quita la selección para que un clic en el archivo lo vuelva a abrir
    void CloseDocument()
    {
        WithoutOpening(() => { SelectedFile = null; SelectedFolderItem = null; });
        _path = null;
        FileName = null;
        IsEditing = false;
        Document.Text = "";
        Document.UndoStack.ClearAll();
        IsDirty = false;
        Markdown = "";
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

    // Solo un panel tiene selección: al elegir algo en uno se quita la del otro. Si no, volver a hacer clic
    // en un archivo que sigue seleccionado en el otro panel no lo abriría (la selección no cambia).
    partial void OnSelectedFileChanged(FsNode? oldValue, FsNode? newValue)
    {
        if (!_revertingSelection && newValue is not null)
            _ = OpenFromTreeAsync(newValue, () => SelectedFile = oldValue, () => SelectedFolderItem = null);
    }

    partial void OnSelectedFolderItemChanged(FsNode? oldValue, FsNode? newValue)
    {
        if (_revertingSelection || newValue is null) return;
        if (newValue.IsFolder) WithoutOpening(() => SelectedFile = null);
        else _ = OpenFromTreeAsync(newValue, () => SelectedFolderItem = oldValue, () => SelectedFile = null);
    }

    // Si el usuario cancela, la selección del árbol vuelve a donde estaba; si se abrió, se limpia el otro panel
    async Task OpenFromTreeAsync(FsNode node, Action revertSelection, Action clearOtherPanel) =>
        WithoutOpening(await OpenAsync(node.FullPath) ? clearOtherPanel : revertSelection);

    void WithoutOpening(Action changeSelection)
    {
        _revertingSelection = true;
        changeSelection();
        _revertingSelection = false;
    }

    /// <summary>
    /// Permite dejar el archivo actual: true si no hay cambios, o si el usuario eligió guardar (y se guardó)
    /// o no guardar. False si canceló o falló el guardado.
    /// </summary>
    public async Task<bool> CanLeaveAsync()
    {
        if (!IsDirty || ConfirmUnsaved is null) return true;
        return await ConfirmUnsaved(FileName ?? "") switch
        {
            UnsavedChoice.Save => TrySave(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>Abre un archivo. Devuelve false si no se abrió (cancelado o error de lectura).</summary>
    public async Task<bool> OpenAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (path == _path) return true;
        if (!await CanLeaveAsync()) return false;

        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
            return false;
        }

        _path = path;
        FileName = Path.GetFileName(path);
        Document.Text = text;
        Document.UndoStack.ClearAll();
        IsDirty = false;
        Markdown = text;
        Error = null;
        return true;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (!value) Markdown = Document.Text;
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    void Save() => TrySave();

    /// <summary>Guarda si hay cambios (solo por acción del usuario). Devuelve false si falló la escritura.</summary>
    public bool TrySave()
    {
        if (!IsDirty || _path is null) return true;
        try
        {
            File.WriteAllText(_path, Document.Text);
            IsDirty = false;
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
