using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OneRoomHealth.Hardware.Modules.Biamp;

/// <summary>
/// Thread-safe Telnet client for Biamp device communication.
/// Maintains a persistent connection with automatic reconnection.
/// </summary>
internal class BiampTelnetClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _ipAddress;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _isConnected = false;
    private bool _disposed = false;

    private const int ConnectionTimeoutMs = 5000;
    private const int CommandTimeoutMs = 1000;
    private const int ReadBufferSize = 4096;

    /// <summary>
    /// Gets whether the client is currently connected.
    /// </summary>
    public bool IsConnected => _isConnected && _client?.Connected == true;

    public BiampTelnetClient(
        ILogger logger,
        string ipAddress,
        int port,
        string username,
        string password)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ipAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
        _port = port;
        _username = username ?? "control";
        _password = password ?? "";
    }

    /// <summary>
    /// Connect to the Biamp device and authenticate.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BiampTelnetClient));

        try
        {
            _logger.LogDebug("Connecting to Biamp device at {IpAddress}:{Port}", _ipAddress, _port);

            _client = new TcpClient();

            // Connect with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectionTimeoutMs);

            await _client.ConnectAsync(_ipAddress, _port, timeoutCts.Token);
            _stream = _client.GetStream();

            // Login sequence
            // 1. Wait for "login:" prompt
            await ReadUntilAsync("login:", timeoutCts.Token);

            // 2. Send username
            await WriteLineAsync(_username);

            // 3. Wait for "Password:" prompt
            await ReadUntilAsync("Password:", timeoutCts.Token);

            // 4. Send password
            await WriteLineAsync(_password);

            // 5. Wait for command prompt ">"
            await ReadUntilAsync(">", timeoutCts.Token);

            _isConnected = true;
            _logger.LogInformation("Connected to Biamp device at {IpAddress}:{Port}", _ipAddress, _port);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Connection to Biamp device at {IpAddress}:{Port} timed out", _ipAddress, _port);
            Disconnect();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Biamp device at {IpAddress}:{Port}", _ipAddress, _port);
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Disconnect from the Biamp device.
    /// </summary>
    public void Disconnect()
    {
        _isConnected = false;

        try
        {
            _stream?.Close();
            _stream?.Dispose();
        }
        catch { }

        try
        {
            _client?.Close();
            _client?.Dispose();
        }
        catch { }

        _stream = null;
        _client = null;
    }

    /// <summary>
    /// Send a command to the Biamp device and return the parsed response value.
    /// Returns null on error or -ERR response.
    /// </summary>
    public async Task<string?> SendCommandAsync(string command, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BiampTelnetClient));

        await _commandLock.WaitAsync(ct);
        try
        {
            // Reconnect if needed
            if (!IsConnected)
            {
                _logger.LogDebug("Not connected, attempting reconnect before command");
                if (!await ConnectAsync(ct))
                {
                    return null;
                }
            }

            // Send command
            _logger.LogDebug("Sending command: {Command}", command);
            await WriteLineAsync(command);

            // Read response with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(CommandTimeoutMs);

            var response = await ReadLineAsync(timeoutCts.Token);
            _logger.LogDebug("Response: {Response}", response);

            return ParseResponse(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Command timed out: {Command}", command);
            Disconnect();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command failed: {Command}", command);
            Disconnect();
            return null;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Send a reboot command. This will disconnect the client after sending.
    /// </summary>
    public async Task<bool> SendRebootAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BiampTelnetClient));

        await _commandLock.WaitAsync(ct);
        try
        {
            if (!IsConnected)
            {
                if (!await ConnectAsync(ct))
                {
                    return false;
                }
            }

            _logger.LogInformation("Sending reboot command to Biamp device at {IpAddress}", _ipAddress);
            await WriteLineAsync("DEVICE reboot");

            // Try to read response briefly - device may disconnect immediately
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(500);
                await ReadLineAsync(timeoutCts.Token);
            }
            catch
            {
                // Expected - device may disconnect before responding
            }

            // Disconnect - device is rebooting
            Disconnect();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reboot command");
            Disconnect();
            return false;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Parse a Biamp response.
    /// Format: +OK {"value":"<value>"} or +OK {"value":<number>} or -ERR <message>
    /// </summary>
    private string? ParseResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        response = response.Trim();

        if (response.StartsWith("+OK"))
        {
            // Try to extract value from JSON
            // Pattern: "value":"<string>" or "value":<number>
            var stringMatch = Regex.Match(response, @"""value""\s*:\s*""([^""]+)""");
            if (stringMatch.Success)
            {
                return stringMatch.Groups[1].Value;
            }

            var numMatch = Regex.Match(response, @"""value""\s*:\s*([^,}\s]+)");
            if (numMatch.Success)
            {
                return numMatch.Groups[1].Value;
            }

            // No value field, just success
            return "OK";
        }
        else if (response.StartsWith("-ERR"))
        {
            _logger.LogWarning("Biamp returned error: {Response}", response);
            return null;
        }

        // Unknown format, return as-is
        return response;
    }

    /// <summary>
    /// Write a line to the Telnet stream.
    /// </summary>
    private async Task WriteLineAsync(string text)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        var bytes = Encoding.ASCII.GetBytes(text + "\n");
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
    }

    /// <summary>
    /// Read until a specific string is found.
    /// </summary>
    private async Task<string> ReadUntilAsync(string marker, CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        var buffer = new byte[ReadBufferSize];
        var result = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
            if (bytesRead == 0)
                throw new IOException("Connection closed by remote host");

            var text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            result.Append(text);

            if (result.ToString().Contains(marker))
                break;
        }

        return result.ToString();
    }

    /// <summary>
    /// Read a single line from the Telnet stream.
    /// </summary>
    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        var buffer = new byte[ReadBufferSize];
        var result = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
            if (bytesRead == 0)
                throw new IOException("Connection closed by remote host");

            var text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            result.Append(text);

            // Check for end of response (newline or prompt)
            var current = result.ToString();
            if (current.Contains('\n') || current.Contains('>'))
            {
                // Extract first complete line
                var lines = current.Split('\n');
                if (lines.Length > 0)
                {
                    return lines[0].TrimEnd('\r');
                }
            }
        }

        return result.ToString().TrimEnd('\r', '\n');
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disconnect();
        _commandLock.Dispose();
    }
}
