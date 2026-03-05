using Chapi.Presentation.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Chapi.Presentation.Views.Tabs;

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
        _debounceToken?.Cancel();
        _debounceToken = new System.Threading.CancellationTokenSource();
        var token = _debounceToken.Token;
        try
        {
            await Task.Delay(1500, token);
            if (_viewModel != null)
                await _viewModel.RefreshPreviewAsync();
        }
        catch (TaskCanceledException) { }
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

    public void NavigatePreview()
    {
        if (!_isInitialized || _viewModel == null) return;
        try
        {
            PreviewWebView.NavigateToString(_viewModel.PreviewHtml);
        }
        catch { }
    }


}
