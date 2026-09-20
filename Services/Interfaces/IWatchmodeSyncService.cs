using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services.Streaming;

public interface IWatchmodeSyncService
{
    Task SyncLibraryAsync();
}
