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
        Socket m_ListenSocket;
        int m_CurrentConnectionCount;

        public int ConnectionCount => m_CurrentConnectionCount;
        public bool IsRunning { get; private set; } = false;
        public int Port { get; private set; } = 6000;

        //this list is only increasing
        List<Connection> m_ConnectionList;
        ConcurrentQueue<int> m_FreeConnectionIds = new ConcurrentQueue<int>();
        ManualResetEvent m_RunningResetEvent = new ManualResetEvent(true);
        WaitOrTimerCallback m_DisconnectWaitCallback;

        public Server()
        {
            m_CurrentConnectionCount = 0;
            m_FreeConnectionIds = new ConcurrentQueue<int>();
            m_ConnectionList = new List<Connection>(16);
            m_DisconnectWaitCallback = new WaitOrTimerCallback(OnDisconnectComplete);
        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        public void Start(int port)
        {
            if (IsRunning) throw new Exception("Server is already running");

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

            m_RunningResetEvent.Reset();
            IsRunning = true;
            StartAccept(m_ListenSocket, acceptEventArg);
        }

        // Begins an operation to accept a connection request from the client 
        private void StartAccept(Socket socket, SocketAsyncEventArgs args)
        {
            Connection connection = null;

            if (m_FreeConnectionIds.TryDequeue(out var connID))
            {
                connection = m_ConnectionList[connID - 1];
            }
            else
            {
                connection = new Connection(m_ConnectionList.Count + 1, this);
                connection.ReceiveArg.Completed += ProcessReceive;
                m_ConnectionList.Add(connection);
            }

            args.UserToken = connection;
            args.AcceptSocket = connection.Socket;
            if (!socket.AcceptAsync(args)) ProcessAccept(socket, args);
        }

        private void ProcessAccept(object sender, SocketAsyncEventArgs args)
        {
            var connection = (Connection)args.UserToken;

            if (args.SocketError == SocketError.Success)
            {
                var socket = sender as Socket;
                var currentNumber = Interlocked.Increment(ref m_CurrentConnectionCount);
                //send initial message that indicates socket is accepted
                connection.IsConnected = true;

                m_Logger?.Debug($"Client {connection.ConnectionID} connected, Current Count : {currentNumber}");

                try
                {
                    m_MessageHandler.OnConnected(connection.ConnectionID);
                }
                catch (Exception e)
                {
                    m_Logger?.Exception(e);
                }

                connection.DisconnectEvent.Reset(1);
                StartReceive(connection);

                args.AcceptSocket = null;
                StartAccept(socket, args);
            }
            else
            {
                connection.CloseSocket();

                m_FreeConnectionIds.Enqueue(connection.ConnectionID);
                var listenSocket = (Socket)sender;

                //check if valid listen socket
                if (IsRunning)
                    StartAccept(listenSocket, args);
                else
                    m_RunningResetEvent.Set();
            }
        }

        public void Stop()
        {
            if (!IsRunning) throw new Exception("Server is not running");

            IsRunning = false;
            m_ListenSocket.Close();
            m_ListenSocket = null;
            //m_RunningResetEvent.WaitOne();

            //now usertokens are fixed count
            foreach (var connection in m_ConnectionList)
            {
                //socket could be already disposed
                connection.CloseSocket();
            }
        }

        /// <summary>
        /// Send to multiple recipients without creating multiple Message object
        /// </summary>
        public bool Send(IList<int> recipients, Message message)
        {
            for (int i = 0; i < recipients.Count; i++)
            {
                message.Retain();
                Send(recipients[i], message);
            }

            //now release retained by pop()
            message.Release();
            return true;
        }

        /// <summary>
        /// Send to one client
        /// </summary>
        public bool Send(int connectionID, Message message)
        {
            if (message.Position == 0)
            {
                message.Release();
                return false;
            }

            //1 is reserved, so let it -1 index of tokenlist
            if (connectionID <= 0 || connectionID > m_ConnectionList.Count)
            {
                message.Release();
                return false;
            }

            var connection = m_ConnectionList[connectionID - 1];

            if (!connection.IsConnected)
            {
                message.Release();
                return false;
            }

            Send(connection, message);
            return true;
        }

        /// <summary>
        /// Disconnect client
        /// </summary>
        public bool Disconnect(int connectionId)
        {
            if (connectionId <= 0 || connectionId > m_ConnectionList.Count)
            {
                return false;
            }
            else
            {
                var connection = m_ConnectionList[connectionId - 1];
                return connection.CloseSocket();
            }
        }

        public IPEndPoint GetConnectionAddress(int connectionId)
        {
            //1 is reserved, so let it -1 index of tokenlist
            if (connectionId <= 0 || connectionId > m_ConnectionList.Count)
            {
                return null;
            }

            var connection = m_ConnectionList[connectionId - 1];
            return connection.IsConnected ? (IPEndPoint)connection.Socket.RemoteEndPoint : null;
        }

        protected override bool CloseSocket(Connection connection, bool withCallback)
        {
            var result = base.CloseSocket(connection, withCallback);

            // decrement the counter keeping track of the total number of clients connected to the server
            ThreadPool.RegisterWaitForSingleObject(connection.DisconnectEvent.WaitHandle, m_DisconnectWaitCallback, connection, 3000, true);
            return result;
        }

        private void OnDisconnectComplete(object sender, bool success)
        {
            var connection = sender as Connection;
            if (connection.Socket.Connected)
            {
                try
                {
                    connection.Socket.Dispose();
                }
                catch { }
                connection.RebuildSocket();
                m_Logger?.Warning("Socket Rebuilt As it does not support reuse");
            }
            m_FreeConnectionIds.Enqueue(connection.ConnectionID);
            var currentNumber = Interlocked.Decrement(ref m_CurrentConnectionCount);
            m_Logger?.Debug($"Client {connection.ConnectionID} Disconnected, Current Count : {currentNumber}");
        }
    }
}
