using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace UnlitSocket
{
    public class Peer
    {
        static bool s_IsMono { get; } = Type.GetType("Mono.Runtime") != null;

        public ConnectionStatusChangeDelegate OnConnected;
        public ConnectionStatusChangeDelegate OnDisconnected;
        public DataReceivedDelegate OnDataReceived;

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
            try
            {
                Select(new List<Socket>() { token.Socket }, 1000);
                token.Socket.BeginReceive(token.ReceiveArg.BufferList, SocketFlags.None, new AsyncCallback(cb), token);
                //bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
                //if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
            }
            catch
            {
                CloseSocket(token);
            }
        }

        private void cb(IAsyncResult ar)
        {
            if (ar.IsCompleted)
            {
                ((UserToken)ar.AsyncState).Socket.EndReceive(ar);

            }
        }


        public static void Select(List<Socket> sockets, int microSeconds)
        {
            int error;
            sockets.Add(null);
            sockets.Add(null);
            sockets.Add(null);
            sockets.Add(null);

            var socket = sockets.ToArray();
            try
            {
                SocketSelector.Select(ref socket, microSeconds, out error);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }


            UnityEngine.Debug.Log($"{socket[0]} {socket[1]} {socket[2]} {socket[3]} {socket[4]}");

        }


        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var token = e.UserToken as UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                if (token.CurrentMessage != null)
                {
                    //true means we have received all bytes for message
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
                    bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
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


