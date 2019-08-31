using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace TcpNetworking
{
    public class Client
    {
        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        public delegate void OnReceivedDelegate(Message message);
        public OnReceivedDelegate OnReceived;

        private int m_receiveBufferSize;
        SocketAsyncEventArgs m_ReceiveArg;
        SocketAsyncEventArgs m_SendArg;
        ILogReceiver m_Logger;
        Socket m_Socket;

        ConcurrentQueue<Message> m_MessagePool = new ConcurrentQueue<Message>();
        ConcurrentQueue<Message> m_ReceivedMessage = new ConcurrentQueue<Message>();

        public Client(int receiveBufferSize)
        {
            m_receiveBufferSize = receiveBufferSize;
            m_ReceiveArg = new SocketAsyncEventArgs();
            m_ReceiveArg.Completed += ProcessReceive;
            m_ReceiveArg.SetBuffer(new byte[receiveBufferSize], 0, receiveBufferSize);
            m_SendArg = new SocketAsyncEventArgs();
            m_SendArg.Completed += ProcessSend;
        }

        public void SetLogger(ILogReceiver logger) => m_Logger = logger;

        public void Connect(IPEndPoint remoteEndPoint)
        {
            if (Status != ConnectionStatus.Disconnected)
            {
                m_Logger?.Debug("Invalid connect function call");
                return;
            }

            m_Socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_Socket.SendTimeout = 5000;
            m_Socket.NoDelay = true;

            var acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += ProcessConnect;
            RemoteEndPoint = remoteEndPoint;
            acceptEventArg.RemoteEndPoint = remoteEndPoint;
            Status = ConnectionStatus.Connecting;
            bool isPending = m_Socket.ConnectAsync(acceptEventArg);
            if (!isPending) ProcessConnect(null, acceptEventArg);
        }

        private void ProcessConnect(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                Status = ConnectionStatus.Connected;

                bool isPending = m_Socket.ReceiveAsync(m_ReceiveArg);
                if (!isPending) ProcessReceive(null, m_ReceiveArg);
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        private void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                m_Logger?.Debug($"Client ReceivedData Offset : {e.Offset} Count : {e.BytesTransferred}");
                bool isPending = m_Socket.ReceiveAsync(e);
                if (!isPending) ProcessReceive(sender, e);
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        public void Send(Message message)
        {
            if (Status != ConnectionStatus.Connected)
            {
                message.Retain();
                message.Release();
                return;
            }

            try
            {
                message.Retain();
                var data = message.DataProvider.GetData();
                message.Args.SetBuffer(data.Array, data.Offset, data.Count);
                bool isPending = m_Socket.SendAsync(message.Args);
                if (!isPending) ProcessSend(null, message.Args);
            }
            catch
            {
                message.Release();
            }
        }

        private void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                m_Logger?.Debug($"Byte Transfered : { e.BytesTransferred }");
                ((Message)e.UserToken).Release();
            }
            else
            {
                m_Logger?.Debug("Send Failed");
                ((Message)e.UserToken).Release();
            }
        }

        public void Update()
        {
            Message receivedMessage;
            while(m_ReceivedMessage.TryDequeue(out receivedMessage))
            {
                receivedMessage.Retain();
                OnReceived?.Invoke(receivedMessage);
                receivedMessage.Release();
            }
        }

        public void Disconnect()
        {
            if (m_Socket != null) m_Socket.Disconnect(false);
        }

        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            Status = ConnectionStatus.Disconnected;

            try
            {
                m_Socket.Shutdown(SocketShutdown.Send);
            }
            catch { }

            m_Socket.Close();
            m_Socket = null;
        }

        public Message CreateMessage(IDataProvider data)
        {
            Message message;
            if (!m_MessagePool.TryDequeue(out message))
            {
                message = new Message(m_MessagePool);
                m_Logger?.Debug("NewMessage");
                message.Args.Completed += ProcessSend;
            }
            message.DataProvider = data;
            return message;
        }
    }
}
