using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace UnlitSocket
{
    public enum ConnectionStatus { Disconnected, Connecting, Connected }
    public enum MessageType { Connected, Disconnected, Data }

    public interface ILogReceiver
    {
        void Debug(string str);
        void Exception(string exception);
    }

    public partial class Message
    {
        ConcurrentQueue<Message> m_MessagePool = new ConcurrentQueue<Message>();

        internal SocketAsyncEventArgs Args { get; private set; }

        int m_RefCount = 0;

        public int ConnectionID;
        public ArraySegment<byte> Data;
        public MessageType Type;

        public Message()
        {
            Args = new SocketAsyncEventArgs();
            Args.UserToken = this;
        }

        public void Retain()
        {
            Interlocked.Increment(ref m_RefCount);
        }

        public void Release()
        {
            if(Interlocked.Decrement(ref m_RefCount) == 0)
            {
                Recycle();
            }
        }

        public void Recycle()
        {
            m_MessagePool?.Enqueue(this);
        }
    }

    public class AsyncUserToken
    {
        public int ConnectionID { get; private set; }
        public Socket Socket { get; set; }

        byte[] m_SizeReadArray = new byte[2];
        public SocketAsyncEventArgs ReceiveArg { get; private set; }
        public Message CurrentMessage = null;

        public AsyncUserToken(int id)
        {
            ConnectionID = id;
            ReceiveArg = new SocketAsyncEventArgs();
            ReceiveArg.UserToken = this;
            ReceiveArg.SetBuffer(m_SizeReadArray, 0, 2);
        }

        public void Clear()
        {
            Socket = null;
            ReceiveArg.SetBuffer(m_SizeReadArray, 0, 2);
        }
    }

    class AsyncUserTokenPool
    {
        Stack<AsyncUserToken> m_pool;

        public AsyncUserTokenPool(int capacity)
        {
            m_pool = new Stack<AsyncUserToken>(capacity);
        }

        public void Push(AsyncUserToken item)
        {
            if (item == null) { throw new ArgumentNullException("Items added to a SocketAsyncEventArgsPool cannot be null"); }
            lock (m_pool)
            {
                m_pool.Push(item);
            }
        }

        public AsyncUserToken Pop()
        {
            lock (m_pool)
            {
                return m_pool.Pop();
            }
        }

        public int Count
        {
            get { return m_pool.Count; }
        }
    }
}

