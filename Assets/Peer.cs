using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket
{
    public class Peer
    {
        public ConnectionStatusChangeDelegate OnConnected;
        public ConnectionStatusChangeDelegate OnDisconnected;
        public DataReceivedDelegate OnDataReceived;

        protected ConcurrentQueue<SocketAsyncEventArgs> m_SendArgsPool = new ConcurrentQueue<SocketAsyncEventArgs>();
        protected ThreadSafeQueue<ReceivedMessage> m_ReceivedMessages = new ThreadSafeQueue<ReceivedMessage>();
        protected ILogReceiver m_Logger;

        private List<ReceivedMessage> m_ReceiveMesageCache = new List<ReceivedMessage>(10);
        private static List<UserToken> s_ReceiveLoopSockets = new List<UserToken>();
        protected static ConcurrentQueue<UserToken> s_RemovedSockets = new ConcurrentQueue<UserToken>();
        protected static ConcurrentQueue<UserToken> s_AddedSockets = new ConcurrentQueue<UserToken>();

        public bool IsRunning { get; private set; } = false;
        private static volatile int s_RunRequestCount = 0;

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
        }

        protected void StartReceive(UserToken token)
        {
            token.ReadyToReceiveLength();
            s_AddedSockets.Enqueue(token);
        }

        protected void StartReceiveLoop()
        {
            var startCount = Interlocked.Increment(ref s_RunRequestCount);
            IsRunning = true;

            if (startCount == 1)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    while (true)
                    {
                        while (s_AddedSockets.TryDequeue(out var newToken)) s_ReceiveLoopSockets.Add(newToken);
                        while (s_RemovedSockets.TryDequeue(out var removedToken)) s_ReceiveLoopSockets.Remove(removedToken);
                        UserToken currentToken;
                        for (int i = 0; i < s_ReceiveLoopSockets.Count; i++)
                        {
                            currentToken = s_ReceiveLoopSockets[i];
                            try
                            {
                                if (!currentToken.IsReceiving && currentToken.Socket.Available > 0)
                                {
                                    currentToken.IsReceiving = true;
                                    bool isPending = currentToken.Socket.ReceiveAsync(currentToken.ReceiveArg);
                                    if (!isPending) ProcessReceive(currentToken.Socket, currentToken.ReceiveArg);
                                }
                            }
                            catch
                            {
                                currentToken.Owner.CloseSocket(currentToken);
                            }
                        }
                        Thread.Sleep(1);
                    }
                });
            }
        }

        protected void StopReceiveLoop()
        {
            IsRunning = false;
        }

        protected static void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var token = e.UserToken as UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                if (token.CurrentMessage != null)
                {
                    //true means we have received all bytes for message
                    if (token.AppendReceivedBuffer(e.BytesTransferred)) 
                    {
                        token.Owner.m_ReceivedMessages.Enqueue(new ReceivedMessage(token.ConnectionID, MessageType.Data, token.CurrentMessage));

                        //clear message
                        token.ReadyToReceiveLength();
                        token.CurrentMessage = null;
                    }
                }
                else
                {
                    //true means we have received all bytes for length
                    if (token.HandleLengthReceive(e.BytesTransferred))
                    {
                        //now prepare a message to receive actual data
                        token.CurrentMessage = Message.Pop();
                        token.ReadyToReceiveMessage();
                    }
                }

                try
                {
                    if(token.Socket.Available > 0)
                    {
                        bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
                        if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
                    }
                    else
                    {
                        token.IsReceiving = false;
                    }
                }
                catch
                {
                    token.Owner.CloseSocket(token);
                }
            }
            else
            {
                token.Owner.CloseSocket(token);
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
                        OnDataReceived?.Invoke(receivedMessage.ConnectionID, receivedMessage.MessageData);
                        break;
                }
            }
            catch { throw; }
            finally { receivedMessage.MessageData?.Release(); }
        }

        protected virtual void CloseSocket(UserToken token)
        {
            //were we receiving message? if ture, clear message
            if (token.CurrentMessage != null)
            {
                token.CurrentMessage.Release();
                token.CurrentMessage = null;
            }

            s_RemovedSockets.Enqueue(token);
            token.IsReceiving = false;

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


