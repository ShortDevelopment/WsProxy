
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

Option<IPEndPoint> remoteEndpointOption = new("--remote", "Endpoint that hosts the tcp server")
{
    IsRequired = true,
};
remoteEndpointOption.AddAlias("-r");
Option<int> portOption = new("--port", "Port that should host the ws server")
{
    IsRequired = true
};
portOption.AddAlias("-p");

RootCommand command = new("Receive only tcp -> ws proxy");
command.AddOption(remoteEndpointOption);
command.AddOption(portOption);
command.SetHandler(RunServerAsync, remoteEndpointOption, portOption);
await command.InvokeAsync(args);

async Task RunServerAsync(IPEndPoint remoteEndpoint, int localPort)
{
    var app = WebApplication.Create(args);
    app.UseWebSockets();

    app.Map("/ws", async (context) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await HandleSocketAsync(remoteEndpoint, socket);
    });

    await app.RunAsync($"http://localhost:{localPort}");
}

static async ValueTask HandleSocketAsync(IPEndPoint remoteEndpoint, WebSocket socket, CancellationToken cancellationToken = default)
{
    using TcpClient client = new();
    await client.ConnectAsync(remoteEndpoint);

    var buffer = new byte[1024];

    using var stream = client.GetStream();
    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
    {
        if (!stream.DataAvailable)
        {
            await Task.Delay(1, cancellationToken);
            continue;
        }

        var writtenBytes = await stream.ReadAsync(buffer, cancellationToken);
        await socket.SendAsync(buffer.AsMemory()[0..writtenBytes], WebSocketMessageType.Binary, true, cancellationToken);
    }
    Console.WriteLine("Closed connection");
}