using Chapi.Application.Interfaces;
using Chapi.Application.UseCases.AI;
using Chapi.Application.UseCases.Documentation;
using Chapi.Domain.Documentation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Chapi.Presentation.ViewModels;

public class DocumentationViewModel : INotifyPropertyChanged
{
    // ─── Dependencies ──────────────────────────────────────────────────────────

    private readonly ApplyTemplateUseCase _applyTemplate;
    private readonly GenerateDocumentSectionUseCase _generateSection;
    private readonly GenerateAllDocumentSectionsUseCase _generateAllSections;
    private readonly ExportDocumentUseCase _exportDocument;
    private readonly IDocumentPersistenceService _persistence;
    private readonly IDocSynthesizerService _synthesizer;

    // ─── State ─────────────────────────────────────────────────────────────────

    private DocumentSession _session = new();
    private DocSection? _selectedSection;
    private string _previewHtml = string.Empty;
    private bool _isGenerating;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _projectContext = string.Empty;
    private string _aiPrompt = string.Empty;
    private bool _generateAll;

    // ─── Constructor ───────────────────────────────────────────────────────────

    public DocumentationViewModel(
        ApplyTemplateUseCase applyTemplate,
        GenerateDocumentSectionUseCase generateSection,
        GenerateAllDocumentSectionsUseCase generateAllSections,
        ExportDocumentUseCase exportDocument,
        IDocumentPersistenceService persistence,
        IDocSynthesizerService synthesizer,
        IKrokiDiagramService krokiService)
    {
        _applyTemplate = applyTemplate;
        _generateSection = generateSection;
        _generateAllSections = generateAllSections;
        _exportDocument = exportDocument;
        _persistence = persistence;
        _synthesizer = synthesizer;
        KrokiService = krokiService;

        NewDocumentCommand = new AsyncRelayCommand(_ => NewDocumentAsync());
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync());
        ExportWordCommand = new AsyncRelayCommand(_ => ExportWordAsync());
        ExportMarkdownCommand = new AsyncRelayCommand(_ => ExportMarkdownAsync());
        GenerateSectionCommand = new AsyncRelayCommand(_ => GenerateSectionAsync(), _ => CanGenerate());
        GenerateAllSectionsCommand = new AsyncRelayCommand(_ => GenerateAllSectionsAsync(), _ => CanGenerate());
        RefreshPreviewCommand = new AsyncRelayCommand(_ => RefreshPreviewAsync());
        ApplyTemplateCommand = new AsyncRelayCommand<DocTemplate>(t => ApplyTemplateAsync(t));
        SelectSectionCommand = new RelayCommand<DocSection>(s => SelectedSection = s);
        RemoveSectionCommand = new RelayCommand<DocSection>(RemoveSection);
        ChangeDiagramFormatCommand = new RelayCommand<string>(ChangeDiagramFormat);

        // Carga plantilla inicial
        _ = ApplyTemplateAsync(DocTemplate.ModeloSoftware);
    }


    private async Task OpenSessionAsync(DocumentSession session)
    {
        if (session == null) return;

        try
        {
            _isLoading = true;
            StatusMessage = $"Cargando '{session.Title}'...";

            _session = session;
            CurrentTemplate = session.Template;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                Sections.Clear();
                foreach (var section in session.Sections.OrderBy(s => s.Order))
                {
                    Sections.Add(section);
                }

                // Actualizar campo privado para evitar disparar el setter y RefreshPreview prematuro
                _selectedSection = Sections.FirstOrDefault();

                // Notificar cambios manualmente
                OnPropertyChanged(nameof(SelectedSection));
                OnPropertyChanged(nameof(IsTextSection));
                OnPropertyChanged(nameof(IsDiagramSection));
                OnPropertyChanged(nameof(IsImageSection));
                OnPropertyChanged(nameof(Session));
                OnPropertyChanged(nameof(Sections));

                await RefreshPreviewAsync();
            });

            StatusMessage = $"✅ '{session.Title}' recuperado correctamente.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error al cargar sesión: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ─── Properties ────────────────────────────────────────────────────────────

    public DocumentSession Session
    {
        get => _session;
        private set { _session = value; OnPropertyChanged(); }
    }

    public ObservableCollection<DocSection> Sections { get; } = new();

    public DocSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTextSection));
            OnPropertyChanged(nameof(IsDiagramSection));
            OnPropertyChanged(nameof(IsImageSection));
            _ = RefreshPreviewAsync();
        }
    }

    public bool IsTextSection => _selectedSection?.Type is DocSectionType.Text or DocSectionType.Table;
    public bool IsDiagramSection => _selectedSection?.Type == DocSectionType.Diagram;
    public bool IsImageSection => _selectedSection?.Type == DocSectionType.Image;

    public string PreviewHtml
    {
        get => _previewHtml;
        set { _previewHtml = value; OnPropertyChanged(); }
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            _isGenerating = value;
            OnPropertyChanged();
            ((AsyncRelayCommand)GenerateSectionCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string AiPrompt
    {
        get => _aiPrompt;
        set { _aiPrompt = value; OnPropertyChanged(); }
    }

    public bool GenerateAll
    {
        get => _generateAll;
        set { _generateAll = value; OnPropertyChanged(); }
    }

    private DocTemplate _currentTemplate;
    public DocTemplate CurrentTemplate
    {
        get => _currentTemplate;
        private set { _currentTemplate = value; OnPropertyChanged(); }
    }

    // Expuesto para que el code-behind pueda navegar el WebView
    internal IKrokiDiagramService KrokiService { get; }

    // ─── Commands ──────────────────────────────────────────────────────────────

    public ICommand NewDocumentCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ExportWordCommand { get; }
    public ICommand ExportMarkdownCommand { get; }
    public ICommand GenerateSectionCommand { get; }
    public ICommand GenerateAllSectionsCommand { get; }
    public ICommand RefreshPreviewCommand { get; }
    public ICommand ApplyTemplateCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand RemoveSectionCommand { get; }
    public ICommand ChangeDiagramFormatCommand { get; }

    // ─── Command Implementations ───────────────────────────────────────────────

    public async Task ApplyTemplateAsync(DocTemplate template)
    {
        if (_isLoading) return;

        // 1. Evitar reset si ya estamos en esta plantilla y hay contenido
        if (_session.Template == template && Sections.Any()) return;

        // 2. Guardar progreso actual antes de cambiar
        if (Sections.Any())
        {
            await SaveAsync();
        }

        // 3. Buscar si existe una sesión guardada para ESTA plantilla en ESTE proyecto
        try
        {
            var sessions = await _persistence.GetAllAsync(_session.ProjectName);
            var existing = sessions.FirstOrDefault(s => s.Template == template);

            if (existing != null)
            {
                await OpenSessionAsync(existing);
                return;
            }
        }
        catch { /* Fallback a creación de nueva si falla la búsqueda */ }

        // 4. Si no existe, aplicar la plantilla base (Creación limpia)
        StatusMessage = $"🆕 Iniciando nueva sesión: {template}";
        ApplyTemplateInternal(template);
    }

    private void ApplyTemplateInternal(DocTemplate template)
    {
        _isLoading = true;
        try
        {
            // Preservar contexto del proyecto antes de crear nueva sesión
            var projectName = _session.ProjectName;
            var projectPath = _session.ProjectPath;

            _session = new DocumentSession
            {
                Id = Guid.NewGuid().ToString(), // Identidad única obligatoria
                ProjectName = projectName,
                ProjectPath = projectPath,
                Template = template
            };

            CurrentTemplate = template;
            Sections.Clear();

            var (title, sections) = _applyTemplate.Execute(template);
            _session.Title = title;

            int i = 1;
            foreach (var section in sections)
            {
                section.Order = i++;
                Sections.Add(section);
            }

            SyncSectionsToSession();
            _selectedSection = Sections.FirstOrDefault();

            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(IsTextSection));
            OnPropertyChanged(nameof(IsDiagramSection));
            OnPropertyChanged(nameof(IsImageSection));
            OnPropertyChanged(nameof(Session));

            _ = RefreshPreviewAsync();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RemoveSection(DocSection? section)
    {
        if (section == null) return;
        var idx = Sections.IndexOf(section);
        Sections.Remove(section);
        if (_selectedSection == section)
            SelectedSection = Sections.ElementAtOrDefault(Math.Max(0, idx - 1));
    }

    private void ChangeDiagramFormat(string? format)
    {
        if (_selectedSection?.Type != DocSectionType.Diagram || format == null) return;
        _selectedSection.DiagramFormat = format.ToLowerInvariant() == "mermaid"
            ? DiagramFormat.Mermaid
            : DiagramFormat.PlantUml;
        OnPropertyChanged(nameof(SelectedSection));
    }

    private Task NewDocumentAsync()
    {
        ApplyTemplateInternal(CurrentTemplate);
        StatusMessage = "Nuevo documento creado.";
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        StatusMessage = "Guardando...";
        SyncSectionsToSession();
        var ok = await _persistence.SaveAsync(_session);
        StatusMessage = ok ? "✅ Guardado correctamente." : "❌ Error al guardar.";
    }

    private async Task ExportWordAsync()
    {
        StatusMessage = "Generando Word...";
        SyncSectionsToSession();
        var path = GetSaveFilePath("Word Document (*.docx)|*.docx", $"{_session.Title}.docx");
        if (path == null) return;
        var ok = await _exportDocument.ExportToWordAsync(_session, path);
        StatusMessage = ok ? $"✅ Word exportado: {path}" : "❌ Error al exportar.";
        if (ok) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private async Task ExportMarkdownAsync()
    {
        StatusMessage = "Exportando Markdown...";
        SyncSectionsToSession();
        var path = GetSaveFilePath("Markdown (*.md)|*.md", $"{_session.Title}.md");
        if (path == null) return;
        var ok = await _exportDocument.ExportToMarkdownAsync(_session, path);
        StatusMessage = ok ? $"✅ Markdown exportado: {path}" : "❌ Error al exportar.";
    }

    private async Task GenerateSectionAsync()
    {
        if (_selectedSection == null || IsGenerating) return;
        IsGenerating = true;

        var instruction = _aiPrompt;
        AiPrompt = string.Empty;
        StatusMessage = $"🤖 Generando '{_selectedSection.Title}'...";

        try
        {
            if (string.IsNullOrEmpty(_projectContext) && !string.IsNullOrEmpty(_session.ProjectPath))
                _projectContext = await _synthesizer.AnalyzeProjectContextAsync(_session.ProjectPath);

            await _generateSection.ExecuteAsync(_selectedSection, instruction, _projectContext);
            OnPropertyChanged(nameof(SelectedSection));
            await RefreshPreviewAsync();
            StatusMessage = $"✅ '{_selectedSection.Title}' generado correctamente.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }
    private async Task GenerateAllSectionsAsync()
    {
        if (IsGenerating) return;
        IsGenerating = true;
        var instruction = _aiPrompt;
        AiPrompt = string.Empty;

        try
        {
            if (string.IsNullOrEmpty(_projectContext) && !string.IsNullOrEmpty(_session.ProjectPath))
                _projectContext = await _synthesizer.AnalyzeProjectContextAsync(_session.ProjectPath);

            await _generateAllSections.ExecuteAsync(Sections, instruction, _projectContext, (section, current, total) =>
            {
                SelectedSection = section;
                StatusMessage = $"🤖 Generando '{section.Title}' ({current}/{total})...";
            });

            StatusMessage = "✅ Documento completo generado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    public async Task RefreshPreviewAsync()
    {
        if (!_isLoading) SyncSectionsToSession();
        PreviewHtml = await BuildFullPreviewHtml();
    }

    // ─── Preview Builder ───────────────────────────────────────────────────────

    private async Task<string> BuildFullPreviewHtml()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(GetHtmlHeader(_session.Title));
        sb.Append($"<div class='cover'><h1>{System.Net.WebUtility.HtmlEncode(_session.Title)}</h1>");
        sb.Append($"<p class='subtitle'>Documentación Técnica de Ingeniería - Versión {_session.Version}</p></div>");
        sb.Append("<hr/>");

        int num = 1;
        foreach (var section in Sections.OrderBy(s => s.Order))
        {
            sb.Append($"<div class='section' id='sec-{num}'>");
            sb.Append($"<h2>{num}. {System.Net.WebUtility.HtmlEncode(section.Title)}</h2>");

            switch (section.Type)
            {
                case DocSectionType.Text:
                case DocSectionType.Table:
                    var md = string.IsNullOrWhiteSpace(section.Content)
                        ? "_Escribe el contenido aquí..._"
                        : section.Content;
                    sb.Append($"<div class='content'>{Markdig.Markdown.ToHtml(md)}</div>");
                    break;

                case DocSectionType.Diagram:
                    if (!string.IsNullOrWhiteSpace(section.DiagramCode))
                    {
                        var fmt = section.DiagramFormat == DiagramFormat.Mermaid ? "mermaid" : "plantuml";
                        var svg = await KrokiService.RenderToSvgAsync(section.DiagramCode, fmt);
                        sb.Append("<div class='diagram-editor'>");
                        sb.Append($"<div class='diagram-label'>EDITOR ({fmt.ToUpper()})</div>");
                        sb.Append($"<pre class='code'>{System.Net.WebUtility.HtmlEncode(section.DiagramCode)}</pre>");
                        sb.Append("</div>");
                        sb.Append($"<div class='diagram-preview'>{svg}</div>");
                    }
                    else
                    {
                        sb.Append("<p class='placeholder'>Diagrama pendiente de generación...</p>");
                    }
                    break;

                case DocSectionType.Image:
                    if (!string.IsNullOrWhiteSpace(section.ImageBase64))
                        sb.Append($"<img src='data:{section.ImageMimeType};base64,{section.ImageBase64}' class='capture'/>");
                    break;
            }

            sb.Append("</div>");
            num++;
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string GetHtmlHeader(string title) => $$$"""
        <!DOCTYPE html><html lang="es"><head><meta charset="UTF-8"/>
        <title>{{{System.Net.WebUtility.HtmlEncode(title)}}}</title>
        <style>
          body { font-family: 'Segoe UI', Calibri, sans-serif; margin: 0; background: #f4f4f4; color: #222; }
          .cover { text-align:center; padding: 40px 20px; background:#fff; margin-bottom:20px; }
          .cover h1 { font-size: 2em; font-weight:900; color:#1a1a2e; letter-spacing:1px; }
          .subtitle { color:#555; font-style:italic; }
          hr { border: 2px solid #e0e0e0; margin: 0 20px 20px; }
          .section { background:#fff; margin: 16px 20px; border-radius:8px; padding:20px 24px;
                      box-shadow:0 1px 4px rgba(0,0,0,.08); }
          h2 { font-size:1.3em; color:#1a1a2e; border-left:4px solid #5c6bc0; padding-left:10px; }
          .content { line-height:1.7; }
          .placeholder { color:#999; font-style:italic; padding:16px 0; }
          .diagram-editor { background:#1e1e2e; border-radius:6px; padding:12px; margin-bottom:8px; }
          .diagram-label { color:#7e8aba; font-size:.75em; letter-spacing:1px; margin-bottom:6px; }
          pre.code { color:#a6e22e; font-size:.85em; margin:0; white-space:pre-wrap; }
          .diagram-preview { border:1px solid #e0e0e0; border-radius:6px; padding:12px;
                             background:#fafafa; text-align:center; overflow:auto; }
          .diagram-preview svg { max-width:100%; height:auto; }
          .capture { max-width:100%; border-radius:6px; border:1px solid #ddd; }
          table { border-collapse:collapse; width:100%; font-size:.9em; }
          th, td { border:1px solid #ddd; padding:8px 12px; text-align:left; }
          th { background:#5c6bc0; color:#fff; }
          tr:nth-child(even) { background:#f8f8f8; }
        </style></head><body>
        """;

    // ─── Helpers ───────────────────────────────────────────────────────────────

    public async Task SetProjectContextAsync(string projectName, string projectPath)
    {
        if (_isLoading) return;

        // Limpieza profunda preventiva
        _session = new DocumentSession
        {
            ProjectName = projectName,
            ProjectPath = projectPath
        };
        _projectContext = string.Empty;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Sections.Clear();
            PreviewHtml = string.Empty;
        });

        try
        {
            var sessions = await _persistence.GetAllAsync(projectName);
            var latest = sessions.FirstOrDefault();
            if (latest != null)
            {
                await OpenSessionAsync(latest);
                // Asegurar que el CurrentTemplate se sincronice con la sesión cargada
                CurrentTemplate = latest.Template;
            }
            else
            {
                await ApplyTemplateAsync(DocTemplate.ModeloSoftware);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error al inicializar contexto: {ex.Message}";
        }
    }

    private void SyncSectionsToSession() =>
        _session.Sections = Sections.ToList();

    private bool CanGenerate() => !IsGenerating && _selectedSection != null;

    private static string? GetSaveFilePath(string filter, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultName,
            DefaultExt = System.IO.Path.GetExtension(defaultName)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    // ─── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
