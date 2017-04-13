using System.Windows.Input;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.ServiceLocation;
using PonyMLR.Infrastructure;

namespace PonyMLR.Modules.Build
{
    public class BuildNavigatorViewModel : ViewModelBase, INavigationAware
    {
        // Property variables
        private bool? p_IsChecked;

        /// <summary>
        /// Default constructor.
        /// </summary>
        public BuildNavigatorViewModel()
        {
            this.Initialize();
        }

        /// <summary>
        /// Initializes the view model.
        /// </summary>
        private void Initialize()
        {
            // Initialize command properties
            this.ShowBuildView = new ShowBuildViewCommand(this);

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
        /// Loads the view for Build Module
        /// </summary>
        public ICommand ShowBuildView { get; set; }

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
            if (publisher == "BuildModule") return;

            // Otherwise, uncheck this button
            this.IsChecked = false;
        }
    }
}
