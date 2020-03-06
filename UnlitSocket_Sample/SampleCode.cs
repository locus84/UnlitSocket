using System;
using System.Collections.Generic;
using UnlitSocket;

namespace UnlitSocket_Sample
{
    class SampleCode
    {
        public void StartServer()
        {
            var port = 1090;
            var server = new Server();
            server.Start(port);

            //stops accept and disconnect all
            server.Stop();
        }

        public void StartClient()
        {
            var port = 1090;
            var client = new Client();
            client.Connect("localhost", port);

            //if you want track client connected/failed
            client.Connect("localhost", port).Wait();
            var isConnected = client.Status == ConnectionStatus.Connected;
        }

        public void DisconnectAClinet(Server server, int clientId)
        {
            server.Disconnect(clientId);
        }

        public void DisconnectFromServer(Client client)
        {
            client.Disconnect();
        }

        public void SendMessageToServer(Client client)
        {
            var message = Message.Pop();
            message.WriteString("Hi there");
            client.Send(message);
        }
        public void SendMessageToClient(Server server, int clientId)
        {
            var message = Message.Pop();
            message.WriteString("Hi there");
            server.Send(clientId, message);
        }

        public void SendMessageToMultipleClient(Server server, IList<int> clientIds)
        {
            var message = Message.Pop();
            message.WriteString("Hi there");
            server.Send(clientIds, message);
        }

        public void HandleEvents(Peer peer)
        {
            //peer is base class of Server/Client
            Event ev;
            while (peer.TryGetNextEvent(out ev))
            {
                switch (ev.Type)
                {
                    case EventType.Connected:
                        Console.WriteLine($"Client {ev.ConnectionId} connected to server");
                        break;
                    case EventType.Data:
                        //received data
                        Console.WriteLine($"Client {ev.ConnectionId} says : {ev.Message.ReadString()}");
                        ev.Message.Release();//this will recycle message
                        break;
                    case EventType.Disconnected:
                        Console.WriteLine($"Client {ev.ConnectionId} disconnected from server");
                        break;
                }
            }
        }

        public void HandleEvents(Peer peer, List<Event> eventCache)
        {
            //peer is base class of Server/Client
            //write messages into this list
            peer.GetNextEvents(eventCache);

            for(int i = 0; i < eventCache.Count; i++)
            {
                var ev = eventCache[i];
                switch (ev.Type)
                {
                    case EventType.Connected:
                        Console.WriteLine($"Client {ev.ConnectionId} connected to server");
                        break;
                    case EventType.Data:
                        //received data
                        Console.WriteLine($"Client {ev.ConnectionId} says : {ev.Message.ReadString()}");
                        ev.Message.Release();//this will recycle message
                        break;
                    case EventType.Disconnected:
                        Console.WriteLine($"Client {ev.ConnectionId} disconnected from server");
                        break;
                }
            }

            //clear for next operation
            eventCache.Clear();
        }
        public class CustomEventHandler : IEventHandler
        {
            public void OnConnected(int connectionId)
            {
                Console.WriteLine($"Client {connectionId} connected to server");
            }

            public void OnDataReceived(int connectionId, Message msg)
            {
                Console.WriteLine($"Client {connectionId} says : {msg.ReadString()}");
                msg.Release();
            }

            public void OnDisconnected(int connectionId)
            {
                Console.WriteLine($"Client {connectionId} disconnected from server");
            }
        }

        public void SetHandler(Peer serverOrClient)
        {
            serverOrClient.SetHandler(new CustomEventHandler());
        }

        public class CustomLogReceiver : ILogReceiver
        {
            public void Debug(string msg)
            {
                Console.WriteLine(msg);
            }

            public void Exception(Exception exception)
            {
                Console.WriteLine(exception.Message);
            }

            public void Warning(string msg)
            {
                Console.WriteLine(msg);
            }
        }

        public void SetLogger(Peer serverOrClient)
        {
            serverOrClient.SetLogger(new CustomLogReceiver());
        }
    }
}
