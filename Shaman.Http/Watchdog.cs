using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    public class Watchdog : IDisposable
    {

        private Timer timer;

        private int timeout;
        public Watchdog(TimeSpan timeout, Action action)
            : this((int)timeout.TotalMilliseconds, action)
        {
        }

        public Watchdog(int timeout, Action action)
        {

            timer = new Timer(o =>
            {
                action();
            }, null, timeout, -1);

            this.timeout = timeout;
        }

        public void Pulse()
        {
            timer.Change(timeout, -1);
        }

        public void Dispose()
        {
            timer.Dispose();
        }





    }
}
