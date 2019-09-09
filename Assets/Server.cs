using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace UnlitSocket
{
    public class Server : Peer
    {
        private int m_MaxConnectionCount; //maximum connection
        Socket m_ListenSocket;
        int m_TotalBytesRead;
        int m_CurrentConnectionCount;
        public bool IsRunning { get; private set; } = false;

        public int Port { get; private set; }

        AsyncUserTokenPool m_TokenPool;
        Dictionary<int, AsyncUserToken> m_ConnectionDic;

        public Server(int maxConnections)
        {
            m_TotalBytesRead = 0;
            m_CurrentConnectionCount = 0;
            m_MaxConnectionCount = maxConnections;

            m_TokenPool = new AsyncUserTokenPool(maxConnections);
            m_ConnectionDic = new Dictionary<int, AsyncUserToken>(maxConnections);
        }

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
            m_ListenSocket.NoDelay = true;
            m_ListenSocket.SendBufferSize = 512;
            m_ListenSocket.ReceiveBufferSize = 512;
            m_ListenSocket.SendTimeout = 5000;
            m_ListenSocket.DualMode = true;
            m_ListenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
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
                    m_Logger?.Debug($"Client {token.ConnectionID} connected, Current Count : {currentNumber} - {socket.AddressFamily}");

                    token.Socket = e.AcceptSocket;
                    token.Socket.SendTimeout = 5000;
                    token.Socket.NoDelay = true;

                    token.IsConnected = true;
                    m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Connected));
                    StartReceive(token);
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

            Send(token.Socket, message);
        }

        protected override void CloseSocket(AsyncUserToken token)
        {
            base.CloseSocket(token);
            var currentNumber = Interlocked.Decrement(ref m_CurrentConnectionCount);
            m_Logger?.Debug($"client { token.ConnectionID } has been disconnected from the server. There are {currentNumber} clients connected to the server");
            // decrement the counter keeping track of the total number of clients connected to the server
            m_TokenPool.Push(token);
        }
    }
}
