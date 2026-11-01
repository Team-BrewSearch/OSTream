using DiscordRPC;
using OSTream.Core.Models;
using OSTream.Core.Player;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace OSTream.Frontend.Services;

internal sealed class DiscordPresenceService : IDisposable
{
    private const string AppId = "1555014150030237777";
    private const double PositionDriftThresholdSeconds = 2;
    private const int ReconnectDelayMs = 5000;
    private const int MaxReconnectDelayMs = 60000;

    private readonly AppServices _app;
    private readonly ILogger _log;
    private readonly object _presenceLock = new();
    private DiscordRpcClient? _client;
    private bool _disposed;
    private DateTime? _presenceStart;
    private int _reconnectDelay = ReconnectDelayMs;
    private Timer? _reconnectTimer;

    public DiscordPresenceService(AppServices app, ILogger log)
    {
        _app = app;
        _log = log;

        app.Player.Changed += OnPlayerChanged;
        app.Player.TimeChanged += OnTimeChanged;

        _client = new DiscordRpcClient(AppId);
        _client.OnReady += (_, _) =>
        {
            _log.LogInformation("Discord Rich Presence connected");
            _reconnectDelay = ReconnectDelayMs;
            UpdatePresence();
        };
        _client.OnError += (_, e) => _log.LogWarning("Discord RPC error: {Error}", e.Message);
        _client.OnClose += (_, _) =>
        {
            if (_disposed)
            {
                return;
            }

            _log.LogWarning("Discord RPC connection closed");
            if (_client is { } old)
            {
                _client = null;
                old.Dispose();
            }
            ScheduleReconnect();
        };

        try
        {
            _client.Initialize();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to initialize Discord RPC");
            _client.Dispose();
            _client = null;
            ScheduleReconnect();
        }
    }

    private void ScheduleReconnect()
    {
        if (_disposed || _reconnectTimer is not null)
        {
            return;
        }

        _log.LogInformation("Scheduling Discord RPC reconnect in {Delay}ms", _reconnectDelay);
        _reconnectTimer = new Timer(_ =>
        {
            if (_disposed)
            {
                return;
            }

            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            Connect();
        }, null, _reconnectDelay, Timeout.Infinite);

        _reconnectDelay = Math.Min(_reconnectDelay * 2, MaxReconnectDelayMs);
    }

    private void Connect()
    {
        if (_disposed || _client is not null)
        {
            return;
        }

        _log.LogInformation("Attempting to connect to Discord RPC...");
        _client = new DiscordRpcClient(AppId);
        _client.OnReady += (_, _) =>
        {
            _log.LogInformation("Discord Rich Presence reconnected");
            _reconnectDelay = ReconnectDelayMs;
            UpdatePresence();
        };
        _client.OnError += (_, e) => _log.LogWarning("Discord RPC error: {Error}", e.Message);
        _client.OnClose += (_, _) =>
        {
            if (_disposed)
            {
                return;
            }

            _log.LogWarning("Discord RPC connection closed");
            if (_client is { } old)
            {
                _client = null;
                old.Dispose();
            }
            ScheduleReconnect();
        };

        try
        {
            _client.Initialize();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord RPC connection attempt failed");
            _client.Dispose();
            _client = null;
            ScheduleReconnect();
        }
    }

    private void OnPlayerChanged()
    {
        UpdatePresence();
    }

    private void OnTimeChanged(PlaybackTime time)
    {
        lock (_presenceLock)
        {
            if (_disposed || _client is null || !_client.IsInitialized || _app.Player.Status != PlayStatus.Playing)
            {
                return;
            }

            if (_presenceStart is not { } start || Math.Abs(time.Current - (DateTime.UtcNow - start).TotalSeconds) > PositionDriftThresholdSeconds)
            {
                UpdatePresence();
            }
        }
    }

    private void UpdatePresence()
    {
        lock (_presenceLock)
        {
            DiscordRpcClient? client = _client;
            if (_disposed || client is null || !client.IsInitialized)
            {
                return;
            }

            try
            {
                var activity = BuildActivity();
                if (activity is null)
                {
                    client.ClearPresence();
                    _presenceStart = null;
                }
                else
                {
                    client.SetPresence(activity);
                    _presenceStart = activity.Timestamps?.Start;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Discord presence update failed");
            }
        }
    }

    private RichPresence? BuildActivity()
    {
        var player = _app.Player;
        var track = player.Current;

        try
        {
            if (track is null || player.Status == PlayStatus.Idle)
            {
                var idle = new RichPresence();
                idle.Details = "Idle";
                idle.Assets = new Assets();
                idle.Assets.LargeImageKey = "logo";
                idle.Assets.LargeImageText = "OSTream";
                return idle;
            }

            var presence = new RichPresence();
            presence.Assets = new Assets();
            presence.Assets.LargeImageKey = "logo";
            presence.Assets.LargeImageText = "OSTream";

            string? albumTitle = track.Album?.Title;
            string details = string.IsNullOrWhiteSpace(track.Title) ? "Unknown Track" : track.Title;

            switch (player.Status)
            {
                case PlayStatus.Loading:
                    presence.Details = details;
                    presence.State = "Loading…";
                    break;
                case PlayStatus.Playing:
                    presence.Details = details;
                    presence.State = string.IsNullOrWhiteSpace(albumTitle) ? "Unknown Album" : albumTitle;
                    presence.Timestamps = new Timestamps();
                    presence.Timestamps.Start = DateTime.UtcNow - TimeSpan.FromSeconds(Math.Clamp(player.Time.Current, 0, double.MaxValue));
                    break;
                case PlayStatus.Paused:
                    presence.Details = details;
                    presence.State = "Paused";
                    break;
                default:
                    return null;
            }

            return presence;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Discord BuildActivity failed: track={Track}, status={Status}", track?.Title, player.Status);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_presenceLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _app.Player.Changed -= OnPlayerChanged;
        _app.Player.TimeChanged -= OnTimeChanged;

        _reconnectTimer?.Dispose();
        _reconnectTimer = null;

        try
        {
            _client?.ClearPresence();
        }
        catch
        {
        }

        _client?.Dispose();
    }
}
