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

        public Client() => m_Connection = CreateConnection(0);

        public void Connect(string host, int port, float timeOutSec = 5f)
        {
            if (host == "localhost") host = "127.0.0.1";
            Connect(new IPEndPoint(IPAddress.Parse(host), port), timeOutSec);
        }

        public void Connect(IPEndPoint remoteEndPoint, float timeOutSec = 5f)
        {
            if (Status != ConnectionStatus.Disconnected) return;

            RemoteEndPoint = remoteEndPoint;

            try
            {
                m_Connection.DisconnectEvent.Reset(1);

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
                //socket is built, let's assume it's connected
                //so that disconnect(connection) can handle this
                m_Connection.IsConnected = true;

                //wait for connecting
                if (!connectAr.AsyncWaitHandle.WaitOne((int)(timeOut * 1000), true)) throw new SocketException(10060);
                m_Connection.Socket.EndConnect(connectAr);

                //now it's connected
                Status = ConnectionStatus.Connected;
                
                m_Logger?.Debug($"Connected to server");

                m_MessageHandler.OnConnected(ClientID);
                StartReceive(m_Connection);
            }
            catch (Exception e)
            {
                //it's not started receiving, so no need to do anything.
                //just release signal, and handle socket destruction, no callback required
                Disconnect(m_Connection);
                Status = ConnectionStatus.Disconnected;
                m_Logger?.Warning($"Connect Failed : {e.Message}");
                m_Connection.DisconnectEvent.Signal();
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
            if (Status == ConnectionStatus.Disconnected) return;
            m_Connection.Socket.Close();
            Console.WriteLine("Close");
            Disconnect(m_Connection);
            m_Connection.DisconnectEvent.Wait();
        }

        protected override void StopReceive(Connection connection)
        {
            Status = ConnectionStatus.Disconnected;
            m_Logger?.Debug($"Disconnected from server");
            base.StopReceive(connection);
        }
    }
}
