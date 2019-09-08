using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;

namespace UnlitSocket
{
    public class Peer
    {
        public ConnectionStatusChangeDelegate OnConnected;
        public ConnectionStatusChangeDelegate OnDisconnected;
        public DataReceivedDelegate OnDataReceived;

        protected ConcurrentBag<SocketAsyncEventArgs> m_SendArgsPool = new ConcurrentBag<SocketAsyncEventArgs>();
        protected ConcurrentQueue<ReceivedMessage> m_ReceivedMessages = new ConcurrentQueue<ReceivedMessage>();
        protected ILogReceiver m_Logger;

        protected struct ReceivedMessage
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

        public void SetLogger(ILogReceiver logger) => m_Logger = logger;

        /// <summary>
        /// message must be retained before calling this function
        /// </summary>
        protected virtual void Send(Socket socket, Message message)
        {
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
                bool isPending = socket.SendAsync(sendArg);
                if (!isPending) ProcessSend(socket, sendArg);
            }
            catch
            {
                m_Logger?.Debug($"Send Failed Recycling Message");
                message.Release();
                sendArg.UserToken = null;
                sendArg.BufferList.RemoveAt(0);
                sendArg.BufferList = null;
                m_SendArgsPool.Add(sendArg);
            }
        }

        protected virtual void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    m_Logger?.Debug($"Byte Transfered : { e.BytesTransferred }");
                    ((Message)e.UserToken).Release();
                    e.UserToken = null;
                    e.BufferList.RemoveAt(0);
                    e.BufferList = null;
                    m_SendArgsPool.Add(e);
                }
                else
                {
                    m_Logger?.Debug("Send Failed");
                    ((Message)e.UserToken).Release();
                    e.UserToken = null;
                    e.BufferList.RemoveAt(0);
                    e.BufferList = null;
                    m_SendArgsPool.Add(e);
                }
            }
            catch(System.Exception ex)
            {
                m_Logger?.Debug(ex.ToString());
            }
        }

        protected void StartReceive(AsyncUserToken token)
        {
            bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
            if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var token = e.UserToken as AsyncUserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                //this is initial length
                //TODO have to check bytesTransferred
                if (token.CurrentMessage == null)
                {
                    var size = MessageReader.ReadUInt16(token.ReceiveArg.Buffer);
                    token.CurrentMessage = Message.Pop(size);
                    token.CurrentMessage.BindToArgsReceive(token.ReceiveArg, size);
                }
                else
                {
                    m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Data, token.CurrentMessage));
                    token.ClearMessage();
                }

                bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
                if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
            }
            else
            {
                token.ClearMessage();
                CloseSocket(token);
            }
        }

        public virtual void Update()
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
                        throw new System.Exception("Unknown MessageType");
                }
            }
        }

        protected virtual void CloseSocket(AsyncUserToken token)
        {
            // close the socket associated with the client
            try
            {
                token.Socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch (System.Exception) { }
            token.Socket.Close();
            token.Socket = null;
            token.IsConnected = false;
            m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Disconnected));
        }
    }
}


