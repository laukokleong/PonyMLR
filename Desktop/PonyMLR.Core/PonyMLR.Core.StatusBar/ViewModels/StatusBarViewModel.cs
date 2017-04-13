using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Practices.Prism.ViewModel;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.Prism.Commands;
using PonyMLR.Infrastructure;

namespace PonyMLR.Core.StatusBar
{
    public class StatusBarViewModel : ViewModelBase
    {
        private IEventAggregator eventaggregator;
        SubscriptionToken _statusUpdateToken;

        private string _statusmessage;

        public StatusBarViewModel(IEventAggregator eventaggregator)
        {
            this.eventaggregator = eventaggregator;

            _statusUpdateToken = eventaggregator.GetEvent<StatusBarUpdateEvent>().Subscribe(StatusBarUpdate, ThreadOption.UIThread, true);
            eventaggregator.GetEvent<StatusBarSuppressMessageEvent>().Subscribe(DisableStatusMessageUpdate, ThreadOption.UIThread, true);
        }

        public void StatusBarUpdate(string message)
        {
                StatusMessage = message;
        }

        public void DisableStatusMessageUpdate(bool suppress)
        {
            if (suppress == true)
                eventaggregator.GetEvent<StatusBarUpdateEvent>().Unsubscribe(_statusUpdateToken);
        }

        public string StatusMessage
        {
            get
            {
                return this._statusmessage;
            }
            set
            {
                if (this._statusmessage == value) return;
                this._statusmessage = value;
                RaisePropertyChangedEvent("StatusMessage");
            }
        }
    }
}
