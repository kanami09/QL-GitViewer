using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     一个作业对象（job object），用来连根收掉我们起的 git 进程。
    ///     <para>
    ///     必须这么做，因为 Git for Windows 的 <c>cmd\git.exe</c> 只是个壳：它会再拉起
    ///     <c>mingw64\bin\git.exe</c> 真身，参数一模一样。<see cref="Process.Kill" />
    ///     杀掉的是壳，真身当场变成孤儿 —— 它还堵在往管道里写日志，于是一直赖着不走。
    ///     </para>
    ///     <para>
    ///     进程一进 job，它之后派生的子进程会自动跟着进来；job 句柄一关，
    ///     <c>KILL_ON_JOB_CLOSE</c> 会把里面还活着的全部杀掉，正常退出的进程则自己离开。
    ///     </para>
    /// </summary>
    internal sealed class GitProcessJob : IDisposable
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;

        private readonly object _lock = new object();
        private IntPtr _handle;

        private GitProcessJob(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>创建一个"句柄一关就杀光"的 job。</summary>
        /// <returns>创建或配置失败时返回 null，调用方退回"只杀我们自己起的那个进程"。</returns>
        public static GitProcessJob TryCreate()
        {
            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
                return null;

            var info = new JobObjectExtendedLimitInformationData
            {
                BasicLimitInformation = new JobObjectBasicLimitInformationData
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            var size = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformationData));
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(info, buffer, false);

                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)size))
                {
                    CloseHandle(handle);
                    return null;
                }
            }
            catch (Exception)
            {
                CloseHandle(handle);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return new GitProcessJob(handle);
        }

        /// <summary>
        ///     把进程放进 job。要尽早调用 —— 壳进程派生真身只需要几毫秒，
        ///     赶在那之前放进来，真身才会跟着一起进来。
        /// </summary>
        public void Assign(Process process)
        {
            lock (_lock)
            {
                if (_handle == IntPtr.Zero)
                    return;

                try
                {
                    AssignProcessToJobObject(_handle, process.Handle);
                }
                catch (Exception)
                {
                    // 进程已经退出了，拿不到句柄。没什么要收的。
                }
            }
        }

        /// <summary>关掉 job 句柄，里面还活着的进程会一并被杀掉。</summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_handle == IntPtr.Zero)
                    return;

                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>JOBOBJECT_BASIC_LIMIT_INFORMATION。字段顺序和名字必须与 Win32 一致。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformationData
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        /// <summary>IO_COUNTERS。这里用不上，但它占着扩展结构中间的位置，不能省。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCountersData
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformationData
        {
            public JobObjectBasicLimitInformationData BasicLimitInformation;
            public IoCountersData IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
