using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Collections.Concurrent;

namespace UnlitSocket
{
    public class Server : Peer
    {
        private int m_MaxConnectionCount; //maximum connection
        Socket m_ListenSocket;
        int m_CurrentConnectionCount;
        int m_AcceptedCount;
        public bool IsRunning { get; private set; } = false;

        public int Port { get; private set; }

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
                var token = new UserToken(i);
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
        private void StartAccept(Socket socket, SocketAsyncEventArgs acceptEventArg)
        {
            bool isPending = false;
            isPending = socket.AcceptAsync(acceptEventArg);
            if (!isPending) ProcessAccept(socket, acceptEventArg);
        }

        private void ProcessAccept(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success && m_TokenPool.TryDequeue(out var token))
            {
                var socket = sender as Socket;
                var currentNumber = Interlocked.Increment(ref m_CurrentConnectionCount);
                m_AcceptedCount++;
                m_Logger?.Debug($"Client {token.ConnectionID} connected, Current Count : {currentNumber}");

                token.Socket = e.AcceptSocket;
                token.Socket.SendTimeout = 5000;
                token.Socket.NoDelay = true;

                token.IsConnected = true;
                m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Connected));
                StartReceive(token);

                e.AcceptSocket = null;
                StartAccept(socket, e);
            }
            else
            {
                //we failed to receive new connection, let's close socket if there is
                if (e.AcceptSocket != null)
                    e.AcceptSocket.Close();

                if (IsRunning)
                    StartAccept((Socket)sender, e);
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            m_ListenSocket?.Close();
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
        public void Send(IList<int> recipients, Message message)
        {
            for(int i = 0; i < recipients.Count; i++)
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
