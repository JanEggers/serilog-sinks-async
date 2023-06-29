using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace Serilog.Sinks.Async
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class ImmutableAsyncSink : ILogEventSink, IAsyncLogEventSinkInspector, IDisposable
    {
        readonly ILogEventSink _wrappedSink;
        readonly bool _blockWhenFull;
        readonly Task _worker;
        readonly IAsyncLogEventSinkMonitor _monitor;
        readonly int _bufferCapacity;

        ImmutableList<LogEvent> _queue;
        long _droppedMessages;
        bool _isDisposed;
        ManualResetEvent _evt = new ManualResetEvent(false);

        public ImmutableAsyncSink(ILogEventSink wrappedSink, int bufferCapacity, bool blockWhenFull, IAsyncLogEventSinkMonitor monitor = null)
        {
            if (bufferCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(bufferCapacity));
            _wrappedSink = wrappedSink ?? throw new ArgumentNullException(nameof(wrappedSink));
            _blockWhenFull = blockWhenFull;
            _bufferCapacity = bufferCapacity;
            _queue = ImmutableList<LogEvent>.Empty;
            _worker = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            _monitor = monitor;
            monitor?.StartMonitoring(this);
        }

        public void Emit(LogEvent logEvent)
        {
            if (_isDisposed)
                return;

            try
            {
                if (_blockWhenFull)
                {
                    SpinWait.SpinUntil(() => _queue.Count < _bufferCapacity);
                }

                var added = ImmutableInterlocked.Update(ref _queue, queue =>
                {
                    if (_queue.Count >= _bufferCapacity)
                    {
                        Interlocked.Increment(ref _droppedMessages);
                        SelfLog.WriteLine("{0} unable to enqueue, capacity {1}", typeof(BackgroundWorkerSink), _bufferCapacity);
                        return queue;
                    }

                    return queue.Add(logEvent);
                });

                if (added) 
                {
                    _evt.Set();
                }
            }
            catch (InvalidOperationException)
            {
                // Thrown in the event of a race condition when we try to add another event after
                // CompleteAdding has been called
            }
        }

        public void Dispose()
        {
            // Prevent any more events from being added
            _isDisposed = true;
            _evt?.Set();

            // Allow queued events to be flushed
            _worker.Wait();

            (_wrappedSink as IDisposable)?.Dispose();
            _evt?.Dispose();

            _monitor?.StopMonitoring(this);
        }

        void Pump()
        {
            try
            {
                while (!_isDisposed)
                {
                    if (_queue.IsEmpty)
                    {
                        _evt.WaitOne();
                    }

                    var events = ImmutableList<LogEvent>.Empty;
                    ImmutableInterlocked.Update(ref _queue, queue => 
                    {
                        events = queue;
                        return ImmutableList<LogEvent>.Empty;
                    });

                    foreach (var next in events)
                    {
                        try
                        {
                            _wrappedSink.Emit(next);
                        }
                        catch (Exception ex)
                        {
                            SelfLog.WriteLine("{0} failed to emit event to wrapped sink: {1}", typeof(BackgroundWorkerSink), ex);
                        }
                    }
                }
            }
            catch (Exception fatal)
            {
                SelfLog.WriteLine("{0} fatal error in worker thread: {1}", typeof(BackgroundWorkerSink), fatal);
            }
        }

        int IAsyncLogEventSinkInspector.BufferSize => _bufferCapacity;

        int IAsyncLogEventSinkInspector.Count => _queue.Count;

        long IAsyncLogEventSinkInspector.DroppedMessagesCount => _droppedMessages;
    }
}
