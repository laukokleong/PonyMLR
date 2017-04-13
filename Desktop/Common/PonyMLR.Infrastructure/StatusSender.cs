using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Practices.Prism.Events;

namespace PonyMLR.Infrastructure
{
    public class StatusSender
    {
        private IEventAggregator eventaggregator;

        public StatusSender(IEventAggregator eventaggregator)
        {
            this.eventaggregator = eventaggregator;
        }

        public void SendStatus(string status)
        {
            eventaggregator.GetEvent<StatusBarUpdateEvent>().Publish(status);
        }

        public void SuppressFutureMessage(bool suppresss)
        {
            eventaggregator.GetEvent<StatusBarSuppressMessageEvent>().Publish(suppresss);
        }
    }
}
