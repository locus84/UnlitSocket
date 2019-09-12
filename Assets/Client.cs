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

        UserToken m_Token;

        public Client(int clientID, int receiveBufferSize)
        {
            m_Token = new UserToken(clientID);
            m_Token.ReceiveArg.Completed += ProcessReceive;
        }

        public void Connect(IPEndPoint remoteEndPoint)
        {
            if (Status != ConnectionStatus.Disconnected)
            {
                m_Logger?.Debug("Invalid connect function call");
                return;
            }

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

        /// <summary>
        /// Send to the server
        /// </summary>
        public void Send(Message message)
        {
            if(message.Position == 0)
            {
                message.Release();
                return;
            }

            if (Status != ConnectionStatus.Connected)
            {
                message.Release();
                return;
            }

            Send(m_Token.Socket, message);
        }

        public void Disconnect()
        {
            try
            {
                var socket = m_Token.Socket;
                if (socket != null) socket.Disconnect(false);
            }
            catch { }
        }

        protected override void CloseSocket(UserToken token)
        {
            Status = ConnectionStatus.Disconnected;
            base.CloseSocket(token);
        }
    }
}
