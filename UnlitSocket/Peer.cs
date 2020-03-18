using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;

namespace UnlitSocket
{
    public abstract class Peer
    {
        public bool NoDelay = true;
        public int SendBufferSize = 512;
        public int ReceiveBufferSize = 512;
        public KeepAliveOption KeepAliveStatus = new KeepAliveOption(true, 30000, 5000);

        internal ConcurrentQueue<SocketArgs> m_SendArgsPool = new ConcurrentQueue<SocketArgs>();
        internal ThreadSafeQueue<Event> m_ReceivedEvents = new ThreadSafeQueue<Event>();

        protected ILogReceiver m_Logger;
        protected IEventHandler m_EventHandler;

        public void SetLogger(ILogReceiver logger) => m_Logger = logger;
        public void SetHandler(IEventHandler handler)
        {
            if (handler == null) throw new ArgumentNullException();
            m_EventHandler = handler;
        }

        public Peer()
        {
            m_EventHandler = new DefaultEventHandler(m_ReceivedEvents);
        }

        protected Connection CreateConnection(int connectionId)
        {
            var newConn = new Connection(connectionId);
            newConn.BuildSocket(NoDelay, KeepAliveStatus, SendBufferSize, ReceiveBufferSize);
            newConn.ReceiveArg.Completed += ProcessReceive;
            newConn.DisconnectArg.Completed += ProcessDisconnect;
            return newConn;
        }

        #region SendHandler
        public abstract bool Send(int connectionId, Message message);

        public abstract bool Send(IList<int> connectionIds, Message message);

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

            conn.Lock.Retain();

            try
            {
                bool isPending = socket.SendAsync(args);
                if (!isPending) ProcessSend(socket, args);
            }
            catch(Exception e)
            {
                m_Logger?.Exception(e);
                args.ClearMessage();
                Disconnect(args.Connection);
                args.Connection.Lock.Release();
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
                m_Logger?.Warning("Send Error : " + e.SocketError);
                Disconnect(sendArgs.Connection);
            }

            sendArgs.ClearMessage();
            sendArgs.Connection.Lock.Release();
            sendArgs.Connection = null;
            m_SendArgsPool.Enqueue(sendArgs);
        }
        #endregion

        #region ReceiveHandler
        protected void StartReceive(Connection connection)
        {
            try
            {
                bool isPending = connection.Socket.ReceiveAsync(connection.ReceiveArg);
                if (!isPending) ProcessReceive(connection.Socket, connection.ReceiveArg);
            }
            catch(Exception e)
            {
                m_Logger?.Exception(e);
                StopReceive(connection);
            }
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var receiveArgs = e as SocketArgs;
            var connection = receiveArgs.Connection;

            if (e.SocketError == SocketError.Success)
            {
                if(e.BytesTransferred > 0)
                {
                    if (connection.TryReleaseMessage(out var message))
                        m_EventHandler.OnDataReceived(connection.ConnectionID, message);
                    StartReceive(connection);
                }
                else
                {
                    //safe disconnect as byte is zero
                    StopReceive(connection);
                }
            }
            else
            {
                m_Logger?.Warning("Receive Error : " + e.SocketError);
                StopReceive(connection);
            }
        }

        protected virtual void StopReceive(Connection conn)
        {
            //clear receiving messages
            conn.ClearReceiving();
            Disconnect(conn);
            //connected false can be called anywhere, but disconnect event should be called once
            m_EventHandler.OnDisconnected(conn.ConnectionID);
            conn.Lock.Release();
        }
        #endregion

        #region EventHandler
        //default message handler, add received messages to message queue by created new ReceivedMessage
        private class DefaultEventHandler : IEventHandler
        {
            ThreadSafeQueue<Event> m_ReceivedEvents;
            public DefaultEventHandler(ThreadSafeQueue<Event> eventQueue) => m_ReceivedEvents = eventQueue;
            public void OnConnected(int connectionId) =>
                m_ReceivedEvents.Enqueue(new Event(connectionId, EventType.Connected));
            public void OnDataReceived(int connectionId, Message msg) =>
                m_ReceivedEvents.Enqueue(new Event(connectionId, EventType.Data, msg));
            public void OnDisconnected(int connectionId) =>
                m_ReceivedEvents.Enqueue(new Event(connectionId, EventType.Disconnected));
        }

        public bool TryGetNextEvent(out Event message)
        {
            return m_ReceivedEvents.TryDequeue(out message);
        }

        public void GetNextEvents(List<Event> messageCache)
        {
            //don't clear message cache, as client should handle on their own
            //messageCache.Clear();
            m_ReceivedEvents.DequeueAll(messageCache);
        }
        #endregion

        #region DisconnectHandler
        protected virtual bool Disconnect(Connection conn)
        {
            if (!conn.TrySetDisconnected()) return false;
            //on client, we always rebuild socket
            conn.Socket.Dispose();
            conn.BuildSocket(NoDelay, KeepAliveStatus, SendBufferSize, ReceiveBufferSize);
            //disconnect should also signal
            conn.Lock.Release();
            return true;
        }

        internal virtual void ProcessDisconnect(object sender, SocketAsyncEventArgs e)
        {
            //do nothing here
        }
        #endregion
    }
}


