using System.Collections;
using System.Collections.Generic;


namespace UnlitSocket
{
    public class ThreadSafeQueue<T>
    {
        Queue<T> m_InnerQueue = new Queue<T>();

        public void Enqueue(T item)
        {
            lock (m_InnerQueue)
            {
                m_InnerQueue.Enqueue(item);
            }
        }

        public bool TryDequeue(out T result)
        {
            lock (m_InnerQueue)
            {
                result = default(T);
                if (m_InnerQueue.Count > 0)
                {
                    result = m_InnerQueue.Dequeue();
                    return true;
                }
                return false;
            }
        }

        public void DequeueAll(List<T> list)
        {
            lock (m_InnerQueue)
            {
                list.AddRange(m_InnerQueue);
                m_InnerQueue.Clear();
            }
        }
    }
}
