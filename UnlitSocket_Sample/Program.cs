using System;
using System.Net;
using System.Threading;
using UnlitSocket;

namespace UnlitSocket_Sample
{
    class Program
    {
        static Server server;
        static Client client;

        public class Logger : ILogReceiver
        {
            public void Debug(string str)
            {
                Console.WriteLine(str);
            }

            public void Exception(Exception exception)
            {
                Console.WriteLine(exception.ToString());
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Server : s, Clinet : c");
            while(true)
            {
                if (Console.ReadKey().KeyChar.ToString().ToLower() == "c")
                {
                    StartClient();
                    break;
                }
                else if (Console.ReadKey().KeyChar.ToString().ToLower() == "s")
                {
                    StartServer();
                    break;
                }
            }

            while (true) Console.ReadLine();
        }

        static void StartClient()
        {
            client = new Client();
            client.SetLogger(new Logger());
            client.OnConnected += OnConnect;
            client.OnDisconnected += OnDisconnect;
            client.OnDataReceived += OnData;

            var ep = new IPEndPoint(IPAddress.Loopback, 6000);
            new Thread(new ThreadStart(Update)).Start();

            for(int i = 0; i < 10; i++)
            {
                var newClient = new Client();
                newClient.Connect(ep);
            }

            while (true)
            {
                client.Connect(ep);
                while (client.Status == ConnectionStatus.Connecting)
                    Thread.Sleep(100);

                if (client.Status == ConnectionStatus.Connected)
                    client.Disconnect();

                while (client.Status == ConnectionStatus.Connected)
                    Thread.Sleep(100);
            }
        }


        static void StartServer()
        { 
            server = new Server(10);
            server.Init();
            server.Start(6000);
            server.OnDataReceived += OnData;
            server.OnConnected += OnConnect;
            server.OnDisconnected += OnDisconnect;
            server.SetLogger(new Logger());
        }

        private static void Update()
        {
            while(true)
            {
                Thread.Sleep(100);
                server?.Update();
                client?.Update();
            }
        }

        private static void OnConnect(int connectionID)
        {
            Console.WriteLine($"Connection : {connectionID} - Connected");
        }

        private static void OnDisconnect(int connectionID)
        {
            Console.WriteLine($"Connection : {connectionID} - Disconnected");
        }

        private static void OnData(int connectionID, Message message, ref bool autoRecycle)
        {
            var receivedMsg = message.ReadString();
            if(receivedMsg == "exit")
            {
                server.Disconnect(connectionID);
            }
            Console.WriteLine($"Connection : {connectionID} - Msg : {receivedMsg}");
        }
    }
}
