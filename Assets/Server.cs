using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System;
using System.Threading;
using System.Net;
using System.Collections.Concurrent;

namespace UnlitSocket
{
    public class Server
    {
        private int m_MaxConnectionCount; //maximum connection
        private int m_MaxMessageSize; //buffer size, can be varied
        Socket m_ListenSocket;
        int m_TotalBytesRead;
        int m_CurrentConnectionCount;
        ILogReceiver m_Logger;
        public bool IsRunning { get; private set; } = false;

        public int Port { get; private set; }

        AsyncUserTokenPool m_TokenPool;
        Dictionary<int, AsyncUserToken> m_ConnectionDic;
        ConcurrentBag<SocketAsyncEventArgs> m_SendArgsPool = new ConcurrentBag<SocketAsyncEventArgs>();
        ConcurrentQueue<ReceivedMessage> m_ReceivedMessages = new ConcurrentQueue<ReceivedMessage>();

        public ConnectionStatusChangeDelegate OnConnected;
        public ConnectionStatusChangeDelegate OnDisconnected;
        public DataReceivedDelegate OnDataReceived;

        struct ReceivedMessage
        {
            public int ConnectionID;
            public MessageType Type;
            public Message MessageData;
            public ReceivedMessage(int connectionID, MessageType type, Message message = null)
            {
                ConnectionID = connectionID;
                Type = type;
                MessageData = message;
            }
        }

        public Server(int maxConnections, int maxMessageSize)
        {
            m_TotalBytesRead = 0;
            m_CurrentConnectionCount = 0;
            m_MaxConnectionCount = maxConnections;
            m_MaxMessageSize = maxMessageSize;

            m_TokenPool = new AsyncUserTokenPool(maxConnections);
            m_ConnectionDic = new Dictionary<int, AsyncUserToken>(maxConnections);
        }

        public void SetLogger(ILogReceiver logger) => m_Logger = logger;

        public void Init()
        {
            //init buffer, use given value in initializer
            for (int i = 0; i < m_MaxConnectionCount; i++)
            {
                var token = new AsyncUserToken(i);
                token.ReceiveArg.Completed += ProcessReceive;
                m_ConnectionDic.Add(i, token);
                m_TokenPool.Push(token);
            }
        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        public void Start(int port)
        {
            // create the socket which listens for incoming connections
            m_ListenSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            m_ListenSocket.DualMode = true;
            m_ListenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            m_ListenSocket.Listen(1024);

            var acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += ProcessAccept;

            StartAccept(m_ListenSocket, acceptEventArg);
            IsRunning = true;
        }

        // Begins an operation to accept a connection request from the client 
        private void StartAccept(Socket socket, SocketAsyncEventArgs acceptEventArg)
        {
            bool isPending = false;
            isPending = socket.AcceptAsync(acceptEventArg);
            if (!isPending) ProcessAccept(socket, acceptEventArg);
        }

        private void ProcessAccept(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                var socket = sender as Socket;
                var currentNumber = Interlocked.Increment(ref m_CurrentConnectionCount);

                if (currentNumber > m_MaxConnectionCount)
                {
                    //close bad accepted socket
                    if (e.AcceptSocket != null)
                        e.AcceptSocket.Disconnect(false);
                    Interlocked.Decrement(ref m_CurrentConnectionCount);
                }
                else
                {
                    var token = m_TokenPool.Pop();
                    token.IsConnected = true;
                    m_Logger?.Debug($"Client {token.ConnectionID} connected, Current Count : {currentNumber} - {socket.AddressFamily}");

                    token.Socket = e.AcceptSocket;
                    token.Socket.SendTimeout = 5000;
                    token.Socket.NoDelay = true;

                    bool isPending = e.AcceptSocket.ReceiveAsync(token.ReceiveArg);
                    if (!isPending) ProcessReceive(e.AcceptSocket, token.ReceiveArg);
                }

                e.AcceptSocket = null;
                StartAccept(socket, e);
            }
            else
            {
                //close bad accepted socket
                if (e.AcceptSocket != null)
                    e.AcceptSocket.Close();

                if (IsRunning)
                    StartAccept((Socket)sender, e);
            }
        }

        public void Stop()
        {
            IsRunning = false;
            m_ListenSocket?.Close();
        }

        private void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            // check if the remote host closed the connection
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                //increment the count of the total bytes receive by the server
                Interlocked.Add(ref m_TotalBytesRead, e.BytesTransferred);
                m_Logger?.Debug($"Server ReceivedData Offset from client {token.ConnectionID} : {e.Offset} Count : {e.BytesTransferred}");

                //this is initial length
                //TODO have to check bytesTransferred
                if(token.CurrentMessage == null)
                {
                    var size = MessageReader.ReadUInt16(token.ReceiveArg.Buffer);
                    token.CurrentMessage = Message.Pop(size);
                    token.CurrentMessage.BindToArgsReceive(token.ReceiveArg, size);
                }
                else
                {
                    m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Data, token.CurrentMessage));
                    token.ClearMessage();
                }

                bool isPending = token.Socket.ReceiveAsync(e);
                if (!isPending) ProcessReceive(token.Socket, e);
            }
            else
            {
                token.ClearMessage();
                CloseClientSocket(e);
            }
        }

        public void Send(int connectionID, Message message)
        {
            message.Retain();
            AsyncUserToken token;

            if(!m_ConnectionDic.TryGetValue(connectionID, out token))
            {
                message.Release();
                return;
            }

            if (!token.IsConnected)
            {
                message.Release();
                return;
            }

            SocketAsyncEventArgs sendArg;
            if (!m_SendArgsPool.TryTake(out sendArg))
            {
                sendArg = new SocketAsyncEventArgs();
                sendArg.Completed += ProcessSend;
            }

            message.ConnectionID = connectionID;
            sendArg.UserToken = message;
            message.BindToArgsSend(sendArg, message.Position);

            try
            {
                bool isPending = token.Socket.SendAsync(sendArg);
                if (!isPending) ProcessSend(token.Socket, sendArg);
            }
            catch
            {
                m_Logger?.Debug($"Send Failed Recycling Message");
                sendArg.BufferList = null;
                sendArg.UserToken = null;
                m_SendArgsPool.Add(sendArg);
                message.Release();
            }
        }

        private void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                m_Logger?.Debug($"Byte Transfered : { e.BytesTransferred }");
                ((Message)e.UserToken).Release();
                e.UserToken = null;
                e.BufferList = null;
                m_SendArgsPool.Add(e);
            }
            else
            {
                m_Logger?.Debug("Send Failed");
                ((Message)e.UserToken).Release();
                e.UserToken = null;
                e.BufferList = null;
                m_SendArgsPool.Add(e);
            }
        }

        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;

            // close the socket associated with the client
            try
            {
                token.Socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch (Exception) { }

            token.Socket.Close();
            token.IsConnected = false;
            m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Disconnected));
            var currentNumber = Interlocked.Decrement(ref m_CurrentConnectionCount);
            m_Logger?.Debug($"client { token.ConnectionID } has been disconnected from the server. There are {currentNumber} clients connected to the server");
            // decrement the counter keeping track of the total number of clients connected to the server

            m_TokenPool.Push(token);
        }


        public void Update()
        {
            ReceivedMessage receivedMessage;
            while(m_ReceivedMessages.TryDequeue(out receivedMessage))
            {
                switch(receivedMessage.Type)
                {
                    case MessageType.Connected:
                        OnConnected?.Invoke(receivedMessage.ConnectionID);
                        break;
                    case MessageType.Disconnected:
                        OnDisconnected?.Invoke(receivedMessage.ConnectionID);
                        break;
                    case MessageType.Data:
                        receivedMessage.MessageData.Retain();
                        OnDataReceived?.Invoke(receivedMessage.ConnectionID, receivedMessage.MessageData);
                        receivedMessage.MessageData.Release();
                        break;
                    default:
                        throw new Exception("Unknown MessageType");
                }
            }
        }
    }
}
