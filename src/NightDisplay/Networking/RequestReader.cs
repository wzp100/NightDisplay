using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NightDisplay
{
    /// <summary>One buffer per connection. Read-ahead bytes also feed the request body.</summary>
    public sealed class RequestReader
    {
        readonly NetworkStream stream;
        readonly byte[] buffer = new byte[4096];
        int position, count;
        public RequestReader(NetworkStream stream) { this.stream = stream; }
        async Task Fill()
        {
            count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            position = 0;
            if (count == 0) throw new IOException("连接已关闭。");
        }
        public async Task<string> ReadLine(int maximum)
        {
            using (var line = new MemoryStream())
            {
                while (true)
                {
                    if (position == count) await Fill().ConfigureAwait(false);
                    byte value = buffer[position++];
                    if (value == 10) return Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r');
                    if (line.Length >= maximum) throw new IOException("请求行过长。");
                    line.WriteByte(value);
                }
            }
        }
        public async Task ReadBody(int length)
        {
            while (length > 0)
            {
                if (position == count) await Fill().ConfigureAwait(false);
                int consumed = Math.Min(length, count - position);
                position += consumed;
                length -= consumed;
            }
        }
    }
}
