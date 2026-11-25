using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.TextMate;
using ColorTextBlock.Avalonia;
using MdViewer.ViewModels;
using TextMateSharp.Grammars;

namespace MdViewer.Views;

public partial class MainWindow : Window
{
    const double NarrowWidth = 760;

    // Resaltado del editor: se prepara después de mostrar la ventana (cargar gramáticas retrasa el arranque)
    RegistryOptions? _registry;
    TextMate.Installation? _textMate;
    bool? _narrow;
    bool _closeConfirmed;

    // Icono de copiar (dos hojas superpuestas, 16x16)
    static readonly Geometry CopyIcon = Geometry.Parse(
        "M5.5,1.5 H13.5 A1,1 0 0 1 14.5,2.5 V10.5 A1,1 0 0 1 13.5,11.5 H5.5 A1,1 0 0 1 4.5,10.5 V2.5 A1,1 0 0 1 5.5,1.5 Z " +
        "M4.5,4.5 H2.5 A1,1 0 0 0 1.5,5.5 V13.5 A1,1 0 0 0 2.5,14.5 H10.5 A1,1 0 0 0 11.5,13.5 V11.5");

    // Markdown.Avalonia crea por código el botón de copiar de los bloques de código con el texto "Copy"
    // (los estilos no pueden cambiarlo); al cargarse se reemplaza su contenido por el icono.
    static MainWindow() => LoadedEvent.AddClassHandler<Button>((button, _) =>
    {
        if (!button.Classes.Contains("CopyButton") || button.Content is Avalonia.Controls.Shapes.Path) return;
        var icon = new Avalonia.Controls.Shapes.Path { Data = CopyIcon, Width = 16, Height = 16, StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round };
        icon.Bind(Shape.StrokeProperty, button.GetObservable(ForegroundProperty));
        button.Content = icon;
        ToolTip.SetTip(button, "Copiar");
    });

    public MainWindow()
    {
        InitializeComponent();

        // Resaltado de sintaxis Markdown en el editor, con colores según el tema (cuando la ventana ya se ve)
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _registry = new RegistryOptions(ThemeName.LightPlus);
            _textMate = Editor.InstallTextMate(_registry);
            _textMate.SetGrammar(_registry.GetScopeByLanguageId("markdown"));
            ApplyEditorTheme();
        }, DispatcherPriority.Background);
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        ApplyExplorerLayout();
        Viewer.AddHandler(KeyDownEvent, CopyViewerSelection, RoutingStrategies.Tunnel);
        Viewer.AddHandler(PointerPressedEvent, SelectWordOrParagraph, RoutingStrategies.Tunnel);
        Viewer.AddHandler(PointerReleasedEvent, KeepMultiClickSelection, RoutingStrategies.Tunnel);
        Viewer.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Markdown") Dispatcher.UIThread.Post(ApplyViewerWidth, DispatcherPriority.Loaded);
        };
        SetupWebViewer();
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    // ---------- Visor web: documentos con HTML (los demás usan el visor nativo, más rápido) ----------

    string? _webRendered;          // Markdown que tiene cargado el visor web
    bool _webExpectNavigation;     // la próxima navegación es la página generada (no un clic en un enlace)

    bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    void SetupWebViewer()
    {
        // Datos del navegador en la carpeta del usuario (Program Files no permite escribir)
        WebViewer.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = false;
            if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                windows.UserDataFolder = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer", "WebView2");
        };
        WebViewer.NavigationStarted += (_, e) =>
        {
            if (_webExpectNavigation) { _webExpectNavigation = false; return; }
            e.Cancel = true;
            if (e.Request is { } uri) OpenLink(uri);
        };
        WebViewer.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } uri) OpenLink(uri);
        };
        Split.PropertyChanged += (_, e) =>
        {
            if (e.Property == SplitView.IsPaneOpenProperty || e.Property == SplitView.DisplayModeProperty) UpdateWebViewer();
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            if (WebViewer.IsVisible) _ = WebViewer.InvokeScript($"setTheme({(IsDark ? "true" : "false")})");
            else _webRendered = null; // se vuelve a generar con el tema nuevo la próxima vez que se muestre
        };
    }

    // Muestra el visor web cuando el documento tiene HTML y lo carga si cambió. Es una ventana nativa del sistema
    // (se dibuja encima de todo), así que se oculta mientras el explorador se superpone en ventanas angostas.
    void UpdateWebViewer()
    {
        if (DataContext is not MainViewModel vm) return;
        var show = vm.ShowWebViewer && !(Split.DisplayMode == SplitViewDisplayMode.Overlay && Split.IsPaneOpen);
        if (show && !ReferenceEquals(_webRendered, vm.Markdown))
        {
            try
            {
                _webRendered = vm.Markdown;
                _webExpectNavigation = true;
                WebViewer.Navigate(MarkdownHtml.WritePage(vm.Markdown, vm.DocumentFolder, IsDark, Math.Round(WidthSlider.Value / 2) * 2));
            }
            catch (Exception)
            {
                // Sin navegador del sistema (p. ej. Linux sin WebKitGTK): los documentos con HTML van al visor nativo
                vm.WebViewerUnavailable = true;
                show = false;
            }
        }
        WebViewer.IsVisible = show;
    }

    // Enlaces del visor web: anclas en la misma página, otros .md en la app, el resto en la app del sistema
    async void OpenLink(Uri uri)
    {
        if (uri.Fragment.Length > 1 && (!uri.IsFile || Directory.Exists(uri.LocalPath)))
        {
            var id = System.Text.Json.JsonSerializer.Serialize(Uri.UnescapeDataString(uri.Fragment[1..]));
            await WebViewer.InvokeScript($"document.getElementById({id})?.scrollIntoView()");
        }
        else if (uri.IsFile && IOPath.GetExtension(uri.LocalPath).Equals(".md", StringComparison.OrdinalIgnoreCase))
            Vm.OpenExternal(uri.LocalPath);
        else
            await Launcher.LaunchUriAsync(uri);
    }

    WindowState _restoreState = WindowState.Maximized;

    /// <summary>Restaura la ventana si estaba minimizada (al estado que tenía) y la pone al frente.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Activate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && change.GetNewValue<WindowState>() is var state and not WindowState.Minimized)
            _restoreState = state;
    }

    void ApplyEditorTheme() =>
        _textMate?.SetTheme(_registry!.LoadTheme(ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus));

    void ToggleTheme(object? sender, RoutedEventArgs e) =>
        Application.Current!.RequestedThemeVariant =
            ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;

    void TogglePane(object? sender, RoutedEventArgs e) => Split.IsPaneOpen = !Split.IsPaneOpen;

    async void ShowAbout(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);

    async void AddFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Agregar carpetas", AllowMultiple = true });
        Vm.Add(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    async void AddFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Agregar archivos Markdown",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        });
        Vm.Add(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    // Arrastrar el borde derecho del explorador cambia su ancho
    void ResizePane(object? sender, VectorEventArgs e) =>
        Split.OpenPaneLength = Math.Clamp(Split.OpenPaneLength + e.Vector.X, 160, Math.Max(160, Bounds.Width - 300));

    // ---------- Paneles Archivos / Carpetas: alto con el separador y botón ▾ para plegarlos ----------

    const double PanelMinHeight = 90;
    GridLength _filesHeight = new(1, GridUnitType.Star), _foldersHeight = new(1, GridUnitType.Star);

    // Clic en cualquier parte del encabezado (flecha, título o espacio vacío), salvo en el botón +
    void FilesHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (!IsOnButton(e)) TogglePanel(FilesBody, FoldersBody);
    }

    void FoldersHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (!IsOnButton(e)) TogglePanel(FoldersBody, FilesBody);
    }

    static bool IsOnButton(RoutedEventArgs e) => (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null;

    // Siempre queda un panel abierto: minimizar uno con el otro ya minimizado abre el otro
    void TogglePanel(Control body, Control other)
    {
        // Recuerda el alto actual de los paneles abiertos (incluido lo ajustado con el separador)
        if (FilesBody.IsVisible) _filesHeight = ExplorerGrid.RowDefinitions[0].Height;
        if (FoldersBody.IsVisible) _foldersHeight = ExplorerGrid.RowDefinitions[2].Height;
        body.IsVisible = !body.IsVisible;
        if (!body.IsVisible) other.IsVisible = true;
        ApplyExplorerLayout();
    }

    // Panel abierto: ocupa su alto (proporcional). Minimizado: solo su encabezado, sin separador.
    void ApplyExplorerLayout()
    {
        var rows = ExplorerGrid.RowDefinitions;
        bool filesOpen = FilesBody.IsVisible, foldersOpen = FoldersBody.IsVisible;
        rows[0].Height = filesOpen ? _filesHeight : GridLength.Auto;
        rows[0].MinHeight = filesOpen ? PanelMinHeight : 0;
        rows[2].Height = foldersOpen ? _foldersHeight : GridLength.Auto;
        rows[2].MinHeight = foldersOpen ? PanelMinHeight : 0;
        ExplorerSplitter.IsVisible = filesOpen && foldersOpen;
        FilesToggle.Classes.Set("collapsed", !filesOpen);
        FoldersToggle.Classes.Set("collapsed", !foldersOpen);
    }

    // Clic en cualquier parte de la fila de una carpeta: la despliega o la pliega (no hace falta atinar a la flecha)
    void ExplorerRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FsNode { IsFolder: true } folder }) folder.IsExpanded = !folder.IsExpanded;
    }

    // En pantallas angostas el explorador se superpone y se cierra al abrir un archivo
    void ExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_narrow == true && sender is TreeView { SelectedItem: FsNode { IsFolder: false } })
            Split.IsPaneOpen = false;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = e.NewSize.Width < NarrowWidth;
        if (narrow == _narrow) return;
        _narrow = narrow;
        Split.DisplayMode = narrow ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        Split.IsPaneOpen = !narrow;
    }

    // Barra inferior: ancho máximo del texto en el visor (lo usa Styles/GithubMarkdown.axaml)
    void ViewerWidthChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyViewerWidth();
        if (WebViewer?.IsVisible == true) _ = WebViewer.InvokeScript($"setWidth({Math.Round(e.NewValue / 2) * 2})");
        else _webRendered = null; // se vuelve a generar con el ancho nuevo la próxima vez que se muestre
    }

    bool _multiClick;

    // Visor: doble clic selecciona la palabra y triple clic el párrafo (Markdown.Avalonia solo trae arrastrar).
    // Se trabaja sobre la selección de la librería, así Ctrl+C la copia igual.
    void SelectWordOrParagraph(object? sender, PointerPressedEventArgs e)
    {
        _multiClick = false;
        if (e.ClickCount < 2 || !e.GetCurrentPoint(Viewer).Properties.IsLeftButtonPressed) return;

        // Los bloques de texto no reciben el puntero (la librería lo maneja a nivel de documento),
        // así que se busca el bloque cuyos límites contienen el clic.
        var blocks = Viewer.GetVisualDescendants().OfType<CTextBlock>().ToList();
        var block = blocks.FirstOrDefault(b => !b.Classes.Contains("ListMarker") && new Rect(b.Bounds.Size).Contains(e.GetPosition(b)));
        if (block is null) return;

        foreach (var other in blocks) other.ClearSelection();
        var text = block.Text;
        if (e.ClickCount >= 3)
            block.Select(0, text.Length);
        else
        {
            // Índice del carácter bajo el puntero: se selecciona de ahí al final y se lee el inicio
            var point = e.GetPosition(block);
            block.Select(block.CalcuatePointerFrom(point.X, point.Y), block.GetEnd());
            var index = Math.Clamp(block.Selection?.From ?? 0, 0, text.Length);
            int start = index, end = index;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            while (end < text.Length && IsWordChar(text[end])) end++;
            if (end > start) block.Select(start, end);
            else block.ClearSelection();
        }
        _multiClick = true;
        e.Handled = true;
    }

    // Evita que la librería, al soltar el botón, reemplace la selección del doble/triple clic
    void KeepMultiClickSelection(object? sender, PointerReleasedEventArgs e)
    {
        if (!_multiClick) return;
        _multiClick = false;
        e.Handled = true;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // Ctrl+C en el visor. Markdown.Avalonia (CTextBlock.GetSelectedText) omite el código en línea y los
    // enlaces, así que el texto se arma aquí con las posiciones de la selección sobre el texto completo.
    async void CopyViewerSelection(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || Clipboard is null) return;
        var parts = new List<string>();
        foreach (var element in Viewer.GetVisualDescendants())
        {
            if (element is CTextBlock { Selection: { } sel } block && sel.To > sel.From && !block.Classes.Contains("ListMarker"))
                parts.Add(block.Text[sel.From..Math.Min(sel.To, block.Text.Length)]);
            else if (element is AvaloniaEdit.TextEditor { SelectionLength: > 0 } code)
                parts.Add(code.SelectedText);
        }
        if (parts.Count == 0) return;
        e.Handled = true;
        await Clipboard.SetTextAsync(string.Join(Environment.NewLine, parts));
    }

    // El ancho se asigna directo al panel del documento (una sola propiedad) para que arrastrar sea fluido.
    // Markdown.Avalonia recrea ese panel al cargar otro contenido, por eso se reaplica cuando cambia el Markdown.
    void ApplyViewerWidth()
    {
        if (Viewer is null || WidthSlider is null) return;
        var panel = Viewer.GetVisualDescendants().OfType<StackPanel>()
            .FirstOrDefault(p => p.Classes.Contains("Markdown_Avalonia_MarkdownViewer"));
        // Ancho par: al centrar, cada lado se mueve un píxel entero y el texto no "tiembla" por redondeo
        if (panel is not null) panel.MaxWidth = Math.Round(WidthSlider.Value / 2) * 2;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm) return;
        vm.ConfirmUnsaved = AskUnsavedAsync;
        vm.PropertyChanged += (_, p) =>
        {
            if (p.PropertyName is nameof(MainViewModel.ShowWebViewer) or nameof(MainViewModel.Markdown)) UpdateWebViewer();
        };
    }

    // Al cerrar con cambios pendientes, pregunta; nunca guarda por su cuenta
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !Vm.IsDirty) return;
        e.Cancel = true;
        if (await Vm.CanLeaveAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Diálogo: Guardar / No guardar / Cancelar. Cerrarlo o pulsar Esc equivale a Cancelar.</summary>
    async Task<UnsavedChoice> AskUnsavedAsync(string fileName)
    {
        var choice = UnsavedChoice.Cancel;
        var dialog = new Window
        {
            Title = "Cambios sin guardar",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = Icon,
        };
        Button Option(string text, UnsavedChoice value, bool primary = false)
        {
            var button = new Button { Content = text, MinWidth = 110, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            if (primary) button.Classes.Add("accent");
            button.Click += (_, _) => { choice = value; dialog.Close(); };
            return button;
        }
        dialog.KeyDown += (_, k) => { if (k.Key == Key.Escape) dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = $"«{fileName}» tiene cambios sin guardar.\nSi continúas sin guardar, los cambios se perderán.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 440,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        Option("Guardar", UnsavedChoice.Save, primary: true),
                        Option("No guardar", UnsavedChoice.Discard),
                        Option("Cancelar", UnsavedChoice.Cancel),
                    },
                },
            },
        };
        await dialog.ShowDialog(this);
        return choice;
    }
}
