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
        bool IsPlaying,
        string SessionDisplayName,
        string? TrackTitle,
        string? Artist);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    private GlobalSystemMediaTransportControlsSession? GetCurrentSession()
    {
        if (_manager is null)
        {
            return null;
        }

        var sessions = _manager.GetSessions();
        return sessions is null || sessions.Count == 0 ? null : _manager.GetCurrentSession() ?? sessions[0];
    }

    public async Task<PlaybackSnapshot> GetSnapshotAsync()
    {
        var session = GetCurrentSession();
        if (session is null)
        {
            return new PlaybackSnapshot(false, false, false, false, false, "No media session", null, null);
        }

        var playbackInfo = session.GetPlaybackInfo();
        var controls = playbackInfo?.Controls;
        var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        string? trackTitle = null;
        string? artist = null;
        try
        {
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            trackTitle = string.IsNullOrWhiteSpace(mediaProperties?.Title) ? null : mediaProperties!.Title;
            artist = string.IsNullOrWhiteSpace(mediaProperties?.Artist) ? null : mediaProperties!.Artist;
        }
        catch
        {
            // Metadata lookup can fail for some providers; controls still work.
        }

        var displayName = GetDisplayName(session.SourceAppUserModelId);

        return new PlaybackSnapshot(
            true,
            controls?.IsPreviousEnabled == true,
            controls?.IsPlayPauseToggleEnabled == true,
            controls?.IsNextEnabled == true,
            isPlaying,
            displayName,
            trackTitle,
            artist);
    }



    public async Task PreviousAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return;

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls?.IsPreviousEnabled == true)
        {
            await session.TrySkipPreviousAsync();
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return;

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls?.IsPlayPauseToggleEnabled == true)
        {
            await session.TryTogglePlayPauseAsync();
        }
    }

    public async Task NextAsync()
    {
        var session = GetCurrentSession();
        if (session is null) return;

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls?.IsNextEnabled == true)
        {
            await session.TrySkipNextAsync();
        }
    }

    private static string GetDisplayName(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return "Unknown app";
        }

        var raw = sourceAppUserModelId;
        var withoutAppSuffix = raw.Split('!')[0];
        var parts = withoutAppSuffix.Split('.', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return raw;
        }

        var candidate = parts[^1].Replace("_", " ");
        return string.IsNullOrWhiteSpace(candidate) ? raw : candidate;
    }


}