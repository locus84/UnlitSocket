using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace UnlitSocket
{
    public class Client : Peer
    {
        public int ClientID => m_Token.ConnectionID;

        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        AsyncUserToken m_Token;

        public Client(int clientID, int receiveBufferSize)
        {
            m_Token = new AsyncUserToken(clientID);
            m_Token.ReceiveArg.Completed += ProcessReceive;
        }

        public void Connect(IPEndPoint remoteEndPoint)
        {
            if (Status != ConnectionStatus.Disconnected)
            {
                m_Logger?.Debug("Invalid connect function call");
                return;
            }
            try
            {
                m_Token.Socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                m_Token.Socket.SendTimeout = 5000;
                m_Token.Socket.NoDelay = true;

                var acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += ProcessConnect;
                RemoteEndPoint = remoteEndPoint;
                acceptEventArg.RemoteEndPoint = remoteEndPoint;
                Status = ConnectionStatus.Connecting;
                bool isPending = m_Token.Socket.ConnectAsync(acceptEventArg);
                if (!isPending) ProcessConnect(m_Token.Socket, acceptEventArg);
            }
            catch(System.Exception e)
            {
                m_Logger?.Debug(e.ToString());
            }
        }

        private void ProcessConnect(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                Status = ConnectionStatus.Connected;
                m_ReceivedMessages.Enqueue(new ReceivedMessage(ClientID, MessageType.Connected));
                StartReceive(m_Token);
            }
            else
            {
                m_Logger?.Debug(e.SocketError.ToString());
                CloseSocket(m_Token);
            }
        }

        public void Send(Message message)
        {
            message.Retain();

            if (Status != ConnectionStatus.Connected)
            {
                message.Release();
                return;
            }

            Send(m_Token.Socket, message);
        }

        public void Disconnect()
        {
            if (m_Token.Socket != null) m_Token.Socket.Disconnect(false);
        }

        protected override void CloseSocket(AsyncUserToken token)
        {
            Status = ConnectionStatus.Disconnected;
            base.CloseSocket(token);
        }
    }
}
