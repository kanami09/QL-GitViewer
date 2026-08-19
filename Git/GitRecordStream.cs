using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickLook.Plugin.GitViewer.Git
{
    /// <summary>
    ///     一条还活着的 git 命令的标准输出，按分隔符切成记录，供调用方分批取用。
    ///     <para>
    ///     和 <see cref="GitCommandRunner.Run" /> 的"跑完再读"不同，这里进程一直留着，
    ///     要多少读多少。不读的时候 git 自己会阻塞在写管道上，等于免费的惰性求值：
    ///     既不用把整个 <c>git log</c> 灌进内存，也不用像 <c>--skip</c> 分页那样
    ///     每翻一页就从 HEAD 重走一遍历史。
    ///     </para>
    ///     <para>
    ///     非线程安全：同一时刻只能有一个线程在 <see cref="ReadRecords" /> 里。
    ///     调用方要自己保证读取是串行的。
    ///     </para>
    /// </summary>
    public sealed class GitRecordStream : IDisposable
    {
        /// <summary>单次读取等待的上限。整条命令不设总时限，只要还在出数据就一直读。</summary>
        private const int ReadTimeoutMs = 10000;

        private const int ChunkSize = 8192;

        private readonly char[] _chunk = new char[ChunkSize];
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly Queue<string> _pending = new Queue<string>();

        private readonly Process _process;
        private readonly TextReader _reader;
        private readonly GitCommandRunner _owner;
        private readonly CancellationTokenRegistration _registration;
        private readonly char _separator;

        private bool _disposed;

        /// <summary>没读完的那次异步读取。<see cref="_chunk" /> 还归它用，不能重新发起读取。</summary>
        private Task<int> _read;

        internal GitRecordStream(GitCommandRunner owner, Process process, char separator,
            CancellationTokenRegistration registration)
        {
            _owner = owner;
            _process = process;
            _reader = process.StandardOutput;
            _separator = separator;
            _registration = registration;
        }

        private GitRecordStream()
        {
            IsFinished = true;
            Failed = true;
        }

        /// <summary>命令根本没起来时用的空流：读不出任何记录，也不用调用方到处判 null。</summary>
        internal static GitRecordStream CreateFailed()
        {
            return new GitRecordStream();
        }

        /// <summary>输出已经读到头，或者进程没了。</summary>
        public bool IsFinished { get; private set; }

        /// <summary>读取中途出过错，拿到的内容是不完整的。</summary>
        public bool Failed { get; private set; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            IsFinished = true;

            _registration.Dispose();

            if (_owner != null)
                _owner.Release(this);

            if (_process != null)
            {
                GitCommandRunner.TryKill(_process);

                // 关掉读端。万一有进程躲过了 kill（壳进程派生的真身，见 GitProcessJob），
                // 它下一次往管道里写就会失败，自己退掉。
                try
                {
                    _process.StandardOutput.Close();
                }
                catch (Exception)
                {
                    // 管道已经断了。
                }

                _process.Dispose();
            }

            // 杀掉进程会让还挂着的那次读取以异常收场。没人再去看它的结果，
            // 这里主动观察一下，免得留下未被观察的任务异常。
            if (_read != null)
            {
                _read.ContinueWith(t => { var ignored = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
                _read = null;
            }
        }

        /// <summary>
        ///     最多取 <paramref name="count" /> 条记录。返回条数不足即说明输出已经读完
        ///     （或出错），此时 <see cref="IsFinished" /> 为 true。
        /// </summary>
        public List<string> ReadRecords(int count, CancellationToken ct)
        {
            var records = new List<string>();

            while (records.Count < count)
            {
                if (_pending.Count > 0)
                {
                    records.Add(_pending.Dequeue());
                    continue;
                }

                if (IsFinished || _disposed || ct.IsCancellationRequested)
                    break;

                Fill();
            }

            return records;
        }

        /// <summary>读一块输出，切成记录塞进待取队列。</summary>
        private void Fill()
        {
            if (_read == null)
                _read = _reader.ReadAsync(_chunk, 0, _chunk.Length);

            int count;

            try
            {
                // 等不到就把任务留着下次接着等 —— 缓冲区还在它手里，
                // 这时候再发起一次读取就会两边同时写同一个数组。
                if (!_read.Wait(ReadTimeoutMs))
                {
                    Failed = true;
                    IsFinished = true;
                    return;
                }

                count = _read.Result;
                _read = null;
            }
            catch (Exception)
            {
                // 进程被杀掉时管道会直接断开，走的就是这条路。
                _read = null;
                Failed = true;
                IsFinished = true;
                return;
            }

            if (count <= 0)
            {
                IsFinished = true;

                // 最后一条记录后面没有分隔符时，缓冲里剩下的就是它。
                if (_buffer.Length > 0)
                {
                    _pending.Enqueue(_buffer.ToString());
                    _buffer.Length = 0;
                }

                return;
            }

            for (var i = 0; i < count; i++)
                if (_chunk[i] == _separator)
                {
                    _pending.Enqueue(_buffer.ToString());
                    _buffer.Length = 0;
                }
                else
                {
                    _buffer.Append(_chunk[i]);
                }
        }
    }
}
