using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LumiereMediaPlayer.Controls
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
