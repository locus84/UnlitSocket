using System;
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

        public Client() : this(0) { }

        internal Client(int clientID)
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

            Status = ConnectionStatus.Connecting;
            RemoteEndPoint = remoteEndPoint;
            System.Threading.Tasks.Task.Run(ConnectInternal);
        }

        private void ConnectInternal()
        {
            try
            {
                m_Token.Socket = new Socket(RemoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                m_Token.Socket.SendTimeout = 5000;
                m_Token.Socket.NoDelay = true;
                var connectAr = m_Token.Socket.BeginConnect(RemoteEndPoint, null, null);

                //wait for connecting
                if (!connectAr.AsyncWaitHandle.WaitOne(5000, true)) throw new SocketException(10060);
                m_Token.Socket.EndConnect(connectAr);
                SetKeepAlive(m_Token.Socket, true, 5000, 1000);

                //wait for initial message
                var buffer = new byte[1];
                var helloAr = m_Token.Socket.BeginReceive(buffer, 0, 1, SocketFlags.None, out var socketError, null, null);
                if (!helloAr.AsyncWaitHandle.WaitOne(5000, true)) throw new SocketException(10060);
                if (socketError != SocketError.Success) throw new SocketException((int)socketError);
                m_Token.Socket.EndReceive(helloAr);

                //rejected by server due to max connection
                if (buffer[0] == 0) throw new Exception("Max Connection Reached");

                //now it's connected
                Status = ConnectionStatus.Connected;
                m_Logger?.Debug($"Connection {ClientID} has been connected to server");
                m_ReceivedMessages.Enqueue(new ReceivedMessage(ClientID, MessageType.Connected));
                StartReceive(m_Token);
            }
            catch (Exception e)
            {
                CloseSocket(m_Token);
                m_Logger.Exception(e);
            }
        }

        /// <summary>
        /// Send to the server
        /// </summary>
        public void Send(Message message)
        {
            if (message.Position == 0)
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
