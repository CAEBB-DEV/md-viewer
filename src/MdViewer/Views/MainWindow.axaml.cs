using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
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

    // Resaltado del editor: las gramáticas se cargan en segundo plano al mostrar la ventana y se instalan en el
    // editor la primera vez que se edita (hacerlo en el hilo de la interfaz retrasaba el arranque)
    Task<RegistryOptions>? _registryTask;
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
        // Valor local: los estilos de la librería ganan al de App.axaml (mano en todos los botones)
        button.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(button, "Copiar");
    });

    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) => _registryTask ??= Task.Run(() => new RegistryOptions(ThemeName.LightPlus));
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        ApplyExplorerLayout();
        LayoutUpdated += (_, _) => UpdateFilesMinHeight(); // al abrir/cerrar archivos y al cambiar el tamaño
        ShowWidth();
        // Ctrl+O / Ctrl+N: lo mismo que los botones Abrir y Crear de Archivos (en código: un binding desde
        // Window.KeyBindings no llega a la ventana)
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
            Command = new RelayCommand(() => AddFile(null, new RoutedEventArgs())),
        });
        KeyBindings.Add(new KeyBinding // Ctrl+N: crear archivo nuevo
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Control),
            Command = new RelayCommand(() => CreateFile(null, new RoutedEventArgs())),
        });
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

    // Se crea con el primer documento con HTML: iniciar el navegador del sistema es lo más lento del arranque
    // (en Linux carga GTK y WebKitGTK y lanza sus procesos), y la mayoría de los documentos no lo necesitan.
    NativeWebView? _webViewer;
    NativeWebView WebViewer => _webViewer ??= CreateWebViewer();

    void SetupWebViewer()
    {
        Split.PropertyChanged += (_, e) =>
        {
            if (e.Property == SplitView.IsPaneOpenProperty || e.Property == SplitView.DisplayModeProperty) UpdateWebViewer();
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            if (_webViewer?.IsVisible == true) _ = _webViewer.InvokeScript($"setTheme({(IsDark ? "true" : "false")})");
            else _webRendered = null; // se vuelve a generar con el tema nuevo la próxima vez que se muestre
        };
    }

    NativeWebView CreateWebViewer()
    {
        var web = new NativeWebView { IsVisible = false };
        web.Bind(NativeWebView.BackgroundProperty, this.GetObservable(BackgroundProperty));
        // Datos del navegador en la carpeta del usuario (Program Files no permite escribir)
        web.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = false;
            if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                windows.UserDataFolder = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer", "WebView2");
        };
        web.NavigationStarted += (_, e) =>
        {
            if (_webExpectNavigation) { _webExpectNavigation = false; return; }
            e.Cancel = true;
            if (e.Request is { } uri) OpenLink(uri);
        };
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { } uri) OpenLink(uri);
        };
        WebHost.Children.Add(web);
        return web;
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
        if (_webViewer is not null) _webViewer.IsVisible = show;
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
        Vm.AddFolders(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    // Crear: documento nuevo sin título, directo en el editor (la ubicación se pide al guardarlo)
    void CreateFile(object? sender, RoutedEventArgs e)
    {
        Vm.NewDocument();
        Dispatcher.UIThread.Post(() => Editor.Focus(), DispatcherPriority.Loaded);
    }

    // Guardar un documento nuevo: el diálogo de guardar elige carpeta y nombre (.md)
    async Task<string?> AskSavePathAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar archivo Markdown",
            SuggestedFileName = suggestedName,
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    async void AddFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir archivos Markdown",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        });
        Vm.OpenFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    // Arrastrar el borde derecho del explorador cambia su ancho
    void ResizePane(object? sender, VectorEventArgs e) =>
        Split.OpenPaneLength = Math.Clamp(Split.OpenPaneLength + e.Vector.X, 160, Math.Max(160, Bounds.Width - 300));

    // ---------- Paneles Archivos / Carpetas: alto con el separador y botón ▾ para plegar Carpetas ----------
    // Archivos no se pliega y nunca baja de la mitad del alto; Carpetas se puede plegar hasta su encabezado.

    const double PanelMinHeight = 90;
    GridLength _filesHeight = new(1, GridUnitType.Star), _foldersHeight = new(1, GridUnitType.Star);

    // Clic en cualquier parte del encabezado (flecha, título o espacio vacío), salvo en el botón +
    void FoldersHeaderTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        // Recuerda el alto actual de los paneles (incluido lo ajustado con el separador)
        if (FoldersBody.IsVisible)
        {
            _filesHeight = ExplorerGrid.RowDefinitions[0].Height;
            _foldersHeight = ExplorerGrid.RowDefinitions[2].Height;
        }
        FoldersBody.IsVisible = !FoldersBody.IsVisible;
        ApplyExplorerLayout();
    }

    // Carpetas abierto: cada panel ocupa su alto (proporcional). Plegado: solo su encabezado, sin separador.
    void ApplyExplorerLayout()
    {
        var rows = ExplorerGrid.RowDefinitions;
        var foldersOpen = FoldersBody.IsVisible;
        rows[0].Height = foldersOpen ? _filesHeight : new GridLength(1, GridUnitType.Star);
        rows[2].Height = foldersOpen ? _foldersHeight : GridLength.Auto;
        rows[2].MinHeight = foldersOpen ? PanelMinHeight : 0;
        ExplorerSplitter.IsVisible = foldersOpen;
        FoldersToggle.Classes.Set("collapsed", !foldersOpen);
    }

    // Archivos nunca tapa un archivo abierto: su alto mínimo es el que necesita para mostrarlos todos (crece al abrir
    // más y el separador no lo deja bajar de ahí). Tope: Carpetas conserva su mínimo; si no caben, Archivos se desplaza.
    void UpdateFilesMinHeight()
    {
        // Alto de las filas (no el del área desplazable, que se estira hasta llenar el panel)
        var rows = FilesTree.GetRealizedContainers().ToList();
        var content = FilesHint.IsVisible
            ? FilesHint.Bounds.Height + FilesHint.Margin.Top + FilesHint.Margin.Bottom
            : rows.Count == 0 ? 0 : rows.Average(r => r.Bounds.Height) * FilesTree.ItemCount + FilesTree.Padding.Top + FilesTree.Padding.Bottom;
        var needed = FilesHeader.Bounds.Bottom + FilesHeader.Margin.Bottom + content + 2; // + borde del panel
        var max = ExplorerGrid.Bounds.Height - ExplorerGrid.RowDefinitions[1].ActualHeight
                  - (FoldersBody.IsVisible ? PanelMinHeight : FoldersBody.FindAncestorOfType<Border>()?.Bounds.Height ?? 0);
        var min = Math.Round(Math.Max(0, Math.Min(needed, max)));
        var row = ExplorerGrid.RowDefinitions[0];
        if (Math.Abs(row.MinHeight - min) > 0.5) row.MinHeight = min;
    }

    // Clic en cualquier parte de la fila de una carpeta: la despliega o la pliega (no hace falta atinar a la flecha)
    void ExplorerRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FsNode { IsFolder: true } folder }) folder.IsExpanded = !folder.IsExpanded;
    }

    // Doble clic en un archivo de Carpetas: deja de ser vista previa y pasa a Archivos.
    // En una carpeta, el primer clic ya la desplegó; se marca como atendido para que el TreeViewItem no la vuelva
    // a plegar (él también alterna con doble clic) y dos clics rápidos no la dejen como estaba.
    void ExplorerRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: FsNode node }) return;
        if (node.IsFolder) e.Handled = true;
        else Vm.KeepOpen(node);
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
        ShowWidth();
        ApplyViewerWidth();
        if (_webViewer?.IsVisible == true) _ = _webViewer.InvokeScript($"setWidth({Math.Round(e.NewValue / 2) * 2})");
        else _webRendered = null; // se vuelve a generar con el ancho nuevo la próxima vez que se muestre
    }

    // Cuadro de texto del ancho: muestra el valor del slider; lo escrito se aplica con Enter (dentro de los límites)
    void ShowWidth()
    {
        if (WidthBox is not null && WidthSlider is not null) WidthBox.Text = Math.Round(WidthSlider.Value).ToString();
    }

    void WidthBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(WidthBox.Text?.Trim(), out var width))
                WidthSlider.Value = Math.Clamp(width, WidthSlider.Minimum, WidthSlider.Maximum);
            ShowWidth(); // también corrige un valor fuera de rango o no numérico
            WidthBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ShowWidth();
            e.Handled = true;
        }
    }

    // Salir del cuadro sin Enter descarta lo escrito
    void WidthBoxLostFocus(object? sender, FocusChangedEventArgs e) => ShowWidth();

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
        vm.AskSavePath = AskSavePathAsync;
        vm.PropertyChanged += (_, p) =>
        {
            if (p.PropertyName is nameof(MainViewModel.ShowWebViewer) or nameof(MainViewModel.Markdown)) UpdateWebViewer();
            if (p.PropertyName == nameof(MainViewModel.IsEditing) && vm.IsEditing) InstallEditorHighlighting();
        };
    }

    // Resaltado de sintaxis Markdown en el editor, con colores según el tema
    async void InstallEditorHighlighting()
    {
        if (_textMate is not null) return;
        var registry = await (_registryTask ??= Task.Run(() => new RegistryOptions(ThemeName.LightPlus)));
        if (_textMate is not null) return; // se instaló mientras se esperaba
        _registry = registry;
        _textMate = Editor.InstallTextMate(registry);
        _textMate.SetGrammar(registry.GetScopeByLanguageId("markdown"));
        ApplyEditorTheme();
    }

    // Al cerrar con cambios pendientes, pregunta; nunca guarda por su cuenta
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !Vm.HasUnsaved) return;
        e.Cancel = true;
        if (await Vm.ConfirmCloseAllAsync())
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
