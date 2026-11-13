using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Services;

public interface IConfigService
{
    AppConfig Config { get; }
}
