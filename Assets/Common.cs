using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace UnlitSocket
{
    public delegate void ConnectionStatusChangeDelegate(int connectionID);
    public delegate void DataReceivedDelegate(int connectionID, Message message);

    public enum ConnectionStatus { Disconnected, Connecting, Connected }
    public enum MessageType { Connected, Disconnected, Data }

    public interface ILogReceiver
    {
        void Debug(string str);
        void Exception(string exception);
    }

    /// <summary>
    /// what is this? mono SAEA implementation actually eats a thread, so if there's more than usual connection,
    /// all threadpool thread are eaten and does nothing for newly added job.
    /// </summary>
    public static class SocketSelector
    {
        public delegate void SelectDelegate(ref System.Net.Sockets.Socket[] sockets, int microSeconds, out int error);
        public static readonly SelectDelegate Select;

        static SocketSelector()
        {
            var mthod = typeof(System.Net.Sockets.Socket).GetMethod("Select_internal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Select = (SelectDelegate)mthod.CreateDelegate(typeof(SelectDelegate));
        }

    }

    public class SocketSelectReadList
    {
        Socket[] m_InnerArray = new Socket[256];
        Socket[] m_CopyArray = new Socket[256];
        public Queue<Socket> SignaledSockets = new Queue<Socket>();
        Dictionary<Socket, int> m_SocketToIndex = new Dictionary<Socket, int>();

        int totalSize = 0;

        private void EnsureSize(int size)
        {
            while (m_InnerArray.Length < size + 3) //3 is nullcount
            {
                var prevArray = m_InnerArray;
                m_InnerArray = new Socket[prevArray.Length * 2];
                m_CopyArray = new Socket[prevArray.Length * 2];
                Array.Copy(prevArray, m_InnerArray, prevArray.Length);
            }
        }

        public void AddSocket(Socket socket)
        {
            EnsureSize(totalSize + 1);
            m_InnerArray[totalSize] = socket;
            m_SocketToIndex.Add(socket, totalSize++);
        }

        //int m_ReadCount = 0;
        //int m_WriteCount = 0;
        //int m_ErrorCount = 0;
        //int m_TotalCount = 0;

        //private void EnsureSize(int size)
        //{
        //    while (m_InnerArray.Length < size + 3) //3 is nullcount
        //    {
        //        var prevArray = m_InnerArray;
        //        m_InnerArray = new Socket[m_InnerArray.Length * 2];
        //        Array.Copy(prevArray, m_InnerArray, prevArray.Length);
        //    }
        //}

        ////inserts socket at index, so next socket should be appended
        //public void AddSocketToRead(Socket socket)
        //{
        //    EnsureSize(m_TotalCount + 1);

        //    //last errorIndex 
        //    var initialErrorIndex = m_ReadCount + m_WriteCount + 2;
        //    m_InnerArray[initialErrorIndex + m_ErrorCount] = m_InnerArray[initialErrorIndex];
        //    m_InnerArray[initialErrorIndex] = null;

        //    //last errorIndex 
        //    var initialWriteIndex = m_ReadCount + 1;
        //    m_InnerArray[initialWriteIndex + m_WriteCount] = m_InnerArray[initialWriteIndex];
        //    m_InnerArray[initialWriteIndex] = null;

        //    m_InnerArray[m_ReadCount] = socket;

        //    m_ReadCount++;
        //    m_TotalCount++;
        //}

        //private void AppendWrite(Socket socket)
        //{
        //    EnsureSize(m_TotalCount + 1);

        //    var initialErrorIndex = m_ReadCount + m_WriteCount + 2;
        //    m_InnerArray[initialErrorIndex + m_ErrorCount] = m_InnerArray[initialErrorIndex];
        //    m_InnerArray[initialErrorIndex] = null;

        //    var initialWriteIndex = m_ReadCount + 1;
        //    m_InnerArray[initialWriteIndex + m_WriteCount] = socket;

        //    m_WriteCount++;
        //    m_TotalCount++;
        //}

        //private void AppendError(Socket socket)
        //{
        //    EnsureSize(m_TotalCount + 1);

        //    var initialErrorIndex = m_ReadCount + m_WriteCount + 2;
        //    m_InnerArray[initialErrorIndex + m_ErrorCount] = socket;

        //    m_ErrorCount++;
        //    m_TotalCount++;
        //}

        //private void ShrinkAtIndex(int index)
        //{

        //}

        public void Select(int microSeconds)
        {
            int error;
            var sockets = m_CopyArray;
            Buffer.BlockCopy(m_InnerArray, 0, sockets, 0, m_InnerArray.Length);
            SocketSelector.Select.Invoke(ref sockets, microSeconds, out error);

            //exception occured
            if (error != 0) throw new SocketException(error);
            if (sockets == null) return; //nulled, nothing

            for(int i = 0; i < sockets.Length; i++)
            {
                var current = sockets[i];
                if (current == null) break;
                var index = m_SocketToIndex[current];
                m_SocketToIndex.Remove(current);
                SignaledSockets.Enqueue(current);
                if(totalSize > index + 1)
                {
                    m_InnerArray[index] = m_InnerArray[totalSize - 1];
                    m_SocketToIndex[m_InnerArray[index]] = totalSize - 1;
                }
                else
                {
                    m_InnerArray[index] = null;
                }

                totalSize--;
            }
        }
    }



    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntFloat
    {
        [FieldOffset(0)]
        public float floatValue;

        [FieldOffset(0)]
        public uint intValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntDouble
    {
        [FieldOffset(0)]
        public double doubleValue;

        [FieldOffset(0)]
        public ulong longValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntDecimal
    {
        [FieldOffset(0)]
        public ulong longValue1;

        [FieldOffset(8)]
        public ulong longValue2;

        [FieldOffset(0)]
        public decimal decimalValue;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct UIntGuid
    {
        [FieldOffset(0)]
        public ulong longValue1;

        [FieldOffset(8)]
        public ulong longValue2;

        [FieldOffset(0)]
        public Guid guidValue;
    }
}

