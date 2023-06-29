using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Channels;

namespace Serilog.Sinks.Async
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class ChannelSink : ILogEventSink, IAsyncLogEventSinkInspector, IDisposable
    {
        readonly ILogEventSink _wrappedSink;
        readonly bool _blockWhenFull;
        readonly Channel<LogEvent> _queue;
        readonly Task _worker;
        readonly IAsyncLogEventSinkMonitor _monitor;
        readonly int _bufferCapacity;

        long _droppedMessages;

        public ChannelSink(ILogEventSink wrappedSink, int bufferCapacity, bool blockWhenFull, IAsyncLogEventSinkMonitor monitor = null)
        {
            if (bufferCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(bufferCapacity));
            _wrappedSink = wrappedSink ?? throw new ArgumentNullException(nameof(wrappedSink));
            _blockWhenFull = blockWhenFull;
            _bufferCapacity = bufferCapacity;
            _queue = Channel.CreateBounded<LogEvent>(bufferCapacity);
            _worker = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            _monitor = monitor;
            monitor?.StartMonitoring(this);
        }

        public void Emit(LogEvent logEvent)
        {
            if (_queue.Reader.Completion.IsCompleted)
                return;

            try
            {
                if (_blockWhenFull)
                {
                    if (!_queue.Writer.TryWrite(logEvent))
                    {
                        _queue.Writer.WriteAsync(logEvent).AsTask().GetAwaiter().GetResult();
                    }
                }
                else
                {
                    if (!_queue.Writer.TryWrite(logEvent))
                    {
                        Interlocked.Increment(ref _droppedMessages);
                        SelfLog.WriteLine("{0} unable to enqueue, capacity {1}", typeof(BackgroundWorkerSink), _bufferCapacity);
                    }
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
            _queue.Writer.TryComplete();

            // Allow queued events to be flushed
            _worker.Wait();

            (_wrappedSink as IDisposable)?.Dispose();

            _monitor?.StopMonitoring(this);
        }

        async Task Pump()
        {
            try
            {
                var reader = _queue.Reader;
                var completion = reader.Completion;

                while (!completion.IsCompleted) 
                {
                    while (reader.TryRead(out var next))
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

                    await reader.WaitToReadAsync();
                }
            }
            catch (Exception fatal)
            {
                SelfLog.WriteLine("{0} fatal error in worker thread: {1}", typeof(BackgroundWorkerSink), fatal);
            }
        }

        int IAsyncLogEventSinkInspector.BufferSize => _bufferCapacity;

        int IAsyncLogEventSinkInspector.Count => _queue.Reader.Count;

        long IAsyncLogEventSinkInspector.DroppedMessagesCount => _droppedMessages;
    }
}
