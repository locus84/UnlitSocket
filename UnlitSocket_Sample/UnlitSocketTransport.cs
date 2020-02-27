﻿//// wraps Telepathy for use as HLAPI TransportLayer
//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Net;
//using System.Net.Sockets;
//using UnityEngine;

//namespace Mirror
//{
//    public class UnlitSocketTransport : Transport
//    {
//        // scheme used by this transport
//        // "tcp4" means tcp with 4 bytes header, network byte order
//        public const string Scheme = "unlit";

//        public ushort port = 7777;

//        [Tooltip("Nagle Algorithm can be disabled by enabling NoDelay")]
//        public bool NoDelay = true;

//        protected UnlitSocket.Client client = new UnlitSocket.Client();
//        protected UnlitSocket.Server server = new UnlitSocket.Server();
//        protected UnlitSocketLogger logger = new UnlitSocketLogger();

//        Coroutine m_ConnectCoroutine = null;

//        List<UnlitSocket.ReceivedMessage> receivedMessages = new List<UnlitSocket.ReceivedMessage>();

//        byte[] receiveBuffer = new byte[1024];

//        public class UnlitSocketLogger : UnlitSocket.ILogReceiver
//        {
//            public void Debug(string msg)
//            {
//                UnityEngine.Debug.Log(msg);
//            }

//            public void Exception(Exception exception)
//            {
//                UnityEngine.Debug.LogException(exception);
//            }

//            public void Warning(string msg)
//            {
//                UnityEngine.Debug.LogWarning(msg);
//            }
//        }

//        void Awake()
//        {
//            Debug.Log(ServerUri());
//            // tell Telepathy to use Unity's Debug.Log
//            client.SetLogger(logger);
//            server.SetLogger(logger);

//            // configure
//            client.NoDelay = NoDelay;
//            server.NoDelay = NoDelay;

//            Debug.Log("UnlitSocket initialized");
//        }

//        public override bool Available()
//        {
//            // C#'s built in TCP sockets run everywhere except on WebGL
//            return Application.platform != RuntimePlatform.WebGLPlayer;
//        }

//        public void EnsureBufferSize(int size)
//        {
//            while (receiveBuffer.Length < size)
//            {
//                receiveBuffer = new byte[receiveBuffer.Length * 2];
//            }
//        }

//        // client
//        public override bool ClientConnected() => client.Status == UnlitSocket.ConnectionStatus.Connected;
//        public override void ClientConnect(string address)
//        {
//            client.Connect(address, port);
//            if (m_ConnectCoroutine != null) StopCoroutine(m_ConnectCoroutine);
//            m_ConnectCoroutine = StartCoroutine(WaitForConnect());
//        }
//        public override void ClientConnect(Uri uri)
//        {
//            if (uri.Scheme != Scheme)
//                throw new ArgumentException($"Invalid url {uri}, use {Scheme}://host:port instead", nameof(uri));

//            int serverPort = uri.IsDefaultPort ? port : uri.Port;
//            client.Connect(uri.Host, serverPort);
//            if (m_ConnectCoroutine != null) StopCoroutine(m_ConnectCoroutine);
//            m_ConnectCoroutine = StartCoroutine(WaitForConnect());
//        }

//        IEnumerator WaitForConnect()
//        {
//            while (client.Status == UnlitSocket.ConnectionStatus.Connecting)
//                yield return null;
//            if (client.Status == UnlitSocket.ConnectionStatus.Disconnected)
//                OnClientDisconnected.Invoke();
//        }


//        public override bool ClientSend(int channelId, ArraySegment<byte> segment)
//        {
//            var msg = UnlitSocket.Message.Pop();
//            msg.WriteSegment(segment);
//            return client.Send(msg);
//        }

//        void ProcessClientMessages()
//        {
//            client.GetNextMessages(receivedMessages);
//            for (int i = 0; i < receivedMessages.Count; i++)
//            {
//                var receivedMsg = receivedMessages[i];
//                switch (receivedMsg.Type)
//                {
//                    case UnlitSocket.MessageType.Connected:
//                        OnClientConnected.Invoke();
//                        break;
//                    case UnlitSocket.MessageType.Data:
//                        var msg = receivedMsg.MessageData;
//                        EnsureBufferSize(msg.Size);
//                        msg.ReadBytes(receiveBuffer, 0, msg.Size);
//                        OnClientDataReceived.Invoke(new ArraySegment<byte>(receiveBuffer, 0, msg.Size), Channels.DefaultReliable);
//                        msg.Release();
//                        break;
//                    case UnlitSocket.MessageType.Disconnected:
//                        OnClientDisconnected.Invoke();
//                        break;
//                }
//            }
//        }

//        public override void ClientDisconnect()
//        {
//            if (m_ConnectCoroutine != null) StopCoroutine(m_ConnectCoroutine);
//            client.Disconnect();
//        }

//        public void LateUpdate()
//        {
//            ProcessClientMessages();
//            ProcessServerMessage();
//        }

//        public override Uri ServerUri()
//        {
//            UriBuilder builder = new UriBuilder();
//            builder.Scheme = Scheme;
//            builder.Host = Dns.GetHostName();
//            builder.Port = port;
//            return builder.Uri;
//        }

//        // server
//        public override bool ServerActive() => server.IsRunning;
//        public override void ServerStart() => server.Start(port);
//        public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment)
//        {
//            var msg = UnlitSocket.Message.Pop();
//            msg.WriteSegment(segment);
//            return server.Send(connectionIds, msg);
//        }

//        void ProcessServerMessage()
//        {
//            server.GetNextMessages(receivedMessages);
//            for (int i = 0; i < receivedMessages.Count; i++)
//            {
//                var receivedMsg = receivedMessages[i];
//                switch (receivedMsg.Type)
//                {
//                    case UnlitSocket.MessageType.Connected:
//                        OnServerConnected.Invoke(receivedMsg.ConnectionId);
//                        break;
//                    case UnlitSocket.MessageType.Data:
//                        var msg = receivedMsg.MessageData;
//                        EnsureBufferSize(msg.Size);
//                        msg.ReadBytes(receiveBuffer, 0, msg.Size);
//                        OnServerDataReceived.Invoke(receivedMsg.ConnectionId, new ArraySegment<byte>(receiveBuffer, 0, msg.Size), Channels.DefaultReliable);
//                        msg.Release();
//                        break;
//                    case UnlitSocket.MessageType.Disconnected:
//                        OnServerDisconnected.Invoke(receivedMsg.ConnectionId);
//                        break;
//                }
//            }
//        }

//        public override bool ServerDisconnect(int connectionId) => server.Disconnect(connectionId);
//        public override string ServerGetClientAddress(int connectionId)
//        {
//            try
//            {
//                var ep = server.GetConnectionAddress(connectionId);
//                return ep == null ? "unknown" : ep.ToString();
//            }
//            catch (SocketException)
//            {
//                return "unknown";
//            }
//        }
//        public override void ServerStop() => server.Stop();

//        // common
//        public override void Shutdown()
//        {
//            Debug.Log("UnlitSocketTransport Shutdown()");
//            if (client.Status != UnlitSocket.ConnectionStatus.Disconnected) client.Disconnect();
//            if (server.IsRunning) server.Stop();
//        }

//        public override int GetMaxPacketSize(int channelId)
//        {
//            return ushort.MaxValue;
//        }

//        public override string ToString()
//        {
//            if (server.IsRunning)
//            {
//                return "UnlitSocket Server port: " + port;
//            }
//            else if (client.Status != UnlitSocket.ConnectionStatus.Disconnected)
//            {
//                return "UnlitSocket Client ip: " + client.RemoteEndPoint;
//            }
//            return "UnlitSocket (inactive/disconnected)";
//        }
//    }
//}
