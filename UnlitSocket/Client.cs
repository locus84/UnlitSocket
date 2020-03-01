using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket
{
    public class Client : Peer
    {
        public int ClientID => m_Connection.ConnectionID;

        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        Connection m_Connection;
        bool initial = true;

        public Client()
        {
            m_Connection = new Connection(0, this);
            m_Connection.ReceiveArg.Completed += ProcessReceive;
        }

        public void Connect(string host, int port, float timeOutSec = 5f)
        {
            if (host == "localhost") host = "127.0.0.1";
            Connect(new IPEndPoint(IPAddress.Parse(host), port), timeOutSec);
        }

        public void Connect(IPEndPoint remoteEndPoint, float timeOutSec = 5f)
        {
            if (Status != ConnectionStatus.Disconnected) return;

            RemoteEndPoint = remoteEndPoint;

            m_Connection.DisconnectEvent.Reset(1);

            try
            {
                if (initial) initial = false;
                else m_Connection.RebuildSocket();

                var asyncResult = m_Connection.Socket.BeginConnect(RemoteEndPoint, null, null);
                var connectThread = new Thread(() => ConnectInternal(timeOutSec, asyncResult));

                Status = ConnectionStatus.Connecting;
                connectThread.Start();
            }
            catch
            {
                m_Connection.DisconnectEvent.Signal();
                throw;
            }
        }

        private void ConnectInternal(float timeOut, IAsyncResult connectAr)
        {
            try
            {
                //wait for connecting
                if (!connectAr.AsyncWaitHandle.WaitOne((int)(timeOut * 1000), true)) throw new SocketException(10060);
                m_Connection.Socket.EndConnect(connectAr);

                //now it's connected
                Status = ConnectionStatus.Connected;
                m_Connection.IsConnected = true;
                m_Logger?.Debug($"Connected to server");

                try
                {
                    m_MessageHandler.OnConnected(ClientID);
                }
                catch (Exception e)
                {
                    m_Logger?.Exception(e);
                }

                StartReceive(m_Connection);
            }
            catch (Exception e)
            {
                CloseSocket(m_Connection, false);
                m_Connection.DisconnectEvent.Signal();
                m_Logger?.Warning(e.Message);
            }
        }

        /// <summary>
        /// Send to the server
        /// </summary>
        public bool Send(Message message)
        {
            if (message.Position == 0)
            {
                message.Release();
                return false;
            }

            if (!m_Connection.IsConnected)
            {
                message.Release();
                return false;
            }

            Send(m_Connection, message);
            return true;
        }

        public void Disconnect()
        {
            m_Connection.Socket.Close();
            m_Connection.CloseSocket();
            m_Connection.DisconnectEvent.Wait();
        }

        protected override bool CloseSocket(Connection connection, bool withCallback)
        {
            Status = ConnectionStatus.Disconnected;
            m_Logger?.Debug($"Disconnected from server");
            return base.CloseSocket(connection, withCallback);
        }
    }
}
