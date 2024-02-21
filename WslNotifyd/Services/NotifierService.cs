using Google.Protobuf;
using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifyd.DBus;

namespace WslNotifyd.Services
{
    internal class NotifierService : Notifier.NotifierBase
    {
        private readonly ILogger<NotifierService> _logger;
        private readonly Notifications _notifications;
        private volatile uint _notifySerial = 0;
        private volatile uint _closeNotificationSerial = 0;
        public NotifierService(ILogger<NotifierService> logger, Notifications notifications)
        {
            _logger = logger;
            _notifications = notifications;
        }

        public override async Task CloseNotification(IAsyncStreamReader<CloseNotificationRequest> requestStream, IServerStreamWriter<CloseNotificationReply> responseStream, ServerCallContext context)
        {
            var watcher = new EventWatcher<CloseNotificationRequest>();
            async Task HandleCloseNotification(Notifications sender, Notifications.CloseNotificationEventArgs args)
            {
                var serial = _closeNotificationSerial;
                Interlocked.Increment(ref _closeNotificationSerial);
                var reply = new CloseNotificationReply()
                {
                    NotificationId = args.NotificationId,
                    SerialId = serial,
                };
                var tcs = new TaskCompletionSource();
                void handler(CloseNotificationRequest req)
                {
                    if (req.SerialId == serial)
                    {
                        watcher.OnEventOccured -= handler;
                        if (req.Success)
                        {
                            tcs.SetResult();
                        }
                        else
                        {
                            tcs.SetException(new Exception(req.Error.ErrorMessage));
                        }
                    }
                }
                watcher.OnEventOccured += handler;
                await Task.WhenAll(tcs.Task, responseStream.WriteAsync(reply, context.CancellationToken));
            }
            _notifications.OnCloseNotification += HandleCloseNotification;
            try
            {
                if (!context.CancellationToken.IsCancellationRequested)
                {
                    await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                    {
                        watcher.FireEvent(request);
                    }
                }
            }
            finally
            {
                _notifications.OnCloseNotification -= HandleCloseNotification;
            }
        }

        public override async Task Notify(IAsyncStreamReader<NotifyRequest> requestStream, IServerStreamWriter<NotifyReply> responseStream, ServerCallContext context)
        {
            var watcher = new EventWatcher<NotifyRequest>();
            async Task<uint> handleNotify(Notifications sender, Notifications.NotifyEventArgs args)
            {
                var serial = _notifySerial;
                Interlocked.Increment(ref _notifySerial);
                var reply = new NotifyReply()
                {
                    AppName = args.AppName,
                    ReplacesId = args.ReplacesId,
                    AppIcon = args.AppIcon,
                    Summary = args.Summary,
                    Body = args.Body,
                    ExpireTimeout = args.ExpireTimeout,
                    NotificationId = args.NotificationId,
                    SerialId = serial,
                };
                reply.Actions.AddRange(args.Actions);
                foreach (var (k, v) in args.Hints)
                {
                    if (v is byte v1)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            ByteVal = v1,
                        });
                    }
                    else if (v is bool v2)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            BoolVal = v2,
                        });
                    }
                    else if (v is short v3)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            ShortVal = v3,
                        });
                    }
                    else if (v is ushort v4)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            UshortVal = v4,
                        });
                    }
                    else if (v is int v5)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            IntVal = v5,
                        });
                    }
                    else if (v is uint v6)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            UintVal = v6,
                        });
                    }
                    else if (v is long v7)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            LongVal = v7,
                        });
                    }
                    else if (v is ulong v8)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            UlongVal = v8,
                        });
                    }
                    else if (v is float v9)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            FloatVal = v9,
                        });
                    }
                    else if (v is double v10)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            DoubleVal = v10,
                        });
                    }
                    else if (v is string v11)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            StringVal = v11,
                        });
                    }
                    else if (v is byte[] v12)
                    {
                        reply.Hints.Add(k, new NotificationHintVariant()
                        {
                            BytesVal = ByteString.CopyFrom(v12),
                        });
                    }
                }
                var tcs = new TaskCompletionSource<uint>();
                void handler(NotifyRequest req)
                {
                    if (req.SerialId == serial)
                    {
                        watcher.OnEventOccured -= handler;
                        if (req.Success)
                        {
                            tcs.SetResult(req.NotificationId);
                        }
                        else
                        {
                            tcs.SetException(new Exception(req.Error.ErrorMessage));
                        }
                    }
                }
                watcher.OnEventOccured += handler;
                await Task.WhenAll(tcs.Task, responseStream.WriteAsync(reply, context.CancellationToken));
                return tcs.Task.Result;
            }
            _notifications.OnNotify += handleNotify;
            try
            {
                if (!context.CancellationToken.IsCancellationRequested)
                {
                    await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                    {
                        watcher.FireEvent(request);
                    }
                }
            }
            finally
            {
                _notifications.OnNotify -= handleNotify;
            }
        }

        public override async Task<NotificationClosedReply> NotificationClosed(IAsyncStreamReader<NotificationClosedRequest> requestStream, ServerCallContext context)
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                _notifications.FireOnClose(request.NotificationId, request.Reason);
            }
            return new NotificationClosedReply();
        }

        public override async Task<ActionInvokedReply> ActionInvoked(IAsyncStreamReader<ActionInvokedRequest> requestStream, ServerCallContext context)
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                _notifications.FireOnAction(request.NotificationId, request.ActionKey);
            }
            return new ActionInvokedReply();
        }

        private class EventWatcher<T>
        {
            public event Action<T>? OnEventOccured;

            public void FireEvent(T req)
            {
                OnEventOccured?.Invoke(req);
            }
        }
    }
}
