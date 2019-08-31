using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System;
using System.Threading;
using System.Net;
using System.Collections.Concurrent;

namespace TcpNetworking
{
    public class Server
    {
        private int m_numConnections;
        private int m_receiveBufferSize;
        BufferManager m_bufferManager;
        Socket listenSocket;
        SocketAsyncEventArgsPool m_readWritePool;
        int m_totalBytesRead;
        int m_numConnectedSockets;
        ILogReceiver m_Logger;
        public bool IsRunning { get; private set; } = false;

        public int Port { get; private set; }

        List<AsyncUserToken> m_UserList = new List<AsyncUserToken>();

        public Server(int numConnections, int receiveBufferSize)
        {
            m_totalBytesRead = 0;
            m_numConnectedSockets = 0;
            m_numConnections = numConnections;
            m_receiveBufferSize = receiveBufferSize;
            m_bufferManager = new BufferManager(receiveBufferSize * numConnections, receiveBufferSize);
            m_readWritePool = new SocketAsyncEventArgsPool(numConnections);
        }

        public void SetLogger(ILogReceiver logger) => m_Logger = logger;

        public void Init()
        {
            //init buffer, use given value in initializer
            m_bufferManager.InitBuffer();

            for (int i = 0; i < m_numConnections; i++)
            {
                //Pre-allocate a set of reusable SocketAsyncEventArgs
                var eventArg = new SocketAsyncEventArgs();
                eventArg.Completed += IO_Completed;
                eventArg.UserToken = new AsyncUserToken();

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
                m_bufferManager.SetBuffer(eventArg);

                // add SocketAsyncEventArg to the pool
                m_readWritePool.Push(eventArg);
            }
        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        public void Start(int port)
        {
            // create the socket which listens for incoming connections
            listenSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.DualMode = true;
            listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            listenSocket.Listen(1024);

            var acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += ProcessAccept;

            StartAccept(listenSocket, acceptEventArg);
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
                var currentNumber = Interlocked.Increment(ref m_numConnectedSockets);

                if (currentNumber > m_numConnections)
                {
                    //close bad accepted socket
                    if (e.AcceptSocket != null)
                        e.AcceptSocket.Disconnect(false);
                    Interlocked.Decrement(ref m_numConnectedSockets);
                }
                else
                {
                    m_Logger?.Debug($"Client connected, Current Count : {currentNumber} - {socket.AddressFamily}");

                    SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
                    var userToken = readEventArgs.UserToken as AsyncUserToken;
                    userToken.Socket = e.AcceptSocket;
                    userToken.Socket.SendTimeout = 5000;
                    userToken.Socket.NoDelay = true;

                    e.UserToken = userToken;

                    lock (m_UserList)
                    {
                        m_UserList.Add(userToken);
                    }

                    bool isPending = e.AcceptSocket.ReceiveAsync(readEventArgs);
                    if (!isPending) ProcessReceive(readEventArgs);
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

        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    m_Logger?.Debug("Event is not send/receive");
                    break;
            }
        }

        public void Update()
        {

        }

        public void Stop()
        {
            lock (m_UserList)
            {
                foreach (var user in m_UserList) user.Socket.Disconnect(false);
            }
            IsRunning = false;
            listenSocket?.Close();
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            // check if the remote host closed the connection
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                //increment the count of the total bytes receive by the server
                Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
                m_Logger?.Debug($"Server ReceivedData Offset : {e.Offset} Count : {e.BytesTransferred}");
                //echo the data received back to the client
                e.SetBuffer(e.Offset, e.BytesTransferred);
                bool isPending = token.Socket.SendAsync(e);
                if (!isPending) ProcessSend(e);
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // done echoing data back to the client
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                // read the next block of data send from the client
                e.SetBuffer(e.Offset, m_receiveBufferSize);
                bool isPending = token.Socket.ReceiveAsync(e);
                if (!isPending) ProcessReceive(e);
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = e.UserToken as AsyncUserToken;

            lock (m_UserList)
            {
                m_UserList.Remove(token);
            }

            // close the socket associated with the client
            try
            {
                token.Socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch (Exception) { }

            token.Socket.Close();

            // Free the SocketAsyncEventArg so they can be reused by another client
            m_readWritePool.Push(e);

            // decrement the counter keeping track of the total number of clients connected to the server
            var currentNumber = Interlocked.Decrement(ref m_numConnectedSockets);

            m_Logger?.Debug($"A client has been disconnected from the server. There are {currentNumber} clients connected to the server");
        }
    }
}
