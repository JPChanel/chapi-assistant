using Chapi.Application.Interfaces;
using Chapi.Application.UseCases.AI;
using Chapi.Application.UseCases.Documentation;
using Chapi.Domain.Documentation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

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
    private bool _showTags = true;
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

        NewDocumentCommand = new AsyncRelayCommand(_ => NewDocumentAsync());
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync());
        ExportWordCommand = new AsyncRelayCommand(_ => ExportWordAsync());
        ExportMarkdownCommand = new AsyncRelayCommand(_ => ExportMarkdownAsync());
        GenerateSectionCommand = new AsyncRelayCommand(_ => GenerateSectionAsync(), _ => !IsGenerating && _selectedSection != null);
        GenerateAllSectionsCommand = new AsyncRelayCommand(_ => GenerateAllSectionsAsync(), _ => !IsGenerating);
        RefreshPreviewCommand = new AsyncRelayCommand(_ => RefreshPreviewAsync());
        ApplyTemplateCommand = new AsyncRelayCommand<DocTemplate>(t => ApplyTemplateAsync(t));
        SelectSectionCommand = new RelayCommand<DocSection>(s => SelectedSection = s);
        RemoveSectionCommand = new RelayCommand<DocSection>(RemoveSection);
        ChangeDiagramFormatCommand = new RelayCommand<string>(ChangeDiagramFormat);

        // Timer de auto-guardado
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoSaveTimer.Tick += async (s, e) => await AutoSaveAsync();
        _autoSaveTimer.Start();

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

            // Restaurar título de documento si se perdió en el guardado
            if (string.IsNullOrWhiteSpace(session.Title))
            {
                var (title, _) = _applyTemplate.Execute(session.Template);
                session.Title = title;
            }

            // CRÍTICO: Reinstanciar Metadata para forzar PropertyChanged en los bindings WPF que referencian índices
            if (session.Metadata != null)
            {
                session.Metadata = new Dictionary<string, string>(session.Metadata);
            }

            // CRÍTIC: En WPF es necesario cambiar la referencia del puntero de ObservableCollection y DocumentSession
            _session = session;
            CurrentTemplate = session.Template; // Fundamental para que IsModeloSoftware/IsDisenoSistema actualicen la UI

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                Sections.Clear();
                foreach (var section in session.Sections.OrderBy(s => s.Order))
                {
                    Sections.Add(section);
                }

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

    public bool ShowTags
    {
        get => _showTags;
        set { _showTags = value; OnPropertyChanged(); }
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
            Sections.Clear();

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
            _session.Metadata["HIST_REV"]                = "[HIST_REV]";
            _session.Metadata["HIST_FECHA_REV"]          = "[HIST_FECHA_REV]";
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
                _session.Metadata["BLOQUE_CU_FIN"]       = "[BLOQUE_CU_FIN]";
                // ── Sección 7: Actividad ─────────────────────────────────────
                _session.Metadata["BLOQUE_ACT_INICIO"]   = "[BLOQUE_ACT_INICIO]";
                _session.Metadata["CU_ID_ACT"]           = "[CU_ID_ACT]";
                _session.Metadata["CU_NOM_ACT"]          = "[CU_NOM_ACT]";
                _session.Metadata["IMG_ACTIVIDAD"]       = "[IMG_ACTIVIDAD]";
                _session.Metadata["BLOQUE_ACT_FIN"]      = "[BLOQUE_ACT_FIN]";
                // ── Sección 8: Secuencia ─────────────────────────────────────
                _session.Metadata["BLOQUE_SEQ_INICIO"]   = "[BLOQUE_SEQ_INICIO]";
                _session.Metadata["CU_ID_SEQ"]           = "[CU_ID_SEQ]";
                _session.Metadata["CU_NOM_SEQ"]          = "[CU_NOM_SEQ]";
                _session.Metadata["IMG_SECUENCIA"]       = "[IMG_SECUENCIA]";
                _session.Metadata["BLOQUE_SEQ_FIN"]      = "[BLOQUE_SEQ_FIN]";
                // ── Sección 9: Estados ───────────────────────────────────────
                _session.Metadata["BLOQUE_EST_INICIO"]   = "[BLOQUE_EST_INICIO]";
                _session.Metadata["CU_ID_EST"]           = "[CU_ID_EST]";
                _session.Metadata["CU_NOM_EST"]          = "[CU_NOM_EST]";
                _session.Metadata["IMG_ESTADO"]          = "[IMG_ESTADO]";
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
                _session.Metadata["BLOQUE_CAPAS_FIN"]    = "[BLOQUE_CAPAS_FIN]";
                // ── Sección 5: Componentes ───────────────────────────────────
                _session.Metadata["IMG_COMPONENTES"]     = "[IMG_COMPONENTES]";
                _session.Metadata["BLOQUE_COMP_INICIO"]  = "[BLOQUE_COMP_INICIO]";
                _session.Metadata["COMP_NOM"]            = "[COMP_NOM]";
                _session.Metadata["COMP_DESC"]           = "[COMP_DESC]";
                _session.Metadata["BLOQUE_COMP_FIN"]     = "[BLOQUE_COMP_FIN]";
                // ── Sección 6: Clases ────────────────────────────────────────
                _session.Metadata["IMG_CLASES_SISTEMA"]  = "[IMG_CLASES_SISTEMA]";
                _session.Metadata["BLOQUE_CLASE_DET_INICIO"] = "[BLOQUE_CLASE_DET_INICIO]";
                _session.Metadata["CLASE_TITULO"]        = "[CLASE_TITULO]";
                _session.Metadata["CLASE_ATRIB"]         = "[CLASE_ATRIB]";
                _session.Metadata["CLASE_OPER"]          = "[CLASE_OPER]";
                _session.Metadata["CLASE_AGREG"]         = "[CLASE_AGREG]";
                _session.Metadata["CLASE_ASOC"]          = "[CLASE_ASOC]";
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
                _session.Metadata["TABLA_OBJ_PQ"]        = "[TABLA_OBJ_PQ]";
                _session.Metadata["TABLA_OBJ_PROC"]      = "[TABLA_OBJ_PROC]";
                _session.Metadata["TABLA_OBJ_VISTAS"]    = "[TABLA_OBJ_VISTAS]";
                _session.Metadata["TABLA_OBJ_FUNC"]      = "[TABLA_OBJ_FUNC]";
                _session.Metadata["TABLA_OBJ_IDX"]       = "[TABLA_OBJ_IDX]";
            }

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
            
            // Forzamos avisar que toda la sesión y el template cambió (refresco profundo WPF)
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(CurrentTemplate));

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
        StatusMessage = ok ? "✅ Guardado correctamente." : "❌ Error al guardar.";
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

            // 1. Generar Metadata estructurada (JSON) para las etiquetas faltantes
            StatusMessage = "🤖 Analizando el proyecto y estructurando los datos principales (JSON)...";
            var keysToGenerate = _session.Metadata
                .Where(kvp => string.IsNullOrWhiteSpace(kvp.Value) || (kvp.Value.StartsWith("[") && kvp.Value.EndsWith("]")))
                .Select(kvp => kvp.Key)
                .ToList();

            if (keysToGenerate.Any())
            {
                var generatedData = await _synthesizer.GenerateMetadataAsync(keysToGenerate, _projectContext, instruction);
                var newMeta = new Dictionary<string, string>(_session.Metadata);
                foreach (var kvp in generatedData)
                {
                    if (newMeta.ContainsKey(kvp.Key))
                    {
                        newMeta[kvp.Key] = kvp.Value;
                    }
                }
                _session.Metadata = newMeta;
                OnPropertyChanged(nameof(Session)); // Forzar refresco UI de los TextBox enlazados
            }

            // 2. Generar el contenido markdown/diagramas de las secciones sueltas
            await _generateAllSections.ExecuteAsync(Sections, instruction, _projectContext, async (section, current, total) =>
            {
                SelectedSection = section; // Selecciona visualmente la sección activa
                StatusMessage = $"🤖 Redactando sección '{section.Title}' ({current}/{total})...";
                await RefreshPreviewAsync(); // Renderiza HTML fresco en pantalla por cada sección que se agrega
            });

            // Forzar guardado inmediato
            await _persistence.SaveAsync(_session);

            StatusMessage = "✅ Etiquetado inteligente y Secciones generadas con éxito.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error AI: {ex.Message}";
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
