using System;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class WatchmodeSyncService
    {
        private readonly WatchmodeService _watchmodeService = new();

        public async Task SyncLibraryAsync()
        {
            try
            {
                var savedItems = AppServices.StreamingLibrary.SavedItems;
                var watchmodeItems = savedItems.Where(i => i.Type == StreamingItemType.Movie || i.Type == StreamingItemType.TvShow).ToList();

                if (watchmodeItems.Count == 0) return;

                // 1. Try querying the /changes/ endpoint for the last 2 days
                string startDate = DateTime.Today.AddDays(-2).ToString("yyyyMMdd");
                string endDate = DateTime.Today.ToString("yyyyMMdd");

                var changesResponse = await _watchmodeService.GetChangesAsync(startDate, endDate);
                
                bool syncSuccess = false;
                if (changesResponse?.Changes != null && changesResponse.Changes.Count > 0)
                {
                    bool libraryModified = false;
                    foreach (var item in watchmodeItems)
                    {
                        if (int.TryParse(item.Id, out int watchmodeId))
                        {
                            var matchedChange = changesResponse.Changes.FirstOrDefault(c => c.Id == watchmodeId);
                            if (matchedChange != null)
                            {
                                // Fetch updated details
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
                    syncSuccess = true;
                }

                // 2. Fallback: If changes endpoint was empty/failed (e.g. 401 Unauthorized), 
                // run a lightweight, sequential details-sync for local cache items.
                if (!syncSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("WatchmodeSyncService: Changes endpoint unavailable. Falling back to lightweight direct sync.");
                    bool libraryModified = false;

                    foreach (var item in watchmodeItems)
                    {
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
