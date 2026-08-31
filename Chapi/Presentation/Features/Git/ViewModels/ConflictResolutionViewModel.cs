using Chapi.Application.UseCases.Git;
using CommunityToolkit.Mvvm.Input;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Chapi.Presentation.Shared.Mvvm;
using Microsoft.Extensions.DependencyInjection;
using Chapi.Presentation.Features.Projects.Services;

namespace Chapi.Presentation.Features.Git.ViewModels;

public class ConflictResolutionViewModel : ViewModelBase
{
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private readonly string _projectPath;
    private readonly GetConflictsUseCase _getConflictsUseCase;
    private readonly ResolveConflictUseCase _resolveConflictUseCase;

    public ObservableCollection<GitConflict> Conflicts { get; } = new();

    private GitConflict? _selectedConflict;
    public GitConflict? SelectedConflict
    {
        get => _selectedConflict;
        set { SetProperty(ref _selectedConflict, value); UpdateCurrentBlock(); }
    }

    private ConflictBlock? _currentBlock;
    public ConflictBlock? CurrentBlock
    {
        get => _currentBlock;
        set => SetProperty(ref _currentBlock, value);
    }

    private int _currentBlockIndex = 0;
    public int CurrentBlockIndex
    {
        get => _currentBlockIndex;
        set
        {
            if (SetProperty(ref _currentBlockIndex, value))
            {
                UpdateCurrentBlock();
            }
        }
    }

    public int TotalBlocks => SelectedConflict?.Blocks.Count ?? 0;

    public int TotalFiles => Conflicts.Count;

    public int ResolvedFiles => Conflicts.Count(conflict => conflict.IsSaved);

    public int PendingFiles => Conflicts.Count(conflict => !conflict.IsSaved);

    public string FileProgressText => TotalFiles == 0
        ? string.Empty
        : $"Archivos: {ResolvedFiles} resueltos / {TotalFiles}";

    public string SelectedFileProgressText
    {
        get
        {
            if (SelectedConflict == null)
            {
                return string.Empty;
            }

            var index = Conflicts.IndexOf(SelectedConflict);
            return index >= 0 ? $"Archivo {index + 1} de {Math.Max(TotalFiles, 1)}" : string.Empty;
        }
    }

    public string ProgressText => SelectedConflict != null && TotalBlocks > 0
        ? $"Conflicto {CurrentBlockIndex + 1} de {TotalBlocks}"
        : "";

    public string ConflictLineText
    {
        get
        {
            if (CurrentBlock == null)
            {
                return string.Empty;
            }

            if (CurrentBlock.ReplaceWholeFile)
            {
                if (SelectedConflict?.IsExternallyResolved == true)
                {
                    return "El archivo ya fue guardado en disco sin marcadores de conflicto de Git.";
                }

                var structuralRange = GetStructuralRange();
                if (structuralRange == null)
                {
                    return "Git no pudo delimitar una sola zona. El archivo quedo en conflicto estructural.";
                }

                return $"Conflicto estructural detectado alrededor de local {FormatRange(structuralRange.Value.LocalStart, structuralRange.Value.LocalEnd)} y entrante {FormatRange(structuralRange.Value.IncomingStart, structuralRange.Value.IncomingEnd)}.";
            }

            return $"Rango del conflicto: lineas {CurrentBlock.StartLine}-{CurrentBlock.EndLine}";
        }
    }

    public string LocalBlockHeader
    {
        get
        {
            if (CurrentBlock == null)
            {
                return "Cambio Local";
            }

            if (CurrentBlock.ReplaceWholeFile)
            {
                var structuralRange = GetStructuralRange();
                return structuralRange == null
                    ? "Cambio Local"
                    : $"Cambio Local | lineas {FormatRange(structuralRange.Value.LocalStart, structuralRange.Value.LocalEnd)}";
            }

            var start = CurrentBlock.StartLine + 1;
            var end = Math.Max(start, CurrentBlock.SeparatorLine - 1);
            return $"Cambio Local | lineas {start}-{end}";
        }
    }

    public string IncomingBlockHeader
    {
        get
        {
            if (CurrentBlock == null)
            {
                return "Cambio Entrante";
            }

            if (CurrentBlock.ReplaceWholeFile)
            {
                var structuralRange = GetStructuralRange();
                return structuralRange == null
                    ? "Cambio Entrante"
                    : $"Cambio Entrante | lineas {FormatRange(structuralRange.Value.IncomingStart, structuralRange.Value.IncomingEnd)}";
            }

            var start = CurrentBlock.SeparatorLine + 1;
            var end = Math.Max(start, CurrentBlock.EndLine - 1);
            return $"Cambio Entrante | lineas {start}-{end}";
        }
    }

    public string DisplayLocalContent => CurrentBlock?.ReplaceWholeFile == true
        ? FormatStructuralContent(isLocal: true)
        : FormatContentWithLineNumbers(
            CurrentBlock?.LocalContent,
            (CurrentBlock?.StartLine ?? 0) + 1);

    public string DisplayIncomingContent => CurrentBlock?.ReplaceWholeFile == true
        ? FormatStructuralContent(isLocal: false)
        : FormatContentWithLineNumbers(
            CurrentBlock?.IncomingContent,
            (CurrentBlock?.SeparatorLine ?? 0) + 1);

    public IAsyncRelayCommand AcceptLocalCommand { get; }
    public IAsyncRelayCommand AcceptIncomingCommand { get; }
    public IAsyncRelayCommand AcceptBothCommand { get; }
    public IRelayCommand OpenInEditorCommand { get; }
    public IRelayCommand NextConflictCommand { get; }
    public IRelayCommand PreviousConflictCommand { get; }
    public IAsyncRelayCommand MarkResolvedCommand { get; }

    private string _resolvedContent = string.Empty;
    private bool _syncingResolvedContent;
    public string ResolvedContent
    {
        get => _resolvedContent;
        set
        {
            if (!SetProperty(ref _resolvedContent, value))
            {
                return;
            }

            if (!_syncingResolvedContent && CurrentBlock != null)
            {
                CurrentBlock.ResolvedContent = value;
                OnPropertyChanged(nameof(CanSaveCurrentConflict));
                RefreshCommands();
            }
        }
    }

    public string ConflictKindText
    {
        get
        {
            if (SelectedConflict?.IsExternallyResolved == true)
            {
                return "Conflicto resuelto en editor externo (sin marcadores en disco). Revisa el contenido final y pulsa 'Guardar archivo resuelto'.";
            }

            if (SelectedConflict?.HasInlineMarkers == false)
            {
                return "Conflicto estructural detectado. Puedes elegir uno de los lados o editar el resultado final.";
            }

            return "Conflicto inline detectado. Revisa ambos lados y define el bloque final.";
        }
    }

    public bool CanSaveCurrentConflict => SelectedConflict?.IsResolved == true && SelectedConflict?.IsSaved != true;

    public bool IsSelectedConflictEditable => SelectedConflict?.IsSaved != true;

    public event EventHandler? RequestClose;

    public ConflictResolutionViewModel(
        string projectPath,
        GetConflictsUseCase getConflictsUseCase,
        ResolveConflictUseCase resolveConflictUseCase)
    {
        _projectPath = projectPath;
        _getConflictsUseCase = getConflictsUseCase;
        _resolveConflictUseCase = resolveConflictUseCase;

        AcceptLocalCommand = new AsyncRelayCommand(() => AcceptChangeAsync(true), () => CurrentBlock != null && IsSelectedConflictEditable);
        AcceptIncomingCommand = new AsyncRelayCommand(() => AcceptChangeAsync(false), () => CurrentBlock != null && IsSelectedConflictEditable);
        AcceptBothCommand = new AsyncRelayCommand(AcceptBothChangesAsync, () => CurrentBlock != null && IsSelectedConflictEditable);
        OpenInEditorCommand = new RelayCommand(OpenInEditor, () => SelectedConflict != null);
        NextConflictCommand = new RelayCommand(GoToNextBlock, () => SelectedConflict != null && CurrentBlockIndex < TotalBlocks - 1);
        PreviousConflictCommand = new RelayCommand(GoToPreviousBlock, () => SelectedConflict != null && CurrentBlockIndex > 0);
        MarkResolvedCommand = new AsyncRelayCommand(SaveResolvedFileAsync, () => SelectedConflict != null && SelectedConflict.IsResolved && !SelectedConflict.IsSaved);
    }

    public async Task LoadConflictsAsync()
    {
        IsBusy = true;
        try
        {
            var conflicts = await _getConflictsUseCase.ExecuteAsync(_projectPath);
            Conflicts.Clear();
            foreach (var conflict in conflicts)
            {
                Conflicts.Add(conflict);
            }
            SelectedConflict = Conflicts.FirstOrDefault();
            NotifyConflictSummaryChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateCurrentBlock()
    {
        if (SelectedConflict != null && SelectedConflict.Blocks.Any())
        {
            var clampedIndex = Math.Clamp(_currentBlockIndex, 0, SelectedConflict.Blocks.Count - 1);
            if (_currentBlockIndex != clampedIndex)
            {
                _currentBlockIndex = clampedIndex;
                OnPropertyChanged(nameof(CurrentBlockIndex));
            }

            CurrentBlock = SelectedConflict.Blocks[clampedIndex];
            _syncingResolvedContent = true;
            ResolvedContent = CurrentBlock.ResolvedContent ?? string.Empty;
            _syncingResolvedContent = false;
        }
        else
        {
            if (_currentBlockIndex != 0)
            {
                _currentBlockIndex = 0;
                OnPropertyChanged(nameof(CurrentBlockIndex));
            }

            CurrentBlock = null;
            _syncingResolvedContent = true;
            ResolvedContent = string.Empty;
            _syncingResolvedContent = false;
        }

        OnPropertyChanged(nameof(TotalBlocks));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ConflictKindText));
        OnPropertyChanged(nameof(CanSaveCurrentConflict));
        OnPropertyChanged(nameof(IsSelectedConflictEditable));
        OnPropertyChanged(nameof(ConflictLineText));
        OnPropertyChanged(nameof(LocalBlockHeader));
        OnPropertyChanged(nameof(IncomingBlockHeader));
        OnPropertyChanged(nameof(DisplayLocalContent));
        OnPropertyChanged(nameof(DisplayIncomingContent));
        OnPropertyChanged(nameof(SelectedFileProgressText));
        RefreshCommands();
    }

    private void GoToNextBlock()
    {
        if (CurrentBlockIndex < TotalBlocks - 1)
        {
            CurrentBlockIndex++;
        }
    }

    private void GoToPreviousBlock()
    {
        if (CurrentBlockIndex > 0)
        {
            CurrentBlockIndex--;
        }
    }

    private Task AcceptChangeAsync(bool useLocal)
    {
        if (CurrentBlock == null || SelectedConflict == null) return Task.CompletedTask;

        ResolvedContent = useLocal ? CurrentBlock.LocalContent : CurrentBlock.IncomingContent;
        RefreshCommands();
        return Task.CompletedTask;
    }

    private Task AcceptBothChangesAsync()
    {
        if (CurrentBlock == null)
        {
            return Task.CompletedTask;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(CurrentBlock.LocalContent))
        {
            parts.Add(CurrentBlock.LocalContent.TrimEnd('\r', '\n'));
        }

        if (!string.IsNullOrWhiteSpace(CurrentBlock.IncomingContent))
        {
            parts.Add(CurrentBlock.IncomingContent.TrimEnd('\r', '\n'));
        }

        ResolvedContent = string.Join(Environment.NewLine, parts);
        RefreshCommands();
        return Task.CompletedTask;
    }

    private async Task SaveResolvedFileAsync()
    {
        if (SelectedConflict == null || !SelectedConflict.IsResolved) return;

        IsBusy = true;
        try
        {
            // Construir el archivo final (reemplazando los bloques originales)
            // Para mantenerlo simple asumo que ya hicimos el Write en algun lado o 
            // necesitamos leer el archivo y reemplazar las secciones
            var fullPath = !string.IsNullOrWhiteSpace(SelectedConflict.FullPath)
                ? SelectedConflict.FullPath
                : Path.Combine(_projectPath, SelectedConflict.FilePath);
            string finalContent;

            if (!SelectedConflict.HasInlineMarkers)
            {
                finalContent = SelectedConflict.Blocks.FirstOrDefault()?.ResolvedContent ?? string.Empty;
                if (string.IsNullOrEmpty(finalContent) && File.Exists(fullPath))
                {
                    finalContent = await File.ReadAllTextAsync(fullPath);
                }
            }
            else
            {
                var contentLines = (await File.ReadAllLinesAsync(fullPath)).ToList();

                // Reemplazo inverso para no desfasar indices
                for (int i = SelectedConflict.Blocks.Count - 1; i >= 0; i--)
                {
                    var block = SelectedConflict.Blocks[i];
                    var startIndex = Math.Max(0, block.StartLine - 1);
                    var removeCount = Math.Max(0, block.EndLine - block.StartLine + 1);

                    if (startIndex < contentLines.Count && removeCount > 0)
                    {
                        var boundedRemoveCount = Math.Min(removeCount, contentLines.Count - startIndex);
                        contentLines.RemoveRange(startIndex, boundedRemoveCount);
                    }

                    var resolvedLines = block.ResolvedContent?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None) ?? Array.Empty<string>();

                    if (resolvedLines.Length > 0 && string.IsNullOrEmpty(resolvedLines.Last()))
                    {
                        resolvedLines = resolvedLines.Take(resolvedLines.Length - 1).ToArray();
                    }

                    contentLines.InsertRange(startIndex, resolvedLines);
                }

                finalContent = string.Join(Environment.NewLine, contentLines);
            }

            var result = await _resolveConflictUseCase.ExecuteAsync(_projectPath, SelectedConflict.FilePath, finalContent);

            if (result.IsSuccess)
            {
                SelectedConflict.IsSaved = true;
                NotifyConflictSummaryChanged();

                var nextPendingConflict = Conflicts.FirstOrDefault(conflict => !conflict.IsSaved);
                if (nextPendingConflict != null && !ReferenceEquals(nextPendingConflict, SelectedConflict))
                {
                    SelectedConflict = nextPendingConflict;
                }
                else
                {
                    UpdateCurrentBlock();
                }
            }
            else
            {
                Msg.Assistant($"Error al guardar resolución: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Msg.Assistant($"Excepción resolviendo: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenInEditor()
    {
        if (SelectedConflict == null) return;
        var fullPath = !string.IsNullOrWhiteSpace(SelectedConflict.FullPath)
            ? SelectedConflict.FullPath
            : Path.Combine(_projectPath, SelectedConflict.FilePath);

        var launcher = App.ServiceProvider.GetService<ProjectToolLauncher>();
        if (launcher != null)
        {
            launcher.SmartOpen(_projectPath, fullPath);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = "notepad", Arguments = $"\"{fullPath}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Msg.Assistant($"No se pudo abrir editor externo: {ex.Message}");
        }
    }

    private void RefreshCommands()
    {
        AcceptLocalCommand.NotifyCanExecuteChanged();
        AcceptIncomingCommand.NotifyCanExecuteChanged();
        AcceptBothCommand.NotifyCanExecuteChanged();
        OpenInEditorCommand.NotifyCanExecuteChanged();
        NextConflictCommand.NotifyCanExecuteChanged();
        PreviousConflictCommand.NotifyCanExecuteChanged();
        MarkResolvedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyConflictSummaryChanged()
    {
        OnPropertyChanged(nameof(TotalFiles));
        OnPropertyChanged(nameof(ResolvedFiles));
        OnPropertyChanged(nameof(PendingFiles));
        OnPropertyChanged(nameof(FileProgressText));
        OnPropertyChanged(nameof(SelectedFileProgressText));
    }

    private static string FormatContentWithLineNumbers(string? content, int firstLineNumber)
    {
        if (string.IsNullOrEmpty(content))
        {
            return "(sin contenido)";
        }

        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var formatted = lines.Select((line, index) => $"{firstLineNumber + index,4}: {line}");
        return string.Join(Environment.NewLine, formatted);
    }

    private string FormatStructuralContent(bool isLocal)
    {
        if (CurrentBlock == null)
        {
            return "(sin contenido)";
        }

        var structuralRange = GetStructuralRange();
        if (structuralRange == null)
        {
            return FormatContentWithLineNumbers(
                isLocal ? CurrentBlock.LocalContent : CurrentBlock.IncomingContent,
                1);
        }

        var content = isLocal ? CurrentBlock.LocalContent : CurrentBlock.IncomingContent;
        var startLine = isLocal ? structuralRange.Value.LocalDisplayStart : structuralRange.Value.IncomingDisplayStart;
        var endLine = isLocal ? structuralRange.Value.LocalDisplayEnd : structuralRange.Value.IncomingDisplayEnd;

        return FormatContentSliceWithLineNumbers(content, startLine, endLine);
    }

    private StructuralRange? GetStructuralRange()
    {
        if (CurrentBlock?.ReplaceWholeFile != true)
        {
            return null;
        }

        var localLines = SplitLines(CurrentBlock.LocalContent);
        var incomingLines = SplitLines(CurrentBlock.IncomingContent);

        var maxPrefix = Math.Min(localLines.Length, incomingLines.Length);
        var prefix = 0;
        while (prefix < maxPrefix &&
               string.Equals(localLines[prefix], incomingLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var localTail = localLines.Length - 1;
        var incomingTail = incomingLines.Length - 1;
        while (localTail >= prefix &&
               incomingTail >= prefix &&
               string.Equals(localLines[localTail], incomingLines[incomingTail], StringComparison.Ordinal))
        {
            localTail--;
            incomingTail--;
        }

        var localStart = Math.Min(prefix + 1, Math.Max(localLines.Length, 1));
        var incomingStart = Math.Min(prefix + 1, Math.Max(incomingLines.Length, 1));
        var localEnd = Math.Max(localStart, localTail + 1);
        var incomingEnd = Math.Max(incomingStart, incomingTail + 1);

        const int contextLines = 3;
        var localDisplayStart = Math.Max(1, localStart - contextLines);
        var incomingDisplayStart = Math.Max(1, incomingStart - contextLines);
        var localDisplayEnd = Math.Min(Math.Max(localLines.Length, 1), localEnd + contextLines);
        var incomingDisplayEnd = Math.Min(Math.Max(incomingLines.Length, 1), incomingEnd + contextLines);

        return new StructuralRange(
            localStart,
            localEnd,
            incomingStart,
            incomingEnd,
            localDisplayStart,
            localDisplayEnd,
            incomingDisplayStart,
            incomingDisplayEnd);
    }

    private static string[] SplitLines(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<string>();
        }

        return content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    }

    private static string FormatContentSliceWithLineNumbers(string? content, int startLine, int endLine)
    {
        if (string.IsNullOrEmpty(content))
        {
            return "(sin contenido)";
        }

        var lines = SplitLines(content);
        if (lines.Length == 0)
        {
            return "(sin contenido)";
        }

        startLine = Math.Max(1, Math.Min(startLine, lines.Length));
        endLine = Math.Max(startLine, Math.Min(endLine, lines.Length));

        var slice = lines
            .Skip(startLine - 1)
            .Take(endLine - startLine + 1)
            .Select((line, index) => $"{startLine + index,4}: {line}");

        return string.Join(Environment.NewLine, slice);
    }

    private static string FormatRange(int start, int end)
    {
        return start >= end ? start.ToString() : $"{start}-{end}";
    }

    private readonly record struct StructuralRange(
        int LocalStart,
        int LocalEnd,
        int IncomingStart,
        int IncomingEnd,
        int LocalDisplayStart,
        int LocalDisplayEnd,
        int IncomingDisplayStart,
        int IncomingDisplayEnd);
}
