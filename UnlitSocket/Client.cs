﻿using System;
using System.Net;
using System.Net.Sockets;

namespace UnlitSocket
{
    public class Client : Peer
    {
        public int ClientID => m_Token.ConnectionID;

        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        byte[] m_ConnectBuffer = new byte[1];
        UserToken m_Token;

        public Client(IConnection connectionObject = null)
        {
            m_Token = new UserToken(0, this, connectionObject ?? new DefaultConnection(m_ReceivedMessages));
            m_Token.ReceiveArg.Completed += ProcessReceive;
            m_Token.Connection.UserToken = m_Token;
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
            System.Threading.Tasks.Task.Run(() => ConnectInternal());
        }

        private void ConnectInternal()
        {
            try
            {
                if (m_Token.Socket.IsBound) m_Token.RebuildSocket();
                var connectAr = m_Token.Socket.BeginConnect(RemoteEndPoint, null, null);

                //wait for connecting
                if (!connectAr.AsyncWaitHandle.WaitOne(5000, true)) throw new SocketException(10060);
                m_Token.Socket.EndConnect(connectAr);

                //wait for initial message
                var helloAr = m_Token.Socket.BeginReceive(m_ConnectBuffer, 0, 1, SocketFlags.None, out var socketError, null, null);
                if (!helloAr.AsyncWaitHandle.WaitOne(5000, true)) throw new SocketException(10060);
                if (socketError != SocketError.Success && socketError != SocketError.IOPending) throw new SocketException((int)socketError);
                m_Token.Socket.EndReceive(helloAr);

                //rejected by server due to max connection
                if (m_ConnectBuffer[0] == 0) throw new Exception("Max Connection Reached");

                //now it's connected
                Status = ConnectionStatus.Connected;
                m_Logger?.Debug($"Connection {ClientID} has been connected to server");

                try
                {
                    m_Token.Connection.OnConnected();
                }
                catch(Exception e)
                {
                    m_Logger?.Exception(e);
                }

                StartReceive(m_Token);
            }
            catch (Exception e)
            {
                CloseSocket(m_Token);
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
        }

        protected override void CloseSocket(UserToken token)
        {
            Status = ConnectionStatus.Disconnected;
            base.CloseSocket(token);
        }

        public override bool Send(int connectionID, Message msg)
        {
            if(m_Token.ConnectionID == connectionID)
            {
                Send(msg);
                return true;
            }
            else
            {
                msg.Release();
                return false;
            }
        }

        public override void Disconnect(int connectionID)
        {
            if (m_Token.ConnectionID == connectionID) Disconnect();
        }
    }
}
