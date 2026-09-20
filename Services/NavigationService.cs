using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Pages;

namespace LumiereMediaPlayer.Services;

public class NavigationService : INavigationService
{
    private NavigationView? _navigationView;
    private Frame? _frame;
    private bool _isNavigating;

    private readonly Dictionary<string, Type> _pageMap = new()
    {
        { PageKeys.Home, typeof(HomePage) },
        { PageKeys.Music, typeof(MusicLibraryPage) },
        { PageKeys.Videos, typeof(VideoPage) },
        { PageKeys.Playlists, typeof(PlaylistsPage) },
        { PageKeys.NowPlaying, typeof(NowPlayingPage) },
        { PageKeys.Settings, typeof(SettingsPage) },
        { PageKeys.StreamMusic, typeof(StreamingMusicPage) },
        { PageKeys.StreamMovies, typeof(StreamingMoviesPage) },
        { PageKeys.StreamTvShows, typeof(StreamingTvShowsPage) },
        { PageKeys.StreamYouTube, typeof(StreamingYouTubePage) },
        { PageKeys.StreamTwitch, typeof(StreamingTwitchPage) }
    };

    public void Initialize(NavigationView navigationView, Frame frame)
    {
        _navigationView = navigationView;
        _frame = frame;

        if (_navigationView != null)
        {
            _navigationView.SelectionChanged -= OnSelectionChanged;
            _navigationView.SelectionChanged += OnSelectionChanged;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigating) return;

        if (args.IsSettingsSelected)
        {
            NavigateTo(PageKeys.Settings);
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var key = item.Tag?.ToString();
            if (!string.IsNullOrEmpty(key))
            {
                NavigateTo(key);
            }
        }
    }

    public void NavigateTo(string pageKey, object? parameter = null)
    {
        if (_frame == null) return;
        if (_isNavigating) return;

        if (_pageMap.TryGetValue(pageKey, out var pageType))
        {
            NavigateToType(pageType, parameter);
        }
    }

    private void NavigateToType(Type pageType, object? parameter)
    {
        if (_frame == null) return;

        if (_frame.CurrentSourcePageType != pageType || parameter != null)
        {
            try
            {
                _isNavigating = true;
                NavigationTransitionInfo transitionInfo;

                if (pageType == typeof(VideoPage) || pageType == typeof(NowPlayingPage))
                {
                    transitionInfo = new DrillInNavigationTransitionInfo();
                }
                else if (AppServices.Settings.Current.ReduceMotion)
                {
                    transitionInfo = new SuppressNavigationTransitionInfo();
                }
                else
                {
                    transitionInfo = new SlideNavigationTransitionInfo
                    {
                        Effect = SlideNavigationTransitionEffect.FromRight
                    };
                }

                _frame.Navigate(pageType, parameter, transitionInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NavigationService] Navigation to {pageType.Name} failed: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }
    }

    public void GoBack()
    {
        if (_frame != null && _frame.CanGoBack)
        {
            _frame.GoBack();
        }
    }

    /// <summary>
    /// Determines whether the specified page type, content instance, or navigation key belongs to the Streaming section.
    /// </summary>
    public static bool IsStreamingSection(Type? pageType, object? pageContent = null, string? pageKey = null)
    {
        if (!string.IsNullOrEmpty(pageKey))
        {
            if (pageKey == PageKeys.StreamMusic ||
                pageKey == PageKeys.StreamMovies ||
                pageKey == PageKeys.StreamTvShows ||
                pageKey == PageKeys.StreamYouTube ||
                pageKey == PageKeys.StreamTwitch)
            {
                return true;
            }
        }

        if (pageType != null)
        {
            if (pageType == typeof(StreamingMoviesPage) ||
                pageType == typeof(StreamingTvShowsPage) ||
                pageType == typeof(StreamingMusicPage) ||
                pageType == typeof(StreamingYouTubePage) ||
                pageType == typeof(StreamingTwitchPage) ||
                pageType == typeof(StreamingDetailsPage) ||
                pageType.Name.StartsWith("Streaming", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (pageContent != null)
        {
            if (pageContent is StreamingMoviesPage ||
                pageContent is StreamingTvShowsPage ||
                pageContent is StreamingMusicPage ||
                pageContent is StreamingYouTubePage ||
                pageContent is StreamingTwitchPage ||
                pageContent is StreamingDetailsPage ||
                pageContent.GetType().Name.StartsWith("Streaming", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
