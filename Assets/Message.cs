using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace UnlitSocket
{
    public class Message
    {
        const int MAX_BYTE_ARRAY_SIZE = 512;
        const int WARM_UP_MESSAGE_COUNT = 512;

        static ConcurrentQueue<byte[]> s_ByteArrayPool = new ConcurrentQueue<byte[]>();
        static ConcurrentQueue<Message> s_MessagePool = new ConcurrentQueue<Message>();

        internal const int MAX_STRING_LENGTH = 1024 * 32;
        internal static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);
        internal static readonly byte[] StringBuffer = new byte[MAX_STRING_LENGTH];

        internal int ConnectionID;
        public int Capacity => MAX_BYTE_ARRAY_SIZE * m_InnerDatas.Count;
        private byte[] m_SendSize = new byte[2];
        private List<ArraySegment<byte>> m_InnerDatas = new List<ArraySegment<byte>>(5); //it can be go up to several, let it be 5
        public int Position = 0;
        private int m_RefCount = 0;

        static Message()
        {
            for(int i = 0; i < WARM_UP_MESSAGE_COUNT; i++)
            {
                s_ByteArrayPool.Enqueue(new byte[MAX_BYTE_ARRAY_SIZE]);
                s_MessagePool.Enqueue(new Message());
            }
        }

        private ref byte ByteAtIndex(int position)
        {
            return ref m_InnerDatas[Math.DivRem(position, MAX_BYTE_ARRAY_SIZE, out var remainder)].Array[remainder];
        }

        public void CheckSize(int count)
        {
            if (Position + count > Capacity)
            {
                throw new EndOfStreamException("ReadByte out of range:" + ToString());
            }
        }

        public void EnsureSize(int amount)
        {
            while (Position + amount >= MAX_BYTE_ARRAY_SIZE * m_InnerDatas.Count)
            {
                byte[] newByteArray;
                if (!s_ByteArrayPool.TryDequeue(out newByteArray)) newByteArray = new byte[MAX_BYTE_ARRAY_SIZE];
                m_InnerDatas.Add(new ArraySegment<byte>(newByteArray));
            }
        }

        public byte ReadByte()
        {
            CheckSize(1);
            return ByteAtIndex(Position++);
        }

        public byte ReadByteNoCheck()
        {
            return ByteAtIndex(Position++);
        }

        public void ReadBytes(byte[] bytes, int offset, int count)
        {
            // check if passed byte array is big enough
            if (count > bytes.Length - offset)
            {
                throw new EndOfStreamException("ReadBytes can't read " + count + " + bytes because the passed byte[] only has length " + bytes.Length);
            }

            CheckSize(count);
            var bytesLeft = count;

            while (bytesLeft > 0)
            {
                //get innerdata Index and remainder
                var quotient = Math.DivRem(Position, MAX_BYTE_ARRAY_SIZE, out var remainder);
                //count we can read in this data arry or bytes left.
                var readCount = Math.Min(MAX_BYTE_ARRAY_SIZE - remainder, bytesLeft);
                //copy buffer to provided bytes.
                Buffer.BlockCopy(m_InnerDatas[quotient].Array, remainder, bytes, offset + count - bytesLeft, readCount);
                //append howmuch we wrote
                bytesLeft -= readCount;
                Position += readCount;
            }
        }

        public void WriteByte(byte value)
        {
            EnsureSize(1);
            ByteAtIndex(Position++) = value;
        }

        public void WriteByteNoCheck(byte value)
        {
            ByteAtIndex(Position++) = value;
        }

        public void WriteBytes(byte[] bytes, int offset, int count)
        {
            // check if passed byte array is big enough
            if (count > bytes.Length - offset)
            {
                throw new EndOfStreamException("WriteBytes can't write " + count + " + bytes because the passed byte[] only has length " + bytes.Length);
            }

            EnsureSize(count);
            var bytesLeft = count;

            while (bytesLeft > 0)
            {
                //get innerdata Index and remainder
                var quotient = Math.DivRem(Position, MAX_BYTE_ARRAY_SIZE, out var remainder);
                //count we can write in this data arry or bytes left.
                var writeCount = Math.Min(MAX_BYTE_ARRAY_SIZE - remainder, bytesLeft);
                //copy provided bytes to buffer.
                Buffer.BlockCopy(bytes, offset + count - bytesLeft, m_InnerDatas[quotient].Array, remainder, writeCount);
                //append howmuch we wrote
                bytesLeft -= writeCount;
                Position += writeCount;
            }
        }

        public void Clear()
        {
            //keep only one, discard rest
            for(int i = m_InnerDatas.Count - 1; i >= 0; i--)
            {
                if(i == 0)
                {
                    //need to reset array segment as full size
                    m_InnerDatas[0] = new ArraySegment<byte>(m_InnerDatas[0].Array);
                }
                else
                {
                    var poolToReturn = m_InnerDatas[i];
                    s_ByteArrayPool.Enqueue(poolToReturn.Array);
                    m_InnerDatas.RemoveAt(i);
                }
            }

            Position = 0;
        }

        public void Retain()
        {
            Interlocked.Increment(ref m_RefCount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref m_RefCount) == 0)
            {
                Push(this);
            }
        }

        public static void Push(Message msg)
        {
            msg.Clear();
            s_MessagePool.Enqueue(msg);
        }

        public static Message Pop()
        {
            return s_MessagePool.TryDequeue(out var msg) ? msg : new Message();
        }

        public static Message Pop(int ensureSize)
        {
            var msg = Pop();
            msg.EnsureSize(ensureSize);
            return msg;
        }

        public void BindToArgsReceive(System.Net.Sockets.SocketAsyncEventArgs args, int count)
        {
            var quotient = Math.DivRem(count, MAX_BYTE_ARRAY_SIZE, out var remainder);
            if (remainder > 0) m_InnerDatas[quotient] = new ArraySegment<byte>(m_InnerDatas[quotient].Array, 0, remainder);
            args.BufferList = m_InnerDatas;
        }

        public void BindToArgsSend(System.Net.Sockets.SocketAsyncEventArgs args, int count)
        {
            var quotient = Math.DivRem(count, MAX_BYTE_ARRAY_SIZE, out var remainder);
            if (remainder > 0) m_InnerDatas[quotient] = new ArraySegment<byte>(m_InnerDatas[quotient].Array, 0, remainder);
            
            //set size only on here
            MessageWriter.WriteUInt16(m_SendSize, (ushort)count);
            m_InnerDatas.Insert(0, new ArraySegment<byte>(m_SendSize));
            args.BufferList = m_InnerDatas;
            m_InnerDatas.RemoveAt(0);
        }
    }
}
