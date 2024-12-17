using System;

namespace WebsocketServer
{
    internal class Program
    {
        const string URL = "http://127.0.0.1:8888/";

        static private Server _server;

        static void Main(string[] args)
        {
            _server = new Server(URL);
            _server.Start();
            Console.ReadKey();
        }
    }
}
