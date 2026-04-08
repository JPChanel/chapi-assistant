using Chapi.Domain.Documentation;
using Chapi.Presentation.Features.Documentation.ViewModels;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Chapi.Presentation.Features.Documentation.Views;

public partial class DocumentationView : UserControl
{
    private DocumentationViewModel? _viewModel;
    private bool _isInitialized;
    private System.Threading.CancellationTokenSource? _debounceToken;

    public DocumentationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel = DataContext as DocumentationViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        // El WebView2 está colapsado por ahora — inicializar silenciosamente
        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
        }
        catch { /* WebView2 no disponible o no visible */ }
    }

    // Debounce para actualizar preview al escribir texto
    private async void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceToken?.Cancel();
        _debounceToken = new System.Threading.CancellationTokenSource();
        var token = _debounceToken.Token;
        try
        {
            await Task.Delay(800, token);
            if (_viewModel != null)
                await _viewModel.RefreshPreviewAsync();
        }
        catch (TaskCanceledException) { }
    }

    // Debounce para actualizar preview al editar diagramas
    private async void DiagramEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
            SerializeDynamicItemsForTextBox(textBox);
        }

        _debounceToken?.Cancel();
        _debounceToken = new System.Threading.CancellationTokenSource();
        var token = _debounceToken.Token;
        try
        {
            await Task.Delay(1500, token);
            if (_viewModel != null)
            {
                _viewModel.NotifyMetadataBindingsChanged();
                await _viewModel.RefreshPreviewAsync();
            }
        }
        catch (TaskCanceledException) { }
    }

    private void DynamicItemTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
        SerializeDynamicItemsForTextBox(textBox);
    }


    // Botón explícito "Iniciar Análisis AI" — solo se llama desde aquí
    private void AiGenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var prompt = AiPromptBox.Text?.Trim();

        _viewModel.AiPrompt = prompt ?? string.Empty;

        if (_viewModel.GenerateAll)
        {
            _viewModel.GenerateAllSectionsCommand.Execute(null);
        }
        else
        {
            if (string.IsNullOrEmpty(prompt) && _viewModel.SelectedSection == null) return;
            _viewModel.GenerateSectionCommand.Execute(null);
        }

        AiPromptBox.Clear();
    }

    // Enter en el input de la consola AI también dispara la generación
    private void AiPromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AiGenerateButton_Click(sender, e);
    }

    private async void DynamicItemTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        SerializeDynamicItemsForTextBox(textBox);
        await _viewModel.RefreshPreviewAsync();
    }

    private void IndexTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not DocumentationIndexItem indexItem)
            return;

        _viewModel ??= DataContext as DocumentationViewModel;
        if (_viewModel == null)
            return;

        _viewModel.SelectedSection = indexItem.Section;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ScrollToSection(indexItem.Section.Title)));
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => FlushPendingDocumentationEdits();

    private void ExportWordButton_Click(object sender, RoutedEventArgs e) => FlushPendingDocumentationEdits();

    private void FlushPendingDocumentationEdits()
    {
        _viewModel ??= DataContext as DocumentationViewModel;
        if (_viewModel?.Session?.Metadata == null)
            return;

        foreach (var textBox in FindVisualChildren<TextBox>(this))
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();

        foreach (var itemsControl in FindVisualChildren<ItemsControl>(this))
        {
            if (itemsControl.Tag is not string itemsKey || string.IsNullOrWhiteSpace(itemsKey))
                continue;

            SerializeItemsControl(itemsControl, itemsKey);
        }

        _viewModel.NotifyMetadataBindingsChanged();
    }

    private void SerializeDynamicItemsForTextBox(TextBox textBox)
    {
        var itemsControl = FindAncestor<ItemsControl>(textBox);
        if (itemsControl?.Tag is not string itemsKey || string.IsNullOrWhiteSpace(itemsKey))
            return;

        SerializeItemsControl(itemsControl, itemsKey);
    }

    private void SerializeItemsControl(ItemsControl itemsControl, string itemsKey)
    {
        _viewModel ??= DataContext as DocumentationViewModel;
        if (_viewModel?.Session?.Metadata == null)
            return;

        var rows = new List<Dictionary<string, string>>();
        foreach (var item in itemsControl.Items)
        {
            if (item is not IDictionary<string, string> row)
                continue;

            rows.Add(row.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase));
        }

        _viewModel.Session.Metadata[itemsKey] = JsonSerializer.Serialize(rows);
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T typed)
                return typed;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    public void NavigatePreview()
    {
        if (!_isInitialized || _viewModel == null) return;
        try
        {
            PreviewWebView.NavigateToString(_viewModel.PreviewHtml);
        }
        catch { }
    }

    private void ScrollToSection(string? sectionTitle)
    {
        if (string.IsNullOrWhiteSpace(sectionTitle))
            return;

        var match = FindVisualChildren<TextBlock>(DocumentContentRoot)
            .Select(textBlock => new
            {
                Element = textBlock,
                Score = GetSectionMatchScore(sectionTitle, textBlock.Text)
            })
            .Where(x => x.Score >= 0 && x.Element.IsVisible)
            .OrderBy(x => x.Score)
            .ThenBy(x => GetVerticalPosition(x.Element))
            .FirstOrDefault();

        if (match?.Element == null)
            return;

        var targetPoint = match.Element.TransformToAncestor(DocumentContentRoot).Transform(new Point(0, 0));
        DocumentScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetPoint.Y - 20));
    }

    private static double GetVerticalPosition(FrameworkElement element)
    {
        try
        {
            return element.TransformToAncestor(System.Windows.Application.Current.MainWindow).Transform(new Point(0, 0)).Y;
        }
        catch
        {
            return double.MaxValue;
        }
    }

    private static int GetSectionMatchScore(string targetTitle, string? candidateText)
    {
        var target = NormalizeSectionKey(targetTitle);
        var candidate = NormalizeSectionKey(candidateText);

        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(candidate))
            return -1;

        if (candidate == target)
            return 0;

        if (candidate.Contains(target, StringComparison.Ordinal))
            return 1;

        if (target.Contains(candidate, StringComparison.Ordinal))
            return 2;

        return -1;
    }

    private static string NormalizeSectionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var lastWasSpace = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '.' or ':' or '/' or '-' or '_')
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }
        }

        return builder.ToString().Trim();
    }


}
