using Microsoft.Extensions.FileProviders;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.IO;
using System;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// Middleware para servir la carpeta "UploadedFiles" como pública
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads" // Ruta pública
});

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
    KeepAliveInterval = TimeSpan.FromSeconds(1200)
};
app.UseWebSockets(webSocketOptions);

// Cambiado a ConcurrentDictionary
var activeSockets = new ConcurrentDictionary<Guid, WebSocket>();
bool isReady = false;
var uploadedFiles = new List<string>();

app.MapGet("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var socketId = Guid.NewGuid();
        activeSockets.TryAdd(socketId, webSocket);

        await SendMessage(webSocket, $"{isReady}");

        await HandleWebSocketAsync(socketId, webSocket, activeSockets, context);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.MapPost("/toggle-flag", async context =>
{
    isReady = !isReady;

    foreach (var kvp in activeSockets)
    {
        var socket = kvp.Value;
        if (socket.State == WebSocketState.Open)
        {
            await SendMessage(socket, $"{isReady}");
        }
    }

    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("Flag toggled!");
});

app.MapGet("/", async context =>
{
    await context.Response.WriteAsync("Hello World");
});

app.MapGet("/download/{fileName}", async (HttpContext context, string fileName) =>
{
    var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedFiles");
    var filePath = Path.Combine(uploadsPath, fileName);

    if (!File.Exists(filePath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("Archivo no encontrado.");
        return;
    }

    context.Response.ContentType = "application/octet-stream";
    context.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

    await context.Response.SendFileAsync(filePath);
});

app.MapControllers();

app.Run();

static async Task SendMessage(WebSocket socket, string message)
{
    var buffer = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
}

async Task HandleWebSocketAsync(Guid socketId, WebSocket webSocket, ConcurrentDictionary<Guid, WebSocket> activeSockets, HttpContext context)
{
    var buffer = new byte[1024 * 4];
    using var memoryStream = new MemoryStream(); // Acumulará los datos binarios completos.

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

                // Agregar los datos recibidos al MemoryStream
                memoryStream.Write(buffer, 0, result.Count);

                // Si este es el último fragmento del archivo
                if (result.EndOfMessage)
                {
                    var fileData = memoryStream.ToArray(); // Convertir a un array completo de bytes.
                    memoryStream.SetLength(0); // Reiniciar el MemoryStream para próximos archivos.

                    var fileExtension = GetFileExtension(fileData);
                    if (fileExtension == null)
                    {
                        await SendMessage(webSocket, "No se pudo detectar el tipo de archivo.");
                        return;
                    }

                    var fileName = $"file_{Guid.NewGuid()}{fileExtension}";
                    var filePath = Path.Combine(uploadsPath, fileName);

                    await File.WriteAllBytesAsync(filePath, fileData);
                    uploadedFiles.Add(fileName);

                    var fileUrl = $"{context.Request.Scheme}://{context.Request.Host}/download/{fileName}";

                    var notificationMessage = $"{fileUrl}";

                    await SendMessage(webSocket, $"{fileUrl}");

                    foreach (var kvp in activeSockets)
                    {
                        var socket = kvp.Value;
                        if (socket.State == WebSocketState.Open)
                        {
                            await SendMessage(socket, notificationMessage);
                        }
                    }
                }
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine("Cliente desconectado.");
                break;
            }
            if (!result.EndOfMessage && result.Count == 0)
            {
                await SendMessage(webSocket, "Manteniendo conexión activa...");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    finally
    {
        //activeSockets.TryRemove(socketId, out _);
        //await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cierre normal", CancellationToken.None);
    }
}

string GetFileExtension(byte[] fileData)
{
    if (fileData[0] == 137 && fileData[1] == 80 && fileData[2] == 78 && fileData[3] == 71) return ".png";
    if (fileData[0] == 255 && fileData[1] == 216 && fileData[2] == 255) return ".jpg";
    if (fileData[0] == 37 && fileData[1] == 80 && fileData[2] == 68 && fileData[3] == 70) return ".pdf";
    if (fileData[0] == 80 && fileData[1] == 75 && fileData[2] == 3 && fileData[3] == 4) return ".zip";
    return null;
}
