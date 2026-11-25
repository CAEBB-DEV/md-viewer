using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MdViewer.ViewModels;
using MdViewer.Views;

namespace MdViewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            // Archivo pasado al abrir (Explorador de Windows / "Abrir con" / línea de comandos): se carga después de
            // que la ventana aparece, para no retrasar el arranque si el documento es pesado.
            if (desktop.Args is [var file, ..])
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() => vm.OpenExternal(file), DispatcherPriority.Background);

            // Archivos abiertos mientras la app ya está abierta (los envía la nueva instancia antes de cerrarse)
            SingleInstance.Listen(path => Dispatcher.UIThread.Post(() =>
            {
                if (path is not null) vm.OpenExternal(path);
                window.BringToFront();
            }));

            // macOS no pasa el archivo como argumento: Finder lo entrega con un evento de activación
            // (al abrir la app con un documento o con la app ya abierta)
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e is not FileActivatedEventArgs files) return;
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in files.Files)
                            if (item.TryGetLocalPath() is { } path) vm.OpenExternal(path);
                        window.BringToFront();
                    }, DispatcherPriority.Background);
                };
        }

        base.OnFrameworkInitializationCompleted();
    }
}