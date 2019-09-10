using System;
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
            if (message.Position == 0)
            {
                message.Release();
                return;
            }

            SocketAsyncEventArgs sendArg;
            if (!m_SendArgsPool.TryTake(out sendArg))
            {
                sendArg = new SocketAsyncEventArgs();
                sendArg.BufferList = new List<ArraySegment<byte>>();
                sendArg.Completed += ProcessSend;
            }

            sendArg.UserToken = message;
            message.BindToArgsSend(sendArg);

            try
            {
                bool isPending = socket.SendAsync(sendArg);
                if (!isPending) ProcessSend(socket, sendArg);
            }
            catch
            {
                //send failed, let's release message
                message.Release();
                sendArg.UserToken = null;
                m_SendArgsPool.Add(sendArg);
            }
        }

        protected virtual void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                ((Message)e.UserToken).Release();
                e.UserToken = null;
                m_SendArgsPool.Add(e);
            }
            else
            {
                //send failed, let's release
                ((Message)e.UserToken).Release();
                e.UserToken = null;
                m_SendArgsPool.Add(e);
            }
        }

        protected void StartReceive(AsyncUserToken token)
        {
            token.ReadyToReceiveLength();
            bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
            if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var token = e.UserToken as AsyncUserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                if (token.CurrentMessage != null)
                {
                    //return true means we have received all bytes
                    if (token.AppendReceivedBuffer(e.BytesTransferred)) 
                    {
                        m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Data, token.CurrentMessage));

                        //clear message
                        token.ReadyToReceiveLength();
                        token.CurrentMessage = null;
                    }
                }
                else 
                {
                    if(token.HandleLengthReceive(e.BytesTransferred))
                    {
                        token.CurrentMessage = Message.Pop();
                        token.ReadyToReceiveMessage();
                    }
                }

                bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
                if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
            }
            else
            {
                if(token.CurrentMessage != null)
                {
                    token.CurrentMessage.Release();
                    token.CurrentMessage = null;
                }
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
                        OnDataReceived?.Invoke(receivedMessage.ConnectionID, receivedMessage.MessageData);
                        receivedMessage.MessageData.Release();
                        break;
                    default:
                        throw new Exception("Unknown MessageType");
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
            catch { }
            token.Socket.Close();
            token.Socket = null;
            token.IsConnected = false;
            m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Disconnected));
        }
    }
}


