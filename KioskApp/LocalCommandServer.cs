using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace KioskApp;

/// <summary>
/// Simple HTTP server running on http://127.0.0.1:8787
/// Provides /navigate endpoint for external systems to control kiosk URL.
/// This is the default mode - lightweight remote navigation control.
/// </summary>
public static class LocalCommandServer
{
    private static HttpListener? _listener;
    private static MainWindow? _mainWindow;
    private static bool _isRunning = false;

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    public static bool IsRunning => _isRunning;

    /// <summary>
    /// Gets the port this server listens on.
    /// </summary>
    public static int Port => 8787;

    public static async Task StartAsync(MainWindow window)
    {
        if (_isRunning)
        {
            Logger.Log("LocalCommandServer already running");
            return;
        }

        _mainWindow = window;
        _listener = new HttpListener();

        try
        {
            _listener.Prefixes.Add("http://127.0.0.1:8787/");
            _listener.Start();
            _isRunning = true;
            Logger.Log("LocalCommandServer started on http://127.0.0.1:8787");

            await Task.Run(async () =>
            {
                while (_listener != null && _listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        _ = Task.Run(() => HandleRequest(context));
                    }
                    catch (HttpListenerException)
                    {
                        // Listener was stopped
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Listener was disposed
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"LocalCommandServer error: {ex.Message}");
                    }
                }
            });
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access Denied
        {
            Logger.Log("LocalCommandServer failed: Access denied. Run as Administrator or configure URL ACL.");
            _isRunning = false;
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 183) // Port already in use
        {
            Logger.Log("LocalCommandServer failed: Port 8787 already in use.");
            _isRunning = false;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to start LocalCommandServer: {ex.Message}");
            _isRunning = false;
        }
    }

    private static async void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Health check
            if (request.HttpMethod == "GET" && request.Url?.LocalPath == "/health")
            {
                response.StatusCode = 200;
                await WriteResponse(response, new { status = "ok", server = "LocalCommandServer", port = 8787 });
                return;
            }

            // Navigate endpoint
            if (request.HttpMethod == "POST" && request.Url?.LocalPath == "/navigate")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                Logger.Log($"POST /navigate: {body}");

                var command = JsonSerializer.Deserialize<NavigateCommand>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (command?.Url != null && Uri.TryCreate(command.Url, UriKind.Absolute, out _))
                {
                    _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow.NavigateToUrl(command.Url));
                    Logger.Log($"Navigating to: {command.Url}");

                    response.StatusCode = 200;
                    await WriteResponse(response, new { success = true, message = $"Navigating to {command.Url}" });
                }
                else
                {
                    response.StatusCode = 400;
                    await WriteResponse(response, new { success = false, message = "Invalid URL" });
                }
            }
            else
            {
                response.StatusCode = 404;
                await WriteResponse(response, new {
                    success = false,
                    message = "Not found. Available endpoints: POST /navigate, GET /health"
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"LocalCommandServer handler error: {ex.Message}");
            response.StatusCode = 500;
            await WriteResponse(response, new { success = false, message = ex.Message });
        }
        finally
        {
            response.Close();
        }
    }

    private static async Task WriteResponse(HttpListenerResponse response, object data)
    {
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data);
        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    public static void Stop()
    {
        if (_listener != null)
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error stopping LocalCommandServer: {ex.Message}");
            }
            finally
            {
                _listener = null;
                _isRunning = false;
                Logger.Log("LocalCommandServer stopped");
            }
        }
    }

    private class NavigateCommand
    {
        public string? Url { get; set; }
    }
}
