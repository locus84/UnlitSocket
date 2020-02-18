using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Collections.Concurrent;
using System;

namespace UnlitSocket
{
    public class Server : Peer
    {
        private int m_MaxConnectionCount; //maximum connection
        Socket m_ListenSocket;
        int m_CurrentConnectionCount;
        public bool IsRunning { get; private set; } = false;
        public int Port { get; private set; }

        //initial hellomessage Buffer
        static byte[] s_AcceptedMessage = new byte[] { 1 };
        static byte[] s_RejectedMessage = new byte[] { 0 };

        ConcurrentQueue<UserToken> m_TokenPool;
        Dictionary<int, UserToken> m_ConnectionDic;

        public Server(int maxConnections)
        {
            m_CurrentConnectionCount = 0;
            m_MaxConnectionCount = maxConnections;

            m_TokenPool = new ConcurrentQueue<UserToken>();
            m_ConnectionDic = new Dictionary<int, UserToken>(maxConnections);
        }

        public void Init()
        {
            //init buffer, use given value in initializer
            for (int i = 0; i < m_MaxConnectionCount; i++)
            {
                var token = new UserToken(i, this);
                token.ReceiveArg.Completed += ProcessReceive;
                m_ConnectionDic.Add(i, token);
                m_TokenPool.Enqueue(token);
            }
        }

        public void Init<T>() where T : IConnection, new()
        {
            //init buffer, use given value in initializer
            for (int i = 0; i < m_MaxConnectionCount; i++)
            {
                var token = new UserToken(i, this, new T());
                token.Connection.UserToken = token;
                token.ReceiveArg.Completed += ProcessReceive;
                m_ConnectionDic.Add(i, token);
                m_TokenPool.Enqueue(token);
            }
        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        public void Start(int port)
        {
            Port = port;
            // create the socket which listens for incoming connections
            m_ListenSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            m_ListenSocket.NoDelay = true;
            m_ListenSocket.Blocking = false;
            m_ListenSocket.SendBufferSize = 512;
            m_ListenSocket.ReceiveBufferSize = 512;
            m_ListenSocket.SendTimeout = 5000;
            m_ListenSocket.DualMode = true;
            m_ListenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));
            m_ListenSocket.Listen(100);

            var acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += ProcessAccept;

            StartAccept(m_ListenSocket, acceptEventArg);
            IsRunning = true;
        }

        // Begins an operation to accept a connection request from the client 
        private void StartAccept(Socket socket, SocketAsyncEventArgs args)
        {
            if(!socket.AcceptAsync(args)) ProcessAccept(socket, args);
        }

        private void ProcessAccept(object sender, SocketAsyncEventArgs args)
        {
            if (args.SocketError == SocketError.Success && m_TokenPool.TryDequeue(out var token))
            {
                var socket = sender as Socket;
                var currentNumber = Interlocked.Increment(ref m_CurrentConnectionCount);
                m_Logger?.Debug($"Client {token.ConnectionID} connected, Current Count : {currentNumber}");

                token.Socket = args.AcceptSocket;
                token.Socket.SendTimeout = 5000;
                token.Socket.NoDelay = true;
                SetKeepAlive(token.Socket, true, 30000, 5000);
                //send initial message that indicates socket is accepted
                token.Socket.Send(s_AcceptedMessage);
                token.IsConnected = true;

                if (token.Connection != null)
                {
                    try
                    {
                        token.Connection.OnConnected();
                    }
                    catch (Exception e)
                    {
                        m_Logger?.Exception(e);
                    }
                } 
                else
                {
                    m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Connected));
                }
                StartReceive(token);

                args.AcceptSocket = null;
                StartAccept(socket, args);
            }
            else
            {
                //we failed to receive new connection, let's close socket if there is
                if (args.AcceptSocket != null)
                {
                    //send initial message that socket is rejected
                    if(args.AcceptSocket.Connected) args.AcceptSocket.Send(s_RejectedMessage);
                    args.AcceptSocket.Close();
                    args.AcceptSocket = null;
                }

                var listenSocket = (Socket)sender;

                //check if valid listen socket
                if (IsRunning && listenSocket == m_ListenSocket)
                    StartAccept(listenSocket, args);
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            var listenSocket = m_ListenSocket;
            m_ListenSocket = null;

            IsRunning = false;
            listenSocket?.Close();

            foreach(var kv in m_ConnectionDic)
            {
                //socket could be already disposed
                try
                {
                    var socket = kv.Value.Socket;
                    if(socket != null) socket.Disconnect(false);
                }
                catch { }
            }
        }

        /// <summary>
        /// Send to multiple recipients without creating multiple Message object
        /// </summary>
        public void Send(IList<IConnection> recipients, Message message)
        {
            for(int i = 0; i < recipients.Count; i++)
            {
                //hold message not to be recycled, one send release message once.
                message.Retain();
                Send(recipients[i].UserToken, message);
            }

            //now release retained by pop()
            message.Release();
        }


        /// <summary>
        /// Send to multiple recipients without creating multiple Message object
        /// </summary>
        public void Send(IList<int> recipients, Message message)
        {
            for (int i = 0; i < recipients.Count; i++)
            {
                //hold message not to be recycled, one send release message once.
                message.Retain();
                Send(recipients[i], message);
            }

            //now release retained by pop()
            message.Release();
        }

        /// <summary>
        /// Send to one client
        /// </summary>
        public void Send(int connectionID, Message message)
        {
            if(message.Position == 0)
            {
                message.Release();
                return;
            }

            UserToken token;

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

            Send(token.Socket, message);
        }


        /// <summary>
        /// Send to one client
        /// </summary>
        public void Send(UserToken token, Message message)
        {
            if (message.Position == 0)
            {
                message.Release();
                return;
            }

            if (!token.IsConnected)
            {
                message.Release();
                return;
            }

            Send(token.Socket, message);
        }

        /// <summary>
        /// Disconnect client
        /// </summary>
        public void Disconnect(UserToken token)
        {
            try
            {
                var socket = token.Socket;
                if (socket != null) socket.Disconnect(false);
            }
            catch { }
        }

        /// <summary>
        /// Disconnect client
        /// </summary>
        public void Disconnect(int connectionID)
        {
            if (m_ConnectionDic.TryGetValue(connectionID, out var token))
            {
                Disconnect(token);
            }
        }

        protected override void CloseSocket(UserToken token)
        {
            base.CloseSocket(token);
            var currentNumber = Interlocked.Decrement(ref m_CurrentConnectionCount);
            m_Logger?.Debug($"client { token.ConnectionID } has been disconnected from the server. There are {currentNumber} clients connected to the server");
            // decrement the counter keeping track of the total number of clients connected to the server
            m_TokenPool.Enqueue(token);
        }
    }
}
