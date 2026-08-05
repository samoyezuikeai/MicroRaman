using System;
using System.Runtime.InteropServices;

namespace MicroRaman
{
    /// <summary>
    /// 本项目关心的 TUCam SDK 返回码。
    /// </summary>
    internal enum TucamResult : uint
    {
        Success = 0x00000001,
        Abort = 0x80000207,
        Timeout = 0x80000208
    }

    /// <summary>
    /// TUCam API 初始化参数。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TucamInit
    {
        public uint CameraCount;
        public IntPtr ConfigPath;
    }

    /// <summary>
    /// TUCam 设备打开参数及返回的相机句柄。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TucamOpen
    {
        public uint Index;
        public IntPtr CameraHandle;
    }

    /// <summary>
    /// TUCam 原始帧缓冲区描述。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TucamFrame
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Signature;
        public ushort HeaderSize;
        public ushort Offset;
        public ushort Width;
        public ushort Height;
        public uint WidthStep;
        public byte Depth;
        public byte Format;
        public byte Channels;
        public byte ElementBytes;
        public byte RequestedFormat;
        public uint Index;
        public uint ImageSize;
        public uint ReservedSize;
        public uint HistogramSize;
        public IntPtr Buffer;
    }

    /// <summary>
    /// TUCam 原生预览绘制器初始化参数。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TucamDrawInit
    {
        public IntPtr WindowHandle;
        public int Mode;
        public sbyte Channels;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// 一帧图像的源区域和目标显示区域。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TucamDraw
    {
        public int SourceX;
        public int SourceY;
        public int SourceWidth;
        public int SourceHeight;
        public int DestinationX;
        public int DestinationY;
        public int DestinationWidth;
        public int DestinationHeight;
        public IntPtr Frame;
    }

    /// <summary>
    /// 集中声明项目使用到的 TUCam SDK 常量和原生函数。
    /// </summary>
    internal static class TUCamNative
    {
        internal const byte UsualFrameFormat = 0x11;
        internal const uint SequenceCaptureMode = 0x00;
        internal const int ResolutionCapability = 0x00;
        internal const int AutoExposureCapability = 0x03;
        internal const int GlobalGainProperty = 0x00;
        internal const int ExposureTimeProperty = 0x01;
        internal const int DefaultDrawMode = 0x00;

        /// <summary>
        /// 初始化 TUCam API 并枚举相机。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Api_Init 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Api_Init(ref TucamInit init, int timeout);

        /// <summary>
        /// 释放 TUCam API 全局资源。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Api_Uninit 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Api_Uninit();

        /// <summary>
        /// 打开指定索引的相机设备。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Dev_Open 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Dev_Open(ref TucamOpen open);

        /// <summary>
        /// 关闭相机设备句柄。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Dev_Close 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Dev_Close(IntPtr cameraHandle);

        /// <summary>
        /// 设置相机能力项，例如分辨率或自动曝光。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Capa_SetValue 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Capa_SetValue(IntPtr cameraHandle, int capabilityId, int value);

        /// <summary>
        /// 读取曝光、增益等相机属性。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Prop_GetValue 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Prop_GetValue(IntPtr cameraHandle, int propertyId, ref double value, int channel);

        /// <summary>
        /// 设置曝光、增益等相机属性。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Prop_SetValue 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Prop_SetValue(IntPtr cameraHandle, int propertyId, double value, int channel);

        /// <summary>
        /// 分配连续采集使用的帧缓冲区。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Buf_Alloc 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Buf_Alloc(IntPtr cameraHandle, ref TucamFrame frame);

        /// <summary>
        /// 释放相机帧缓冲区。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Buf_Release 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Buf_Release(IntPtr cameraHandle);

        /// <summary>
        /// 中止正在等待帧的阻塞调用。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Buf_AbortWait 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Buf_AbortWait(IntPtr cameraHandle);

        /// <summary>
        /// 等待相机返回下一帧。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Buf_WaitForFrame 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Buf_WaitForFrame(IntPtr cameraHandle, ref TucamFrame frame, int timeout);

        /// <summary>
        /// 开始连续采集。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Cap_Start 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Cap_Start(IntPtr cameraHandle, uint mode);

        /// <summary>
        /// 停止连续采集。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Cap_Stop 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Cap_Stop(IntPtr cameraHandle);

        /// <summary>
        /// 初始化 SDK 原生预览绘制器。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Draw_Init 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Draw_Init(IntPtr cameraHandle, TucamDrawInit drawInit);

        /// <summary>
        /// 将一帧绘制到目标窗口。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Draw_Frame 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Draw_Frame(IntPtr cameraHandle, ref TucamDraw draw);

        /// <summary>
        /// 释放 SDK 原生预览绘制器。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Draw_Uninit 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Draw_Uninit(IntPtr cameraHandle);

        /// <summary>
        /// 配置 SDK 帧队列模式；关闭队列可降低实时预览延迟。
        /// </summary>
        [DllImport("TUCam.dll", CallingConvention = CallingConvention.Cdecl)]
        /// <summary>
        /// 处理 TUCAM_Vendor_SetQueueMode 触发的界面事件。
        /// </summary>
        internal static extern TucamResult TUCAM_Vendor_SetQueueMode(
            IntPtr cameraHandle,
            [MarshalAs(UnmanagedType.Bool)] bool queueMode);
    }
}

