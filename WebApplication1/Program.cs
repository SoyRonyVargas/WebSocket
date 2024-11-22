using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.IO;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors(builder =>
    builder.AllowAnyOrigin()
           .AllowAnyMethod()
           .AllowAnyHeader());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};
app.UseWebSockets(webSocketOptions);

var activeSockets = new ConcurrentBag<WebSocket>();
bool isReady = false;
var uploadedFiles = new List<string>();

app.MapGet("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        activeSockets.Add(webSocket);

        await SendMessage(webSocket, $"{isReady}");

        await HandleWebSocketAsync(webSocket, activeSockets);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.MapPost("/toggle-flag", async context =>
{
    isReady = !isReady;

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

static async Task SendMessage(WebSocket socket, string message)
{
    var buffer = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
}

async Task HandleWebSocketAsync(WebSocket webSocket, ConcurrentBag<WebSocket> activeSockets)
{
    var buffer = new byte[1024 * 4];
    try
    {
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var clientMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Mensaje del cliente: {clientMessage}");
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                Console.WriteLine("Recibiendo archivo...");
                var fileData = new byte[result.Count];
                Array.Copy(buffer, fileData, result.Count);

                // Intentar detectar el tipo de archivo
                var fileExtension = GetFileExtension(fileData); // Obtener la extensión del archivo
                if (fileExtension == null)
                {
                    await SendMessage(webSocket, "No se pudo detectar el tipo de archivo.");
                    return;
                }

                // Generar un nombre único para el archivo
                var fileName = $"file_{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles", fileName);

                var fileDirectory = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles");
                if (!Directory.Exists(fileDirectory))
                {
                    Directory.CreateDirectory(fileDirectory);
                }

                await File.WriteAllBytesAsync(filePath, fileData);
                uploadedFiles.Add(fileName);

                Console.WriteLine($"Archivo guardado como {fileName}");
                await SendMessage(webSocket, $"Archivo {fileName} recibido y guardado.");
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
        activeSockets.TryTake(out _);
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cierre normal", CancellationToken.None);
    }
}

// Método para intentar detectar el tipo de archivo basado en los primeros bytes
string GetFileExtension(byte[] fileData)
{

    // Verifica los primeros bytes para detectar los tipos más comunes
    if (fileData[0] == 137 && fileData[1] == 80 && fileData[2] == 78 && fileData[3] == 71)
    {
        return ".png";  // PNG
    }
    if (fileData[0] == 255 && fileData[1] == 216 && fileData[2] == 255)
    {
        return ".jpg";  // JPEG
    }
    if (fileData[0] == 37 && fileData[1] == 80 && fileData[2] == 68 && fileData[3] == 70)
    {
        return ".pdf";  // PDF
    }
    if (fileData[0] == 80 && fileData[1] == 75 && fileData[2] == 3 && fileData[3] == 4)
    {
        return ".zip";  // ZIP
    }

    // Si no se detecta, devuelve null (puedes agregar más tipos según sea necesario)
    return null;
}
