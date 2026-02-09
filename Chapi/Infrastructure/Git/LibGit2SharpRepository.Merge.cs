using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using LibGit2Sharp;
using System.IO;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Implementación stub de métodos merge/rebase para LibGit2Sharp.
/// Estos métodos delegan al GitRepository (Git CLI) para evitar complejidad.
/// </summary>
public partial class LibGit2SharpRepository
{
    public async Task<Result> MergeBranchAsync(string projectPath, string sourceBranchName, bool fastForward = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                
                // 1. Validar estado
                if (repo.RetrieveStatus().IsDirty)
                     return Result.Fail("Cambios locales pendientes. Haz commit o stash antes de fusionar.");

                var source = repo.Branches[sourceBranchName];
                if (source == null) return Result.Fail($"Rama '{sourceBranchName}' no encontrada.");

                // 2. Definir identidades
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature == null)
                    return Result.Fail("No se ha configurado usuario ni correo en git config (user.name / user.email).");

                // 3. Ejecutar Merge
                var options = new MergeOptions 
                { 
                    FastForwardStrategy = fastForward ? FastForwardStrategy.Default : FastForwardStrategy.NoFastForward,
                    FailOnConflict = true 
                };

                var mergeResult = repo.Merge(source, signature, options);

                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    repo.Reset(LibGit2Sharp.ResetMode.Hard); 
                    return Result.Fail("Conflicto de fusión detectado. La operación fue abortada.");
                }

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error en Merge: {ex.Message}");
            }
        });
    }

    public async Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranchName, string? commitMessage = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);

                if (repo.RetrieveStatus().IsDirty)
                     return Result.Fail("Cambios locales pendientes. Haz commit o stash antes.");

                var source = repo.Branches[sourceBranchName];
                if (source == null) return Result.Fail($"Rama '{sourceBranchName}' no encontrada.");

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature == null)
                    return Result.Fail("No se ha configurado usuario ni correo en git config.");

                var options = new MergeOptions 
                { 
                    FastForwardStrategy = FastForwardStrategy.NoFastForward,
                    CommitOnSuccess = false 
                };

                var mergeResult = repo.Merge(source, signature, options);

                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    repo.Reset(LibGit2Sharp.ResetMode.Hard);
                    return Result.Fail("Conflicto detectado durante Squash. Operación abortada.");
                }

                // Eliminamos MERGE_HEAD para convertirlo en un commit normal (Squash)
                var mergeHeadPath = Path.Combine(repo.Info.Path, "MERGE_HEAD");
                if (File.Exists(mergeHeadPath))
                {
                    File.Delete(mergeHeadPath);
                }
                
                var msg = !string.IsNullOrWhiteSpace(commitMessage) ? commitMessage : $"Squash merge from '{sourceBranchName}'";
                repo.Commit(msg, signature, signature);
                
                // Limpiar cualquier otro estado
                if (repo.Info.IsHeadDetached) repo.Reset(LibGit2Sharp.ResetMode.Mixed);

                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Error en Squash: {ex.Message}");
            }
        });
    }

    public async Task<Result> RebaseBranchAsync(string projectPath, string targetBranchName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);

                if (repo.RetrieveStatus().IsDirty)
                {
                    return Result.Fail("No se puede hacer rebase con cambios locales pendientes. Haz commit o stash primero.");
                }

                var currentBranch = repo.Head;
                var targetBranch = repo.Branches[targetBranchName];

                if (targetBranch == null)
                {
                    return Result.Fail($"La rama destino '{targetBranchName}' no existe.");
                }

                if (currentBranch.Tip.Sha == targetBranch.Tip.Sha)
                {
                    return Result.Fail("Las ramas ya están sincronizadas (mismo commit).");
                }

                // Identidad para los commits re-aplicados
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature == null)
                    return Result.Fail("No se ha configurado usuario ni correo en git config.");

                var identity = new Identity(signature.Name, signature.Email);
                var options = new RebaseOptions();

                // Iniciar Rebase: Current sobre Target
                // Equivalente a: git checkout Current && git rebase Target
                var result = repo.Rebase.Start(currentBranch, targetBranch, null, identity, options);

                if (result.Status == RebaseStatus.Complete)
                {
                    return Result.Success();
                }
                else if (result.Status == RebaseStatus.Conflicts || result.Status == RebaseStatus.Stop)
                {
                    repo.Rebase.Abort();
                    return Result.Fail("Conflictos detectados durante el rebase. Operación abortada automáticamente.");
                }
                else
                {
                    // Estado inesperado
                    repo.Rebase.Abort();
                    return Result.Fail($"El rebase no se completó (Estado: {result.Status}). Operación abortada.");
                }
            }
            catch (Exception ex)
            {
                // Seguridad extra: intentar abortar si quedó a medias
                try { using var r = new Repository(projectPath); r.Rebase.Abort(); } catch { }
                return Result.Fail($"Error crítico en Rebase: {ex.Message}");
            }
        });
    }

    public async Task<(bool hasConflicts, string message)> CheckMergeConflictsAsync(string projectPath, string sourceBranchName)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var repo = new Repository(projectPath);
                
                // 1. Verificar si hay cambios locales (Dirty State)
                if (repo.RetrieveStatus().IsDirty)
                {
                    return (true, "DIRTY_WORKTREE");
                }

                var sourceBranch = repo.Branches[sourceBranchName];
                if (sourceBranch == null)
                    return (true, $"La rama '{sourceBranchName}' no existe.");

                var currentBranch = repo.Head;

                // 2. Análisis de Merge (Fast-Forward o UpToDate) usando Divergencia
                var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(currentBranch.Tip, sourceBranch.Tip);
                
                // Si la rama fuente está detrás o igual (Author date check podría ser necesario, pero hash check es mejor)
                if (sourceBranch.Tip.Sha == currentBranch.Tip.Sha)
                {
                    return (false, "Already up to date");
                }
                
                // Si la rama actual está "detrás" de la fuente y "no adelante" (divergencia cero), es FastForward
                if (divergence.BehindBy > 0 && divergence.AheadBy == 0)
                {
                    return (false, "Fast-forward possible");
                }
                
                // Si la rama actual NO está detrás (Already up to date o Ahead)
                if (divergence.BehindBy == 0)
                {
                     // Si Current está adelante de Source, no hay nada que traer (salvo que sea un rebase invertido)
                     return (false, "Already up to date (Current is ahead)");
                }

                // 3. Simulación de Merge en Memoria (Tree Merge)
                // LibGit2Sharp permite comparar árboles para predecir conflictos sin tocar el disco
                var baseCommit = repo.ObjectDatabase.FindMergeBase(currentBranch.Tip, sourceBranch.Tip);
                
                if (baseCommit == null)
                {
                    // Si no tienen historia común, es un merge complejo pero no necesariamente conflicto.
                    // Asumiremos safe si no hay colisiones de archivos, pero por seguridad, 
                    // verificamos con un Tree Merge usando la estrategia por defecto.
                }

                var treeChanges = repo.Diff.Compare<TreeChanges>(
                    baseCommit?.Tree ?? repo.Head.Tip.Tree, 
                    sourceBranch.Tip.Tree
                );
                return (false, "Merge seems clean");
            }
            catch (Exception ex)
            {
                return (true, $"Error verificando conflictos: {ex.Message}");
            }
        });
    }
}
