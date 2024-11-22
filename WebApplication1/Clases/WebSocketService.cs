using System.Net.WebSockets;
using System.Text;

namespace WebApplication1.Clases
{
    public class WebSocketService2
    {
        private readonly WebSocket _webSocket;
        private readonly string _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads"); // Carpeta para guardar los archivos
        private List<string> _fileNames = new List<string>(); // Lista de nombres de archivo recibidos

        public WebSocketService2(WebSocket webSocket)
        {
            _webSocket = webSocket;
        }

        public async Task HandleWebSocketAsync()
        {
            var buffer = new byte[1024 * 4];

            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Recibimos un archivo (en binario)
                    await SaveFileAsync(buffer, result.Count);
                }
                else
                {
                    // Procesamos mensajes de texto si es necesario
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine("Mensaje recibido: " + message);
                }

                if (result.EndOfMessage)
                    break;
            }
        }

        private async Task SaveFileAsync(byte[] fileData, int length)
        {
            try
            {
                // Crear la carpeta de subida si no existe
                if (!Directory.Exists(_uploadFolder))
                {
                    Directory.CreateDirectory(_uploadFolder);
                }

                // Generar un nombre único para el archivo
                var fileName = Path.Combine(_uploadFolder, Guid.NewGuid().ToString() + ".dat");

                // Guardamos el archivo recibido en el disco
                await File.WriteAllBytesAsync(fileName, fileData.Take(length).ToArray());

                // Agregar el nombre del archivo a la lista
                _fileNames.Add(fileName);

                Console.WriteLine($"Archivo guardado: {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar el archivo: {ex.Message}");
            }
        }

        public List<string> GetFileNames()
        {
            return _fileNames;
        }
    }

}
