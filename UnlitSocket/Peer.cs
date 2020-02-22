#if UNITY_5_3_OR_NEWER
#define UNITY_RECEIVE
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;

namespace UnlitSocket
{
    public abstract class Peer
    {
        public ConnectionStatusChangeDelegate OnConnected;
        public ConnectionStatusChangeDelegate OnDisconnected;
        public DataReceivedDelegate OnDataReceived;
        public bool NoDelay { get; set; } = true;


        protected ConcurrentQueue<SocketAsyncEventArgs> m_SendArgsPool = new ConcurrentQueue<SocketAsyncEventArgs>();
        protected ThreadSafeQueue<ReceivedMessage> m_ReceivedMessages = new ThreadSafeQueue<ReceivedMessage>();
        protected ILogReceiver m_Logger;

        private List<ReceivedMessage> m_ReceiveMesageCache = new List<ReceivedMessage>(10);

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
            if (!m_SendArgsPool.TryDequeue(out sendArg))
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
                m_SendArgsPool.Enqueue(sendArg);
            }
        }

        protected virtual void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            //it doesn't matter we success or not
            ((Message)e.UserToken).Release();
            e.UserToken = null;
            m_SendArgsPool.Enqueue(e);
            if(e.SocketError != SocketError.Success)
            {
                try {
                    var socket = sender as Socket;
                    if (socket.Connected) socket.Disconnect(true);
                }
                catch { }
            }
        }

        protected void StartReceive(UserToken token)
        {
            token.ReadyToReceiveLength();
            try
            {
#if UNITY_RECEIVE
                bool isPending = token.Socket.ReceiveAsyncWithMono(token.ReceiveArg);
#else
                bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
#endif
                if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
            }
            catch
            {
                CloseSocket(token);
            }
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var token = e.UserToken as UserToken;

#if UNITY_RECEIVE
            var transferCount = token.LastTransferCount;
#else
            var transferCount = e.BytesTransferred;
#endif
            if (transferCount > 0 && e.SocketError == SocketError.Success)
            {
                if (token.CurrentMessage != null)
                {
                    //true means we have received all bytes for message
                    if (token.AppendReceivedBuffer(transferCount)) 
                    {
                        token.Connection.OnDataReceived(token.CurrentMessage);
                        token.CurrentMessage.Release();

                        //clear message
                        token.ReadyToReceiveLength();
                        token.CurrentMessage = null;
                    }
                }
                else
                {
                    //true means we have received all bytes for length
                    if (token.HandleLengthReceive(transferCount))
                    {
                        //now prepare a message to receive actual data
                        token.CurrentMessage = Message.Pop();
                        token.ReadyToReceiveMessage();
                    }
                }

                try
                {
#if UNITY_RECEIVE
                    bool isPending = token.Socket.ReceiveAsyncWithMono(token.ReceiveArg);
#else
                    bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
#endif
                    if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
                }
                catch
                {
                    CloseSocket(token);
                }
            }
            else
            {
                CloseSocket(token);
            }
        }


        /// <summary>
        /// this should be called in only one thread, much faster
        /// </summary>
        public virtual void Update()
        {
            m_ReceivedMessages.DequeueAll(m_ReceiveMesageCache);
            for(int i = 0; i < m_ReceiveMesageCache.Count; i++)
            {
                InvokeMessageCallbacks(m_ReceiveMesageCache[i]);
            }
            m_ReceiveMesageCache.Clear();
        }

        /// <summary>
        /// this can be called in multiple thread
        /// </summary>
        public virtual void UpdateThreadSafe()
        {
            ReceivedMessage receivedMessage;
            while (m_ReceivedMessages.TryDequeue(out receivedMessage))
            {
                InvokeMessageCallbacks(receivedMessage);
            }
        }

        protected void InvokeMessageCallbacks(ReceivedMessage receivedMessage)
        {
            bool recycle = true;
            try
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
                        {
                            OnDataReceived?.Invoke(receivedMessage.ConnectionID, receivedMessage.MessageData, ref recycle);
                        }
                        break;
                }
            }
            catch(Exception e) { m_Logger?.Exception(e); }
            finally { 
                if(recycle && receivedMessage.MessageData != null) 
                    receivedMessage.MessageData.Release(); 
            }
        }

        protected virtual void CloseSocket(UserToken token)
        {
            //were we receiving message? if ture, clear message
            if (token.CurrentMessage != null)
            {
                token.CurrentMessage.Release();
                token.CurrentMessage = null;
            }

            // close the socket associated with the client
            try
            {
                token.Socket.Disconnect(true);
            }
            // throws if client process has already closed
            catch { }

            token.IsConnected = false;
            try
            {
                token.Connection.OnDisconnected();
            }
            catch(Exception e)
            {
                m_Logger?.Exception(e);
            }
        }

        protected class DefaultConnection : IConnection
        {
            ThreadSafeQueue<ReceivedMessage> m_MessageQueue;
            public UserToken UserToken { get; set; }

            public DefaultConnection(ThreadSafeQueue<ReceivedMessage> messageQueue)
            {
                m_MessageQueue = messageQueue;
            }

            public void OnConnected()
            {
                m_MessageQueue.Enqueue(new ReceivedMessage(UserToken.ConnectionID, MessageType.Connected));
            }

            public void OnDataReceived(Message msg)
            {
                m_MessageQueue.Enqueue(new ReceivedMessage(UserToken.ConnectionID, MessageType.Data, msg));
                msg.Retain(); //to prevent release
            }

            public void OnDisconnected()
            {
                m_MessageQueue.Enqueue(new ReceivedMessage(UserToken.ConnectionID, MessageType.Disconnected));
            }
        }

        public abstract bool Send(int connectionID, Message msg);
        public abstract void Disconnect(int connectionID);
    }
}


