using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoSLCut.Timeline;
using MegaCrit.Sts2.Core.Logging;

namespace AutoSLCut.Obs;

internal static class ObsWebSocketRecordingWatcher
{
    private const int OutputEventsSubscription = 64;

    private static readonly object Sync = new();

    private static CancellationTokenSource? _cts;

    private static Task? _backgroundTask;

    private static bool _started;

    public static void Start()
    {
        if (!AutoSLCutSettings.EnableObsWebSocket)
        {
            Log.Info("[AutoSLCut] OBS WebSocket watcher disabled by settings.");
            return;
        }

        lock (Sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _cts = new CancellationTokenSource();
            _backgroundTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _cts?.Cancel();
        }
    }

    private static async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"[AutoSLCut] OaBS watcher error: {ex.Message}");
            }

            try
            {
                await Task.Delay(AutoSLCutSettings.ObsReconnectDelayMilliseconds, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task ConnectAndListenAsync(CancellationToken cancellationToken)
    {
        using ClientWebSocket socket = new ClientWebSocket();
        Uri endpoint = new Uri(AutoSLCutSettings.ObsWebSocketUrl);
        await socket.ConnectAsync(endpoint, cancellationToken);
        Log.Info($"[AutoSLCut] Connected to OBS WebSocket at {endpoint}");

        string? helloJson = await ReceiveTextMessageAsync(socket, cancellationToken);
        if (string.IsNullOrWhiteSpace(helloJson))
        {
            Log.Warn("[AutoSLCut] OBS hello message was empty.");
            return;
        }

        if (!TryBuildIdentifyPayload(helloJson, out object? identifyPayload, out string? identifyError))
        {
            Log.Warn($"[AutoSLCut] Failed to build OBS identify payload: {identifyError}");
            return;
        }

        await SendJsonAsync(socket, identifyPayload!, cancellationToken);

        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            string? json = await ReceiveTextMessageAsync(socket, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                break;
            }

            HandleMessage(json);
        }

        Log.Info("[AutoSLCut] OBS WebSocket disconnected.");
    }

    private static bool TryBuildIdentifyPayload(string helloJson, out object? payload, out string? error)
    {
        payload = null;
        error = null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(helloJson);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("op", out JsonElement opEl) || opEl.GetInt32() != 0)
            {
                error = "Unexpected hello opcode.";
                return false;
            }

            JsonElement data = root.GetProperty("d");
            int rpcVersion = data.GetProperty("rpcVersion").GetInt32();

            if (!data.TryGetProperty("authentication", out JsonElement authEl))
            {
                payload = new
                {
                    op = 1,
                    d = new
                    {
                        rpcVersion,
                        eventSubscriptions = OutputEventsSubscription
                    }
                };
                return true;
            }

            string challenge = authEl.GetProperty("challenge").GetString() ?? string.Empty;
            string salt = authEl.GetProperty("salt").GetString() ?? string.Empty;
            string auth = BuildAuthenticationString(AutoSLCutSettings.ObsWebSocketPassword, salt, challenge);

            payload = new
            {
                op = 1,
                d = new
                {
                    rpcVersion,
                    eventSubscriptions = OutputEventsSubscription,
                    authentication = auth
                }
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildAuthenticationString(string password, string salt, string challenge)
    {
        using SHA256 sha = SHA256.Create();
        byte[] secretBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
        string secret = Convert.ToBase64String(secretBytes);

        byte[] authBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authBytes);
    }

    private static void HandleMessage(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("op", out JsonElement opEl))
            {
                return;
            }

            int op = opEl.GetInt32();
            if (op != 5)
            {
                return;
            }

            JsonElement data = root.GetProperty("d");
            string eventType = data.GetProperty("eventType").GetString() ?? string.Empty;
            bool hasEventData = data.TryGetProperty("eventData", out JsonElement eventData);

            if (eventType == "RecordingStarted")
            {
                SLCutTimelineTracker.RecordRecordingStart("OBS.RecordingStarted");
                return;
            }

            if (eventType == "RecordingStopped")
            {
                string? outputPath = hasEventData ? TryGetOutputPath(eventData) : null;
                SLCutTimelineTracker.RecordRecordingEnd("OBS.RecordingStopped", outputPath);
                return;
            }

            if (eventType != "RecordStateChanged")
            {
                return;
            }

            if (!hasEventData)
            {
                return;
            }

            if (eventData.TryGetProperty("outputState", out JsonElement outputStateEl))
            {
                string outputState = outputStateEl.GetString() ?? string.Empty;
                if (outputState == "OBS_WEBSOCKET_OUTPUT_STARTED" || outputState == "OBS_WEBSOCKET_OUTPUT_RESUMED")
                {
                    SLCutTimelineTracker.RecordRecordingStart("OBS.RecordStateChanged");
                }
                else if (outputState == "OBS_WEBSOCKET_OUTPUT_STOPPED")
                {
                    string? outputPath = TryGetOutputPath(eventData);
                    SLCutTimelineTracker.RecordRecordingEnd("OBS.RecordStateChanged", outputPath);
                }

                return;
            }

            if (eventData.TryGetProperty("outputActive", out JsonElement outputActiveEl))
            {
                bool outputActive = outputActiveEl.GetBoolean();
                if (outputActive)
                {
                    SLCutTimelineTracker.RecordRecordingStart("OBS.RecordStateChanged");
                }
                else
                {
                    string? outputPath = TryGetOutputPath(eventData);
                    SLCutTimelineTracker.RecordRecordingEnd("OBS.RecordStateChanged", outputPath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[AutoSLCut] Failed to parse OBS message: {ex.Message}");
        }
    }

    private static string? TryGetOutputPath(JsonElement eventData)
    {
        if (eventData.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetStringProperty(eventData, "outputPath", out string? outputPath))
        {
            return outputPath;
        }

        if (TryGetStringProperty(eventData, "outputFile", out string? outputFile))
        {
            return outputFile;
        }

        if (TryGetStringProperty(eventData, "path", out string? path))
        {
            return path;
        }

        return null;
    }

    private static bool TryGetStringProperty(JsonElement jsonElement, string propertyName, out string? value)
    {
        value = null;
        if (!jsonElement.TryGetProperty(propertyName, out JsonElement propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? rawValue = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        value = rawValue;
        return true;
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string?> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        using MemoryStream stream = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
