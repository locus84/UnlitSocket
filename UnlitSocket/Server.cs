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
        public bool IsRunning { get; private set; } = false;
        public int Port { get; private set; } = 6000;

        //this list is only increasing
        List<UserToken> m_TokenList;
        ConcurrentQueue<int> m_FreeConnectionIds = new ConcurrentQueue<int>();
        ManualResetEvent m_RunningResetEvent = new ManualResetEvent(true);

        public Server()
        {
            m_CurrentConnectionCount = 0;
            m_FreeConnectionIds = new ConcurrentQueue<int>();
            m_TokenList = new List<UserToken>(16);
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
            UserToken token = null;

            if(m_FreeConnectionIds.TryDequeue(out var connID))
            {
                token = m_TokenList[connID - 1];
            }
            else
            {
                token = new UserToken(m_TokenList.Count + 1, this);
                token.ReceiveArg.Completed += ProcessReceive;
                m_TokenList.Add(token);
            }

            args.UserToken = token;
            args.AcceptSocket = token.Socket;
            if (!socket.AcceptAsync(args)) ProcessAccept(socket, args);
        }

        private void ProcessAccept(object sender, SocketAsyncEventArgs args)
        {
            var token = (UserToken)args.UserToken;

            if (args.SocketError == SocketError.Success)
            {
                var socket = sender as Socket;
                var currentNumber = Interlocked.Increment(ref m_CurrentConnectionCount);
                //send initial message that indicates socket is accepted
                token.IsConnected = true;

                m_Logger?.Debug($"Client {token.ConnectionID} connected, Current Count : {currentNumber}");

                try
                {
                    m_MessageHandler.OnConnected(token.ConnectionID);
                }
                catch (Exception e)
                {
                    m_Logger?.Exception(e);
                }

                StartReceive(token);

                args.AcceptSocket = null;
                StartAccept(socket, args);
            }
            else
            {
                try
                {
                    if (args.AcceptSocket.Connected)
                        args.AcceptSocket.Disconnect(true);
                }
                catch { }

                m_FreeConnectionIds.Enqueue(token.ConnectionID);
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
            m_RunningResetEvent.WaitOne();

            //now usertokens are fixed count
            foreach(var token in m_TokenList)
            {
                //socket could be already disposed
                try
                {
                    var socket = token.Socket;
                    if(socket != null) socket.Disconnect(true);
                }
                catch { }
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
            if(message.Position == 0)
            {
                message.Release();
                return false;
            }

            //1 is reserved, so let it -1 index of tokenlist
            if(connectionID <= 0 || connectionID > m_TokenList.Count)
            {
                message.Release();
                return false;
            }

            var token = m_TokenList[connectionID - 1];

            if (!token.IsConnected)
            {
                message.Release();
                return false;
            }

            Send(token.Socket, message);
            return true;
        }

        /// <summary>
        /// Disconnect client
        /// </summary>
        public void Disconnect(int connectionID)
        {
            try
            {
                //no id check, will be done by exception
                var socket = m_TokenList[connectionID - 1].Socket;
                if (socket.Connected) socket.Disconnect(true);
            }
            catch { }
        }

        protected override void CloseSocket(UserToken token, bool withCallback)
        {
            base.CloseSocket(token, withCallback);
            var currentNumber = Interlocked.Decrement(ref m_CurrentConnectionCount);

            m_Logger?.Debug($"Client {token.ConnectionID} Disconnected, Current Count : {currentNumber}");
            // decrement the counter keeping track of the total number of clients connected to the server
            m_FreeConnectionIds.Enqueue(token.ConnectionID);
        }
    }
}
