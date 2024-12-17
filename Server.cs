using OBRLibrary;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WebsocketServer
{
    public class Server
    {
        private Filter _filter;
        private HttpListener _server;

        public Server(string URL)
        {
            _filter = new Filter();
            _server = new HttpListener();
            _server.Prefixes.Add(URL);
            _server.Start();
            Console.WriteLine($"Сервер начал работу: {URL}");
        }

        public void Start()
        {
            try
            {
                CustomerExpectation();
            }
            catch
            {
                throw new Exception("Ошибка приема клиентов.");
            }
        }

        private void CustomerExpectation()
        {
            while (true)
            {
                HttpListenerContext context = _server.GetContext();
                Console.WriteLine("Клиент подключился.");

                if (context.Request.IsWebSocketRequest)
                {
                    new Thread(new ParameterizedThreadStart(ProcessWebSocketRequest)).Start(context);

                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }

        private async void ProcessWebSocketRequest(object obj)
        {
            HttpListenerContext context = (HttpListenerContext)obj;
            HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
            WebSocket websocket = webSocketContext.WebSocket;

            try
            {
                List<byte> imageDataChunks = new List<byte>();

                bool isFirstMessage = true;
                bool isThread = false;

                int totalSize = 0;

                while (websocket.State == WebSocketState.Open)
                {

                    byte[] buffer = new byte[4096];
                    WebSocketReceiveResult result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("Клиент оперативно ретировался.");
                        break;
                    }

                    if (isFirstMessage)
                    {
                        totalSize = BitConverter.ToInt32(buffer, 0);
                        isThread = buffer[4] == 1;

                        imageDataChunks = new List<byte>(totalSize);
                        isFirstMessage = false;
                        continue;
                    }

                    imageDataChunks.AddRange(buffer.Take(result.Count));
                    if (imageDataChunks.Count == totalSize)
                    {
                        byte[] imageData = imageDataChunks.ToArray();

                        Console.WriteLine($"Получено байт от клиента: {imageData.Length} bytes");

                        (Bitmap output, double time) = OperFilter(imageData, isThread);
                        SendMessage(websocket, output, time);

                        buffer = null;
                        imageData = null;
                        imageDataChunks.Clear();
                        isFirstMessage = true;
                        totalSize = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex}");
                try
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                    Console.WriteLine("Соединение с клиентом успешно закрыто.");
                }
                catch (WebSocketException websocketEx)
                {
                    Console.WriteLine($"Соединение почему-то не закрылось: {websocketEx.Message}");
                }
                finally
                {
                    websocket.Dispose();
                }
            }
        }

        private void SendMessage(WebSocket websocket, Bitmap bitmap, double time)
        {
            byte[] imageData;
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                imageData = ms.ToArray();
            }

            string base64Image = Convert.ToBase64String(imageData);
            string json = $"{{\"image\": \"{base64Image.ToString()}\", \"time\": \"{time.ToString()}\"}}";
            websocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None);

            imageData = null;
            bitmap.Dispose();
        }

        private (Bitmap, double) OperFilter(byte[] imageData, bool isThread)
        {
            Bitmap bitmap = new Bitmap(new MemoryStream(imageData));
            TimeHelper timeHelper = new TimeHelper();

            timeHelper.Start();
            Bitmap result = isThread ? _filter.QuantizeImageParallel(bitmap, 8) : _filter.QuantizeImage(bitmap);
            double time = timeHelper.Stop();

           // Bitmap output = ConvertBytesToBitmap(result);

            bitmap.Dispose();
            timeHelper = null;
            //result = null;

            return (result, time);
        }
    }
}
