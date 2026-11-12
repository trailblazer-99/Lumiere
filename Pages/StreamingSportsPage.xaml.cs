using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.ViewModels;

namespace LumiereMediaPlayer.Pages
{
    public sealed partial class StreamingSportsPage : Page
    {
        public StreamingSportsViewModel ViewModel { get; }

        public StreamingSportsPage()
        {
            ViewModel = AppServices.StreamingSportsViewModel;
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            try
            {
                base.OnNavigatedTo(e);
                await ViewModel.LoadEventsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsPage] OnNavigatedTo error: {ex.Message}");
            }
        }

        private void OnLeagueFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string league)
            {
                ViewModel.SelectLeague(league);
            }
        }

        private async void OnMatchCardTapped(object sender, TappedRoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement elem && elem.DataContext is WatchmodeLiveEvent ev)
                {
                    await ViewModel.SelectEventAsync(ev);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsPage] OnMatchCardTapped error: {ex.Message}");
            }
        }

        private async void OnViewStreamsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is WatchmodeLiveEvent ev)
                {
                    await ViewModel.SelectEventAsync(ev);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsPage] OnViewStreamsClick error: {ex.Message}");
            }
        }

        private void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WatchmodeTeam team)
            {
                ViewModel.ToggleFavoriteTeam(team);
            }
        }

        private void OnCloseDetailsClick(object sender, RoutedEventArgs e)
        {
            ViewModel.CloseDetails();
        }

        private void OnDetailsScrimTapped(object sender, TappedRoutedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, DetailsOverlay))
            {
                ViewModel.CloseDetails();
            }
        }

        private async void OnOpenStreamSourceClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Tag is WatchmodeLiveSource source)
                {
                    await ViewModel.OpenSourceAsync(source);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingSportsPage] OnOpenStreamSourceClick error: {ex.Message}");
            }
        }
    }
}
