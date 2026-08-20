using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace MicroRaman
{
    /// <summary>
    /// 封装扫描平台使用到的 TANGO 文本指令及返回值解析。
    /// </summary>
    internal sealed class Command
    {
        private const int QueryTimeoutMilliseconds = 3000;
        private const int QueryAttemptCount = 3;

        /// <summary>
        /// 读取平台当前 X、Y、Z 坐标。
        /// </summary>
        public StagePosition ReadPosition()
        {
            string response = SerialPortManager.SendAndReceive(
                "?pos",
                QueryTimeoutMilliseconds,
                QueryAttemptCount);
            string[] values = SplitResponse(response, 3, "读取平台位置");
            return new StagePosition
            {
                X = ParseNumber(values[0], "X 位置"),
                Y = ParseNumber(values[1], "Y 位置"),
                Z = ParseNumber(values[2], "Z 位置")
            };
        }

        /// <summary>
        /// 读取 X、Y 轴的单位代码，用于换算标定距离和容差。
        /// </summary>
        public int[] ReadDimensions()
        {
            string response = SerialPortManager.SendAndReceive(
                "?dim",
                QueryTimeoutMilliseconds,
                QueryAttemptCount);
            string[] values = SplitResponse(response, 2, "读取平台坐标单位");
            return new[]
            {
                (int)ParseNumber(values[0], "X 单位"),
                (int)ParseNumber(values[1], "Y 单位")
            };
        }

        /// <summary>
        /// 读取 Z 轴的位置单位代码。
        /// </summary>
        public int ReadZDimension()
        {
            string response = SerialPortManager.SendAndReceive(
                "?dim z",
                QueryTimeoutMilliseconds,
                QueryAttemptCount);
            string[] values = SplitResponse(response, 1, "读取 Z 轴坐标单位");
            return (int)ParseNumber(values[0], "Z 单位");
        }


        /// <summary>
        /// 读取 Z 轴当前软件下限和上限；单位与 ?dim z 一致。
        /// </summary>
        public StageAxisLimits ReadZSoftwareLimits()
        {
            string response = SerialPortManager.SendAndReceive(
                "?lim z",
                QueryTimeoutMilliseconds,
                QueryAttemptCount);
            string[] values = SplitResponse(response, 2, "读取 Z 轴软件限位");
            return new StageAxisLimits
            {
                Lower = ParseNumber(values[0], "Z 软件下限"),
                Upper = ParseNumber(values[1], "Z 软件上限")
            };
        }

        /// <summary>
        /// 确认 TANGO 正在执行 Z 轴软件/行程限位控制。
        /// </summary>
        public bool IsZLimitControlEnabled()
        {
            string response = SerialPortManager.SendAndReceive(
                "?limctr z",
                QueryTimeoutMilliseconds,
                QueryAttemptCount);
            string[] values = SplitResponse(response, 1, "读取 Z 轴限位控制状态");
            return (int)ParseNumber(values[0], "Z 限位控制状态") == 1;
        }

        /// <summary>
        /// 以绝对坐标移动 Z 轴。仅向已确认安全的负向硬限位回零时允许 S 响应。
        /// </summary>
        public void MoveAbsoluteZ(double z, bool allowHardwareLimitStop)
        {
            SendZMove(
                string.Format(CultureInfo.InvariantCulture, "moa z {0:R}", z),
                allowHardwareLimitStop);
        }

        /// <summary>
        /// 以绝对坐标移动 X、Y 轴；Z 轴保持不变。
        /// </summary>
        public void MoveAbsoluteXY(double x, double y)
        {
            SendAbsoluteMove(string.Format(
                CultureInfo.InvariantCulture,
                "moa {0:R} {1:R}",
                x,
                y));
        }

        /// <summary>
        /// 以一个 TANGO 矢量指令同时移动到 X、Y、Z 三轴绝对坐标。
        /// </summary>
        public void MoveAbsoluteXYZ(double x, double y, double z)
        {
            SendAbsoluteMove(string.Format(
                CultureInfo.InvariantCulture,
                "moa {0:R} {1:R} {2:R}",
                x,
                y,
                z));
        }

        private static void SendAbsoluteMove(string command)
        {
            string response = SerialPortManager.SendAndReceive(command);
            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidOperationException("平台移动没有返回完成状态。");
            if (response.IndexOf('E') >= 0 || response.IndexOf('S') >= 0 || response.IndexOf('L') >= 0)
                throw new InvalidOperationException("平台移动失败，TANGO 返回：" + response);
        }

        private static void SendZMove(string command, bool allowHardwareLimitStop)
        {
            SerialPortManager.SendWithoutResponse(command);
            WaitForZMoveToStop(allowHardwareLimitStop);
        }

        private static void WaitForZMoveToStop(bool allowHardwareLimitStop)
        {
            const int timeoutMilliseconds = 60000;
            Stopwatch timeout = Stopwatch.StartNew();
            Thread.Sleep(50);

            while (timeout.ElapsedMilliseconds < timeoutMilliseconds)
            {
                string response = SerialPortManager.SendAndReceive(
                    "?statusaxis z",
                    QueryTimeoutMilliseconds,
                    QueryAttemptCount);

                // 若先读到移动指令迟到的多字符自动状态回复，忽略并重新查询。
                if (response.Length != 1)
                {
                    Thread.Sleep(50);
                    continue;
                }

                char state = response[0];
                if (state == 'M')
                {
                    Thread.Sleep(50);
                    continue;
                }
                if (state == '@' || state == 'J' || state == 'A' || state == 'D'
                    || (allowHardwareLimitStop && state == 'S'))
                    return;

                throw new InvalidOperationException("Z 轴移动异常停止，TANGO 状态：" + state);
            }

            throw new TimeoutException("等待 Z 轴停止超时。");
        }

        /// <summary>
        /// 将控制器返回文本拆分成至少指定数量的字段。
        /// </summary>
        private static string[] SplitResponse(string response, int minimumCount, string operation)
        {
            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidOperationException(operation + "失败：串口没有返回数据。");
            string[] values = response.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < minimumCount)
                throw new InvalidOperationException(operation + "失败：返回格式不正确（" + response + "）。");
            return values;
        }

        /// <summary>
        /// 按控制器固定使用的小数点格式解析数值。
        /// </summary>
        private static double ParseNumber(string text, string name)
        {
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException(name + "不是有效数字：" + text);
            return value;
        }
    }
}
