using Chapi.Application.UseCases.Git;
using Chapi.Domain.Entities;
using Chapi.Infrastructure.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace Chapi.Presentation.ViewModels;

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
        set { SetProperty(ref _currentBlockIndex, value); UpdateCurrentBlock(); }
    }

    public int TotalBlocks => SelectedConflict?.Blocks.Count ?? 0;

    public string ProgressText => SelectedConflict != null && TotalBlocks > 0
        ? $"Conflicto {CurrentBlockIndex + 1} de {TotalBlocks}"
        : "";

    public ICommand AcceptLocalCommand { get; }
    public ICommand AcceptIncomingCommand { get; }
    public ICommand OpenInEditorCommand { get; }
    public ICommand NextConflictCommand { get; }
    public ICommand PreviousConflictCommand { get; }
    public ICommand MarkResolvedCommand { get; }

    public event EventHandler? RequestClose;

    public ConflictResolutionViewModel(
        string projectPath,
        GetConflictsUseCase getConflictsUseCase,
        ResolveConflictUseCase resolveConflictUseCase)
    {
        _projectPath = projectPath;
        _getConflictsUseCase = getConflictsUseCase;
        _resolveConflictUseCase = resolveConflictUseCase;

        AcceptLocalCommand = new RelayCommand(async _ => await AcceptChangeAsync(true), _ => CurrentBlock != null && !CurrentBlock.IsResolved);
        AcceptIncomingCommand = new RelayCommand(async _ => await AcceptChangeAsync(false), _ => CurrentBlock != null && !CurrentBlock.IsResolved);
        OpenInEditorCommand = new RelayCommand(_ => OpenInEditor(), _ => SelectedConflict != null);
        NextConflictCommand = new RelayCommand(_ => GoToNextBlock(), _ => SelectedConflict != null && CurrentBlockIndex < TotalBlocks - 1);
        PreviousConflictCommand = new RelayCommand(_ => GoToPreviousBlock(), _ => SelectedConflict != null && CurrentBlockIndex > 0);
        MarkResolvedCommand = new RelayCommand(async _ => await SaveResolvedFileAsync(), _ => SelectedConflict != null && SelectedConflict.IsResolved);
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
            CurrentBlockIndex = Math.Clamp(CurrentBlockIndex, 0, SelectedConflict.Blocks.Count - 1);
            CurrentBlock = SelectedConflict.Blocks[CurrentBlockIndex];
        }
        else
        {
            CurrentBlock = null;
        }

        OnPropertyChanged(nameof(TotalBlocks));
        OnPropertyChanged(nameof(ProgressText));
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

    private async Task AcceptChangeAsync(bool useLocal)
    {
        if (CurrentBlock == null || SelectedConflict == null) return;

        CurrentBlock.ResolvedContent = useLocal ? CurrentBlock.LocalContent : CurrentBlock.IncomingContent;
        RefreshCommands();

        // Autoadvance
        if (CurrentBlockIndex < TotalBlocks - 1)
        {
            GoToNextBlock();
        }
        else if (SelectedConflict.IsResolved)
        {
            // Auto sugerir guardar si todos estan resueltos
            await SaveResolvedFileAsync();
        }
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
            var fullPath = Path.Combine(_projectPath, SelectedConflict.FilePath);
            var contentLines = (await File.ReadAllLinesAsync(fullPath)).ToList();

            // Reemplazo inverso para no desfasar indices
            for (int i = SelectedConflict.Blocks.Count - 1; i >= 0; i--)
            {
                var block = SelectedConflict.Blocks[i];
                contentLines.RemoveRange(block.StartLine - 1, block.EndLine - block.StartLine + 1);

                var resolvedLines = block.ResolvedContent?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None) ?? Array.Empty<string>();

                // Si la ultima linea es vacia y es por el split, corregimos.
                if (resolvedLines.Length > 0 && string.IsNullOrEmpty(resolvedLines.Last()))
                {
                    resolvedLines = resolvedLines.Take(resolvedLines.Length - 1).ToArray();
                }

                contentLines.InsertRange(block.StartLine - 1, resolvedLines);
            }

            var finalContent = string.Join(Environment.NewLine, contentLines);
            var result = await _resolveConflictUseCase.ExecuteAsync(_projectPath, SelectedConflict.FilePath, finalContent);

            if (result.IsSuccess)
            {
                Conflicts.Remove(SelectedConflict);
                SelectedConflict = Conflicts.FirstOrDefault();
                if (!Conflicts.Any())
                {
                    RequestClose?.Invoke(this, EventArgs.Empty);
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
        var fullPath = Path.Combine(_projectPath, SelectedConflict.FilePath);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "code", Arguments = $"\"{fullPath}\"", UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "notepad", Arguments = $"\"{fullPath}\"", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Msg.Assistant($"No se pudo abrir editor externo: {ex.Message}");
            }
        }
    }

    private void RefreshCommands()
    {
        ((RelayCommand)AcceptLocalCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AcceptIncomingCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NextConflictCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousConflictCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MarkResolvedCommand).RaiseCanExecuteChanged();
    }
}
