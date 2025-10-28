using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace KioskApp;

/// <summary>
/// Local HTTP server running on http://127.0.0.1:8787
/// Accepts POST /navigate with JSON body: { "url": "https://example.com" }
/// </summary>
public static class LocalCommandServer
{
    private static HttpListener? _listener;
    private static MainWindow? _mainWindow;

    public static async Task StartAsync(MainWindow window)
    {
        _mainWindow = window;
        _listener = new HttpListener();
        
        try
        {
            _listener.Prefixes.Add("http://127.0.0.1:8787/");
            _listener.Start();
            
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error handling request: {ex.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start command server: {ex.Message}");
        }
    }

    private static async void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Only accept POST to /navigate
            if (request.HttpMethod == "POST" && request.Url?.LocalPath == "/navigate")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                
                var command = JsonSerializer.Deserialize<NavigateCommand>(body, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (command?.Url != null && Uri.TryCreate(command.Url, UriKind.Absolute, out _))
                {
                    _mainWindow?.NavigateToUrl(command.Url);
                    
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
                await WriteResponse(response, new { success = false, message = "Not found. Use POST /navigate" });
            }
        }
        catch (Exception ex)
        {
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
            _listener.Stop();
            _listener.Close();
            _listener = null;
        }
    }

    private class NavigateCommand
    {
        public string? Url { get; set; }
    }
}

