using System;
using System.Collections.Generic;
using System.Windows;
using System.Threading.Tasks;
using Microsoft.Practices.Unity;  
using Microsoft.Practices.Prism.UnityExtensions;
using Microsoft.Practices.ServiceLocation;
using Microsoft.Practices.Prism.Modularity;
using Microsoft.Practices.Prism.Regions;
using PonyMLR.Core.DatabaseSelect;
using PonyMLR.Core.ResearchPanel;
using PonyMLR.Core.StatusBar;
using PonyMLR.Modules.Manage;
using PonyMLR.Modules.Calculate;
using PonyMLR.Modules.Build;
using PonyMLR.Modules.Test;

namespace PonyMLR
{
    public class Bootstrapper : UnityBootstrapper
    {     
        /// <summary>
        /// Instantiates the Shell window.
        /// </summary>
        /// <returns>A new ShellWindow window.</returns>
        protected override DependencyObject CreateShell()
        {
            /* This method sets the UnityBootstrapper.Shell property to the ShellWindow
             * we declared elsewhere in this project. Note that the UnityBootstrapper base 
             * class will attach an instance of the RegionManager to the new Shell window. */

            return new Shell();
        }

        /// <summary>
        /// Displays the Shell window to the user.
        /// </summary>
        protected override void InitializeShell()
        {
            base.InitializeShell();

            App.Current.MainWindow = (Window)this.Shell;
            App.Current.MainWindow.Show();
        }

        /// <summary>
        /// Populates the Module Catalog.
        /// </summary>
        /// <returns>A new Module Catalog.</returns>
        protected override IModuleCatalog CreateModuleCatalog()
        {
            var moduleCatalog = new ModuleCatalog();
           
            // custom modules
            moduleCatalog.AddModule(typeof(ManageModule));
            moduleCatalog.AddModule(typeof(CalculateModule));
            moduleCatalog.AddModule(typeof(BuildModule));
            moduleCatalog.AddModule(typeof(TestModule));

            // core modules
            moduleCatalog.AddModule(typeof(DatabaseSelectModule));
            moduleCatalog.AddModule(typeof(ResearchPanelModule));
            moduleCatalog.AddModule(typeof(StatusBarModule));

            return moduleCatalog;
        }

        /// <summary>
        /// Configures the default region adapter mappings to use in the application, in order 
        /// to adapt UI controls defined in XAML to use a region and register it automatically.
        /// </summary>
        /// <returns>The RegionAdapterMappings instance containing all the mappings.</returns>
        protected override RegionAdapterMappings ConfigureRegionAdapterMappings()
        {
            // Call base method
            var mappings = base.ConfigureRegionAdapterMappings();
            if (mappings == null) return null;

            // Add custom mappings
            //var ribbonRegionAdapter = ServiceLocator.Current.GetInstance<RibbonRegionAdapter>();
            //mappings.RegisterMapping(typeof(Ribbon), ribbonRegionAdapter);

            // Set return value
            return mappings;
        }

        protected override void ConfigureContainer()
        {
            base.ConfigureContainer();

            // register the new navigation service that uses synchronous navigation confirmation instead of the async confirmation.
            this.Container.RegisterType(typeof(IRegionNavigationService), typeof(RegionNavigationService));
        }
    }
}
