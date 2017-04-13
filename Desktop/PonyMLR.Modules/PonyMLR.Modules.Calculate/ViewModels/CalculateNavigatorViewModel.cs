using System.Windows.Input;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.ServiceLocation;
using PonyMLR.Infrastructure;

namespace PonyMLR.Modules.Calculate
{
    public class CalculateNavigatorViewModel : ViewModelBase, INavigationAware
    {
        // Property variables
        private bool? p_IsChecked;

        /// <summary>
        /// Default constructor.
        /// </summary>
        public CalculateNavigatorViewModel()
        {
            this.Initialize();
        }

        /// <summary>
        /// Initializes the view model.
        /// </summary>
        private void Initialize()
        {
            // Initialize command properties
            this.ShowCalculateView = new ShowCalculateViewCommand(this);

            // Initialize administrative properties
            this.IsChecked = false;

            // Subscribe to Composite Presentation Events
            var eventAggregator = ServiceLocator.Current.GetInstance<IEventAggregator>();
            var navigationCompletedEvent = eventAggregator.GetEvent<NavigationCompletedEvent>();
            navigationCompletedEvent.Subscribe(OnNavigationCompleted, ThreadOption.UIThread);
        }

        #region INavigationAware Members

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            throw new System.NotImplementedException();
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            throw new System.NotImplementedException();
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            throw new System.NotImplementedException();
        }

        #endregion

        /// <summary>
        /// Loads the view for Calculate Module
        /// </summary>
        public ICommand ShowCalculateView { get; set; }

        /// <summary>
        /// Whether the button is checked (selected).
        /// </summary>
        public bool? IsChecked
        {
            get { return p_IsChecked; }

            set
            {
                base.RaisePropertyChangingEvent("IsChecked");
                p_IsChecked = value;
                base.RaisePropertyChangedEvent("IsChecked");
            }
        }

        /// <summary>
        /// Sets the IsChecked state of the Task Button when navigation is completed.
        /// </summary>
        /// <param name="publisher">The publisher of the event.</param>
        private void OnNavigationCompleted(string publisher)
        {
            // Exit if this module published the event
            if (publisher == "CalculateModule") return;

            // Otherwise, uncheck this button
            this.IsChecked = false;
        }
    }
}
