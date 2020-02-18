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
            server = new Server(1000);
            server.Init();
            server.Start(6000);
            server.OnDataReceived += OnData;
            server.OnDisconnected += OnConnect;
            server.OnDisconnected += OnDisconnect;
            //server.SetLogger(new Logger());

            client = new Client();
            //client.SetLogger(new Logger());

            var ep = new IPEndPoint(IPAddress.Loopback, 6000);
            new Thread(new ThreadStart(Update)).Start();
            new Thread(new ThreadStart(SendCommand)).Start();

            while (true)
            {
                client.Connect(ep);
                while(client.Status == ConnectionStatus.Connecting)
                    Thread.Sleep(100);

                if (client.Status == ConnectionStatus.Connected)
                    client.Disconnect();

                if (client.Status == ConnectionStatus.Connected)
                    Thread.Sleep(100);
            }
        }

        private static void Update()
        {
            while(true)
            {
                Thread.Sleep(100);
                server.Update();
            }

        }

        private static void SendCommand()
        {
            while (true)
            {
                var message = Message.Pop();
                message.WriteString(Console.ReadLine());
                client.Send(message);
            }
        }

        private static void OnConnect(int connectionID)
        {
            //Console.WriteLine($"Connection : {connectionID} - Connected");
        }

        private static void OnDisconnect(int connectionID)
        {
            //Console.WriteLine($"Connection : {connectionID} - Disconnected");
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
