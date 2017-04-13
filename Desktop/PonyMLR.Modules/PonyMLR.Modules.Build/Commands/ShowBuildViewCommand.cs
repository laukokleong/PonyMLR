using System;
using System.Windows.Input;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.Regions;
using Microsoft.Practices.ServiceLocation;
using PonyMLR.Infrastructure;

namespace PonyMLR.Modules.Build
{
    public class ShowBuildViewCommand : ICommand
    {
        // Member variables
        private BuildNavigatorViewModel m_ViewModel;

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ShowBuildViewCommand(BuildNavigatorViewModel viewModel)
        {
            m_ViewModel = viewModel;
        }

        /// <summary>
        /// Callback method invoked when navigation has completed.
        /// </summary>
        /// <param name="result">Provides information about the result of the navigation.</param>
        private void NavigationCompleted(NavigationResult result)
        {
            // Exit if navigation was not successful
            if (result.Result != true) return;

            // Publish ViewRequestedEvent
            var eventAggregator = ServiceLocator.Current.GetInstance<IEventAggregator>();
            var navigationCompletedEvent = eventAggregator.GetEvent<NavigationCompletedEvent>();
            navigationCompletedEvent.Publish("BuildModule");
        }

        #region ICommand Members

        /// <summary>
        /// Whether the ShowModuleAViewCommand is enabled.
        /// </summary>
        public bool CanExecute(object parameter)
        {
            return true;
        }

        /// <summary>
        /// Actions to take when CanExecute() changes.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Executes the ShowModuleAViewCommand
        /// </summary>
        public void Execute(object parameter)
        {
            // Initialize
            var regionManager = ServiceLocator.Current.GetInstance<IRegionManager>();

            /* We invoke the NavigationCompleted() callback 
             * method in our final  navigation request. */

            // Show Workspace
            var BuildContent = new Uri("BuildView", UriKind.Relative);
            regionManager.RequestNavigate(RegionNames.MainRegion, BuildContent, NavigationCompleted);
        }

        #endregion
    }
}
