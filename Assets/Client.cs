using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace UnlitSocket
{
    public class Client
    {
        public IPEndPoint RemoteEndPoint { get; private set; }
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

        public ConnectionStatusChangeDelegate OnConnected;
        public ConnectionStatusChangeDelegate OnDisconnected;
        public DataReceivedDelegate OnDataReceived;

        private int m_receiveBufferSize;
        SocketAsyncEventArgs m_ReceiveArg;
        Message m_CurrentMessage;
        ILogReceiver m_Logger;
        Socket m_Socket;
        byte[] m_SizeReadArray = new byte[2];

        ConcurrentBag<SocketAsyncEventArgs> m_SendArgsPool = new ConcurrentBag<SocketAsyncEventArgs>();
        ConcurrentQueue<Message> m_MessagePool = new ConcurrentQueue<Message>();
        ConcurrentQueue<ReceivedMessage> m_ReceivedMessages = new ConcurrentQueue<ReceivedMessage>();

        struct ReceivedMessage
        {
            public int ConnectionID;
            public MessageType Type;
            public Message MessageData;
            public ReceivedMessage(int connectionID, MessageType type, Message message = null)
            {
                ConnectionID = connectionID;
                Type = type;
                MessageData = message;
            }
        }

        public Client(int receiveBufferSize)
        {
            m_receiveBufferSize = receiveBufferSize;
            m_ReceiveArg = new SocketAsyncEventArgs();
            m_ReceiveArg.Completed += ProcessReceive;
            m_ReceiveArg.SetBuffer(new byte[receiveBufferSize], 0, receiveBufferSize);
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
                //this is initial length
                //TODO have to check bytesTransferred
                if (m_CurrentMessage == null)
                {
                    var size = MessageReader.ReadUInt16(m_ReceiveArg.Buffer);
                    m_CurrentMessage = Message.Pop(size);
                    m_CurrentMessage.BindToArgsReceive(m_ReceiveArg, size);
                }
                else
                {
                    m_ReceivedMessages.Enqueue(new ReceivedMessage(0, MessageType.Data, m_CurrentMessage));
                    m_CurrentMessage = null;
                    m_ReceiveArg.SetBuffer(m_SizeReadArray, 0, 2);
                }

                bool isPending = m_Socket.ReceiveAsync(e);
                if (!isPending) ProcessReceive(m_Socket, e);
            }
            else
            {
                if(m_CurrentMessage != null)
                {
                    m_CurrentMessage = null;
                    m_ReceiveArg.SetBuffer(m_SizeReadArray, 0, 2);
                }
                CloseClientSocket(e);
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

            SocketAsyncEventArgs sendArg;
            if (!m_SendArgsPool.TryTake(out sendArg))
            {
                sendArg = new SocketAsyncEventArgs();
                sendArg.Completed += ProcessSend;
            }

            sendArg.UserToken = message;
            message.BindToArgsSend(sendArg, message.Position);

            try
            {
                bool isPending = m_Socket.SendAsync(sendArg);
                if (!isPending) ProcessSend(m_Socket, sendArg);
            }
            catch
            {
                m_Logger?.Debug($"Send Failed Recycling Message");
                sendArg.BufferList = null;
                sendArg.UserToken = null;
                m_SendArgsPool.Add(sendArg);
                message.Release();
            }
        }

        private void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                m_Logger?.Debug($"Byte Transfered : { e.BytesTransferred }");
                ((Message)e.UserToken).Release();
                e.UserToken = null;
                e.BufferList = null;
                m_SendArgsPool.Add(e);
            }
            else
            {
                m_Logger?.Debug("Send Failed");
                ((Message)e.UserToken).Release();
                e.UserToken = null;
                e.BufferList = null;
                m_SendArgsPool.Add(e);
            }
        }

        public void Update()
        {
            ReceivedMessage receivedMessage;
            while (m_ReceivedMessages.TryDequeue(out receivedMessage))
            {
                switch (receivedMessage.Type)
                {
                    case MessageType.Connected:
                        OnConnected?.Invoke(receivedMessage.ConnectionID);
                        break;
                    case MessageType.Disconnected:
                        OnDisconnected?.Invoke(receivedMessage.ConnectionID);
                        break;
                    case MessageType.Data:
                        receivedMessage.MessageData.Retain();
                        OnDataReceived?.Invoke(receivedMessage.ConnectionID, receivedMessage.MessageData);
                        receivedMessage.MessageData.Release();
                        break;
                    default:
                        throw new Exception("Unknown MessageType");
                }
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
    }
}
