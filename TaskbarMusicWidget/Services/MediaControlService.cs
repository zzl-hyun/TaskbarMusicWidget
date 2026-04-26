using System.Threading.Tasks;
using Windows.Media.Control;

namespace TaskbarMusicWidget.Services;

public sealed class MediaControlService
{
    public sealed record PlaybackSnapshot(
        bool HasSession,
        bool CanPrevious,
        bool CanTogglePlayPause,
        bool CanNext,
        bool IsPlaying);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    private GlobalSystemMediaTransportControlsSession? GetSession()
    {
        return _manager?.GetCurrentSession();
    }

    public Task<PlaybackSnapshot> GetSnapshotAsync()
    {
        var session = GetSession();
        if (session is null)
        {
            return Task.FromResult(new PlaybackSnapshot(false, false, false, false, false));
        }

        var playbackInfo = session.GetPlaybackInfo();
        var controls = playbackInfo?.Controls;
        var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        var snapshot = new PlaybackSnapshot(
            true,
            controls?.IsPreviousEnabled == true,
            controls?.IsPlayPauseToggleEnabled == true,
            controls?.IsNextEnabled == true,
            isPlaying);

        return Task.FromResult(snapshot);
    }

    public async Task PreviousAsync()
    {
        var session = GetSession();
        if (session is null) return;

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls?.IsPreviousEnabled == true)
        {
            await session.TrySkipPreviousAsync();
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        var session = GetSession();
        if (session is null) return;

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls?.IsPlayPauseToggleEnabled == true)
        {
            await session.TryTogglePlayPauseAsync();
        }
    }

    public async Task NextAsync()
    {
        var session = GetSession();
        if (session is null) return;

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls?.IsNextEnabled == true)
        {
            await session.TrySkipNextAsync();
        }
    }
}