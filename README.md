UnlitSocket
===

Very Straightforward TCP network framework using SocketAsyncEventArgs.

1. Message based(max length is ushort.MaxValue).
2. Reuse sockets on Server. No allocation at all.(except collection resizing at first)
3. Connection count is up to your machine.(does not pre-allocate SocketAsyncEventArgs)
4. Message buffers are devided into multiple buffers and cached. You can recycle with ease.
5. Mirror transport support.(https://github.com/vis2k/Mirror)
6. Receiving/sending messages are ThreadSafe.
   
</br>

Usage Examples
===

#### NameSpace
```cs
    using UnlitSocket;
```
#### Create Server
```cs
    public void StartServer()
    {
        var port = 1090;
        var server = new Server();
        server.Start(port);

        //stops accept and disconnect all
        server.Stop();
    }
```
#### Create Client and Connect
```cs
    public void StartClient()
    {
        var port = 1090;
        var client = new Client();
        client.Connect("localhost", port);

        //if you want track client connected/failed
        client.Connect("localhost", port).Wait();
        var isConnected = client.Status == ConnectionStatus.Connected;
    }
```
#### Disconnect
```cs
    public void DisconnectAClinet(Server server, int clientId)
    {
        server.Disconnect(clientId);
    }

    public void DisconnectFromServer(Client client)
    {
        client.Disconnect();
    }
```
#### Sending Message
```cs
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
```
#### Handling Events - Method 1

You have to call this every server/client tick.\
In Unity3d for example, in **Update** function.
ConnectionId of Client is always 0.
```cs
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
```

#### Handling Events - Method 2

Same as Method 1, but more performant as it dequeues received events at once.
```cs
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
```

#### Handling Events - Method 3

Create a class that Implements IEventHandler.
```cs
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
```
And pass it to the server or client.
```cs
    public void SetHandler(Peer serverOrClient)
    {
        serverOrClient.SetHandler(new CustomEventHandler());
    }
```

When you use your own **IMessageHandler**, callbacks are invoked in **Threadpool thread**.\
If you throw an exception or blocks this thread, Client associated this callback won't run anymore or can't process further messages.

#### Log Library Related Messages

Same manner as above, Create a class implements ILogReceiver, and pass it to the server or client.

```cs
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
```
</br>

### Using With Mirror
Copy library source files into somewhere in your project.\
And copy UnlitSocketTransport.cs inside UnlitSocket_Sample, either.\
Remove **#if Unity** define in UnlitSocketTransport.cs.

Declaimer(Important!)
===

On mono windows, All the async(including IAsyncOperation) method does not actually use Completion Port.\
They just eat up Thread pool threads, causing application hang.\
So if you're using mono backend and requires more than 500ish connections. you better use other solution.\
for example, you're building **MMORPG server** with **mono** and you want to host it on **windows machine**. it won't work well.
Just run on **Linux/Mac** and it will be fine.

