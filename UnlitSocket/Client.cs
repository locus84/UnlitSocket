using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket
{
    public class Client : Peer
    {
        public int ClientID => m_Token.ConnectionID;

        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        UserToken m_Token;
        ManualResetEvent DisconnectedEvent = new ManualResetEvent(true);

        public Client()
        {
            m_Token = new UserToken(0, this);
            m_Token.ReceiveArg.Completed += ProcessReceive;
        }

        public void Connect(string host, int port, float timeOutSec = 5f)
        {
            Connect(new IPEndPoint(IPAddress.Parse(host), port), timeOutSec);
        }

        public void Connect(IPEndPoint remoteEndPoint, float timeOutSec = 5f)
        {
            if (Status != ConnectionStatus.Disconnected)
            {
                m_Logger?.Debug("Invalid connect function call");
                return;
            }

            Status = ConnectionStatus.Connecting;
            RemoteEndPoint = remoteEndPoint;

            //call async function but to support synchronized call, we create socket and connect here
            if (m_Token.Socket.IsBound) m_Token.RebuildSocket();
            var asyncResult = m_Token.Socket.BeginConnect(RemoteEndPoint, null, null);

            DisconnectedEvent.Reset();

            var connectThread = new System.Threading.Thread(() => ConnectInternal(timeOutSec, asyncResult));
            connectThread.Start();
        }

        private void ConnectInternal(float timeOut, IAsyncResult connectAr)
        {
            try
            {
                //wait for connecting
                if (!connectAr.AsyncWaitHandle.WaitOne((int)(timeOut * 1000), true)) throw new SocketException(10060);
                m_Token.Socket.EndConnect(connectAr);

                //now it's connected
                Status = ConnectionStatus.Connected;
                m_Logger?.Debug($"Connection {ClientID} has been connected to server");

                try
                {
                    m_MessageHandler.OnConnected(ClientID);
                }
                catch(Exception e)
                {
                    m_Logger?.Exception(e);
                }

                StartReceive(m_Token);
            }
            catch (Exception e)
            {
                CloseSocket(m_Token, false);
                m_Logger?.Exception(e);
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

            if (!m_Token.IsConnected)
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
                m_Token.Socket.Disconnect(true);
                
            }
            catch { }

            //wait for disconnected event will synchronize receive thread.
            DisconnectedEvent.WaitOne();
        }

        protected override void CloseSocket(UserToken token, bool withCallback)
        {
            Status = ConnectionStatus.Disconnected;
            base.CloseSocket(token, withCallback);
            DisconnectedEvent.Set();
        }
    }
}
