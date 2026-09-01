using System.Net.WebSockets;

using var ws = new ClientWebSocket();

var uri =
    new Uri(
        "wss://mahdiasadi.bsite.net/tunnel?target=httpbin.org:443");

Console.WriteLine("Connecting...");

await ws.ConnectAsync(uri, CancellationToken.None);

Console.WriteLine("WEBSOCKET CONNECTED");

await ws.CloseAsync(
    WebSocketCloseStatus.NormalClosure,
    "test",
    CancellationToken.None);

Console.WriteLine("DONE");