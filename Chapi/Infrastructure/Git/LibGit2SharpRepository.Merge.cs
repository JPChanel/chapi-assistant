using Chapi.Domain.Common;
using Chapi.Domain.Entities;
using Chapi.Domain.Enums;
using Chapi.Domain.Interfaces;
using Chapi.Domain.Models;
using LibGit2Sharp;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Implementación stub de métodos merge/rebase para LibGit2Sharp.
/// Estos métodos delegan al GitRepository (Git CLI) para evitar complejidad.
/// </summary>
public partial class LibGit2SharpRepository
{
    public async Task<Result> MergeBranchAsync(string projectPath, string sourceBranch, bool fastForward = true)
    {
        // Delegar al GitRepository (Git CLI) que ya tiene la implementación
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.MergeBranchAsync(projectPath, sourceBranch, fastForward);
    }

    public async Task<Result> SquashMergeBranchAsync(string projectPath, string sourceBranch)
    {
        // Delegar al GitRepository (Git CLI)
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.SquashMergeBranchAsync(projectPath, sourceBranch);
    }

    public async Task<Result> RebaseBranchAsync(string projectPath, string targetBranch)
    {
        // Delegar al GitRepository (Git CLI)
        var executor = new GitCommandExecutor();
        var parser = new GitOutputParser();
        var gitRepo = new GitRepository(executor, parser);
        return await gitRepo.RebaseBranchAsync(projectPath, targetBranch);
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

                // Para ser 100% certeros sin tocar el índice, necesitamos usar repo.Merge.Commits en memoria
                // Sin embargo, LibGit2Sharp NO DEJA hacerlo 'dry-run' fácilmente sin escribir en el índice.
                
                // La mejor aproximación segura SIN git.exe es capturar excepciones controladas
                // O confiar en que si no es FF, podría haber conflictos si tocan los mismos archivos.
                
                // ESTRATEGIA SEGURA:
                // Usamos `repo.ObjectDatabase.CalculateHistoryDivergence` ya lo usamos.
                // Vamos a verificar conflictos a nivel de archivos modificados en ambos lados.
                
                // a) Archivos modificados en Current desde Base
                var changesInCurrent = repo.Diff.Compare<TreeChanges>(baseCommit?.Tree, currentBranch.Tip.Tree)
                                                .Select(c => c.Path).ToHashSet();
                
                // b) Archivos modificados en Source desde Base
                var changesInSource = repo.Diff.Compare<TreeChanges>(baseCommit?.Tree, sourceBranch.Tip.Tree)
                                              .Select(c => c.Path).ToHashSet();
                
                // c) Intersección
                var potentialConflicts = changesInCurrent.Intersect(changesInSource).ToList();
                
                if (potentialConflicts.Any())
                {
                    // Si ambos tocaron el mismo archivo, HAY riesgo alto de conflicto.
                    // Aunque Git es listo (auto-merge), para nuestra UI es mejor advertir.
                    return (true, $"Posible conflicto en {potentialConflicts.Count} archivo(s): {string.Join(", ", potentialConflicts.Take(3))}...");
                }

                return (false, "Merge seems clean");
            }
            catch (Exception ex)
            {
                return (true, $"Error verificando conflictos: {ex.Message}");
            }
        });
    }
}
