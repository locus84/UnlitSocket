using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace UnlitSocket
{
    public abstract class Peer
    {
        public bool NoDelay { get; set; } = true;
        public bool KeepAlive { get; set; } = true;

        protected ConcurrentQueue<SocketArgs> m_SendArgsPool = new ConcurrentQueue<SocketArgs>();
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

        protected Connection CreateConnection(int connectionId)
        {
            var newConn = new Connection(connectionId, this);
            newConn.SocketArg.Completed += ProcessReceive;
            newConn.DisconnectArg.Completed += ProcessDisconnect;
            return newConn;
        }

        #region SendHandler
        /// <summary>
        /// message must be retained before calling this function
        /// </summary>
        protected virtual void Send(Connection conn, Message message)
        {
            var socket = conn.Socket;
            SocketArgs args;
            if (!m_SendArgsPool.TryDequeue(out args))
            {
                args = new SocketArgs();
                args.BufferList = new List<ArraySegment<byte>>();
                args.Completed += ProcessSend;
            }

            args.Message = message;
            args.Connection = conn;
            message.BindToArgsSend(args);

            conn.DisconnectEvent.AddCount();

            try
            {
                bool isPending = socket.SendAsync(args);
                if (!isPending) ProcessSend(socket, args);
            }
            catch(Exception e)
            {
                m_Logger?.Exception(e);
                args.ClearMessage();
                args.Connection.Disconnect();
                args.Connection.DisconnectEvent.Signal();
                args.Connection = null;
                m_SendArgsPool.Enqueue(args);
            }
        }

        protected virtual void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            var sendArgs = (SocketArgs)e;
            //it doesn't matter we success or not
            
            if(e.SocketError != SocketError.Success)
            {
                m_Logger?.Warning("Socket Error : " + e.SocketError);
                sendArgs.Connection.Disconnect();
            }

            sendArgs.ClearMessage();
            sendArgs.Connection.DisconnectEvent.Signal();
            sendArgs.Connection = null;
            m_SendArgsPool.Enqueue(sendArgs);
        }
        #endregion

        #region ReceiveHandler
        protected void StartReceive(Connection connection)
        {
            try
            {
                bool isPending = connection.Socket.ReceiveAsync(connection.SocketArg);
                if (!isPending) ProcessReceive(connection.Socket, connection.SocketArg);
            }
            catch
            {
                StopReceive(connection, true);
            }
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var receiveArgs = e as SocketArgs;
            var connection = receiveArgs.Connection;

            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                if (connection.TryReleaseMessage(out var message)) 
                    m_MessageHandler.OnDataReceived(connection.ConnectionID, message);
                StartReceive(connection);
            }
            else
            {
                StopReceive(connection, true);
            }
        }

        protected virtual void StopReceive(Connection connection, bool withCallback)
        {
            //clear receiving messages
            connection.ClearReceiving();
            connection.Disconnect();
            //connected false can be called anywhere, but disconnect event should be called once

            if(withCallback)
            {
                m_MessageHandler.OnDisconnected(connection.ConnectionID);
                connection.DisconnectEvent.Signal();
            }
        }
        #endregion

        #region MessageHandler
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

        public bool GetNextMessage(out ReceivedMessage message)
        {
            return m_ReceivedMessages.TryDequeue(out message);
        }

        public void GetNextMessages(List<ReceivedMessage> messageCache)
        {
            //don't clear message cache, as client should handle on their own
            //messageCache.Clear();
            m_ReceivedMessages.DequeueAll(messageCache);
        }
        #endregion

        #region DisconnectHandler
        internal virtual void ProcessDisconnect(object sender, SocketAsyncEventArgs e)
        {
            var args = e as SocketArgs;
            args.Connection.DisconnectEvent.Signal();
        }
        #endregion
    }
}


