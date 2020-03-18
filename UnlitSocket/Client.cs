using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UnlitSocket
{
    public class Client : Peer
    {
        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        Connection m_Connection;

        public Client() => m_Connection = CreateConnection(0);

        public Task Connect(string host, int port, float timeOutSec = 5f)
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0) throw new ArgumentException("Invalid Host");
            var address = Dns.GetHostAddresses(host)[0];
            return Connect(new IPEndPoint(address, port), timeOutSec);
        }

        public Task Connect(IPEndPoint remoteEndPoint, float timeOutSec = 5f)
        {
            if (Status != ConnectionStatus.Disconnected) return Task.CompletedTask;

            RemoteEndPoint = remoteEndPoint;

            try
            {
                m_Connection.SetConnectedAndResetEvent();
                var asyncResult = m_Connection.Socket.BeginConnect(RemoteEndPoint, null, null);
                Status = ConnectionStatus.Connecting;

                //socket can be swapped if we disconnect immediately
                var socket = m_Connection.Socket;
                var connectTask = new Task(() => ConnectInternal(socket, timeOutSec, asyncResult));
                connectTask.Start();
                return connectTask;
            }
            catch
            {
                Disconnect(m_Connection);
                Status = ConnectionStatus.Disconnected;
                //receive has not been started, so we signal manually
                m_Connection.Lock.Release();
                throw;
            }
        }

        private void ConnectInternal(Socket socket, float timeOut, IAsyncResult connectAr)
        {
            try
            {
                //wait for connecting
                if (!connectAr.AsyncWaitHandle.WaitOne((int)(timeOut * 1000), true)) throw new SocketException(10060);
                socket.EndConnect(connectAr);

                //now it's connected
                Status = ConnectionStatus.Connected;
                m_Logger?.Debug($"Connected to server");

                m_EventHandler.OnConnected(m_Connection.ConnectionID);
                StartReceive(m_Connection);
            }
            catch (Exception e)
            {
                //it's not started receiving, so no need to do anything.
                //just release signal, and handle socket destruction, no callback required
                Disconnect(m_Connection);
                Status = ConnectionStatus.Disconnected;
                m_Logger?.Warning($"Failed to connect to {RemoteEndPoint} : {e.Message}");
                //receive has not been started, so we signal manually
                m_Connection.Lock.Release();
            }
        }

        public override bool Send(int connectionId, Message message)
        {
            //doesn't matter connectionId.
            return Send(message);
        }

        public override bool Send(IList<int> connectionIds, Message message)
        {
            throw new NotSupportedException("client does not suuport multiple connection send");
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
            Disconnect(m_Connection);
            m_Connection.Lock.Wait();
        }

        protected override void StopReceive(Connection connection)
        {
            Status = ConnectionStatus.Disconnected;
            m_Logger?.Debug($"Disconnected from server");
            base.StopReceive(connection);
        }
    }
}
