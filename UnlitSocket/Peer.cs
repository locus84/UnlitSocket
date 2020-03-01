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

        protected ConcurrentQueue<SendEventArgs> m_SendArgsPool = new ConcurrentQueue<SendEventArgs>();
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
        protected virtual void Send(Connection connection, Message message)
        {
            var socket = connection.Socket;
            SendEventArgs sendArgs;
            if (!m_SendArgsPool.TryDequeue(out sendArgs))
            {
                sendArgs = new SendEventArgs();
                sendArgs.BufferList = new List<ArraySegment<byte>>();
                sendArgs.Completed += ProcessSend;
            }

            sendArgs.Message = message;
            sendArgs.Connection = connection;
            message.BindToArgsSend(sendArgs);

            connection.DisconnectEvent.AddCount();

            try
            {
                bool isPending = socket.SendAsync(sendArgs);
                if (!isPending) ProcessSend(socket, sendArgs);
            }
            catch(Exception e)
            {
                m_Logger?.Exception(e);
                sendArgs.Message.Release();
                sendArgs.Message = null;
                sendArgs.Connection.CloseSocket();
                sendArgs.Connection.DisconnectEvent.Signal();
                sendArgs.Connection = null;
                m_SendArgsPool.Enqueue(sendArgs);
            }
        }

        protected virtual void ProcessSend(object sender, SocketAsyncEventArgs e)
        {
            var sendArgs = (SendEventArgs)e;
            //it doesn't matter we success or not
            sendArgs.Message.Release();
            sendArgs.Message = null;
            
            if(e.SocketError != SocketError.Success)
            {
                m_Logger?.Warning("Socket Error : " + e.SocketError);
                sendArgs.Connection.CloseSocket();
            }

            sendArgs.Connection.DisconnectEvent.Signal();
            sendArgs.Connection = null;
            m_SendArgsPool.Enqueue(sendArgs);
        }

        protected void StartReceive(Connection connection)
        {
            try
            {
                connection.ReadyToReceiveLength();
                bool isPending = connection.Socket.ReceiveAsync(connection.ReceiveArg);
                if (!isPending) ProcessReceive(connection.Socket, connection.ReceiveArg);
            }
            catch
            {
                CloseSocket(connection, true);
            }
        }

        protected void ProcessReceive(object sender, SocketAsyncEventArgs e)
        {
            var receiveArgs = e as ReceiveEventArgs;
            var connection = receiveArgs.Connection;

            var transferCount = e.BytesTransferred;
            if (transferCount > 0 && e.SocketError == SocketError.Success)
            {
                if (connection.CurrentMessage != null)
                {
                    //true means we have received all bytes for message
                    if (connection.AppendReceivedBuffer(transferCount)) 
                    {
                        m_MessageHandler.OnDataReceived(connection.ConnectionID, connection.CurrentMessage);
                        //clear message
                        connection.ReadyToReceiveLength();
                        connection.CurrentMessage = null;
                    }
                }
                else
                {
                    //true means we have received all bytes for length
                    if (connection.HandleLengthReceive(transferCount))
                    {
                        //now prepare a message to receive actual data
                        connection.CurrentMessage = Message.Pop();
                        connection.ReadyToReceiveMessage();
                    }
                }

                try
                {
                    bool isPending = connection.Socket.ReceiveAsync(connection.ReceiveArg);
                    if (!isPending) ProcessReceive(connection.Socket, connection.ReceiveArg);
                }
                catch
                {
                    CloseSocket(connection, true);
                }
            }
            else
            {
                CloseSocket(connection, true);
            }
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

        protected virtual bool CloseSocket(Connection connection, bool withCallback)
        {
            //were we receiving message? if ture, clear message
            if (connection.CurrentMessage != null)
            {
                connection.CurrentMessage.Release();
                connection.CurrentMessage = null;
            }

            var result = connection.CloseSocket();
            //connected false can be called anywhere, but disconnect event should be called once

            if(withCallback)
            {
                try { m_MessageHandler.OnDisconnected(connection.ConnectionID); }
                catch (Exception e) { m_Logger?.Exception(e); }
                connection.DisconnectEvent.Signal();
            }

            return result;
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


