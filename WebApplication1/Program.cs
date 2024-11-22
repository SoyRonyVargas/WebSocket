using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors(builder =>
    builder.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader());
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Habilitar WebSockets
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};
app.UseWebSockets(webSocketOptions);

// Variables compartidas
var activeSockets = new ConcurrentBag<WebSocket>();
bool isReady = false;

// Mapear el endpoint WebSocket
app.MapGet("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        activeSockets.Add(webSocket);

        // Enviar estado inicial de la bandera
        await SendMessage(webSocket, $"{isReady}");

        // Manejar mensajes del cliente
        await HandleWebSocketAsync(webSocket, activeSockets);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

// Endpoint para cambiar el estado de la bandera
app.MapPost("/toggle-flag", async context =>
{
    
    isReady = !isReady;

    // Notificar a todos los usuarios conectados
    foreach (var socket in activeSockets)
    {
        if (socket.State == WebSocketState.Open)
        {
            await SendMessage(socket, $"{isReady}");
        }
    }

    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("Flag toggled!");
});

app.MapControllers();

app.Run();

// Métodos auxiliares
static async Task SendMessage(WebSocket socket, string message)
{
    var buffer = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
}

static async Task HandleWebSocketAsync(WebSocket webSocket, ConcurrentBag<WebSocket> activeSockets)
{
    var buffer = new byte[1024 * 4];
    try
    {
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            // Procesar mensajes del cliente
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var clientMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Mensaje del cliente: {clientMessage}");
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine("Cliente desconectado.");
                break;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    finally
    {
        activeSockets.TryTake(out _); // Eliminar socket desconectado
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cierre normal", CancellationToken.None);
    }
}
