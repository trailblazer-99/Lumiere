using FluentMediaPlayer.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentMediaPlayer.Controls
{
    public sealed partial class NativeStreamingTray : UserControl
    {
        public OptimizedStreamingViewModel ViewModel { get; } = new();

        public NativeStreamingTray()
        {
            this.InitializeComponent();
        }
    }
}
