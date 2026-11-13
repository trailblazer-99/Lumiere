using Microsoft.UI.Xaml.Controls;

namespace LumiereMediaPlayer.Services;

public interface INavigationService
{
    void Initialize(NavigationView navigationView, Frame frame);
    void NavigateTo(string pageKey, object? parameter = null);
    void GoBack();
}
