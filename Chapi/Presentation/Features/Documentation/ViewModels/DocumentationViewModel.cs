using Chapi.Application.Interfaces;
using Chapi.Application.UseCases.AI;
using Chapi.Application.UseCases.Documentation;
using Chapi.Domain.Documentation;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Chapi.Presentation.Shared.Tasks;
using Chapi.Presentation.Shared.Mvvm;

namespace Chapi.Presentation.Features.Documentation.ViewModels;

public class DocumentationViewModel : ViewModelBase
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
    private string _projectContextPath = string.Empty;
    private string _aiPrompt = string.Empty;
    private bool _generateAll;
    private bool _showTags = true;
    private bool _showKrokiCode = true;
    private readonly DispatcherTimer _autoSaveTimer;

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

        NewDocumentCommand = new AsyncRelayCommand(NewDocumentAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ExportWordCommand = new AsyncRelayCommand(ExportWordAsync);
        ExportMarkdownCommand = new AsyncRelayCommand(ExportMarkdownAsync);
        GenerateSectionCommand = new AsyncRelayCommand(GenerateSectionAsync, () => !IsGenerating && _selectedSection != null);
        GenerateAllSectionsCommand = new AsyncRelayCommand(GenerateAllSectionsAsync, () => !IsGenerating);
        RefreshPreviewCommand = new AsyncRelayCommand(RefreshPreviewAsync);
        ApplyTemplateCommand = new AsyncRelayCommand<DocTemplate>(ApplyTemplateAsync);
        SelectSectionCommand = new RelayCommand<DocSection?>(s => SelectedSection = s);
        RemoveSectionCommand = new RelayCommand<DocSection>(RemoveSection);
        ChangeDiagramFormatCommand = new RelayCommand<string>(ChangeDiagramFormat);

        // Timer de auto-guardado
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoSaveTimer.Tick += async (s, e) => await AutoSaveAsync();
        _autoSaveTimer.Start();

        // Carga plantilla inicial
        ApplyTemplateAsync(DocTemplate.ModeloSoftware).Forget("cargando plantilla inicial");
    }


    private async Task OpenSessionAsync(DocumentSession session)
    {
        if (session == null) return;

        try
        {
            _isLoading = true;
            StatusMessage = $"Cargando '{session.Title}'...";

            // Restaurar título de documento si se perdió en el guardado
            if (string.IsNullOrWhiteSpace(session.Title))
            {
                var (title, _) = _applyTemplate.Execute(session.Template);
                session.Title = title;
            }

            EnsureTemplateSections(session);

            // CRÍTICO: Reinstanciar Metadata para forzar PropertyChanged en los bindings WPF que referencian índices
            if (session.Metadata != null)
            {
                session.Metadata = new Dictionary<string, string>(session.Metadata);
                EnsureOptionalMetadataKeys(session.Metadata);
                NormalizeLoadedMetadata(session.Metadata);
            }
            else
            {
                session.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                EnsureOptionalMetadataKeys(session.Metadata);
            }

            // CRÍTIC: En WPF es necesario cambiar la referencia del puntero de ObservableCollection y DocumentSession
            _session = session;
            CurrentTemplate = session.Template; // Fundamental para que IsModeloSoftware/IsDisenoSistema actualicen la UI

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var newSections = new ObservableCollection<DocSection>();
                foreach (var section in session.Sections.OrderBy(s => s.Order))
                {
                    newSections.Add(section);
                }
                Sections = newSections;

                // Actualizar campo privado para evitar disparar el setter y RefreshPreview prematuro
                _selectedSection = Sections.FirstOrDefault();

                // Notificar cambios manualmente y de forma agresiva para refrescar bindings Session.Metadata[...]
                OnPropertyChanged(nameof(Session));
                OnPropertyChanged(nameof(Sections));
                OnPropertyChanged(nameof(SelectedSection));
                OnPropertyChanged(nameof(CurrentTemplate));
                OnPropertyChanged(nameof(IsModeloSoftware));
                OnPropertyChanged(nameof(IsDisenoSistema));
                OnPropertyChanged(nameof(IsTextSection));
                OnPropertyChanged(nameof(IsDiagramSection));
                OnPropertyChanged(nameof(IsImageSection));

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

    private ObservableCollection<DocSection> _sections = new();
    public ObservableCollection<DocSection> Sections
    {
        get => _sections;
        set
        {
            if (ReferenceEquals(_sections, value))
                return;

            if (_sections != null)
                _sections.CollectionChanged -= Sections_CollectionChanged;

            _sections = value;

            if (_sections != null)
                _sections.CollectionChanged += Sections_CollectionChanged;

            RebuildIndexSections();
            OnPropertyChanged();
        }
    }

    private ObservableCollection<DocumentationIndexItem> _indexSections = new();
    public ObservableCollection<DocumentationIndexItem> IndexSections
    {
        get => _indexSections;
        private set { _indexSections = value; OnPropertyChanged(); }
    }

    public DocSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndexSections));
            OnPropertyChanged(nameof(IsTextSection));
            OnPropertyChanged(nameof(IsDiagramSection));
            OnPropertyChanged(nameof(IsImageSection));
            GenerateSectionCommand.NotifyCanExecuteChanged();
            RefreshPreviewAsync().Forget("actualizando vista previa");
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
            GenerateSectionCommand.NotifyCanExecuteChanged();
            GenerateAllSectionsCommand.NotifyCanExecuteChanged();
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

    public bool ShowTags
    {
        get => _showTags;
        set { _showTags = value; OnPropertyChanged(); }
    }

    public bool ShowKrokiCode
    {
        get => _showKrokiCode;
        set
        {
            if (_showKrokiCode == value)
                return;

            _showKrokiCode = value;
            OnPropertyChanged();
            NotifyMetadataBindingsChanged();
        }
    }

    private DocTemplate _currentTemplate;
    public DocTemplate CurrentTemplate
    {
        get => _currentTemplate;
        private set
        {
            _currentTemplate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsModeloSoftware));
            OnPropertyChanged(nameof(IsDisenoSistema));
        }
    }

    public bool IsModeloSoftware => _currentTemplate == DocTemplate.ModeloSoftware;
    public bool IsDisenoSistema => _currentTemplate == DocTemplate.DisenoSistema;

    // Expuesto para que el code-behind pueda navegar el WebView
    internal IKrokiDiagramService KrokiService { get; }

    // ─── Commands ──────────────────────────────────────────────────────────────

    public IAsyncRelayCommand NewDocumentCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand ExportWordCommand { get; }
    public IAsyncRelayCommand ExportMarkdownCommand { get; }
    public IAsyncRelayCommand GenerateSectionCommand { get; }
    public IAsyncRelayCommand GenerateAllSectionsCommand { get; }
    public IAsyncRelayCommand RefreshPreviewCommand { get; }
    public IAsyncRelayCommand<DocTemplate> ApplyTemplateCommand { get; }
    public IRelayCommand<DocSection?> SelectSectionCommand { get; }
    public IRelayCommand<DocSection> RemoveSectionCommand { get; }
    public IRelayCommand<string> ChangeDiagramFormatCommand { get; }

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

            if (existing != null && existing.Sections.Count > 0)
            {
                await OpenSessionAsync(existing);
                return;
            }

            // Limpiar sesiones huérfanas de la antigua o fallida creación para no duplicar JSONs
            foreach (var s in sessions.Where(x => x.Template == template))
            {
                await _persistence.DeleteAsync(s.Id);
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
            Sections = new ObservableCollection<DocSection>();

            var (title, sections) = _applyTemplate.Execute(template);
            _session.Title = title;

            // Inicializar Metadata por defecto para el Header Premium
            _session.Metadata["PROYECTO_NOMBRE"] = projectName ?? "Nuevo Proyecto";
            _session.Metadata["PROYECTO_CODIGO"] = "PRJ-" + Guid.NewGuid().ToString().Substring(0, 4).ToUpper();
            _session.Metadata["DOC_VERSION"] = "1.0";
            _session.Metadata["DOC_MES_ANIO"] = DateTime.Now.ToString("MMMM, yyyy");
            _session.Metadata["ELAB_NOM"] = "Desarrollador Chapi";
            _session.Metadata["ELAB_FECHA"] = DateTime.Now.ToString("dd/MM/yyyy");
            _session.Metadata["REV_NOM"] = "";
            _session.Metadata["REV_FECHA"] = "";
            _session.Metadata["APROB_NOM"] = "";
            _session.Metadata["APROB_FECHA"] = "";
            _session.Metadata["REF_CODIGO"] = _session.Metadata["PROYECTO_CODIGO"];
            _session.Metadata["REF_SISTEMA"] = _session.Metadata["PROYECTO_NOMBRE"];
            _session.Metadata["REF_DOCS"] = "N/A";

            // ── Tags del Historial (comunes a ambas plantillas) ──────────────
            _session.Metadata["BLOQUE_HISTORIAL_INICIO"] = "[BLOQUE_HISTORIAL_INICIO]";
            _session.Metadata["HIST_FECHA_ELAB"]         = "[HIST_FECHA_ELAB]";
            _session.Metadata["HIST_VER"]                = "[HIST_VER]";
            _session.Metadata["HIST_ELAB"]               = "[HIST_ELAB]";
            _session.Metadata["HIST_DESC"]               = "[HIST_DESC]";
            _session.Metadata["HIST_REV"]                = "";
            _session.Metadata["HIST_FECHA_REV"]          = "";
            _session.Metadata["BLOQUE_HISTORIAL_FIN"]    = "[BLOQUE_HISTORIAL_FIN]";

            if (template == DocTemplate.ModeloSoftware)
            {
                // ── Secciones 1-3 ────────────────────────────────────────────
                _session.Metadata["INTRODUCCION"]        = "[INTRODUCCION]";
                _session.Metadata["OBJETIVOS"]           = "[OBJETIVOS]";
                _session.Metadata["ALCANCE"]             = "[ALCANCE]";
                // ── Sección 4: Paquetes ──────────────────────────────────────
                _session.Metadata["PQ_VISTA_LOGICA_DESC"] = "[PQ_VISTA_LOGICA_DESC]";
                _session.Metadata["TABLA_RESUMEN_PQ"]    = "[TABLA_RESUMEN_PQ]";
                _session.Metadata["IMG_VISTA_LOGICA"]    = "[IMG_VISTA_LOGICA]";
                _session.Metadata["BLOQUE_PQ_INICIO"]    = "[BLOQUE_PQ_INICIO]";
                _session.Metadata["PQ_ID_NOM"]           = "[PQ_ID_NOM]";
                _session.Metadata["PQ_DESC"]             = "[PQ_DESC]";
                _session.Metadata["PQ_CLASES_LISTA"]     = "[PQ_CLASES_LISTA]";
                _session.Metadata["BLOQUE_PQ_ITEMS"]     = "";
                _session.Metadata["BLOQUE_PQ_FIN"]       = "[BLOQUE_PQ_FIN]";
                // ── Sección 5: Actores ───────────────────────────────────────
                _session.Metadata["TABLA_ACTORES_LISTA"] = "[TABLA_ACTORES_LISTA]";
                _session.Metadata["IMG_ACTORES"]         = "[IMG_ACTORES]";
                // ── Sección 6: Casos de Uso ──────────────────────────────────
                _session.Metadata["TABLA_CU_LISTADO"]    = "[TABLA_CU_LISTADO]";
                _session.Metadata["IMG_CU_GENERAL"]      = "[IMG_CU_GENERAL]";
                _session.Metadata["BLOQUE_CU_INICIO"]    = "[BLOQUE_CU_INICIO]";
                _session.Metadata["CU_ID"]               = "[CU_ID]";
                _session.Metadata["CU_NOM"]              = "[CU_NOM]";
                _session.Metadata["CU_DESC"]             = "[CU_DESC]";
                _session.Metadata["CU_ACTORES"]          = "[CU_ACTORES]";
                _session.Metadata["CU_PRE"]              = "[CU_PRE]";
                _session.Metadata["CU_FLOW_BASE"]        = "[CU_FLOW_BASE]";
                _session.Metadata["CU_FLOW_ALT"]         = "[CU_FLOW_ALT]";
                _session.Metadata["CU_POST"]             = "[CU_POST]";
                _session.Metadata["CU_RESTRIC"]          = "[CU_RESTRIC]";
                _session.Metadata["CU_PADRE"]            = "[CU_PADRE]";
                _session.Metadata["IMG_PROTOTIPO"]       = "[IMG_PROTOTIPO]";
                _session.Metadata["BLOQUE_CU_ITEMS"]     = "";
                _session.Metadata["BLOQUE_CU_FIN"]       = "[BLOQUE_CU_FIN]";
                // ── Sección 7: Actividad ─────────────────────────────────────
                _session.Metadata["BLOQUE_ACT_INICIO"]   = "[BLOQUE_ACT_INICIO]";
                _session.Metadata["CU_ID_ACT"]           = "[CU_ID_ACT]";
                _session.Metadata["CU_NOM_ACT"]          = "[CU_NOM_ACT]";
                _session.Metadata["CU_DESC_ACT"]         = "";
                _session.Metadata["IMG_ACTIVIDAD"]       = "[IMG_ACTIVIDAD]";
                _session.Metadata["BLOQUE_ACT_ITEMS"]    = "";
                _session.Metadata["BLOQUE_ACT_FIN"]      = "[BLOQUE_ACT_FIN]";
                // ── Sección 8: Secuencia ─────────────────────────────────────
                _session.Metadata["BLOQUE_SEQ_INICIO"]   = "[BLOQUE_SEQ_INICIO]";
                _session.Metadata["CU_ID_SEQ"]           = "[CU_ID_SEQ]";
                _session.Metadata["CU_NOM_SEQ"]          = "[CU_NOM_SEQ]";
                _session.Metadata["CU_DESC_SEQ"]         = "";
                _session.Metadata["IMG_SECUENCIA"]       = "[IMG_SECUENCIA]";
                _session.Metadata["BLOQUE_SEQ_ITEMS"]    = "";
                _session.Metadata["BLOQUE_SEQ_FIN"]      = "[BLOQUE_SEQ_FIN]";
                // ── Sección 9: Estados ───────────────────────────────────────
                _session.Metadata["BLOQUE_EST_INICIO"]   = "[BLOQUE_EST_INICIO]";
                _session.Metadata["CU_ID_EST"]           = "[CU_ID_EST]";
                _session.Metadata["CU_NOM_EST"]          = "[CU_NOM_EST]";
                _session.Metadata["CU_DESC_EST"]         = "";
                _session.Metadata["IMG_ESTADO"]          = "[IMG_ESTADO]";
                _session.Metadata["BLOQUE_EST_ITEMS"]    = "";
                _session.Metadata["BLOQUE_EST_FIN"]      = "[BLOQUE_EST_FIN]";
            }
            else if (template == DocTemplate.DisenoSistema)
            {
                // ── Secciones 1-3 ────────────────────────────────────────────
                _session.Metadata["INTRODUCCION"]        = "[INTRODUCCION]";
                _session.Metadata["OBJETIVOS"]           = "[OBJETIVOS]";
                _session.Metadata["ALCANCE"]             = "[ALCANCE]";
                // ── Sección 4: Arquitectura ──────────────────────────────────
                _session.Metadata["ARQ_DESC_GENERAL"]    = "[ARQ_DESC_GENERAL]";
                _session.Metadata["IMG_ARQUITECTURA"]    = "[IMG_ARQUITECTURA]";
                _session.Metadata["BLOQUE_CAPAS_INICIO"] = "[BLOQUE_CAPAS_INICIO]";
                _session.Metadata["CAPA_NOM"]            = "[CAPA_NOM]";
                _session.Metadata["CAPA_DESC"]           = "[CAPA_DESC]";
                _session.Metadata["BLOQUE_CAPAS_ITEMS"]  = "";
                _session.Metadata["BLOQUE_CAPAS_FIN"]    = "[BLOQUE_CAPAS_FIN]";
                // ── Sección 5: Componentes ───────────────────────────────────
                _session.Metadata["IMG_COMPONENTES"]     = "[IMG_COMPONENTES]";
                _session.Metadata["BLOQUE_COMP_INICIO"]  = "[BLOQUE_COMP_INICIO]";
                _session.Metadata["COMP_NOM"]            = "[COMP_NOM]";
                _session.Metadata["COMP_DESC"]           = "[COMP_DESC]";
                _session.Metadata["BLOQUE_COMP_ITEMS"]   = "";
                _session.Metadata["BLOQUE_COMP_FIN"]     = "[BLOQUE_COMP_FIN]";
                // ── Sección 6: Clases ────────────────────────────────────────
                _session.Metadata["IMG_CLASES_SISTEMA"]  = "[IMG_CLASES_SISTEMA]";
                _session.Metadata["BLOQUE_CLASE_DET_INICIO"] = "[BLOQUE_CLASE_DET_INICIO]";
                _session.Metadata["CLASE_TITULO"]        = "[CLASE_TITULO]";
                _session.Metadata["CLASE_ATRIB"]         = "[CLASE_ATRIB]";
                _session.Metadata["CLASE_OPER"]          = "[CLASE_OPER]";
                _session.Metadata["CLASE_AGREG"]         = "[CLASE_AGREG]";
                _session.Metadata["CLASE_ASOC"]          = "[CLASE_ASOC]";
                _session.Metadata["BLOQUE_CLASE_DET_ITEMS"] = "";
                _session.Metadata["BLOQUE_CLASE_DET_FIN"] = "[BLOQUE_CLASE_DET_FIN]";
                // ── Sección 7: DER ───────────────────────────────────────────
                _session.Metadata["IMG_DER"]             = "[IMG_DER]";
                // ── Sección 8: Diccionario ───────────────────────────────────
                _session.Metadata["TABLA_DICC_RESUMEN"]  = "[TABLA_DICC_RESUMEN]";
                _session.Metadata["BLOQUE_DICC_TABLA_INICIO"] = "[BLOQUE_DICC_TABLA_INICIO]";
                _session.Metadata["DICC_TABLA_TITULO"]   = "[DICC_TABLA_TITULO]";
                _session.Metadata["BLOQUE_COL_INICIO"]   = "[BLOQUE_COL_INICIO]";
                _session.Metadata["COL_NOM"]             = "[COL_NOM]";
                _session.Metadata["COL_TIPO"]            = "[COL_TIPO]";
                _session.Metadata["COL_PK"]              = "[COL_PK]";
                _session.Metadata["COL_DESC"]            = "[COL_DESC]";
                _session.Metadata["BLOQUE_COL_FIN"]      = "[BLOQUE_COL_FIN]";
                _session.Metadata["BLOQUE_DICC_TABLA_FIN"] = "[BLOQUE_DICC_TABLA_FIN]";
                _session.Metadata["BLOQUE_DICC_TABLA_ITEMS"] = "";
                _session.Metadata["TABLA_OBJ_PQ"]        = "[TABLA_OBJ_PQ]";
                _session.Metadata["TABLA_OBJ_PROC"]      = "[TABLA_OBJ_PROC]";
                _session.Metadata["TABLA_OBJ_VISTAS"]    = "[TABLA_OBJ_VISTAS]";
                _session.Metadata["TABLA_OBJ_FUNC"]      = "[TABLA_OBJ_FUNC]";
                _session.Metadata["TABLA_OBJ_IDX"]       = "[TABLA_OBJ_IDX]";
            }
            var newSections = new ObservableCollection<DocSection>();
            int i = 1;
            foreach (var section in sections)
            {
                section.Order = i++;
                newSections.Add(section);
            }
            Sections = newSections;

            SyncSectionsToSession();
            _selectedSection = Sections.FirstOrDefault();

            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(IsTextSection));
            OnPropertyChanged(nameof(IsDiagramSection));
            OnPropertyChanged(nameof(IsImageSection));
            
            // Forzamos avisar que toda la sesión y el template cambió (refresco profundo WPF)
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(CurrentTemplate));
            OnPropertyChanged(nameof(Sections)); // ADD TO REPAINT THE LISTBOX

            RefreshPreviewAsync().Forget("actualizando vista previa");
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

    private async Task NewDocumentAsync()
    {
        try
        {
            var sessions = await _persistence.GetAllAsync(_session.ProjectName);
            foreach (var s in sessions.Where(x => x.Template == CurrentTemplate))
            {
                await _persistence.DeleteAsync(s.Id);
            }
        }
        catch { }

        ApplyTemplateInternal(CurrentTemplate);
        StatusMessage = "Nuevo documento creado.";
    }

    private async Task SaveAsync()
    {
        StatusMessage = "Guardando...";
        SyncSectionsToSession();
        var ok = await _persistence.SaveAsync(_session);
        StatusMessage = ok ? "Guardado correctamente." : "Error al guardar.";
    }

    private async Task AutoSaveAsync()
    {
        if (_isLoading || string.IsNullOrEmpty(_session?.ProjectName)) return;
        
        try
        {
            SyncSectionsToSession();
            await _persistence.SaveAsync(_session);
        }
        catch 
        {
            // Ignorar errores en auto-guardado en background
        }
    }

    private async Task ExportWordAsync()
    {
        StatusMessage = "Generando Word...";
        SyncSectionsToSession();
        await HydrateStructuredMetadataForExportAsync();
        var path = GetSaveFilePath("Word Document (*.docx)|*.docx", $"{_session.Title}.docx");
        if (path == null) return;
        var ok = await _exportDocument.ExportToWordAsync(_session, path);
        StatusMessage = ok ? $"Word exportado: {path}" : "Error al exportar.";
        if (ok) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private async Task ExportMarkdownAsync()
    {
        StatusMessage = "Exportando Markdown...";
        SyncSectionsToSession();
        var path = GetSaveFilePath("Markdown (*.md)|*.md", $"{_session.Title}.md");
        if (path == null) return;
        var ok = await _exportDocument.ExportToMarkdownAsync(_session, path);
        StatusMessage = ok ? $"Markdown exportado: {path}" : "Error al exportar.";
    }

    private async Task EnsureProjectContextAsync(string? statusMessage = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_session.ProjectPath))
            return;

        if (!string.IsNullOrWhiteSpace(_projectContext) &&
            string.Equals(_projectContextPath, _session.ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StatusMessage = statusMessage ?? "Analizando estructura del proyecto...";
        _projectContext = await _synthesizer.AnalyzeProjectContextAsync(_session.ProjectPath, cancellationToken);
        _projectContextPath = _session.ProjectPath;
    }

    private async Task GenerateSectionAsync()
    {
        if (_selectedSection == null || IsGenerating) return;
        IsGenerating = true;

        var selectedSection = _selectedSection;
        var instruction = _aiPrompt;
        AiPrompt = string.Empty;
        StatusMessage = $"Generando metadata para '{selectedSection.Title}'...";

        try
        {
            await EnsureProjectContextAsync($"Analizando proyecto para '{selectedSection.Title}'...");

            var forceRegenerate = !string.IsNullOrWhiteSpace(instruction);
            var scope = BuildGenerationScope(selectedSection, forceRegenerate);
            if (scope.Keys.Count == 0)
            {
                StatusMessage = $"'{selectedSection.Title}' no tiene etiquetas pendientes.";
                return;
            }

            var sectionPrompt = BuildGenerationPrompt(scope, instruction, isGenerateAll: false);
            StatusMessage = scope.Sections.Count > 1
                ? $"Generando metadata para '{selectedSection.Title}' y sus subitems..."
                : $"Generando metadata para '{selectedSection.Title}'...";

            var generatedData = await _synthesizer.GenerateMetadataAsync(scope.Keys, _projectContext, sectionPrompt);
            var updated = ApplyGeneratedMetadata(generatedData, scope.Keys);
            await _persistence.SaveAsync(_session);

            OnPropertyChanged(nameof(SelectedSection));
            await RefreshPreviewAsync();
            StatusMessage = updated > 0
                ? $"'{selectedSection.Title}' actualizado ({updated} etiquetas)."
                : $"La IA no devolvio datos validos para '{selectedSection.Title}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error IA: {ex.Message}";
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
            await EnsureProjectContextAsync("Analizando estructura del proyecto...");

            var forceRegenerate = !string.IsNullOrWhiteSpace(instruction);
            var scopes = BuildTopLevelGenerationScopes(forceRegenerate)
                .Where(scope => scope.Keys.Count > 0)
                .ToList();

            if (!scopes.Any())
            {
                StatusMessage = "No hay etiquetas pendientes por generar.";
                return;
            }

            var updated = 0;
            var totalKeys = scopes.Sum(scope => scope.Keys.Count);

            for (var i = 0; i < scopes.Count; i++)
            {
                var scope = scopes[i];
                StatusMessage = $"Generando bloque {i + 1}/{scopes.Count}: {scope.PrimarySection.Title}";

                var metadataPrompt = BuildGenerationPrompt(scope, instruction, isGenerateAll: true);
                var generatedData = await _synthesizer.GenerateMetadataAsync(scope.Keys, _projectContext, metadataPrompt);
                updated += ApplyGeneratedMetadata(generatedData, scope.Keys);
                await _persistence.SaveAsync(_session);
            }

            await _persistence.SaveAsync(_session);
            await RefreshPreviewAsync();

            StatusMessage = updated > 0
                ? $"Etiquetas generadas: {updated}/{totalKeys}."
                : "La IA no devolvio valores aplicables para las etiquetas solicitadas.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error IA: {ex.Message}";
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

    public void NotifyMetadataBindingsChanged()
    {
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(SelectedSection));
    }

    private async Task<string> BuildFullPreviewHtml()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(GetHtmlHeader(_session.Title, CurrentTemplate));
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
                        sb.Append("<p class='placeholder'>Diagrama pendiente de generacion...</p>");
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

    private sealed record PreviewTheme(
        string PageBackground,
        string PageForeground,
        string CoverBackground,
        string CoverTitle,
        string Subtitle,
        string Divider,
        string SectionBackground,
        string SectionBorder,
        string SectionShadow,
        string HeadingColor,
        string Accent,
        string MutedText,
        string DiagramEditorBackground,
        string DiagramLabel,
        string DiagramCode,
        string DiagramPreviewBackground,
        string DiagramPreviewBorder,
        string TableHeaderBackground,
        string TableHeaderForeground,
        string TableBorder,
        string TableRowAlt);

    private static PreviewTheme GetPreviewTheme(DocTemplate template) =>
        template switch
        {
            DocTemplate.DisenoSistema => new PreviewTheme(
                PageBackground: "#f3f1ec",
                PageForeground: "#2d261f",
                CoverBackground: "#f7f3eb",
                CoverTitle: "#3f3427",
                Subtitle: "#7a6a57",
                Divider: "#d7c8b4",
                SectionBackground: "#fffdf8",
                SectionBorder: "#e2d6c5",
                SectionShadow: "rgba(78, 61, 42, .08)",
                HeadingColor: "#493928",
                Accent: "#b8792f",
                MutedText: "#7d7368",
                DiagramEditorBackground: "#2b241d",
                DiagramLabel: "#d9b98c",
                DiagramCode: "#f6d365",
                DiagramPreviewBackground: "#fbf7f1",
                DiagramPreviewBorder: "#e2d6c5",
                TableHeaderBackground: "#b8792f",
                TableHeaderForeground: "#fffaf2",
                TableBorder: "#d9ccb9",
                TableRowAlt: "#f6efe4"),
            _ => new PreviewTheme(
                PageBackground: "#f4f6fb",
                PageForeground: "#1f2937",
                CoverBackground: "#ffffff",
                CoverTitle: "#1f2a44",
                Subtitle: "#62708a",
                Divider: "#d7ddea",
                SectionBackground: "#ffffff",
                SectionBorder: "#dbe3f0",
                SectionShadow: "rgba(37, 51, 77, .08)",
                HeadingColor: "#24324d",
                Accent: "#4f6fa3",
                MutedText: "#6c7a92",
                DiagramEditorBackground: "#1f2430",
                DiagramLabel: "#8fa1c2",
                DiagramCode: "#b7d77a",
                DiagramPreviewBackground: "#f8fafd",
                DiagramPreviewBorder: "#dbe3f0",
                TableHeaderBackground: "#4f6fa3",
                TableHeaderForeground: "#ffffff",
                TableBorder: "#d6deeb",
                TableRowAlt: "#f7f9fc")
        };

    private static string GetHtmlHeader(string title, DocTemplate template)
    {
        var theme = GetPreviewTheme(template);
        return $$$"""
        <!DOCTYPE html><html lang="es"><head><meta charset="UTF-8"/>
        <title>{{{System.Net.WebUtility.HtmlEncode(title)}}}</title>
        <style>
          :root {
            --page-bg: {{{theme.PageBackground}}};
            --page-fg: {{{theme.PageForeground}}};
            --cover-bg: {{{theme.CoverBackground}}};
            --cover-title: {{{theme.CoverTitle}}};
            --subtitle: {{{theme.Subtitle}}};
            --divider: {{{theme.Divider}}};
            --section-bg: {{{theme.SectionBackground}}};
            --section-border: {{{theme.SectionBorder}}};
            --section-shadow: {{{theme.SectionShadow}}};
            --heading: {{{theme.HeadingColor}}};
            --accent: {{{theme.Accent}}};
            --muted: {{{theme.MutedText}}};
            --diagram-editor-bg: {{{theme.DiagramEditorBackground}}};
            --diagram-label: {{{theme.DiagramLabel}}};
            --diagram-code: {{{theme.DiagramCode}}};
            --diagram-preview-bg: {{{theme.DiagramPreviewBackground}}};
            --diagram-preview-border: {{{theme.DiagramPreviewBorder}}};
            --table-head-bg: {{{theme.TableHeaderBackground}}};
            --table-head-fg: {{{theme.TableHeaderForeground}}};
            --table-border: {{{theme.TableBorder}}};
            --table-row-alt: {{{theme.TableRowAlt}}};
          }
          body { font-family: 'Segoe UI', Calibri, sans-serif; margin: 0; background: var(--page-bg); color: var(--page-fg); }
          .cover { text-align:center; padding: 40px 20px; background:var(--cover-bg); margin-bottom:20px; }
          .cover h1 { font-size: 2em; font-weight:900; color:var(--cover-title); letter-spacing:1px; }
          .subtitle { color:var(--subtitle); font-style:italic; }
          hr { border: 2px solid var(--divider); margin: 0 20px 20px; }
          .section { background:var(--section-bg); margin: 16px 20px; border-radius:8px; padding:20px 24px;
                      box-shadow:0 1px 4px var(--section-shadow); border:1px solid var(--section-border); }
          h2 { font-size:1.3em; color:var(--heading); border-left:4px solid var(--accent); padding-left:10px; }
          .content { line-height:1.7; }
          .placeholder { color:var(--muted); font-style:italic; padding:16px 0; }
          .diagram-editor { background:var(--diagram-editor-bg); border-radius:6px; padding:12px; margin-bottom:8px; }
          .diagram-label { color:var(--diagram-label); font-size:.75em; letter-spacing:1px; margin-bottom:6px; }
          pre.code { color:var(--diagram-code); font-size:.85em; margin:0; white-space:pre-wrap; }
          .diagram-preview { border:1px solid var(--diagram-preview-border); border-radius:6px; padding:12px;
                             background:var(--diagram-preview-bg); text-align:center; overflow:auto; }
          .diagram-preview svg { max-width:100%; height:auto; }
          .capture { max-width:100%; border-radius:6px; border:1px solid var(--table-border); }
          table { border-collapse:collapse; width:100%; font-size:.9em; }
          th, td { border:1px solid var(--table-border); padding:8px 12px; text-align:left; }
          th { background:var(--table-head-bg); color:var(--table-head-fg); }
          tr:nth-child(even) { background:var(--table-row-alt); }
        </style></head><body>
        """;
    }
    private static bool IsStructuralTag(string key) =>
        key.StartsWith("BLOQUE_", StringComparison.OrdinalIgnoreCase) &&
        (key.EndsWith("_INICIO", StringComparison.OrdinalIgnoreCase) ||
         key.EndsWith("_FIN", StringComparison.OrdinalIgnoreCase));

    private static bool IsAIGeneratableKey(string key)
    {
        return !key.Equals("REV_NOM", StringComparison.OrdinalIgnoreCase)
            && !key.Equals("REV_FECHA", StringComparison.OrdinalIgnoreCase)
            && !key.Equals("APROB_NOM", StringComparison.OrdinalIgnoreCase)
            && !key.Equals("APROB_FECHA", StringComparison.OrdinalIgnoreCase)
            && !key.Equals("HIST_REV", StringComparison.OrdinalIgnoreCase)
            && !key.Equals("HIST_FECHA_REV", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPendingTagValue(string value) =>
        string.IsNullOrWhiteSpace(value) || (value.StartsWith("[") && value.EndsWith("]"));

    private List<string> GetKeysForSection(DocSection section, bool forceRegenerate)
    {
        return GetKeysForSections([section], forceRegenerate);
    }

    private List<string> GetKeysForSections(IEnumerable<DocSection> sections, bool forceRegenerate)
    {
        var keyMap = GetSectionKeyMap(CurrentTemplate);
        return sections
            .SelectMany(section => keyMap.TryGetValue(section.Order, out var keys) ? keys : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => _session.Metadata.ContainsKey(k))
            .Where(IsAIGeneratableKey)
            .Where(k => forceRegenerate || IsPendingTagValue(_session.Metadata[k]))
            .ToList();
    }

    private List<GenerationScope> BuildTopLevelGenerationScopes(bool forceRegenerate)
    {
        var scopes = new List<GenerationScope>();
        var orderedSections = Sections.OrderBy(s => s.Order).ToList();

        foreach (var section in orderedSections)
        {
            if (TryParseSubSection(section.Title, out _, out _, out _))
                continue;

            scopes.Add(BuildGenerationScope(section, forceRegenerate));
        }

        return scopes;
    }

    private GenerationScope BuildGenerationScope(DocSection section, bool forceRegenerate)
    {
        var scopedSections = GetSectionsForGeneration(section);
        var keys = GetKeysForSections(scopedSections, forceRegenerate);
        return new GenerationScope(section, scopedSections, keys);
    }

    private List<DocSection> GetSectionsForGeneration(DocSection section)
    {
        var orderedSections = Sections.OrderBy(s => s.Order).ToList();
        var selected = orderedSections.FirstOrDefault(s => ReferenceEquals(s, section)) ?? orderedSections.FirstOrDefault(s => s.Order == section.Order);
        if (selected == null)
            return [section];

        if (TryParseSubSection(selected.Title, out _, out _, out _))
            return [selected];

        var topLevelCounter = 0;
        int? rootNumber = null;
        var scopedSections = new List<DocSection>();

        foreach (var current in orderedSections)
        {
            if (TryParseSubSection(current.Title, out var mainNumber, out _, out _))
            {
                if (rootNumber == mainNumber)
                    scopedSections.Add(current);

                continue;
            }

            topLevelCounter++;

            if (ReferenceEquals(current, selected))
            {
                rootNumber = topLevelCounter;
                scopedSections.Add(current);
                continue;
            }

            if (rootNumber.HasValue)
                break;
        }

        return scopedSections.Count > 0 ? scopedSections : [selected];
    }

    private static string BuildGenerationPrompt(GenerationScope scope, string instruction, bool isGenerateAll)
    {
        var sectionTitles = scope.Sections
            .Select(s => s.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scopeDescriptor = scope.Sections.Count > 1
            ? $"bloque raiz '{scope.PrimarySection.Title}' y sus subitems: {string.Join("; ", sectionTitles.Skip(1))}"
            : $"seccion '{scope.PrimarySection.Title}'";

        if (!string.IsNullOrWhiteSpace(instruction))
            return $"{instruction}{Environment.NewLine}{Environment.NewLine}Aplica esta instruccion solo al {scopeDescriptor}.";

        return isGenerateAll
            ? $"Genera los valores de metadata para el {scopeDescriptor}."
            : $"Genera los valores de metadata para el {scopeDescriptor}.";
    }

    private sealed record GenerationScope(
        DocSection PrimarySection,
        IReadOnlyList<DocSection> Sections,
        IReadOnlyList<string> Keys);

    private static Dictionary<int, string[]> GetSectionKeyMap(DocTemplate template) =>
        template switch
        {
            DocTemplate.ModeloSoftware => new Dictionary<int, string[]>
            {
                [1] = ["INTRODUCCION"],
                [2] = ["OBJETIVOS"],
                [3] = ["ALCANCE"],
                [4] = ["PQ_VISTA_LOGICA_DESC"],
                [5] = ["TABLA_RESUMEN_PQ"],
                [6] = ["IMG_VISTA_LOGICA", "PQ_VISTA_LOGICA_DESC", "TABLA_RESUMEN_PQ", "BLOQUE_PQ_ITEMS"],
                [7] = ["BLOQUE_PQ_ITEMS", "PQ_ID_NOM", "PQ_DESC", "PQ_CLASES_LISTA"],
                [8] = ["TABLA_ACTORES_LISTA", "IMG_ACTORES"],
                [9] = ["TABLA_ACTORES_LISTA"],
                [10] = ["IMG_ACTORES", "TABLA_ACTORES_LISTA"],
                [11] = ["TABLA_CU_LISTADO", "IMG_CU_GENERAL", "BLOQUE_CU_ITEMS"],
                [12] = ["TABLA_CU_LISTADO"],
                [13] = ["BLOQUE_CU_ITEMS", "CU_ID", "CU_NOM", "CU_DESC", "CU_ACTORES", "CU_PRE", "CU_FLOW_BASE", "CU_FLOW_ALT", "CU_POST", "CU_RESTRIC", "CU_PADRE", "IMG_PROTOTIPO"],
                [14] = ["BLOQUE_ACT_ITEMS", "CU_ID_ACT", "CU_NOM_ACT", "CU_DESC_ACT", "IMG_ACTIVIDAD"],
                [15] = ["BLOQUE_SEQ_ITEMS", "CU_ID_SEQ", "CU_NOM_SEQ", "CU_DESC_SEQ", "IMG_SECUENCIA"],
                [16] = ["BLOQUE_EST_ITEMS", "CU_ID_EST", "CU_NOM_EST", "CU_DESC_EST", "IMG_ESTADO"],
            },
            DocTemplate.DisenoSistema => new Dictionary<int, string[]>
            {
                [1] = ["INTRODUCCION"],
                [2] = ["OBJETIVOS"],
                [3] = ["ALCANCE"],
                [4] = ["ARQ_DESC_GENERAL", "IMG_ARQUITECTURA", "BLOQUE_CAPAS_ITEMS"],
                [5] = ["BLOQUE_CAPAS_ITEMS", "CAPA_NOM", "CAPA_DESC"],
                [6] = ["IMG_COMPONENTES", "BLOQUE_COMP_ITEMS"],
                [7] = ["IMG_COMPONENTES", "BLOQUE_COMP_ITEMS"],
                [8] = ["BLOQUE_COMP_ITEMS", "COMP_NOM", "COMP_DESC"],
                [9] = ["IMG_CLASES_SISTEMA", "BLOQUE_CLASE_DET_ITEMS"],
                [10] = ["IMG_CLASES_SISTEMA", "BLOQUE_CLASE_DET_ITEMS"],
                [11] = ["BLOQUE_CLASE_DET_ITEMS", "CLASE_TITULO", "CLASE_ATRIB", "CLASE_OPER", "CLASE_AGREG", "CLASE_ASOC"],
                [12] = ["IMG_DER"],
                [13] = ["TABLA_DICC_RESUMEN"],
                [14] = ["TABLA_DICC_RESUMEN"],
                [15] = ["BLOQUE_DICC_TABLA_ITEMS", "DICC_TABLA_TITULO", "COL_NOM", "COL_TIPO", "COL_PK", "COL_DESC"],
                [16] = ["TABLA_OBJ_PQ"],
                [17] = ["TABLA_OBJ_PROC"],
                [18] = ["TABLA_OBJ_VISTAS"],
                [19] = ["TABLA_OBJ_FUNC"],
                [20] = ["TABLA_OBJ_IDX"],
            },
            _ => new Dictionary<int, string[]>()
        };

    private int ApplyGeneratedMetadata(Dictionary<string, string> generatedData, IEnumerable<string> targetKeys)
    {
        if (generatedData.Count == 0) return 0;

        var updated = 0;
        var normalized = new Dictionary<string, string>(_session.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var key in targetKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var incoming = generatedData.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(incoming.Key) || string.IsNullOrWhiteSpace(incoming.Value))
                continue;

            var cleanValue = NormalizeGeneratedValue(key, incoming.Value);
            if (string.IsNullOrWhiteSpace(cleanValue))
                continue;

            normalized[key] = cleanValue;
            updated++;
        }

        var currentSession = Session;
        currentSession.Metadata = new Dictionary<string, string>(normalized, StringComparer.OrdinalIgnoreCase);
        Session = new DocumentSession { Id = "dummy" };
        Session = currentSession;

        return updated;
    }

    private static string NormalizeGeneratedValue(string key, string rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // Los bloques dinamicos (_ITEMS) se guardan como JSON para render dinamico.
        if (key.StartsWith("BLOQUE_", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("_ITEMS", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // Si la IA devuelve un arreglo JSON para campos narrativos, convertirlo a texto.
        if (TryParseJsonStringArray(value, out var items))
        {
            var cleanedItems = items
                .Select(item => Regex.Replace(item, "^\\s*(?:[-*•]|\\d+[\\.)])\\s*", string.Empty))
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();

            if (cleanedItems.Count == 0)
                return string.Empty;

            if (string.Equals(key, "OBJETIVOS", StringComparison.OrdinalIgnoreCase))
                return string.Join(Environment.NewLine + Environment.NewLine, cleanedItems.Select(item => $"- {item}"));

            return string.Join(Environment.NewLine + Environment.NewLine, cleanedItems);
        }

        return value;
    }

    private static bool TryParseJsonStringArray(string value, out List<string> items)
    {
        items = [];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.EndsWith("]", StringComparison.Ordinal))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        items.Add(text);
                }
            }

            return items.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void NormalizeLoadedMetadata(IDictionary<string, string> metadata)
    {
        var keys = metadata.Keys.ToList();
        foreach (var key in keys)
        {
            var cleaned = NormalizeGeneratedValue(key, metadata[key]);
            if (!string.IsNullOrWhiteSpace(cleaned))
                metadata[key] = cleaned;
        }
    }
    public async Task SetProjectContextAsync(string projectName, string projectPath)
    {
        if (_isLoading) return;
        _isLoading = true; // Activar flag para bloquear AutoGuardado durante la transición de contexto

        try
        {
            // Limpieza profunda preventiva
            _session = new DocumentSession
            {
                ProjectName = projectName,
                ProjectPath = projectPath
            };
            _projectContext = string.Empty;
            _projectContextPath = string.Empty;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Sections = new ObservableCollection<DocSection>();
                PreviewHtml = string.Empty;
            });

            var sessions = await _persistence.GetAllAsync(projectName);
            var latest = sessions.FirstOrDefault();
            if (latest != null && latest.Sections.Count > 0)
            {
                await OpenSessionAsync(latest);
                CurrentTemplate = latest.Template;
            }
            else
            {
                // Si está corrupto o no hay sesiones, arrancar limpio. OpenSessionAsync y ApplyTemplateAsync manejarán _isLoading
                _isLoading = false; 
                await ApplyTemplateAsync(DocTemplate.ModeloSoftware);
                return; // salir tempranamente ya que el ApplyTemplate se encarga del _isLoading
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al inicializar contexto: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SyncSectionsToSession() =>
        _session.Sections = Sections.ToList();

    private void Sections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildIndexSections();
    }

    private void RebuildIndexSections()
    {
        var indexItems = new ObservableCollection<DocumentationIndexItem>();
        var parentsByNumber = new Dictionary<int, DocumentationIndexItem>();
        var topLevelCounter = 0;

        foreach (var section in Sections.OrderBy(s => s.Order))
        {
            if (TryParseSubSection(section.Title, out var mainNumber, out var subsectionNumber, out var cleanTitle)
                && parentsByNumber.TryGetValue(mainNumber, out var parent))
            {
                parent.Children.Add(new DocumentationIndexItem
                {
                    Number = subsectionNumber,
                    Title = cleanTitle,
                    Section = section
                });

                continue;
            }

            topLevelCounter++;
            var item = new DocumentationIndexItem
            {
                Number = topLevelCounter.ToString(),
                Title = CleanSectionTitle(section.Title),
                Section = section
            };

            indexItems.Add(item);
            parentsByNumber[topLevelCounter] = item;
        }

        IndexSections = indexItems;
    }

    private static bool TryParseSubSection(string? title, out int mainNumber, out string subsectionNumber, out string cleanTitle)
    {
        mainNumber = 0;
        subsectionNumber = string.Empty;
        cleanTitle = CleanSectionTitle(title);

        if (string.IsNullOrWhiteSpace(title))
            return false;

        var match = Regex.Match(title, @"^\s*(\d+)\.(\d+)\s+(.*)$");
        if (!match.Success)
            return false;

        mainNumber = int.Parse(match.Groups[1].Value);
        subsectionNumber = $"{match.Groups[1].Value}.{match.Groups[2].Value}";
        cleanTitle = match.Groups[3].Value.Trim();
        return true;
    }

    private static string CleanSectionTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        return Regex.Replace(title, @"^\s*\d+(\.\d+)?\s+", string.Empty).Trim();
    }

    private async Task HydrateStructuredMetadataForExportAsync()
    {
        if (string.IsNullOrWhiteSpace(_session.Id))
            return;

        var persisted = await _persistence.LoadAsync(_session.Id);
        if (persisted?.Metadata == null || persisted.Metadata.Count == 0)
            return;

        var structuredKeys = new[]
        {
            "TABLA_RESUMEN_PQ",
            "TABLA_ACTORES_LISTA",
            "TABLA_DICC_RESUMEN",
            "TABLA_OBJ_PQ",
            "TABLA_OBJ_PROC",
            "TABLA_OBJ_VISTAS",
            "TABLA_OBJ_FUNC",
            "TABLA_OBJ_IDX",
            "BLOQUE_PQ_ITEMS",
            "BLOQUE_CU_ITEMS",
            "BLOQUE_ACT_ITEMS",
            "BLOQUE_SEQ_ITEMS",
            "BLOQUE_EST_ITEMS",
            "BLOQUE_CAPAS_ITEMS",
            "BLOQUE_COMP_ITEMS",
            "BLOQUE_CLASE_DET_ITEMS",
            "BLOQUE_DICC_TABLA_ITEMS"
        };

        var updated = 0;
        foreach (var key in structuredKeys)
        {
            _session.Metadata.TryGetValue(key, out var currentValue);
            if (!string.IsNullOrWhiteSpace(currentValue))
                continue;

            if (!persisted.Metadata.TryGetValue(key, out var persistedValue) || string.IsNullOrWhiteSpace(persistedValue))
                continue;

            _session.Metadata[key] = persistedValue;
            updated++;
        }

        if (updated > 0)
            NotifyMetadataBindingsChanged();
    }

    private static void EnsureOptionalMetadataKeys(IDictionary<string, string> metadata)
    {
        var optionalKeys = new[]
        {
            "BLOQUE_CU_ITEMS",
            "BLOQUE_PQ_ITEMS",
            "BLOQUE_ACT_ITEMS",
            "BLOQUE_SEQ_ITEMS",
            "BLOQUE_EST_ITEMS",
            "BLOQUE_CAPAS_ITEMS",
            "BLOQUE_COMP_ITEMS",
            "BLOQUE_CLASE_DET_ITEMS",
            "BLOQUE_DICC_TABLA_ITEMS",
            "CU_DESC_ACT",
            "CU_DESC_SEQ",
            "CU_DESC_EST"
        };

        foreach (var key in optionalKeys)
        {
            if (!metadata.ContainsKey(key))
                metadata[key] = string.Empty;
        }
    }

    private void EnsureTemplateSections(DocumentSession session)
    {
        var (_, templateSections) = _applyTemplate.Execute(session.Template);
        var canonical = templateSections.OrderBy(s => s.Order).ToList();
        if (canonical.Count == 0)
            return;

        var existingByOrder = session.Sections
            .GroupBy(s => s.Order)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var expected in canonical)
        {
            if (!existingByOrder.TryGetValue(expected.Order, out var current))
            {
                session.Sections.Add(new DocSection
                {
                    Order = expected.Order,
                    Title = expected.Title,
                    Type = expected.Type
                });
                continue;
            }

            current.Title = expected.Title;
            current.Type = expected.Type;
        }

        session.Sections = session.Sections
            .OrderBy(s => s.Order)
            .ToList();
    }

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
}

