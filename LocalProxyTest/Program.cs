
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;


// ============================================================
// CONFIG
// ============================================================



var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory()) 
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var configuration = builder.Build();

string ProxyKey = configuration["ProxyKey"];


string Backend = configuration["Host"];

const int LocalPort = 8888;


// ============================================================
// HTTP CLIENT
// ============================================================

using var http =
    new HttpClient
    {
        Timeout =
            TimeSpan.FromSeconds(30)
    };


// ============================================================
// LISTENER
// ============================================================

var listener =
    new TcpListener(
        IPAddress.Loopback,
        LocalPort);

listener.Start();


Console.WriteLine();
Console.WriteLine(
    "======================================");

Console.WriteLine(
    " Private Tunnel Proxy V3");

Console.WriteLine(
    "======================================");

Console.WriteLine();

Console.WriteLine(
    $"HTTP  : 127.0.0.1:{LocalPort}");

Console.WriteLine(
    $"HTTPS : 127.0.0.1:{LocalPort}");

Console.WriteLine(
    $"SOCKS5: 127.0.0.1:{LocalPort}");

Console.WriteLine();

Console.WriteLine(
    $"Backend: {Backend}");

Console.WriteLine();

Console.WriteLine(
    "Proxy is ready.");

Console.WriteLine();


// ============================================================
// ACCEPT CLIENTS
// ============================================================

while (true)
{
    var client =
        await listener.AcceptTcpClientAsync();


    client.NoDelay = true;


    _ = Task.Run(
        () =>
            HandleClientAsync(
                client));
}


// ============================================================
// CLIENT
// ============================================================

async Task HandleClientAsync(
    TcpClient client)
{
    try
    {
        using (client)
        {
            var stream =
                client.GetStream();


            var first =
                await ReadByteAsync(
                    stream);


            if (first < 0)
                return;


            // SOCKS5
            if (first == 0x05)
            {
                await HandleSocks5Async(
                    stream);

                return;
            }


            // HTTP
            await HandleHttpAsync(
                stream,
                (byte)first);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"CLIENT ERROR: {ex.Message}");
    }
}


// ============================================================
// HTTP
// ============================================================

async Task HandleHttpAsync(
    NetworkStream local,
    byte firstByte)
{
    var header =
        await ReadHeaderAsync(
            local,
            firstByte);


    if (string.IsNullOrWhiteSpace(header))
        return;


    var lines =
        header.Split(
            "\r\n",
            StringSplitOptions.None);


    var parts =
        lines[0].Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);


    if (parts.Length < 2)
    {
        await HttpErrorAsync(
            local,
            400,
            "Bad Request");

        return;
    }


    var method =
        parts[0].ToUpperInvariant();


    var target =
        parts[1];


    Console.WriteLine(
        $"HTTP {method} {target}");


    if (method == "CONNECT")
    {
        await HandleConnectAsync(
            local,
            target);

        return;
    }


    await HandleNormalHttpAsync(
        local,
        method,
        target,
        lines);
}


// ============================================================
// HTTPS CONNECT
// ============================================================

async Task HandleConnectAsync(
    NetworkStream local,
    string target)
{
    if (!TryParseHostPort(
            target,
            443,
            out var host,
            out var port))
    {
        await HttpErrorAsync(
            local,
            400,
            "Invalid CONNECT");

        return;
    }


    if (!AllowedPort(port))
    {
        await HttpErrorAsync(
            local,
            403,
            "Port not allowed");

        return;
    }


    Console.WriteLine(
        $"CONNECT -> {host}:{port}");


    var session =
        await CreateSessionAsync(
            host,
            port);


    if (session == null)
    {
        await HttpErrorAsync(
            local,
            502,
            "Tunnel creation failed");

        return;
    }


    var response =
        "HTTP/1.1 200 Connection Established\r\n" +
        "Proxy-Agent: PrivateTunnelProxyV3\r\n" +
        "\r\n";


    await local.WriteAsync(
        Encoding.ASCII.GetBytes(
            response));


    await local.FlushAsync();


    await TunnelAsync(
        local,
        session);
}


// ============================================================
// NORMAL HTTP
// ============================================================

async Task HandleNormalHttpAsync(
    NetworkStream local,
    string method,
    string target,
    string[] headers)
{
    if (!Uri.TryCreate(
            target,
            UriKind.Absolute,
            out var uri))
    {
        await HttpErrorAsync(
            local,
            400,
            "Invalid URL");

        return;
    }


    if (
        !uri.Scheme.Equals(
            "http",
            StringComparison.OrdinalIgnoreCase))
    {
        await HttpErrorAsync(
            local,
            400,
            "Use CONNECT for HTTPS");

        return;
    }


    var port =
        uri.Port > 0
            ? uri.Port
            : 80;


    if (!AllowedPort(port))
    {
        await HttpErrorAsync(
            local,
            403,
            "Port not allowed");

        return;
    }


    var session =
        await CreateSessionAsync(
            uri.Host,
            port);


    if (session == null)
    {
        await HttpErrorAsync(
            local,
            502,
            "Tunnel creation failed");

        return;
    }


    try
    {
        var request =
            BuildHttpRequest(
                method,
                uri,
                headers);


        await SendChunkAsync(
            session,
            Encoding.ASCII.GetBytes(
                request),
            CancellationToken.None);


        await ReceiveUntilClosedAsync(
            local,
            session);
    }
    finally
    {
        await CloseSessionAsync(
            session);
    }
}


// ============================================================
// CREATE
// ============================================================

async Task<string?> CreateSessionAsync(
    string host,
    int port)
{
    using var request =
        new HttpRequestMessage(
            HttpMethod.Post,
            Backend +
            "/tunnel/create");


    request.Headers.Add(
        "X-Proxy-Key",
        ProxyKey);


    request.Headers.Add(
        "X-Target-Host",
        host);


    request.Headers.Add(
        "X-Target-Port",
        port.ToString());


    try
    {
        using var response =
            await http.SendAsync(
                request);


        var text =
            await response.Content
                .ReadAsStringAsync();


        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(
                $"CREATE FAILED: " +
                $"{response.StatusCode} " +
                text);

            return null;
        }


        var session =
            text.Trim();


        if (string.IsNullOrWhiteSpace(
                session))
        {
            return null;
        }


        Console.WriteLine(
            $"SESSION {session}");


        return session;
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"CREATE ERROR: " +
            ex.Message);

        return null;
    }
}


// ============================================================
// TUNNEL
// ============================================================

async Task TunnelAsync(
    NetworkStream local,
    string session)
{
    using var cts =
        new CancellationTokenSource();


    var upload =
        UploadAsync(
            local,
            session,
            cts.Token);


    var download =
        DownloadAsync(
            local,
            session,
            cts.Token);


    await Task.WhenAny(
        upload,
        download);


    cts.Cancel();


    try
    {
        await Task.WhenAll(
            upload,
            download);
    }
    catch
    {
    }


    await CloseSessionAsync(
        session);
}


// ============================================================
// UPLOAD
// ============================================================

async Task UploadAsync(
    NetworkStream local,
    string session,
    CancellationToken cancellation)
{
    var buffer =
        new byte[32 * 1024];


    try
    {
        while (
            !cancellation.IsCancellationRequested)
        {
            var count =
                await local.ReadAsync(
                    buffer.AsMemory(),
                    cancellation);


            if (count == 0)
                break;


            var data =
                new byte[count];


            Buffer.BlockCopy(
                buffer,
                0,
                data,
                0,
                count);


            await SendChunkAsync(
                session,
                data,
                cancellation);
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"UPLOAD ERROR: {ex.Message}");
    }
}


// ============================================================
// SEND
// ============================================================

async Task SendChunkAsync(
    string session,
    byte[] data,
    CancellationToken cancellation)
{
    using var content =
        new ByteArrayContent(
            data);


    content.Headers.ContentType =
        new MediaTypeHeaderValue(
            "application/octet-stream");


    using var request =
        new HttpRequestMessage(
            HttpMethod.Post,
            Backend +
            "/tunnel/" +
            session +
            "/send");


    request.Headers.Add(
        "X-Proxy-Key",
        ProxyKey);


    request.Content =
        content;


    using var response =
        await http.SendAsync(
            request,
            cancellation);


    if (!response.IsSuccessStatusCode)
    {
        throw new IOException(
            "SEND failed: " +
            response.StatusCode);
    }
}


// ============================================================
// DOWNLOAD
// ============================================================

async Task DownloadAsync(
    NetworkStream local,
    string session,
    CancellationToken cancellation)
{
    try
    {
        while (
            !cancellation.IsCancellationRequested)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    Backend +
                    "/tunnel/" +
                    session +
                    "/receive");


            request.Headers.Add(
                "X-Proxy-Key",
                ProxyKey);


            using var response =
                await http.SendAsync(
                    request,
                    HttpCompletionOption
                        .ResponseHeadersRead,
                    cancellation);


            if (
                response.StatusCode ==
                HttpStatusCode.NoContent)
            {
                continue;
            }


            if (!response.IsSuccessStatusCode)
                break;


            var data =
                await response.Content
                    .ReadAsByteArrayAsync(
                        cancellation);


            if (data.Length == 0)
                continue;


            await local.WriteAsync(
                data,
                cancellation);


            await local.FlushAsync(
                cancellation);
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"DOWNLOAD ERROR: " +
            ex.Message);
    }
}


// ============================================================
// RECEIVE
// ============================================================

async Task ReceiveUntilClosedAsync(
    NetworkStream local,
    string session)
{
    try
    {
        while (true)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    Backend +
                    "/tunnel/" +
                    session +
                    "/receive");


            request.Headers.Add(
                "X-Proxy-Key",
                ProxyKey);


            using var response =
                await http.SendAsync(
                    request,
                    HttpCompletionOption
                        .ResponseHeadersRead);


            if (
                response.StatusCode ==
                HttpStatusCode.NoContent)
            {
                continue;
            }


            if (!response.IsSuccessStatusCode)
                break;


            var data =
                await response.Content
                    .ReadAsByteArrayAsync();


            if (data.Length == 0)
                continue;


            await local.WriteAsync(
                data);


            await local.FlushAsync();
        }
    }
    catch
    {
    }
}


// ============================================================
// CLOSE
// ============================================================

async Task CloseSessionAsync(
    string session)
{
    try
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                Backend +
                "/tunnel/" +
                session +
                "/close");


        request.Headers.Add(
            "X-Proxy-Key",
            ProxyKey);


        await http.SendAsync(
            request);
    }
    catch
    {
    }
}


// ============================================================
// SOCKS5
// ============================================================

async Task HandleSocks5Async(
    NetworkStream local)
{
    var methodsCount =
        await ReadByteAsync(
            local);


    if (methodsCount < 0)
        return;


    await ReadExactlyAsync(
        local,
        methodsCount);


    // No authentication
    await local.WriteAsync(
        new byte[]
        {
            0x05,
            0x00
        });


    await local.FlushAsync();


    var request =
        await ReadExactlyAsync(
            local,
            4);


    if (request[0] != 0x05)
        return;


    // CONNECT only
    if (request[1] != 0x01)
    {
        await SocksFailureAsync(
            local,
            0x07);

        return;
    }


    var type =
        request[3];


    string host;


    // IPv4
    if (type == 0x01)
    {
        var address =
            await ReadExactlyAsync(
                local,
                4);


        host =
            new IPAddress(
                address)
            .ToString();
    }

    // DOMAIN
    else if (type == 0x03)
    {
        var length =
            await ReadByteAsync(
                local);


        if (length <= 0)
            return;


        var domain =
            await ReadExactlyAsync(
                local,
                length);


        host =
            Encoding.ASCII.GetString(
                domain);
    }

    // IPv6
    else if (type == 0x04)
    {
        var address =
            await ReadExactlyAsync(
                local,
                16);


        host =
            new IPAddress(
                address)
            .ToString();
    }
    else
    {
        await SocksFailureAsync(
            local,
            0x08);

        return;
    }


    var portBytes =
        await ReadExactlyAsync(
            local,
            2);


    var port =
        (portBytes[0] << 8) |
        portBytes[1];


    Console.WriteLine(
        $"SOCKS5 -> {host}:{port}");


    if (!AllowedPort(port))
    {
        await SocksFailureAsync(
            local,
            0x02);

        return;
    }


    var session =
        await CreateSessionAsync(
            host,
            port);


    if (session == null)
    {
        await SocksFailureAsync(
            local,
            0x01);

        return;
    }


    await local.WriteAsync(
        new byte[]
        {
            0x05,
            0x00,
            0x00,
            0x01,

            0x00,
            0x00,
            0x00,
            0x00,

            0x00,
            0x00
        });


    await local.FlushAsync();


    await TunnelAsync(
        local,
        session);
}


// ============================================================
// HTTP REQUEST
// ============================================================

string BuildHttpRequest(
    string method,
    Uri uri,
    string[] headers)
{
    var path =
        string.IsNullOrWhiteSpace(
            uri.PathAndQuery)
            ? "/"
            : uri.PathAndQuery;


    var sb =
        new StringBuilder();


    sb.Append(method);
    sb.Append(' ');
    sb.Append(path);
    sb.Append(" HTTP/1.1\r\n");


    var hasHost = false;


    foreach (var line in headers)
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;


        var colon =
            line.IndexOf(':');


        if (colon <= 0)
            continue;


        var name =
            line[..colon]
                .Trim()
                .ToLowerInvariant();


        if (
            name == "proxy-connection" ||
            name == "proxy-authorization" ||
            name == "connection")
        {
            continue;
        }


        if (name == "host")
            hasHost = true;


        sb.Append(line);
        sb.Append("\r\n");
    }


    if (!hasHost)
    {
        sb.Append(
            "Host: ");


        sb.Append(
            uri.Host);


        sb.Append("\r\n");
    }


    sb.Append(
        "Connection: close\r\n");


    sb.Append(
        "\r\n");


    return sb.ToString();
}


// ============================================================
// BYTE
// ============================================================

async Task<int> ReadByteAsync(
    NetworkStream stream)
{
    var buffer =
        new byte[1];


    var count =
        await stream.ReadAsync(
            buffer,
            0,
            1);


    return count == 0
        ? -1
        : buffer[0];
}


// ============================================================
// EXACT
// ============================================================

async Task<byte[]> ReadExactlyAsync(
    NetworkStream stream,
    int count)
{
    var data =
        new byte[count];


    var offset = 0;


    while (offset < count)
    {
        var read =
            await stream.ReadAsync(
                data,
                offset,
                count - offset);


        if (read == 0)
        {
            throw new IOException(
                "Connection closed");
        }


        offset += read;
    }


    return data;
}


// ============================================================
// HEADER
// ============================================================

async Task<string> ReadHeaderAsync(
    NetworkStream stream,
    byte firstByte)
{
    var data =
        new List<byte>
        {
            firstByte
        };


    var buffer =
        new byte[1];


    while (data.Count < 64 * 1024)
    {
        var count =
            await stream.ReadAsync(
                buffer,
                0,
                1);


        if (count == 0)
            break;


        data.Add(
            buffer[0]);


        var n =
            data.Count;


        if (
            n >= 4 &&
            data[n - 4] == '\r' &&
            data[n - 3] == '\n' &&
            data[n - 2] == '\r' &&
            data[n - 1] == '\n')
        {
            break;
        }
    }


    return Encoding.ASCII.GetString(
        data.ToArray());
}


// ============================================================
// HTTP ERROR
// ============================================================

async Task HttpErrorAsync(
    NetworkStream stream,
    int code,
    string message)
{
    var body =
        Encoding.UTF8.GetBytes(
            message);


    var header =
        $"HTTP/1.1 {code} {message}\r\n" +
        "Content-Type: text/plain\r\n" +
        $"Content-Length: {body.Length}\r\n" +
        "Connection: close\r\n" +
        "\r\n";


    await stream.WriteAsync(
        Encoding.ASCII.GetBytes(
            header));


    await stream.WriteAsync(
        body);


    await stream.FlushAsync();
}


// ============================================================
// SOCKS FAILURE
// ============================================================

async Task SocksFailureAsync(
    NetworkStream stream,
    byte code)
{
    await stream.WriteAsync(
        new byte[]
        {
            0x05,
            code,
            0x00,
            0x01,

            0x00,
            0x00,
            0x00,
            0x00,

            0x00,
            0x00
        });


    await stream.FlushAsync();
}


// ============================================================
// HOST / PORT
// ============================================================

bool TryParseHostPort(
    string value,
    int defaultPort,
    out string host,
    out int port)
{
    host = "";
    port = defaultPort;


    value =
        value.Trim();


    if (value.StartsWith("["))
    {
        var end =
            value.IndexOf(']');


        if (end <= 0)
            return false;


        host =
            value.Substring(
                1,
                end - 1);


        if (
            value.Length > end + 1 &&
            value[end + 1] == ':')
        {
            if (!int.TryParse(
                    value[(end + 2)..],
                    out port))
            {
                return false;
            }
        }


        return true;
    }


    var colon =
        value.LastIndexOf(':');


    if (colon > 0)
    {
        host =
            value[..colon];


        if (!int.TryParse(
                value[(colon + 1)..],
                out port))
        {
            return false;
        }


        return true;
    }


    host =
        value;


    return !string.IsNullOrWhiteSpace(
        host);
}


// ============================================================
// PORT
// ============================================================

bool AllowedPort(
    int port)
{
    return port == 80 ||
           port == 443;
}

