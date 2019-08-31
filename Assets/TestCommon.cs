using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace TcpNetworking
{
    public enum ConnectionStatus { Disconnected, Connecting, Connected }

    public interface ILogReceiver
    {
        void Debug(string str);
        void Exception(string exception);
    }

    public class Message
    {
        ConcurrentQueue<Message> m_MessagePool;
        public IDataProvider DataProvider;
        internal SocketAsyncEventArgs Args { get; private set; }
        int RefCount = 0;

        public Message(ConcurrentQueue<Message> pool)
        {
            m_MessagePool = pool;
            Args = new SocketAsyncEventArgs();
            Args.UserToken = this;
        }

        public void Retain()
        {
            Interlocked.Increment(ref RefCount);
        }

        public void Release()
        {
            if(Interlocked.Decrement(ref RefCount) == 0)
            {
                m_MessagePool?.Enqueue(this);
            }
        }
    }

    public interface IDataProvider
    {
        ArraySegment<byte> GetData();
    }

    class BufferManager
    {
        int m_numBytes;
        byte[] m_buffer;
        Stack<int> m_freeIndexPool;
        int m_currentIndex;
        int m_bufferSize;

        public BufferManager(int totalBytes, int bufferSize)
        {
            m_numBytes = totalBytes;
            m_currentIndex = 0;
            m_bufferSize = bufferSize;
            m_freeIndexPool = new Stack<int>();
        }

        public void InitBuffer()
        {
            m_buffer = new byte[m_numBytes];
        }

        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (m_freeIndexPool.Count > 0)
            {
                args.SetBuffer(m_buffer, m_freeIndexPool.Pop(), m_bufferSize);
            }
            else
            {
                if ((m_numBytes - m_bufferSize) < m_currentIndex) return false;
                args.SetBuffer(m_buffer, m_currentIndex, m_bufferSize);
                m_currentIndex += m_bufferSize;
            }
            return true;
        }

        public void FreeBuffer(SocketAsyncEventArgs args)
        {
            m_freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }
    }

    public class AsyncUserToken
    {
        public Socket Socket { get; set; }
        public SocketAsyncEventArgs SendArgs { get; private set; }
        public void SetSendArgs(SocketAsyncEventArgs args) => SendArgs = args;
    }

    class SocketAsyncEventArgsPool
    {
        Stack<SocketAsyncEventArgs> m_pool;

        public SocketAsyncEventArgsPool(int capacity)
        {
            m_pool = new Stack<SocketAsyncEventArgs>(capacity);
        }

        public void Push(SocketAsyncEventArgs item)
        {
            if (item == null) { throw new ArgumentNullException("Items added to a SocketAsyncEventArgsPool cannot be null"); }
            lock (m_pool)
            {
                m_pool.Push(item);
            }
        }

        public SocketAsyncEventArgs Pop()
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

