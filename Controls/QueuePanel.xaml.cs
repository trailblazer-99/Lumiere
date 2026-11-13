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

    private void OnPlayEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LumiereMediaPlayer.Models.QueueEntry entry)
        {
            ViewModel.PlayEntryCommand.Execute(entry);
        }
    }

    private void OnRemoveEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LumiereMediaPlayer.Models.QueueEntry entry)
        {
            ViewModel.RemoveEntryCommand.Execute(entry);
        }
    }

    private void OnQueueDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.SyncOrderFromEntries();
    }
}
