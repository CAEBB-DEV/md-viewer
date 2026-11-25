using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MdViewer.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var assembly = typeof(AboutWindow).Assembly;
        VersionText.Text = assembly.GetName().Version?.ToString(2) ?? "—";
        var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value;
        DateText.Text = DateTime.TryParse(buildDate, CultureInfo.InvariantCulture, out var date)
            ? date.ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es"))
            : "—";
        TechText.Text = $".NET {Environment.Version.ToString(3)} · Avalonia {typeof(Window).Assembly.GetName().Version?.ToString(3)}\n" +
                        "Markdown.Avalonia · AvaloniaEdit · CommunityToolkit.Mvvm";
    }

    void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
