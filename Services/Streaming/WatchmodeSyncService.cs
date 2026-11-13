using System;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class WatchmodeSyncService : IWatchmodeSyncService
    {
        private readonly WatchmodeService _watchmodeService = new();

        public async Task SyncLibraryAsync()
        {
            try
            {
                var savedItems = AppServices.StreamingLibrary.SavedItems;
                var watchmodeItems = savedItems.Where(i => i.Type == StreamingItemType.Movie || i.Type == StreamingItemType.TvShow).ToList();

                if (watchmodeItems.Count == 0) return;

                if (HttpHelper.IsRateLimited)
                {
                    System.Diagnostics.Debug.WriteLine("WatchmodeSyncService: Proxy rate limited. Skipping sync.");
                    return;
                }

                // 1. Try querying the /changes/ endpoint for the last 2 days
                string startDate = DateTime.Today.AddDays(-2).ToString("yyyyMMdd");
                string endDate = DateTime.Today.ToString("yyyyMMdd");

                var changesResponse = await _watchmodeService.GetChangesAsync(startDate, endDate);

                if (changesResponse != null)
                {
                    // Changes endpoint was queried successfully
                    if (changesResponse.Changes != null && changesResponse.Changes.Count > 0)
                    {
                        bool libraryModified = false;
                        foreach (var item in watchmodeItems)
                        {
                            if (int.TryParse(item.Id, out int watchmodeId))
                            {
                                var matchedChange = changesResponse.Changes.FirstOrDefault(c => c.Id == watchmodeId);
                                if (matchedChange != null)
                                {
                                    if (HttpHelper.IsRateLimited) break;
                                    var details = await _watchmodeService.GetDetailsAsync(watchmodeId);
                                    if (details != null)
                                    {
                                        item.Title = details.Title ?? item.Title;
                                        item.PosterUrl = details.DisplayPoster ?? item.PosterUrl;
                                        item.Subtitle = details.Year?.ToString() ?? item.Subtitle;
                                        libraryModified = true;
                                    }
                                }
                            }
                        }

                        if (libraryModified)
                        {
                            AppServices.StreamingLibrary.Save();
                        }
                    }

                    // Successfully synchronized via /changes/
                    return;
                }

                // 2. Fallback: If changes endpoint was unavailable (e.g. 401/404) and NOT rate limited,
                // run a lightweight, sequential details-sync for local cache items.
                if (!HttpHelper.IsRateLimited)
                {
                    System.Diagnostics.Debug.WriteLine("WatchmodeSyncService: Changes endpoint unavailable. Falling back to lightweight direct sync.");
                    bool libraryModified = false;

                    foreach (var item in watchmodeItems)
                    {
                        if (HttpHelper.IsRateLimited)
                        {
                            System.Diagnostics.Debug.WriteLine("WatchmodeSyncService: Rate limit encountered during direct sync. Aborting remaining items.");
                            break;
                        }

                        if (int.TryParse(item.Id, out int watchmodeId))
                        {
                            var details = await _watchmodeService.GetDetailsAsync(watchmodeId);
                            if (details != null)
                            {
                                bool itemChanged = false;
                                if (item.Title != details.Title && !string.IsNullOrEmpty(details.Title))
                                {
                                    item.Title = details.Title;
                                    itemChanged = true;
                                }
                                var displayPoster = details.DisplayPoster;
                                if (item.PosterUrl != displayPoster && !string.IsNullOrEmpty(displayPoster))
                                {
                                    item.PosterUrl = displayPoster;
                                    itemChanged = true;
                                }
                                var displayYear = details.Year?.ToString();
                                if (item.Subtitle != displayYear && !string.IsNullOrEmpty(displayYear))
                                {
                                    item.Subtitle = displayYear;
                                    itemChanged = true;
                                }

                                if (itemChanged)
                                {
                                    libraryModified = true;
                                }
                            }

                            // Avoid hitting API rate limit
                            await Task.Delay(500);
                        }
                    }

                    if (libraryModified)
                    {
                        AppServices.StreamingLibrary.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WatchmodeSyncService Sync Error: {ex.Message}");
            }
        }
    }
}
