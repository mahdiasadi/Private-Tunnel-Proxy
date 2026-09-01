
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

var ProxyKey = builder.Configuration["ProxyKey"];
// ============================================================
// CONFIG
// ============================================================



const int MaxSessions = 30;

const int MaxQueuedBytes = 8 * 1024 * 1024;

const int SessionTimeoutSeconds = 180;


// ============================================================
// SESSION STORE
// ============================================================

var sessions =
    new ConcurrentDictionary<string, TunnelSession>();


// ============================================================
// HOME
// ============================================================

app.MapGet("/", () =>
{
    Cleanup();

    return Results.Json(new
    {
        success = true,
        message = "Private Tunnel Proxy V3",
        sessions = sessions.Count,
        utc = DateTime.UtcNow
    });
});


// ============================================================
// HEALTH
// ============================================================

app.MapGet("/health", () =>
{
    Cleanup();

    return Results.Json(new
    {
        success = true,
        sessions = sessions.Count,
        utc = DateTime.UtcNow
    });
});


// ============================================================
// CREATE SESSION
// ============================================================

app.MapPost("/tunnel/create", async (
    HttpContext context) =>
{
    if (!Authorize(context))
    {
        context.Response.StatusCode = 401;
        return;
    }


    Cleanup();


    if (sessions.Count >= MaxSessions)
    {
        context.Response.StatusCode = 429;

        await context.Response.WriteAsync(
            "Maximum sessions reached");

        return;
    }


    var host =
        context.Request.Headers[
            "X-Target-Host"]
        .ToString()
        .Trim();


    var portText =
        context.Request.Headers[
            "X-Target-Port"]
        .ToString()
        .Trim();


    if (string.IsNullOrWhiteSpace(host))
    {
        context.Response.StatusCode = 400;

        await context.Response.WriteAsync(
            "Missing target host");

        return;
    }


    if (!int.TryParse(
            portText,
            out var port))
    {
        context.Response.StatusCode = 400;

        await context.Response.WriteAsync(
            "Invalid target port");

        return;
    }


    // --------------------------------------------------------
    // Only HTTP / HTTPS
    // --------------------------------------------------------

    if (port != 80 && port != 443)
    {
        context.Response.StatusCode = 403;

        await context.Response.WriteAsync(
            "Only ports 80 and 443 are allowed");

        return;
    }


    // --------------------------------------------------------
    // Resolve
    // --------------------------------------------------------

    IPAddress[] addresses;

    try
    {
        addresses =
            await Dns.GetHostAddressesAsync(host);
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 502;

        await context.Response.WriteAsync(
            "DNS error: " + ex.Message);

        return;
    }


    var ip =
        addresses.FirstOrDefault(
            x =>
                x.AddressFamily ==
                AddressFamily.InterNetwork &&
                !IsPrivate(x));


    if (ip == null)
    {
        context.Response.StatusCode = 403;

        await context.Response.WriteAsync(
            "No usable public IPv4 address");

        return;
    }


    // --------------------------------------------------------
    // TCP connect
    // --------------------------------------------------------

    TcpClient? client = null;


    try
    {
        client =
            new TcpClient();

        client.NoDelay = true;


        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(15));


        await client.ConnectAsync(
            ip,
            port,
            timeout.Token);


        var id =
            Guid.NewGuid()
                .ToString("N");


        var session =
            new TunnelSession(
                id,
                client);


        if (!sessions.TryAdd(
                id,
                session))
        {
            client.Dispose();

            context.Response.StatusCode = 500;
            return;
        }


        // ----------------------------------------------------
        // Background reader:
        //
        // Target -> Queue
        // ----------------------------------------------------

        _ = Task.Run(
            () => ReadTargetAsync(session));


        context.Response.ContentType =
            "text/plain";


        context.Response.Headers.CacheControl =
            "no-store";


        await context.Response.WriteAsync(
            id);


        Console.WriteLine(
            $"CREATE {id} " +
            $"{host}:{port}");
    }
    catch (Exception ex)
    {
        client?.Dispose();

        context.Response.StatusCode = 502;

        await context.Response.WriteAsync(
            ex.Message);
    }
});


// ============================================================
// SEND
// Desktop -> Server -> Target
// ============================================================

app.MapPost(
    "/tunnel/{id}/send",
    async (
        HttpContext context,
        string id) =>
    {
        if (!Authorize(context))
        {
            context.Response.StatusCode = 401;
            return;
        }


        if (!sessions.TryGetValue(
                id,
                out var session))
        {
            context.Response.StatusCode = 404;
            return;
        }


        if (session.IsClosed)
        {
            context.Response.StatusCode = 410;
            return;
        }


        try
        {
            session.Touch();


            // One writer per session.
            await session.WriteLock.WaitAsync(
                context.RequestAborted);


            try
            {
                await context.Request.Body.CopyToAsync(
                    session.Stream,
                    context.RequestAborted);


                await session.Stream.FlushAsync(
                    context.RequestAborted);
            }
            finally
            {
                session.WriteLock.Release();
            }


            session.Touch();


            context.Response.StatusCode = 204;
        }
        catch (OperationCanceledException)
        {
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"SEND {id}: {ex.Message}");

            CloseSession(id);

            context.Response.StatusCode = 502;
        }
    });


// ============================================================
// RECEIVE
//
// Target -> Server -> Desktop
//
// Long polling.
// ============================================================

app.MapGet(
    "/tunnel/{id}/receive",
    async (
        HttpContext context,
        string id) =>
    {
        if (!Authorize(context))
        {
            context.Response.StatusCode = 401;
            return;
        }


        if (!sessions.TryGetValue(
                id,
                out var session))
        {
            context.Response.StatusCode = 404;
            return;
        }


        session.Touch();


        // --------------------------------------------------------
        // Wait up to 25 seconds.
        // --------------------------------------------------------

        var end =
            DateTime.UtcNow.AddSeconds(25);


        while (
            DateTime.UtcNow < end &&
            !context.RequestAborted.IsCancellationRequested)
        {
            if (session.TryDequeue(
                    out var data))
            {
                context.Response.StatusCode = 200;

                context.Response.ContentType =
                    "application/octet-stream";

                context.Response.ContentLength =
                    data!.Length;

                context.Response.Headers.CacheControl =
                    "no-store, no-cache";


                await context.Response.Body.WriteAsync(
                    data,
                    context.RequestAborted);


                await context.Response.Body.FlushAsync(
                    context.RequestAborted);


                session.Touch();

                return;
            }


            if (session.IsClosed)
            {
                context.Response.StatusCode = 204;
                return;
            }


            await Task.Delay(
                40,
                context.RequestAborted);
        }


        context.Response.StatusCode = 204;
    });


// ============================================================
// CLOSE
// ============================================================

app.MapPost(
    "/tunnel/{id}/close",
    (HttpContext context, string id) =>
    {
        if (!Authorize(context))
            return Results.Unauthorized();


        CloseSession(id);


        return Results.Ok(
            new
            {
                success = true
            });
    });


// ============================================================
// START
// ============================================================

app.Run();


// ============================================================
// READ TARGET
// ============================================================

async Task ReadTargetAsync(
    TunnelSession session)
{
    var buffer =
        new byte[32 * 1024];


    try
    {
        while (!session.IsClosed)
        {
            var count =
                await session.Stream.ReadAsync(
                    buffer.AsMemory());


            if (count <= 0)
                break;


            if (
                session.QueuedBytes +
                count >
                MaxQueuedBytes)
            {
                Console.WriteLine(
                    $"QUEUE LIMIT {session.Id}");

                break;
            }


            var data =
                new byte[count];


            Buffer.BlockCopy(
                buffer,
                0,
                data,
                0,
                count);


            session.Enqueue(data);

            session.Touch();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"READ {session.Id}: " +
            ex.Message);
    }
    finally
    {
        session.IsClosed = true;

        Console.WriteLine(
            $"TARGET CLOSED {session.Id}");
    }
}


// ============================================================
// AUTH
// ============================================================

bool Authorize(
    HttpContext context)
{
    var key =
        context.Request.Headers[
            "X-Proxy-Key"]
        .ToString();


    return string.Equals(
        key,
        ProxyKey,
        StringComparison.Ordinal);
}


// ============================================================
// CLEANUP
// ============================================================

void Cleanup()
{
    var now =
        DateTime.UtcNow;


    foreach (var item in sessions)
    {
        var session =
            item.Value;


        if (
            now -
            session.LastActivity >
            TimeSpan.FromSeconds(
                SessionTimeoutSeconds))
        {
            CloseSession(item.Key);
        }
    }
}


// ============================================================
// CLOSE
// ============================================================

void CloseSession(
    string id)
{
    if (sessions.TryRemove(
            id,
            out var session))
    {
        session.Dispose();

        Console.WriteLine(
            $"CLOSE {id}");
    }
}


// ============================================================
// PRIVATE IP CHECK
// ============================================================

bool IsPrivate(
    IPAddress ip)
{
    if (IPAddress.IsLoopback(ip))
        return true;


    if (
        ip.AddressFamily !=
        AddressFamily.InterNetwork)
    {
        return false;
    }


    var b =
        ip.GetAddressBytes();


    // 10.0.0.0/8
    if (b[0] == 10)
        return true;


    // 172.16.0.0/12
    if (
        b[0] == 172 &&
        b[1] >= 16 &&
        b[1] <= 31)
    {
        return true;
    }


    // 192.168.0.0/16
    if (
        b[0] == 192 &&
        b[1] == 168)
    {
        return true;
    }


    // Link local
    if (
        b[0] == 169 &&
        b[1] == 254)
    {
        return true;
    }


    return false;
}


// ============================================================
// SESSION
// ============================================================

sealed class TunnelSession : IDisposable
{
    private readonly ConcurrentQueue<byte[]> queue =
        new();


    private long queuedBytes;


    public string Id
    {
        get;
    }


    public TcpClient Client
    {
        get;
    }


    public NetworkStream Stream
    {
        get;
    }


    public SemaphoreSlim WriteLock
    {
        get;
    } = new(1, 1);


    public volatile bool IsClosed;


    public DateTime LastActivity
    {
        get;
        private set;
    }


    public long QueuedBytes =>
        Interlocked.Read(
            ref queuedBytes);


    public TunnelSession(
        string id,
        TcpClient client)
    {
        Id = id;

        Client = client;

        Stream =
            client.GetStream();

        LastActivity =
            DateTime.UtcNow;
    }


    public void Touch()
    {
        LastActivity =
            DateTime.UtcNow;
    }


    public void Enqueue(
        byte[] data)
    {
        queue.Enqueue(data);


        Interlocked.Add(
            ref queuedBytes,
            data.Length);
    }


    public bool TryDequeue(
        out byte[]? data)
    {
        if (queue.TryDequeue(
                out data))
        {
            Interlocked.Add(
                ref queuedBytes,
                -data.Length);

            return true;
        }


        data = null;

        return false;
    }


    public void Dispose()
    {
        if (IsClosed)
        {
            // Continue cleanup.
        }


        IsClosed = true;


        try
        {
            Stream.Close();
        }
        catch
        {
        }


        try
        {
            Client.Close();
        }
        catch
        {
        }


        WriteLock.Dispose();


        while (
            queue.TryDequeue(
                out var data))
        {
            Interlocked.Add(
                ref queuedBytes,
                -data.Length);
        }
    }
}

