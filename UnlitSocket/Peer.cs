using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;

namespace UnlitSocket
{
    public abstract class Peer
    {
        public bool NoDelay { get; set; } = true;
        public bool KeepAlive { get; set; } = true;

        protected ConcurrentQueue<SocketAsyncEventArgs> m_SendArgsPool = new ConcurrentQueue<SocketAsyncEventArgs>();
        protected ThreadSafeQueue<ReceivedMessage> m_ReceivedMessages = new ThreadSafeQueue<ReceivedMessage>();

        protected ILogReceiver m_Logger;
        protected IMessageHandler m_MessageHandler;

        public void SetLogger(ILogReceiver logger) => m_Logger = logger;
        public void SetHandler(IMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException();
            m_MessageHandler = handler;
        }

        public Peer()
        {
            m_MessageHandler = new DefaultMessageHandler(m_ReceivedMessages);
        }

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
            catch(Exception e)
            {
                //send failed, let's release message
                message.Release();
                sendArg.UserToken = null;
                m_SendArgsPool.Enqueue(sendArg);
                m_Logger.Exception(e);
                // close the socket associated with the client
                try
                {
                    if (socket.Connected) socket.Disconnect(true);
                }
                catch { }
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
                var socket = sender as Socket;
                m_Logger.Warning("Socket Error : " + e.SocketError);

                // close the socket associated with the client
                try
                {
                    if (socket.Connected) socket.Disconnect(true);
                }
                // throws if client process has already closed
                catch { }
            }
        }

        protected void StartReceive(UserToken token)
        {
            token.ReadyToReceiveLength();
            try
            {
                bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
                if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
            }
            catch
            {
                CloseSocket(token, true);
            }
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var token = e.UserToken as UserToken;

            var transferCount = e.BytesTransferred;
            if (transferCount > 0 && e.SocketError == SocketError.Success)
            {
                if (token.CurrentMessage != null)
                {
                    //true means we have received all bytes for message
                    if (token.AppendReceivedBuffer(transferCount)) 
                    {
                        m_MessageHandler.OnDataReceived(token.ConnectionID, token.CurrentMessage);
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
                    bool isPending = token.Socket.ReceiveAsync(token.ReceiveArg);
                    if (!isPending) ProcessReceive(token.Socket, token.ReceiveArg);
                }
                catch
                {
                    CloseSocket(token, true);
                }
            }
            else
            {
                CloseSocket(token, true);
            }
        }

        public bool GetNextMessage(out ReceivedMessage message)
        {
            return m_ReceivedMessages.TryDequeue(out message);
        }

        public void GetNextMessages(List<ReceivedMessage> messageCache)
        {
            messageCache.Clear();
            m_ReceivedMessages.DequeueAll(messageCache);
        }

        protected virtual void CloseSocket(UserToken token, bool withCallback)
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

            //connected false can be called anywhere, but disconnect event should be called once
            token.IsConnected = false;

            if(withCallback)
            {
                try
                {
                    m_MessageHandler.OnDisconnected(token.ConnectionID);
                }
                catch (Exception e)
                {
                    m_Logger?.Exception(e);
                }
            }
        }

        //default message handler, add received messages to message queue by created new ReceivedMessage
        private class DefaultMessageHandler : IMessageHandler
        {
            ThreadSafeQueue<ReceivedMessage> m_ReceivedMessages;
            public DefaultMessageHandler(ThreadSafeQueue<ReceivedMessage> messageQueue) => m_ReceivedMessages = messageQueue;
            public void OnConnected(int connectionId) =>
                m_ReceivedMessages.Enqueue(new ReceivedMessage(connectionId, MessageType.Connected));
            public void OnDataReceived(int connectionId, Message msg) =>
                m_ReceivedMessages.Enqueue(new ReceivedMessage(connectionId, MessageType.Data, msg));
            public void OnDisconnected(int connectionId) =>
                m_ReceivedMessages.Enqueue(new ReceivedMessage(connectionId, MessageType.Disconnected));
        }
    }
}


