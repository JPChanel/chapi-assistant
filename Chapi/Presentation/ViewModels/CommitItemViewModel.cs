using System;

namespace Chapi.Presentation.ViewModels;

/// <summary>
/// ViewModel para un item de commit individual en la lista de historial.
/// </summary>
public class CommitItemViewModel : ViewModelBase
{
    private string _hash;
    private string _shortHash;
    private string _message;
    private string _author;
    private DateTime _date;
    private string _relativeDate;
    private bool _isSynced;
    private string _description;

    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, value);
    }

    public string ShortHash
    {
        get => _shortHash;
        set => SetProperty(ref _shortHash, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Author
    {
        get => _author;
        set => SetProperty(ref _author, value);
    }

    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    public string RelativeDate
    {
        get => _relativeDate;
        set => SetProperty(ref _relativeDate, value);
    }

    public bool IsSynced
    {
        get => _isSynced;
        set 
        {
            if (SetProperty(ref _isSynced, value))
            {
                OnPropertyChanged(nameof(IsUnpushed));
            }
        }
    }

    // Propiedad conveniente para bindings que esperan "IsUnpushed"
    public bool IsUnpushed => !IsSynced;

    public string Summary => _message?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    // Tags
    private System.Collections.ObjectModel.ObservableCollection<string> _tags = new();
    public System.Collections.ObjectModel.ObservableCollection<string> Tags
    {
        get => _tags;
        set 
        {
            if (SetProperty(ref _tags, value))
            {
                OnPropertyChanged(nameof(HasTags));
            }
        }
    }
    public bool HasTags => Tags != null && Tags.Any();
}
