using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumiereMediaPlayer.Controls;

public sealed partial class QueuePanel : UserControl
{
    public QueueViewModel ViewModel { get; } = AppServices.QueueViewModel;

    public QueuePanel()
    {
        InitializeComponent();
    }
}
