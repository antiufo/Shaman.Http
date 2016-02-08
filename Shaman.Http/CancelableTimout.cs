using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    public class CancellableTimout : IDisposable
    {
        private CancellableTimout()
        {
        }


        private Timer timer;
        private Action action;
        private int delay;
        private SynchronizationContext syncCtx;
        public static CancellableTimout Schedule(Action action, int delay)
        {
            return ScheduleInternal(action, delay, SynchronizationContext.Current);
        }
        [RestrictedAccess]
        public static CancellableTimout ScheduleUnsafe(Action action, int delay)
        {
            return ScheduleInternal(action, delay, null);
        }

        private static CancellableTimout ScheduleInternal(Action action, int delay, SynchronizationContext syncCtx)
        {
            var c = new CancellableTimout();
            c.action = action;
            c.syncCtx = syncCtx;
            var t = new Timer(state =>
            {
                var cc = (CancellableTimout)state;
                var a = cc.action;
                if (a != null)
                {
                    if (cc.syncCtx != null)
                    {
                        syncCtx.Post(new SendOrPostCallback(z =>
                        {
                            using (cc)
                            {
                                if (cc.action != null) a();
                            }
                        }), null);
                        return;
                    }
                    else
                    {
                        a();
                    }
                }
                cc.Dispose();
            }, c, delay, Timeout.Infinite);
            c.timer = t;
            c.delay = delay;
            return c;
        }

        public void Restart()
        {
            var t = timer;
            t.Change(delay, Timeout.Infinite);
        }

        public static CancellableTimout Schedule(Action action, TimeSpan delay)
        {
            return Schedule(action, (int)delay.TotalMilliseconds);
        }
        [RestrictedAccess]
        public static CancellableTimout ScheduleUnsafe(Action action, TimeSpan delay)
        {
            return ScheduleUnsafe(action, (int)delay.TotalMilliseconds);
        }

        public void Cancel()
        {
            Dispose();
        }

        public void Dispose()
        {
            action = null;
            var t = timer;
            if (t != null) t.Dispose();
            timer = null;
        }
    }
}
