using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Collections.Concurrent;
using System.Linq;
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
        WaitOrTimerCallback m_OnRecycleReady;

        public Server()
        {
            m_CurrentConnectionCount = 0;
            m_FreeConnectionIds = new ConcurrentQueue<int>();
            m_ConnectionList = new List<Connection>(16);
            m_OnRecycleReady = new WaitOrTimerCallback(RecycleConnection);
        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        public void Start(int port, int backLog = 1024)
        {
            if (IsRunning) return;

            WindowSocket.Run();
            Port = port;
            // create the socket which listens for incoming connections
            m_ListenSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            m_ListenSocket.SendBufferSize = SendBufferSize;
            m_ListenSocket.ReceiveBufferSize = ReceiveBufferSize;
            m_ListenSocket.DualMode = true;
            m_ListenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, Port));
            m_ListenSocket.Listen(backLog);

            var acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += ProcessAccept;

            m_RunningResetEvent.Reset();
            IsRunning = true;

            StartAccept(m_ListenSocket, acceptEventArg);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            WindowSocket.Stop();
            IsRunning = false;
            m_ListenSocket.Dispose();
            m_ListenSocket = null;
            m_RunningResetEvent.WaitOne();

            //now usertokens are fixed count
            foreach (var conn in m_ConnectionList)
            {
                //socket could be already disposed
                Disconnect(conn);
            }
            //don't wait others to disconnect
        }

        // Begins an operation to accept a connection request from the client 
        private void StartAccept(Socket listenSocket, SocketAsyncEventArgs args)
        {
            Connection conn;

            if (m_FreeConnectionIds.TryDequeue(out var connID))
            {
                conn = m_ConnectionList[connID - 1];
            }
            else
            {
                conn = CreateConnection(m_ConnectionList.Count + 1);
                m_ConnectionList.Add(conn);
            }

            args.UserToken = conn;
            args.AcceptSocket = conn.Socket;

            try
            {
                if (!listenSocket.AcceptAsync(args))
                    ProcessAccept(listenSocket, args);
            }
            catch(Exception e)
            {
                //unexpected exception, let's stop
                m_Logger?.Exception(e);
                m_FreeConnectionIds.Enqueue(conn.ConnectionID);
                //we can set false before setting event
                IsRunning = false; 
                m_RunningResetEvent.Set();
            }
        }

        private void ProcessAccept(object sender, SocketAsyncEventArgs args)
        {
            var conn = (Connection)args.UserToken;
            var listenSocket = sender as Socket;

            if (args.SocketError == SocketError.Success)
            {
                var currentNumber = Interlocked.Increment(ref m_CurrentConnectionCount);
                //send initial message that indicates socket is accepted

                conn.SetConnectedAndResetEvent();
                m_Logger?.Debug($"Client {conn.ConnectionID} connected, Current Count : {currentNumber}");
                m_MessageHandler.OnConnected(conn.ConnectionID);

                StartReceive(conn);
                StartAccept(listenSocket, args);
            }
            else
            {
                m_FreeConnectionIds.Enqueue(conn.ConnectionID);

                //check if valid listen socket
                if (IsRunning)
                    StartAccept(listenSocket, args);
                else
                    m_RunningResetEvent.Set();
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

            var conn = m_ConnectionList[connectionID - 1];

            if (!conn.IsConnected)
            {
                message.Release();
                return false;
            }

            Send(conn, message);
            return true;
        }

        /// <summary>
        /// Disconnect client
        /// </summary>
        public bool Disconnect(int connectionId)
        {
            //is valid connection number
            if (connectionId <= 0 || connectionId > m_ConnectionList.Count) return false;

            var conn = m_ConnectionList[connectionId - 1];
            return Disconnect(conn);
        }

        public IPEndPoint GetConnectionAddress(int connectionId)
        {
            //is valid connection number
            if (connectionId <= 0 || connectionId > m_ConnectionList.Count) return null;

            var conn = m_ConnectionList[connectionId - 1];
            return conn.IsConnected ? (IPEndPoint)conn.Socket.RemoteEndPoint : null;
        }

        protected override bool Disconnect(Connection conn)
        {
            if (!conn.TrySetDisconnected()) return false;

            //now we have right to disconnect socket. as it'll be async
            try
            {
                if (conn.Socket.Connected)
                {
                    conn.Socket.Shutdown(SocketShutdown.Both);
                    conn.Socket.Disconnect(true);
                }
            }
            catch { }

            //if released all already
            if(conn.Lock.Release() == 0)
            {
                m_OnRecycleReady(conn, true);
            }
            else
            {
                //wait for others
                ThreadPool.RegisterWaitForSingleObject(conn.Lock.WaitHandle, m_OnRecycleReady, conn, 3000, true);
            }
            return true;
        }

        //this is where actually reuse socket take place, enqueue socket id to freeConnectionids
        private void RecycleConnection(object sender, bool success)
        {
            var conn = sender as Connection;
            m_FreeConnectionIds.Enqueue(conn.ConnectionID);
            var currentNumber = Interlocked.Decrement(ref m_CurrentConnectionCount);
            m_Logger?.Debug($"Client {conn.ConnectionID} Disconnected, Current Count : {currentNumber}");
        }
    }
}
