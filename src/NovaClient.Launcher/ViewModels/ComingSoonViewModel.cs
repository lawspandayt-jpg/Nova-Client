using NovaClient.Launcher.Common;

namespace NovaClient.Launcher.ViewModels;

/// <summary>Honest placeholder for pages that are planned but not built — nothing fake works here.</summary>
public sealed class ComingSoonViewModel : ViewModelBase
{
    public string Title { get; }
    public string Description { get; }

    public ComingSoonViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }
}
