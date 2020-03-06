using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using UnlitSocket;

namespace UnlitSocket_Sample
{
    class Program
    {
        static Server server;
        static List<Client> clients = new List<Client>();

        static IPEndPoint ep = new IPEndPoint(IPAddress.Loopback, 6000);

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
            var thread = new Thread(new ThreadStart(Update));
            thread.Start();

            while (true)
            {
                var command = Console.ReadLine();
                if (command == "start server")
                {
                    if(server != null && server.IsRunning)
                        server.Stop();
                    StartServer();
                }
                if (command == "reconnect client")
                    new Thread(new ThreadStart(StartClient)).Start();
                if (command == "disconnect all")
                    StopClient();
                if (command == "stop server")
                    server?.Stop();
                if (command == "more client")
                    AddMoreClient();
            }
        }

        static void AddMoreClient()
        {
            for (int i = 0; i < 1000; i++)
            {
                var newClient = new Client();
                newClient.Connect(ep);
                clients.Add(newClient);
            }
        }

        static void StartClient()
        {
            Console.WriteLine("StartClient");
            var client = new Client();

            while (true)
            {
                client.Connect(ep);
                while (client.Status == ConnectionStatus.Connecting)
                    Thread.Sleep(100);
                client.Disconnect();
            }
        }

        static void StopClient()
        {
            Console.WriteLine("StopClient");
            for(int i = 0; i < clients.Count; i++)
            {
                clients[i].Disconnect();
            }
            clients.Clear();
        }


        static void StartServer()
        {
            Console.WriteLine("StartServer");
            server = new Server();
            server.Start(ep.Port);
            //server.SetLogger(new Logger());
        }

        private static void Update()
        {
            while(true)
            {
                Thread.Sleep(100);
                if (server != null) Update(server);
                //if (client != null) Update(client);
            }
        }

        private static void Update(Peer peer)
        {
            Event message;
            while(peer.TryGetNextEvent(out message))
            {
                switch(message.Type)
                {
                    case EventType.Connected:
                        Console.WriteLine($"Connection : {message.ConnectionId} - Connected");
                        break;
                    case EventType.Disconnected:
                        Console.WriteLine($"Connection : {message.ConnectionId} - Disconnected");
                        break;
                    case EventType.Data:
                        Console.WriteLine($"Connection : {message.ConnectionId} - Data");
                        var receivedMsg = message.Message.ReadString();
                        if (receivedMsg == "exit" && peer is Server serverPeer) serverPeer.Disconnect(message.ConnectionId);
                        message.Message.Release();
                        break;
                }
            }
        }
    }
}
