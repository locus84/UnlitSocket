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
            public void Debug(string msg)
            {
                Console.WriteLine(msg);
            }

            public void Warning(string msg)
            {
                Console.WriteLine(msg);
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
                var key = Console.ReadKey();

                if (key.KeyChar.ToString().ToLower() == "c")
                {
                    Console.WriteLine();
                    StartClient();
                    break;
                }
                else if (key.KeyChar.ToString().ToLower() == "s")
                {
                    Console.WriteLine();
                    StartServer();
                    break;
                }
                else if (key.KeyChar.ToString().ToLower() == "b")
                {
                    Console.WriteLine();
                    StartServer();
                    StartClient();
                    break;
                }
            }

            while (true) Console.ReadLine();
        }

        static void StartClient()
        {
            Console.WriteLine("StartClient");
            new Thread(new ThreadStart(Update)).Start();
            client = new Client();
            //client.SetLogger(new Logger());

            var ep = new IPEndPoint(IPAddress.Loopback, 6000);

            for(int i = 0; i < 10; i++)
            {
                var newClient = new Client();
                newClient.Connect(ep);
            }

            while (true)
            {
                client.Connect(ep);
                while (client.Status != ConnectionStatus.Disconnected)
                    Thread.Sleep(100);
            }
        }


        static void StartServer()
        {
            Console.WriteLine("StartServer");
            new Thread(new ThreadStart(Update)).Start();
            server = new Server(10);
            server.Start(6000);
            server.SetLogger(new Logger());
        }

        private static void Update()
        {
            while(true)
            {
                Thread.Sleep(100);
                if (server != null) Update(server);
                if (client != null) Update(client);
            }
        }

        private static void Update(Peer peer)
        {
            ReceivedMessage message;
            while(peer.GetNextMessage(out message))
            {
                switch(message.Type)
                {
                    case MessageType.Connected:
                        Console.WriteLine($"Connection : {message.ConnectionId} - Connected");
                        break;
                    case MessageType.Disconnected:
                        Console.WriteLine($"Connection : {message.ConnectionId} - Disconnected");
                        break;
                    case MessageType.Data:
                        Console.WriteLine($"Connection : {message.ConnectionId} - Data");
                        var receivedMsg = message.MessageData.ReadString();
                        if (receivedMsg == "exit" && peer is Server serverPeer) serverPeer.Disconnect(message.ConnectionId);
                        message.MessageData.Release();
                        break;
                }
            }
        }
    }
}
