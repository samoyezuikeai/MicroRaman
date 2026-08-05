using System;
using System.Globalization;

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
        /// 以绝对坐标移动 X、Y 轴；Z 轴保持不变。
        /// </summary>
        public void MoveAbsoluteXY(double x, double y)
        {
            string command = string.Format(
                CultureInfo.InvariantCulture,
                "moa {0:R} {1:R}",
                x,
                y);
            string response = SerialPortManager.SendAndReceive(command);
            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidOperationException("平台移动没有返回完成状态。");
            if (response.IndexOf('E') >= 0 || response.IndexOf('S') >= 0 || response.IndexOf('L') >= 0)
                throw new InvalidOperationException("平台移动失败，TANGO 返回：" + response);
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
