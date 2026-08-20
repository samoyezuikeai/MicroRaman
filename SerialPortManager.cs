using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace MicroRaman
{
    /// <summary>
    /// 提供线程安全的 TANGO 串口生命周期和一问一答通信。
    /// </summary>
    public static class SerialPortManager
    {
        private static readonly object serialSync = new object();

        /// <summary>
        /// 获取串口当前是否已打开。
        /// </summary>
        public static bool IsOpen
        {
            get
            {
                lock (serialSync)
                    return serialPort.IsOpen;
            }
        }

        private static readonly SerialPort serialPort = new SerialPort
        {
            BaudRate = 57600,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.Two,
            Handshake = Handshake.None,

            Encoding = Encoding.ASCII,
            NewLine = "\r",
            ReadTimeout = 30000,
            WriteTimeout = 5000,
        };

        /// <summary>
        /// 打开指定串口；若已有连接则先安全关闭旧连接。
        /// </summary>
        public static void Open(string portName)
        {
            lock (serialSync)
            {
                CloseCore();
                serialPort.PortName = portName;
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
            }
        }

        /// <summary>
        /// 关闭当前串口连接。
        /// </summary>
        public static void Close()
        {
            lock (serialSync)
                CloseCore();
        }

        /// <summary>
        /// 发送一条指令并读取以回车结束的一行响应。
        /// </summary>
        public static string SendAndReceive(string command)
        {
            return SendAndReceive(command, 30000, 1);
        }

        /// <summary>
        /// 只发送指令，不等待自动状态回复。适用于随后通过状态查询等待完成的移动指令。
        /// </summary>
        public static void SendWithoutResponse(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("TANGO 指令不能为空。", nameof(command));

            lock (serialSync)
            {
                if (!serialPort.IsOpen)
                    throw new InvalidOperationException("TANGO 控制器串口尚未连接。");

                serialPort.DiscardInBuffer();
                serialPort.WriteLine(command);
            }
        }

        /// <summary>
        /// 发送一条指令并在指定超时时间内读取响应；仅适合可安全重复发送的查询指令使用重试。
        /// </summary>
        /// <param name="command">发送给 TANGO 控制器的指令。</param>
        /// <param name="readTimeoutMilliseconds">每次读取响应的超时时间（毫秒）。</param>
        /// <param name="attemptCount">总尝试次数；移动等有副作用的指令必须传入 1。</param>
        public static string SendAndReceive(
            string command,
            int readTimeoutMilliseconds,
            int attemptCount)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("TANGO 指令不能为空。", nameof(command));
            if (readTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(readTimeoutMilliseconds));
            if (attemptCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(attemptCount));

            lock (serialSync)
            {
                if (!serialPort.IsOpen)
                    throw new InvalidOperationException("TANGO 控制器串口尚未连接。");

                int originalReadTimeout = serialPort.ReadTimeout;
                try
                {
                    serialPort.ReadTimeout = readTimeoutMilliseconds;
                    for (int attempt = 1; attempt <= attemptCount; attempt++)
                    {
                        try
                        {
                            serialPort.DiscardInBuffer();
                            serialPort.WriteLine(command);
                            return serialPort.ReadLine().Trim();
                        }
                        catch (TimeoutException ex)
                        {
                            if (attempt == attemptCount)
                            {
                                throw new TimeoutException(
                                    string.Format(
                                        "TANGO 指令“{0}”连续 {1} 次没有收到响应（串口 {2}）。请检查控制器电源、串口选择、USB 连接及激光器开启后是否存在通信干扰。",
                                        command,
                                        attemptCount,
                                        serialPort.PortName),
                                    ex);
                            }

                            // 给控制器和 USB 转串口芯片少量恢复时间，再重新发送无副作用的查询。
                            Thread.Sleep(100);
                        }
                    }
                }
                finally
                {
                    serialPort.ReadTimeout = originalReadTimeout;
                }

                throw new InvalidOperationException("TANGO 串口通信进入了不可达状态。");
            }
        }

        /// <summary>
        /// 在已持有串口锁时执行实际关闭操作。
        /// </summary>
        private static void CloseCore()
        {
            if (serialPort.IsOpen)
                serialPort.Close();
        }
    }
}
