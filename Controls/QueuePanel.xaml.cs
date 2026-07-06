using FluentMediaPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentMediaPlayer.Controls;

public sealed partial class QueuePanel : UserControl
{
    public QueueViewModel ViewModel { get; } = AppServices.QueueViewModel;

    public QueuePanel()
    {
        InitializeComponent();
    }
}
