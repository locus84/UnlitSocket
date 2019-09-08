using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnlitSocket;

namespace Tests
{
    public class NewTestScript
    {
        // A Test behaves as an ordinary method
        [Test]
        public void TestStream()
        {
            var message = Message.Pop();
            message.WriteByte(131);
            Debug.Log($"Pos {message.Position} Capa {message.Capacity}");

            for (int i = 0; i < 512; i++)
                message.WriteByte(131);
            Debug.Log($"Pos {message.Position} Capa {message.Capacity}");
            var readCount = 0;

            try
            {
                for (int i = 0; i < 513; i++)
                {
                    message.ReadByte();
                    readCount++;
                }
            }
            catch
            {
                Debug.Log(readCount);
            }
        }

        // A Test behaves as an ordinary method
        [Test]
        public void TestStreamString()
        {
            var message = Message.Pop();
            //message.WriteUInt16(124);
            message.WriteString("하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요" +
                "하하 이것은 좀 길다란 바이트입니다. 이거 사이즈가 512는 넘어야 해요");

            Debug.Log(message.Position);

            message.Position = 0;
            Debug.Log(message.ReadString());
        }

        // A Test behaves as an ordinary method
        [Test]
        public void NewTestScriptSimplePasses()
        {
            // Use the Assert class to test conditions
        }

        // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
        // `yield return null;` to skip a frame.
        [UnityTest]
        public IEnumerator NewTestScriptWithEnumeratorPasses()
        {
            // Use the Assert class to test conditions.
            // Use yield to skip a frame.
            yield return null;
        }
    }
}
